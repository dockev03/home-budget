namespace HomeBudget.Api.Domain.Dashboard;

/// <summary>
/// Disposition de l'accueil choisie par un utilisateur : ordre des blocs et blocs masqués. Sans ligne, la disposition
/// par défaut s'applique. Un bloc inconnu (retiré d'une version) est ignoré ; un bloc absent (ajouté par une version
/// plus récente) prend place à la fin, visible.
/// </summary>
public sealed class DashboardLayout : Entity
{
    /// <summary>Plafond des identifiants reçus, pour borner la requête.</summary>
    public const int MaxBlocks = 32;

    public const int MaxBlockLength = 32;

    public const string KeyFigures = "keyFigures";
    public const string Forecast = "forecast";
    public const string Charts = "charts";
    public const string Comparison = "comparison";
    public const string History = "history";
    public const string TopPayees = "topPayees";
    public const string Wealth = "wealth";
    public const string Accounts = "accounts";

    /// <summary>Blocs de l'accueil, dans l'ordre par défaut.</summary>
    public static readonly IReadOnlyList<string> DefaultOrder =
        [KeyFigures, Forecast, Charts, Comparison, History, TopPayees, Wealth, Accounts];

    public Guid UserId { get; set; }

    public List<string> Order { get; set; } = [];

    public List<string> Hidden { get; set; } = [];

    /// <summary>
    /// Ramène un ordre et une liste de blocs masqués aux blocs connus : sans doublon, blocs manquants ajoutés à la fin
    /// dans l'ordre par défaut, blocs masqués rangés dans l'ordre d'affichage.
    /// </summary>
    public static (List<string> Order, List<string> Hidden) Normalize(IEnumerable<string>? order, IEnumerable<string>? hidden)
    {
        var known = DefaultOrder.ToHashSet(StringComparer.Ordinal);
        var result = (order ?? []).Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        result.AddRange(DefaultOrder.Where(b => !result.Contains(b, StringComparer.Ordinal)));
        var masked = (hidden ?? []).Where(known.Contains).ToHashSet(StringComparer.Ordinal);
        return (result, [.. result.Where(masked.Contains)]);
    }
}
