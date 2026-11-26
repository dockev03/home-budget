namespace HomeBudget.Api.Domain.Advances;

/// <summary>
/// Avance à recevoir (B12) : note de frais, avance professionnelle, participation du CSE… Les revenus reçus
/// y sont rattachés ; l'avance est soldée quand ils atteignent le montant attendu, ou quand elle est close à la main.
/// </summary>
public sealed class Advance : AuditableEntity
{
    public const int MaxLabelLength = 100;

    public required string Label { get; set; }

    /// <summary>Montant attendu, positif.</summary>
    public decimal ExpectedAmount { get; set; }

    /// <summary>Date de la dépense avancée ou de la demande.</summary>
    public DateOnly Date { get; set; }

    public string? Notes { get; set; }

    /// <summary>Soldée à la main, même si le reçu n'atteint pas le montant attendu (reste abandonné).</summary>
    public bool IsClosed { get; set; }

    public static bool IsSettled(decimal expected, decimal received, bool isClosed) => isClosed || received >= expected;
}
