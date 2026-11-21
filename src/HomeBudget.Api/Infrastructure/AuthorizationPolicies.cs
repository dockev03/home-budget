namespace HomeBudget.Api.Infrastructure;

public static class AuthorizationPolicies
{
    public const string Admin = "admin";

    /// <summary>Création, modification et suppression de données (administrateur ou membre, pas lecture seule).</summary>
    public const string Write = "write";
}
