using System.Net;
using System.Text;
using System.Text.Json;

using HomeBudget.Api.Features.Notifications;
using HomeBudget.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace HomeBudget.Tests.Notifications;

/// <summary>M1, M2 : préférences, abonnements des appareils, alertes push et résumé par e-mail.</summary>
public sealed class NotificationTests
{
    /// <summary>Lundi : jour du résumé hebdomadaire.</summary>
    private static readonly DateOnly Monday = new(2026, 3, 9);

    private static object Subscription(WebPushDecryptor device, string endpoint, string? deviceName = "Firefox · Android") => new
    {
        endpoint,
        keys = new { p256dh = device.P256dh, auth = device.Auth },
        device = deviceName,
    };

    private static object Preferences(
        bool budget = true, bool forecast = true, bool dueTomorrow = true, bool pending = true, string summary = "None", string? email = null) => new
        {
            budgetAlerts = budget,
            forecastAlerts = forecast,
            dueTomorrowAlerts = dueTomorrow,
            pendingAlerts = pending,
            emailSummary = summary,
            email,
        };

    private static async Task<AlertService.RunResult> RunAlertsAsync(HomeBudgetFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AlertService>().RunAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Notifications reçues par un appareil, déchiffrées.</summary>
    private static List<JsonElement> Received(PushTestFactory factory, WebPushDecryptor device, string endpoint) =>
        [.. factory.Push.Requests.Where(r => r.Endpoint.ToString() == endpoint)
            .Select(r => JsonDocument.Parse(device.Decrypt(r.Body)).RootElement)];

    private static List<string> Titles(IEnumerable<JsonElement> messages) => [.. messages.Select(m => m.GetProperty("title").GetString()!)];

    [Fact]
    public async Task Sans_cles_VAPID_le_push_est_indisponible()
    {
        using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        using var device = new WebPushDecryptor();

        var settings = await admin.GetJsonAsync("/api/notifications");

        settings.GetProperty("pushAvailable").GetBoolean().ShouldBeFalse();
        settings.GetProperty("vapidPublicKey").ValueKind.ShouldBe(JsonValueKind.Null);
        settings.GetProperty("emailAvailable").GetBoolean().ShouldBeFalse();
        settings.GetProperty("email").GetString().ShouldBe("admin@example.test");
        settings.GetProperty("preferences").GetProperty("budgetAlerts").GetBoolean().ShouldBeTrue();
        settings.GetProperty("preferences").GetProperty("emailSummary").GetString().ShouldBe("None");
        (await admin.PostAsync("/api/notifications/subscriptions", Subscription(device, "https://fcm.googleapis.com/fcm/send/a")))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostAsync("/api/notifications/test")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostAsync("/api/notifications/test-email")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Chaque_utilisateur_gere_ses_propres_appareils()
    {
        using var factory = new PushTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        using var phone = new WebPushDecryptor();
        const string endpoint = "https://fcm.googleapis.com/fcm/send/phone";

        var settings = await admin.GetJsonAsync("/api/notifications");
        settings.GetProperty("pushAvailable").GetBoolean().ShouldBeTrue();
        settings.GetProperty("vapidPublicKey").GetString().ShouldBe(factory.VapidPublicKey);

        // Adresse hors des services push connus, clés invalides : refusées.
        var invalid = await admin.PostAsync("/api/notifications/subscriptions", new
        {
            endpoint = "https://intranet.local/hook",
            keys = new { p256dh = "abc", auth = phone.Auth + "AAAA" },
        });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = (await ApiClient.ReadJsonAsync(invalid)).GetProperty("errors");
        errors.EnumerateObject().Select(e => e.Name).Order().ShouldBe(["endpoint", "keys.auth", "keys.p256dh"]);

        var created = await admin.CreateAsync("/api/notifications/subscriptions", Subscription(phone, endpoint));
        created.GetProperty("device").GetString().ShouldBe("Firefox · Android");
        var id = created.GetProperty("id").GetGuid();

        // Lecture seule compris : chacun reçoit ses notifications, mais ne voit ni ne retire les appareils des autres.
        (await reader.GetJsonAsync("/api/notifications")).GetProperty("devices").GetArrayLength().ShouldBe(0);
        (await reader.DeleteAsync($"/api/notifications/subscriptions/{id}", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.PostAsync("/api/notifications/subscriptions/unsubscribe", new { endpoint })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync("/api/notifications")).GetProperty("devices").GetArrayLength().ShouldBe(1);

        // Le même navigateur ouvert par un autre utilisateur : l'abonnement le suit.
        (await reader.PostAsync("/api/notifications/subscriptions", Subscription(phone, endpoint, null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetJsonAsync("/api/notifications")).GetProperty("devices").GetArrayLength().ShouldBe(0);
        var devices = (await reader.GetJsonAsync("/api/notifications")).GetProperty("devices");
        devices.GetArrayLength().ShouldBe(1);
        devices[0].GetProperty("id").GetGuid().ShouldBe(id);

        (await reader.DeleteAsync($"/api/notifications/subscriptions/{id}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await reader.GetJsonAsync("/api/notifications")).GetProperty("devices").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task La_notification_d_essai_est_chiffree_signee_et_retire_les_abonnements_expires()
    {
        using var factory = new PushTestFactory();
        var admin = await factory.CreateAdminClientAsync();
        using var phone = new WebPushDecryptor();
        using var laptop = new WebPushDecryptor();
        const string phoneUrl = "https://fcm.googleapis.com/fcm/send/phone";
        const string laptopUrl = "https://updates.push.services.mozilla.com/wpush/v2/laptop";
        await admin.CreateAsync("/api/notifications/subscriptions", Subscription(phone, phoneUrl));
        await admin.CreateAsync("/api/notifications/subscriptions", Subscription(laptop, laptopUrl));
        factory.Push.Statuses[laptopUrl] = HttpStatusCode.Gone;

        var result = await admin.CreateAsync("/api/notifications/test", new { });

        result.GetProperty("reached").GetInt32().ShouldBe(1);
        result.GetProperty("removed").GetInt32().ShouldBe(1);
        var request = factory.Push.Requests.Single(r => r.Endpoint.ToString() == phoneUrl);
        request.ContentEncoding.ShouldBe("aes128gcm");
        request.Ttl.ShouldBe("86400");
        request.Authorization.ShouldStartWith("vapid t=");
        request.Authorization.ShouldEndWith($"k={factory.VapidPublicKey}");
        var message = Received(factory, phone, phoneUrl).Single();
        message.GetProperty("title").GetString().ShouldBe("Home Budget");
        message.GetProperty("url").GetString().ShouldBe("/parametres");

        var devices = (await admin.GetJsonAsync("/api/notifications")).GetProperty("devices");
        devices.GetArrayLength().ShouldBe(1);
        devices[0].GetProperty("lastSuccessAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Le_resume_exige_une_adresse_qui_devient_celle_du_compte()
    {
        using var factory = new PushTestFactory();
        var admin = await factory.CreateAdminClientAsync();

        var missing = await admin.PutAsync("/api/notifications/preferences", Preferences(summary: "Weekly", email: " "), null);
        missing.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PutAsync("/api/notifications/preferences", Preferences(email: "pas-une-adresse"), null))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var saved = await admin.PutAsync("/api/notifications/preferences", Preferences(budget: false, summary: "Monthly", email: "foyer@example.test"), null);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var settings = await ApiClient.ReadJsonAsync(saved);
        settings.GetProperty("email").GetString().ShouldBe("foyer@example.test");
        settings.GetProperty("preferences").GetProperty("budgetAlerts").GetBoolean().ShouldBeFalse();
        settings.GetProperty("preferences").GetProperty("emailSummary").GetString().ShouldBe("Monthly");
        (await admin.GetJsonAsync("/api/auth/me")).GetProperty("email").GetString().ShouldBe("foyer@example.test");

        (await admin.PostAsync("/api/notifications/test-email")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        factory.Email.Sent.Single().To.ShouldBe("foyer@example.test");
        factory.Email.Fail = true;
        var failed = await admin.PostAsync("/api/notifications/test-email");
        failed.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await failed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("SMTP indisponible");
    }

    [Fact]
    public async Task Les_alertes_partent_une_fois_selon_les_comptes_visibles_et_les_preferences()
    {
        using var factory = new PushTestFactory { Today = Monday };
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var bob = await factory.CreateUserClientAsync("bob", "Member");
        var shared = await admin.CreateAccountAsync("Commun", 1000m, initialDate: new DateOnly(2026, 1, 1));
        var secret = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso Alice",
            type = "Checking",
            initialBalance = 500m,
            initialDate = "2026-01-01",
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        var groceries = await admin.CategoryIdAsync("Courses");
        var outings = await admin.CreateCategoryAsync("Sorties", "Variable");
        (await admin.PutAsync($"/api/budgets/categories/{groceries}", new { mode = "Amount", value = 100m }, null)).EnsureSuccessStatusCode();
        (await admin.PutAsync($"/api/budgets/categories/{outings}", new { mode = "Amount", value = 50m }, null)).EnsureSuccessStatusCode();

        // Courses : 85 % sur le compte commun (avertissement pour tous) ; Sorties : 120 % sur le compte privé d'Alice.
        await admin.CreateTransactionAsync(shared, -85m, groceries, Monday.AddDays(-2));
        await alice.CreateTransactionAsync(secret, -60m, outings, Monday.AddDays(-1), payeeName: "Cinéma secret");
        // Gros achat prévu jeudi : le solde commun passerait sous zéro.
        await admin.CreateTransactionAsync(shared, -2000m, date: Monday.AddDays(3), payeeName: "Garage");
        await admin.CreateAsync("/api/recurrences", Recurrence(shared, -50m, Monday.AddDays(1), "Loyer", "Automatic"));
        await admin.CreateAsync("/api/recurrences", Recurrence(shared, -40m, Monday, "Électricité", "ToValidate"));

        using var adminPhone = new WebPushDecryptor();
        using var alicePhone = new WebPushDecryptor();
        using var bobPhone = new WebPushDecryptor();
        const string adminUrl = "https://fcm.googleapis.com/fcm/send/admin";
        const string aliceUrl = "https://fcm.googleapis.com/fcm/send/alice";
        const string bobUrl = "https://fcm.googleapis.com/fcm/send/bob";
        await admin.CreateAsync("/api/notifications/subscriptions", Subscription(adminPhone, adminUrl));
        await alice.CreateAsync("/api/notifications/subscriptions", Subscription(alicePhone, aliceUrl));
        await bob.CreateAsync("/api/notifications/subscriptions", Subscription(bobPhone, bobUrl));
        (await bob.PutAsync("/api/notifications/preferences", Preferences(budget: false, pending: false), null)).EnsureSuccessStatusCode();

        await RunAlertsAsync(factory);

        var forAdmin = Received(factory, adminPhone, adminUrl);
        Titles(forAdmin).ShouldBe(["Budget bientôt atteint", "Opérations à valider", "Solde prévu sous le seuil", "Échéance demain"], ignoreOrder: true);
        forAdmin.Single(m => m.GetProperty("title").GetString() == "Budget bientôt atteint").GetProperty("body").GetString()
            .ShouldBe("Courses : 85 % du budget (85,00 € sur 100,00 €).");
        forAdmin.Single(m => m.GetProperty("title").GetString() == "Échéance demain").GetProperty("body").GetString()
            .ShouldBe("Loyer : −50,00 €.");
        forAdmin.Single(m => m.GetProperty("title").GetString() == "Solde prévu sous le seuil").GetProperty("body").GetString()
            .ShouldBe("Passage sous 0,00 € prévu le 12/03.");
        forAdmin.Single(m => m.GetProperty("title").GetString() == "Opérations à valider").GetProperty("url").GetString().ShouldBe("/a-valider");

        // R8 : seule Alice voit le dépassement dû à son compte privé.
        var forAlice = Received(factory, alicePhone, aliceUrl);
        Titles(forAlice).ShouldContain("Budget dépassé");
        forAlice.Single(m => m.GetProperty("title").GetString() == "Budget dépassé").GetProperty("body").GetString()!.ShouldStartWith("Sorties : 120");
        Titles(forAdmin).ShouldNotContain("Budget dépassé");

        // Préférences de Bob : ni budget ni échéances à valider.
        Titles(Received(factory, bobPhone, bobUrl)).ShouldBe(["Solde prévu sous le seuil", "Échéance demain"], ignoreOrder: true);

        // Deuxième vérification : rien de nouveau, rien ne repart.
        var count = factory.Push.Requests.Count;
        (await RunAlertsAsync(factory)).PushSent.ShouldBe(0);
        factory.Push.Requests.Count.ShouldBe(count);

        // Une nouvelle échéance à valider relance l'alerte regroupée (administrateur et Alice ; Bob l'a désactivée).
        await admin.CreateAsync("/api/recurrences", Recurrence(shared, -15m, Monday, "Cantine", "ToValidate"));
        (await RunAlertsAsync(factory)).PushSent.ShouldBe(2);
        Received(factory, adminPhone, adminUrl)[^1].GetProperty("body").GetString().ShouldBe("2 échéances attendent votre confirmation.");
    }

    [Fact]
    public async Task Le_resume_hebdomadaire_part_le_lundi_une_seule_fois()
    {
        using var factory = new PushTestFactory { Today = Monday };
        var admin = await factory.CreateAdminClientAsync();
        var shared = await admin.CreateAccountAsync("Commun", 1000m, initialDate: new DateOnly(2026, 1, 1));
        var groceries = await admin.CategoryIdAsync("Courses");
        var salary = await admin.CategoryIdAsync("Primes");
        await admin.CreateTransactionAsync(shared, -30m, groceries, Monday.AddDays(-3));
        await admin.CreateTransactionAsync(shared, 1500m, salary, Monday.AddDays(-5));
        await admin.CreateTransactionAsync(shared, -99m, groceries, Monday.AddDays(-8)); // semaine précédente
        await admin.CreateAsync("/api/recurrences", Recurrence(shared, -50m, Monday.AddDays(2), "Loyer <maison>", "Automatic"));
        (await admin.PutAsync("/api/notifications/preferences", Preferences(summary: "Weekly", email: "admin@example.test"), null))
            .EnsureSuccessStatusCode();

        (await RunAlertsAsync(factory)).EmailsSent.ShouldBe(1);
        (await RunAlertsAsync(factory)).EmailsSent.ShouldBe(0);

        var email = factory.Email.Sent.Single();
        email.To.ShouldBe("admin@example.test");
        email.Subject.ShouldBe("Home Budget · résumé de la semaine du 02/03 au 08/03");
        email.Text.ShouldContain("Revenus : 1 500,00 €");
        email.Text.ShouldContain("Dépenses : 30,00 €");
        email.Text.ShouldContain("11/03 · Loyer <maison> · −50,00 €");
        email.Text.ShouldContain("https://budget.example.test/");
        // HTML échappé : un libellé ne peut pas injecter de balise.
        email.Html.ShouldContain("Loyer &lt;maison&gt;");
        email.Html.ShouldNotContain("<maison>");
    }

    [Fact]
    public async Task Un_echec_d_envoi_du_resume_est_retente()
    {
        using var factory = new PushTestFactory { Today = new DateOnly(2026, 4, 1) };
        var admin = await factory.CreateAdminClientAsync();
        (await admin.PutAsync("/api/notifications/preferences", Preferences(summary: "Monthly", email: "admin@example.test"), null))
            .EnsureSuccessStatusCode();

        factory.Email.Fail = true;
        (await RunAlertsAsync(factory)).EmailsSent.ShouldBe(0);
        factory.Email.Fail = false;
        (await RunAlertsAsync(factory)).EmailsSent.ShouldBe(1);
        factory.Email.Sent.Single().Subject.ShouldBe("Home Budget · résumé de mars 2026");
    }

    [Theory]
    [InlineData("2026-03-09", "Weekly", "2026-W10")]
    [InlineData("2026-03-10", "Weekly", null)]
    [InlineData("2026-01-05", "Weekly", "2026-W01")]
    [InlineData("2026-04-01", "Monthly", "2026-03")]
    [InlineData("2026-01-01", "Monthly", "2025-12")]
    [InlineData("2026-04-02", "Monthly", null)]
    [InlineData("2026-03-09", "None", null)]
    public void Fenetre_du_resume(string today, string frequency, string? key) =>
        AlertService.Window(Enum.Parse<HomeBudget.Api.Domain.Notifications.EmailSummaryFrequency>(frequency), DateOnly.Parse(today, System.Globalization.CultureInfo.InvariantCulture))?.Key
            .ShouldBe(key);

    [Fact]
    public void Montants_et_listes_au_format_francais()
    {
        NotificationText.Money(1234.5m).ShouldBe("1 234,50 €");
        NotificationText.Money(-0.07m).ShouldBe("−0,07 €");
        NotificationText.List(["A"]).ShouldBe("A");
        NotificationText.List(["A", "B"]).ShouldBe("A et B");
        NotificationText.List(["A", "B", "C", "D", "E"]).ShouldBe("A, B, C et 2 autres");
        Encoding.UTF8.GetByteCount(NotificationText.Money(-999_999_999.99m)).ShouldBeLessThan(40);
    }

    private static object Recurrence(Guid accountId, decimal amount, DateOnly start, string label, string mode) => new
    {
        label,
        accountId,
        toAccountId = (Guid?)null,
        payeeName = label,
        categoryId = (Guid?)null,
        amount,
        notes = (string?)null,
        mode,
        startDate = TestScenario.Iso(start),
        unit = "Month",
        interval = 1,
        end = "Never",
        endDate = (string?)null,
        count = (int?)null,
        isInstallment = false,
    };
}
