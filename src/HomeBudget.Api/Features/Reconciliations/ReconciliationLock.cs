namespace HomeBudget.Api.Features.Reconciliations;

/// <summary>Verrouillage des opérations rapprochées (R7).</summary>
public static class ReconciliationLock
{
    public static IResult Problem() => TypedResults.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Opération rapprochée.",
        detail: "Elle figure sur un relevé rapproché : annulez le rapprochement du compte pour la modifier.");
}
