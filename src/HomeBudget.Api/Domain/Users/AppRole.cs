using Microsoft.AspNetCore.Identity;

namespace HomeBudget.Api.Domain.Users;

public sealed class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
        Id = Guid.CreateVersion7();
    }

    public AppRole(string roleName) : this()
    {
        Name = roleName;
    }
}
