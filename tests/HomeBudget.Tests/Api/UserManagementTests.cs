using System.Net;
using System.Text.Json;

using HomeBudget.Api.Domain.Users;
using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Utilisateurs, invitations, rôles et mot de passe (K1, K5), et journal d'audit (N6).</summary>
public sealed class UserManagementTests
{
    private const string MemberPassword = "un mot de passe assez long";

    private static async Task<string> InviteAsync(ApiClient admin, string userName, string role = "Member", string? displayName = null)
    {
        var created = await admin.CreateAsync("/api/invitations", new { userName, displayName, role });
        return created.GetProperty("token").GetString()!;
    }

    private static async Task<ApiClient> AcceptAsync(HomeBudgetFactory factory, string token)
    {
        var client = factory.CreateApiClient();
        var response = await client.PostAsync("/api/invitations/accept", new { token, password = MemberPassword });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await client.RefreshCsrfAsync();
        return client;
    }

    private static async Task<ApiClient> LoginAsync(HomeBudgetFactory factory, string userName, string password)
    {
        var client = factory.CreateApiClient();
        var response = await client.PostAsync("/api/auth/login", new { userName, password });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await client.RefreshCsrfAsync();
        return client;
    }

    private static async Task<HttpStatusCode> LoginStatusAsync(HomeBudgetFactory factory, string userName, string password) =>
        (await factory.CreateApiClient().PostAsync("/api/auth/login", new { userName, password })).StatusCode;

    private static async Task<JsonElement> UserAsync(ApiClient admin, string userName) =>
        (await admin.GetJsonAsync("/api/users")).EnumerateArray().Single(u => u.GetProperty("userName").GetString() == userName);

    private static async Task<List<string>> AuditActionsAsync(ApiClient admin) =>
        [.. (await admin.GetJsonAsync("/api/audit?pageSize=200")).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("action").GetString()!)];

    [Fact]
    public async Task An_invited_member_chooses_a_password_then_must_enable_two_factor()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var token = await InviteAsync(admin, "alice", displayName: "Alice");
        (await admin.GetJsonAsync("/api/invitations")).GetArrayLength().ShouldBe(1);

        var anonymous = factory.CreateApiClient();
        var preview = await ApiClient.ReadJsonAsync(await anonymous.PostAsync("/api/invitations/lookup", new { token }));
        preview.GetProperty("userName").GetString().ShouldBe("alice");
        preview.GetProperty("role").GetString().ShouldBe("Member");
        // Mot de passe trop court : refusé, l'invitation reste valable.
        (await anonymous.PostAsync("/api/invitations/accept", new { token, password = "court" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var alice = await AcceptAsync(factory, token);

        var me = await alice.GetJsonAsync("/api/auth/me");
        me.GetProperty("displayName").GetString().ShouldBe("Alice");
        me.GetProperty("twoFactorRequired").GetBoolean().ShouldBeTrue();
        // Tant que la double authentification n'est pas active, l'application reste fermée.
        var blocked = await alice.GetAsync("/api/accounts");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApiClient.ReadJsonAsync(blocked)).GetProperty("type").GetString().ShouldBe("urn:home-budget:two-factor-required");

        var setup = await ApiClient.ReadJsonAsync(await alice.PostAsync("/api/account/two-factor/setup"));
        var key = setup.GetProperty("sharedKey").GetString()!;
        (await alice.PostAsync("/api/account/two-factor/enable", new { code = Totp.Compute(key) })).StatusCode.ShouldBe(HttpStatusCode.OK);
        await alice.RefreshCsrfAsync();
        (await alice.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.OK);
        // Imposée : elle ne peut pas être désactivée.
        (await alice.PostAsync("/api/account/two-factor/disable", new { password = MemberPassword })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Le lien ne sert qu'une fois.
        (await anonymous.PostAsync("/api/invitations/accept", new { token, password = MemberPassword })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.GetJsonAsync("/api/invitations")).GetArrayLength().ShouldBe(0);
        var user = await UserAsync(admin, "alice");
        user.GetProperty("role").GetString().ShouldBe("Member");
        user.GetProperty("twoFactorEnabled").GetBoolean().ShouldBeTrue();
        (await AuditActionsAsync(admin)).ShouldContain("InvitationAccepted");
    }

    [Fact]
    public async Task Invitations_are_validated_revocable_and_reserved_to_admins()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var member = await factory.CreateUserClientAsync("bob", "Member");

        (await admin.PostAsync("/api/invitations", new { userName = "bob", role = "Member" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/invitations", new { userName = "élise", role = "Member" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/invitations", new { userName = "carol", role = "Owner" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await member.PostAsync("/api/invitations", new { userName = "carol", role = "Member" })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await member.GetAsync("/api/users/directory")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var token = await InviteAsync(admin, "carol");
        (await admin.PostAsync("/api/invitations", new { userName = "Carol", role = "Member" })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var invitation = (await admin.GetJsonAsync("/api/invitations"))[0];
        (await admin.DeleteAsync($"/api/invitations/{invitation.GetProperty("id").GetGuid()}", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var anonymous = factory.CreateApiClient();
        (await anonymous.PostAsync("/api/invitations/lookup", new { token })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.PostAsync("/api/invitations/accept", new { token, password = MemberPassword })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuditActionsAsync(admin)).ShouldBe(["InvitationRevoked", "UserInvited", "LoginSucceeded"], ignoreOrder: true);
    }

    [Fact]
    public void An_invitation_expires_after_48_hours_or_once_accepted()
    {
        var now = new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        var invitation = new Invitation { UserName = "alice", Role = "Member", TokenHash = Invitation.Hash("x"), ExpiresAt = now.Add(Invitation.Lifetime) };

        invitation.IsPending(now.AddHours(47)).ShouldBeTrue();
        invitation.IsPending(now.AddHours(48)).ShouldBeFalse();
        invitation.AcceptedAt = now;
        invitation.IsPending(now).ShouldBeFalse();
        Invitation.NewToken().ShouldNotBe(Invitation.NewToken());
        Invitation.Hash("abc").Length.ShouldBe(64);
    }

    [Fact]
    public async Task Changing_a_role_closes_the_sessions_and_applies_the_new_rights()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var bob = await factory.CreateUserClientAsync("bob", "Member");
        var bobId = (await UserAsync(admin, "bob")).GetProperty("id").GetGuid();

        var response = await admin.PutAsync($"/api/users/{bobId}/role", new { role = "ReadOnly" }, null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bob.GetAsync("/api/accounts")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var again = await LoginAsync(factory, "bob", TestData.AdminPassword);
        (await again.PostAsync("/api/accounts", new { name = "Livret", type = "Savings", initialBalance = 0m, initialDate = "2026-01-01" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var audit = (await admin.GetJsonAsync("/api/audit?action=RoleChanged")).GetProperty("items")[0];
        audit.GetProperty("details").GetString().ShouldBe("« bob » : membre → lecture seule.");
        audit.GetProperty("targetId").GetGuid().ShouldBe(bobId);
    }

    [Fact]
    public async Task The_last_active_admin_can_be_neither_demoted_nor_deactivated()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var adminId = (await admin.GetJsonAsync("/api/auth/me")).GetProperty("id").GetGuid();

        (await admin.PutAsync($"/api/users/{adminId}/role", new { role = "Member" }, null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutAsync($"/api/users/{adminId}/status", new { isActive = false }, null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutAsync($"/api/users/{adminId}/role", new { role = "Root" }, null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Avec un second administrateur, le premier peut redevenir membre.
        var second = await factory.CreateUserClientAsync("second", "Admin");
        var response = await admin.PutAsync($"/api/users/{adminId}/role", new { role = "Member" }, null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetJsonAsync("/api/auth/me")).GetProperty("roles").EnumerateArray().Single().GetString().ShouldBe("Member");
        (await second.GetAsync("/api/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_deactivated_user_loses_the_session_and_cannot_log_in()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var bob = await factory.CreateUserClientAsync("bob", "Member");
        var bobId = (await UserAsync(admin, "bob")).GetProperty("id").GetGuid();

        (await admin.PutAsync($"/api/users/{bobId}/status", new { isActive = false }, null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await bob.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginStatusAsync(factory, "bob", TestData.AdminPassword)).ShouldBe(HttpStatusCode.Unauthorized);
        var directory = (await admin.GetJsonAsync("/api/users/directory")).EnumerateArray()
            .Single(u => u.GetProperty("id").GetGuid() == bobId);
        directory.GetProperty("isActive").GetBoolean().ShouldBeFalse();

        (await admin.PutAsync($"/api/users/{bobId}/status", new { isActive = true }, null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LoginStatusAsync(factory, "bob", TestData.AdminPassword)).ShouldBe(HttpStatusCode.OK);
        (await AuditActionsAsync(admin)).ShouldContain("UserDeactivated");
        (await AuditActionsAsync(admin)).ShouldContain("UserReactivated");
    }

    [Fact]
    public async Task Changing_the_password_closes_the_other_sessions()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var otherDevice = await LoginAsync(factory, TestData.AdminUserName, TestData.AdminPassword);

        (await admin.PostAsync("/api/account/password", new { currentPassword = "mauvais", newPassword = MemberPassword }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/account/password", new { currentPassword = TestData.AdminPassword, newPassword = "court" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostAsync("/api/account/password", new { currentPassword = TestData.AdminPassword, newPassword = MemberPassword }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await otherDevice.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginStatusAsync(factory, TestData.AdminUserName, TestData.AdminPassword)).ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginStatusAsync(factory, TestData.AdminUserName, MemberPassword)).ShouldBe(HttpStatusCode.OK);
        (await AuditActionsAsync(admin)).ShouldContain("PasswordChanged");
    }

    [Fact]
    public async Task The_audit_log_records_logins_failures_exports_and_deletions()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var account = await admin.CreateAccountAsync("Courant");
        var transaction = await admin.CreateTransactionAsync(account, -5m);
        await admin.DeleteAsync($"/api/transactions/{transaction.GetProperty("id").GetGuid()}", transaction.Version());
        await admin.GetAsync("/api/exports/transactions?format=csv");
        await admin.GetAsync("/api/backups/export");
        (await LoginStatusAsync(factory, TestData.AdminUserName, "mauvais mot de passe")).ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginStatusAsync(factory, "inconnu", "mauvais mot de passe")).ShouldBe(HttpStatusCode.Unauthorized);
        await LoginAsync(factory, TestData.AdminUserName, TestData.AdminPassword);

        var actions = await AuditActionsAsync(admin);

        actions.ShouldContain("Deleted");
        actions.ShouldContain("Exported");
        actions.ShouldContain("BackupExported");
        actions.Count(a => a == "LoginFailed").ShouldBe(2);
        actions.Count(a => a == "LoginSucceeded").ShouldBe(1);
        var deleted = (await admin.GetJsonAsync("/api/audit?action=Deleted")).GetProperty("items")[0];
        deleted.GetProperty("target").GetString().ShouldBe("Transaction");
        deleted.GetProperty("userName").GetString().ShouldBe(TestData.AdminUserName);
        var unknown = (await admin.GetJsonAsync("/api/audit?action=LoginFailed")).GetProperty("items").EnumerateArray()
            .Single(e => e.GetProperty("userId").ValueKind == JsonValueKind.Null);
        unknown.GetProperty("userName").GetString().ShouldBe("inconnu");

        var page = await admin.GetJsonAsync("/api/audit?page=2&pageSize=2");
        page.GetProperty("items").GetArrayLength().ShouldBe(2);
        page.GetProperty("total").GetInt32().ShouldBe(actions.Count);
        (await admin.GetAsync("/api/audit?pageSize=500")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
