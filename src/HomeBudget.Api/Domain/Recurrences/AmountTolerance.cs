namespace HomeBudget.Api.Domain.Recurrences;

public enum ToleranceUnit
{
    /// <summary>Écart admis en euros.</summary>
    Amount,

    /// <summary>Écart admis en pourcentage du montant prévu.</summary>
    Percent,
}

/// <summary>
/// Montant estimé d'une récurrence (E7) : une opération réelle correspond à l'échéance si elle est de même sens
/// et s'écarte du montant prévu d'au plus la tolérance (en euros ou en pourcentage).
/// </summary>
public readonly record struct AmountTolerance(decimal Value, ToleranceUnit Unit)
{
    public const decimal MaxPercent = 100m;

    public const decimal MaxAmount = 999_999_999m;

    /// <summary>Écart de date admis entre une opération importée et l'échéance qu'elle remplace (E6).</summary>
    public const int MatchWindowDays = 5;

    /// <summary>Écart maximal admis autour du montant <paramref name="expected"/>.</summary>
    public decimal MarginFor(decimal expected) => Unit == ToleranceUnit.Percent
        ? decimal.Round(Math.Abs(expected) * Value / 100m, 2, MidpointRounding.AwayFromZero)
        : Value;

    public bool Accepts(decimal expected, decimal actual) =>
        Math.Sign(expected) == Math.Sign(actual) && Math.Abs(actual - expected) <= MarginFor(expected);

    public bool IsValid => Value >= 0 && decimal.Round(Value, 2) == Value
        && (Unit == ToleranceUnit.Percent ? Value <= MaxPercent : Value <= MaxAmount)
        && Enum.IsDefined(Unit);
}
