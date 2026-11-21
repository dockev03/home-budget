using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Domain.Users;

public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public AppUser(string userName) : this()
    {
        UserName = userName;
    }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
