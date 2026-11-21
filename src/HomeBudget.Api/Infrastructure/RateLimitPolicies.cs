namespace HomeBudget.Api.Infrastructure;

public static class RateLimitPolicies
{
    /// <summary>Endpoints d'authentification : limite par adresse IP (anti force brute).</summary>
    public const string Auth = "auth";
}
