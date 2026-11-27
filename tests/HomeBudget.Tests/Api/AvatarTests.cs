using System.Net;
using System.Net.Http.Headers;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

/// <summary>Photo de profil de l'utilisateur connecté (pastille et menu du compte).</summary>
public sealed class AvatarTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7];

    private static Task<HttpResponseMessage> UploadAsync(ApiClient client, byte[] content, string type = "application/octet-stream")
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(type);
        return client.SendContentAsync(HttpMethod.Put, "/api/account/avatar", body);
    }

    [Fact]
    public async Task Avatar_is_uploaded_replaced_served_and_removed()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        (await admin.GetJsonAsync("/api/auth/me")).GetProperty("avatarUpdatedAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await admin.GetAsync("/api/account/avatar")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Le type annoncé est ignoré : seuls les premiers octets comptent.
        var upload = await UploadAsync(admin, Png, "image/jpeg");
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        var first = (await ApiClient.ReadJsonAsync(upload)).GetProperty("avatarUpdatedAt").GetDateTimeOffset();
        (await admin.GetJsonAsync("/api/auth/me")).GetProperty("avatarUpdatedAt").GetDateTimeOffset().ShouldBe(first);

        var served = await admin.GetAsync($"/api/account/avatar?v={Uri.EscapeDataString(first.ToString("O"))}");
        served.StatusCode.ShouldBe(HttpStatusCode.OK);
        served.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await served.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(Png);

        // Une nouvelle photo remplace la précédente.
        (await UploadAsync(admin, Jpeg)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var replaced = await admin.GetAsync("/api/account/avatar");
        replaced.Content.Headers.ContentType!.MediaType.ShouldBe("image/jpeg");
        (await replaced.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(Jpeg);

        var removed = await admin.DeleteAsync("/api/account/avatar", null);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiClient.ReadJsonAsync(removed)).GetProperty("avatarUpdatedAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        (await admin.GetAsync("/api/account/avatar")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_or_oversized_files_are_refused()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();

        (await UploadAsync(admin, "%PDF-1.7\nfaux"u8.ToArray(), "image/png")).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(admin, "<svg></svg>"u8.ToArray(), "image/svg+xml")).StatusCode.ShouldBe(HttpStatusCode.UnsupportedMediaType);
        (await UploadAsync(admin, [])).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var tooLarge = new byte[300 * 1024];
        Png.CopyTo(tooLarge, 0);
        (await UploadAsync(admin, tooLarge)).StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);

        (await admin.GetJsonAsync("/api/auth/me")).GetProperty("avatarUpdatedAt").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Each_user_sees_only_their_own_avatar()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        // Un utilisateur en lecture seule peut aussi choisir sa photo : elle ne touche pas aux données du foyer.
        (await UploadAsync(reader, Jpeg)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync("/api/account/avatar")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reader.GetAsync("/api/account/avatar")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Avatar_requires_a_session()
    {
        await using var factory = new HomeBudgetFactory();
        await factory.CreateAdminClientAsync();
        var anonymous = factory.CreateApiClient();

        (await anonymous.GetAsync("/api/account/avatar")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await UploadAsync(anonymous, Png)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
