using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HomeBudget.Api.Infrastructure.Data;

/// <summary>
/// Stocke un décimal à <c>decimals</c> décimales en entier (comme <see cref="CentsConverter"/> pour les montants),
/// pour garder des valeurs exactes dans SQLite : un taux à 4 décimales est stocké en dix-millièmes.
/// </summary>
public sealed class ScaledDecimalConverter(int decimals) : ValueConverter<decimal, long>(
    value => (long)decimal.Round(value * Scale(decimals), 0, MidpointRounding.AwayFromZero),
    stored => stored / Scale(decimals))
{
    private static decimal Scale(int decimals)
    {
        var scale = 1m;
        for (var i = 0; i < decimals; i++)
        {
            scale *= 10m;
        }
        return scale;
    }
}
