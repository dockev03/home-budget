using System.Globalization;

namespace HomeBudget.Api.Features.Notifications;

/// <summary>Mise en forme française des montants et des dates, indépendante de la culture du serveur (L7).</summary>
public static class NotificationText
{
    private static readonly NumberFormatInfo French = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = " ",
        NegativeSign = "−",
    };

    /// <summary>« 1 234,56 € » (espace fine insécable), « −12,00 € ».</summary>
    public static string Money(decimal amount) => amount.ToString("#,##0.00", French) + " €";

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string DayMonth(DateOnly date) => date.ToString("dd/MM", CultureInfo.InvariantCulture);

    public static string Percent(decimal percent) =>
        Math.Round(percent, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + " %";

    /// <summary>« Loyer, EDF et 2 autres ».</summary>
    public static string List(IReadOnlyList<string> items, int max = 3)
    {
        if (items.Count <= max)
        {
            return items.Count switch
            {
                0 => string.Empty,
                1 => items[0],
                _ => string.Join(", ", items.Take(items.Count - 1)) + " et " + items[^1],
            };
        }
        var rest = items.Count - max;
        return string.Join(", ", items.Take(max)) + $" et {rest} autre{(rest > 1 ? "s" : string.Empty)}";
    }
}
