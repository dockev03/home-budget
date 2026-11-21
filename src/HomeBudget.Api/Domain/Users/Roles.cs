namespace HomeBudget.Api.Domain.Users;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Member = "Member";
    public const string ReadOnly = "ReadOnly";

    public static readonly IReadOnlyList<string> All = [Admin, Member, ReadOnly];
}
