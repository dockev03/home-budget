using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomeBudget.Api.Infrastructure.Data;

/// <summary>
/// Stocke les montants <see cref="decimal"/> (2 décimales) en centimes entiers.
/// SQLite ne sait pas agréger des décimaux ; les entiers garantissent des sommes exactes.
/// </summary>
public sealed class CentsConverter() : ValueConverter<decimal, long>(
    value => ToCents(value),
    cents => FromCents(cents))
{
    public static long ToCents(decimal value) =>
        (long)decimal.Round(value * 100m, 0, MidpointRounding.AwayFromZero);

    public static decimal FromCents(long cents) => cents / 100m;
}
