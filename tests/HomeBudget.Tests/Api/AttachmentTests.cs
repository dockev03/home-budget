using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Pièces jointes des opérations (B7) et leur sauvegarde (H6).</summary>
public sealed class AttachmentTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5];
    private static readonly byte[] Pdf = "%PDF-1.7\nfiche de paie"u8.ToArray();

    private static Task<HttpResponseMessage> UploadAsync(ApiClient client, Guid transactionId, byte[] content, string fileName, string type = "application/octet-stream")
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(type);
        return client.PostContentAsync($"/api/transactions/{transactionId}/attachments?fileName={Uri.EscapeDataString(fileName)}", body);
    }

    private static async Task<(ApiClient Admin, Guid Account, Guid Transaction)> SetupAsync(HomeBudgetFactory factory)
    {
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant", 1000m);
        var transaction = await admin.CreateTransactionAsync(account, 2500m, label: "Salaire");
        return (admin, account, transaction.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Payslip_is_attached_listed_downloaded_and_removed()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, _, transaction) = await SetupAsync(factory);

        // Le type annoncé est ignoré : seuls les premiers octets comptent.
        var upload = await UploadAsync(admin, transaction, Pdf, "fiche-paie-mars.pdf", "image/png");
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var attachment = await ApiClient.ReadJsonAsync(upload);
        attachment.GetProperty("contentType").GetString().ShouldBe("application/pdf");
        attachment.GetProperty("fileName").GetString().ShouldBe("fiche-paie-mars.pdf");
        attachment.GetProperty("size").GetInt64().ShouldBe(Pdf.Length);
        var id = attachment.GetProperty("id").GetGuid();

        var list = await admin.GetJsonAsync($"/api/transactions/{transaction}/attachments");
        list.GetArrayLength().ShouldBe(1);
        (await admin.GetJsonAsync($"/api/transactions/{transaction}")).GetProperty("attachmentCount").GetInt32().ShouldBe(1);

        var content = await admin.GetAsync($"/api/attachments/{id}/content");
        content.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        content.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
        content.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("sandbox");
        (await content.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(Pdf);

        var sha = Convert.ToHexStringLower(SHA256.HashData(Pdf));
        var path = Path.Combine(factory.DataPath, "attachments", sha);
        File.Exists(path).ShouldBeTrue();

        (await admin.DeleteAsync($"/api/attachments/{id}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetJsonAsync($"/api/transactions/{transaction}/attachments")).GetArrayLength().ShouldBe(0);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task Images_are_displayed_and_identical_files_are_stored_once()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, account, first) = await SetupAsync(factory);
        var second = (await admin.CreateTransactionAsync(account, -30m, label: "Courses")).GetProperty("id").GetGuid();

        var a = await ApiClient.ReadJsonAsync(await UploadAsync(admin, first, Png, "ticket.png"));
        var b = await ApiClient.ReadJsonAsync(await UploadAsync(admin, second, Png, "ticket-copie.png"));
        Directory.GetFiles(Path.Combine(factory.DataPath, "attachments")).Length.ShouldBe(1);

        var content = await admin.GetAsync($"/api/attachments/{a.GetProperty("id").GetGuid()}/content");
        content.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("inline");
        var download = await admin.GetAsync($"/api/attachments/{a.GetProperty("id").GetGuid()}/content?download=true");
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");

        // Le fichier reste tant qu'une autre opération l'utilise.
        (await admin.DeleteAsync($"/api/attachments/{a.GetProperty("id").GetGuid()}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/attachments/{b.GetProperty("id").GetGuid()}/content")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unsupported_empty_and_oversized_files_are_refused()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, _, transaction) = await SetupAsync(factory);

        (await UploadAsync(admin, transaction, "<svg onload=alert(1)>"u8.ToArray(), "image.svg", "image/svg+xml"))
            .StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(admin, transaction, "MZ\u0090\0binaire"u8.ToArray(), "facture.pdf"))
            .StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(admin, transaction, [], "vide.pdf")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var big = new byte[(10 * 1024 * 1024) + 1];
        Pdf.CopyTo(big, 0);
        (await UploadAsync(admin, transaction, big, "gros.pdf")).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        (await UploadAsync(admin, Guid.NewGuid(), Pdf, "x.pdf")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        Directory.GetFiles(Path.Combine(factory.DataPath, "attachments")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Attachments_follow_account_visibility_and_roles()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var alice = await factory.CreateUserClientAsync("alice", "Member");
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");
        var mine = (await alice.CreateAsync("/api/accounts", new
        {
            name = "Perso",
            type = "Checking",
            initialBalance = 0m,
            initialDate = TestScenario.Iso(TestScenario.Today.AddYears(-1)),
            isPrivate = true,
        })).GetProperty("id").GetGuid();
        var secret = (await alice.CreateTransactionAsync(mine, -50m)).GetProperty("id").GetGuid();
        var attachment = (await ApiClient.ReadJsonAsync(await UploadAsync(alice, secret, Pdf, "secret.pdf"))).GetProperty("id").GetGuid();

        // R8 : l'administrateur ne voit ni l'opération, ni sa pièce jointe.
        (await admin.GetAsync($"/api/transactions/{secret}/attachments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetAsync($"/api/attachments/{attachment}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/attachments/{attachment}", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await UploadAsync(admin, secret, Png, "intrus.png")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Lecture seule : consultation, mais ni ajout ni suppression.
        var account = await admin.CreateAccountAsync("Commun");
        var shared = (await admin.CreateTransactionAsync(account, -5m)).GetProperty("id").GetGuid();
        var sharedAttachment = (await ApiClient.ReadJsonAsync(await UploadAsync(admin, shared, Png, "ticket.png"))).GetProperty("id").GetGuid();
        (await reader.GetAsync($"/api/attachments/{sharedAttachment}/content")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await UploadAsync(reader, shared, Png, "ticket.png")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.DeleteAsync($"/api/attachments/{sharedAttachment}", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Deleting_the_transaction_removes_its_attachments()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, _, transaction) = await SetupAsync(factory);
        var attachment = (await ApiClient.ReadJsonAsync(await UploadAsync(admin, transaction, Pdf, "fiche.pdf"))).GetProperty("id").GetGuid();
        var version = (await admin.GetJsonAsync($"/api/transactions/{transaction}")).Version();

        (await admin.DeleteAsync($"/api/transactions/{transaction}", version)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/attachments/{attachment}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Backup_contains_attachments_and_archive_restores_missing_files()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, _, transaction) = await SetupAsync(factory);
        await UploadAsync(admin, transaction, Pdf, "fiche.pdf");
        var backup = await (await admin.GetAsync("/api/backups/export")).Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using (var json = JsonDocument.Parse(backup))
        {
            json.RootElement.GetProperty("formatVersion").GetInt32().ShouldBe(9);
            json.RootElement.GetProperty("attachments").GetArrayLength().ShouldBe(1);
        }

        var archive = await admin.GetAsync("/api/backups/attachments");
        archive.StatusCode.ShouldBe(HttpStatusCode.OK);
        archive.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        var zipBytes = await archive.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        var sha = Convert.ToHexStringLower(SHA256.HashData(Pdf));
        using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
        {
            zip.Entries.Select(e => e.FullName).ShouldBe([sha]);
        }

        // Nouvelle instance : la sauvegarde JSON signale les fichiers manquants, l'archive les ajoute.
        await using var other = new HomeBudgetFactory();
        var otherAdmin = await other.CreateAdminClientAsync();
        var restore = await otherAdmin.PostContentAsync("/api/backups/restore", new StringContent(backup, System.Text.Encoding.UTF8, "application/json"));
        restore.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await ApiClient.ReadJsonAsync(restore);
        result.GetProperty("attachments").GetInt32().ShouldBe(1);
        result.GetProperty("missingAttachmentFiles").GetInt32().ShouldBe(1);

        // Une entrée dont le contenu ne correspond pas à son nom est refusée.
        var tampered = new MemoryStream();
        using (var zip = new ZipArchive(tampered, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntryFromFile(Path.Combine(factory.DataPath, "attachments", sha), sha);
            foreach (var (name, bytes) in new[] { (new string('a', 64), Png), ("../evil", Pdf) })
            {
                await using var entry = await zip.CreateEntry(name).OpenAsync(TestContext.Current.CancellationToken);
                await entry.WriteAsync(bytes, TestContext.Current.CancellationToken);
            }
        }
        var import = await otherAdmin.PostContentAsync("/api/backups/attachments", new ByteArrayContent(tampered.ToArray()));
        import.StatusCode.ShouldBe(HttpStatusCode.OK);
        var imported = await ApiClient.ReadJsonAsync(import);
        imported.GetProperty("imported").GetInt32().ShouldBe(1);
        imported.GetProperty("rejected").GetInt32().ShouldBe(2);
        imported.GetProperty("missing").GetInt32().ShouldBe(0);
        File.Exists(Path.Combine(other.DataPath, "evil")).ShouldBeFalse();

        var restored = (await otherAdmin.GetJsonAsync($"/api/transactions/{transaction}/attachments"))[0].GetProperty("id").GetGuid();
        (await (await otherAdmin.GetAsync($"/api/attachments/{restored}/content")).Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken))
            .ShouldBe(Pdf);

        // Réservé aux administrateurs.
        var member = await other.CreateUserClientAsync("membre", "Member");
        (await member.GetAsync("/api/backups/attachments")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
