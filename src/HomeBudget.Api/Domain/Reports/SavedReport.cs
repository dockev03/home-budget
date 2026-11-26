using HomeBudget.Api.Domain.Categories;

namespace HomeBudget.Api.Domain.Reports;

/// <summary>Regroupement d'un rapport personnalisé (G8).</summary>
public enum ReportGroupBy
{
    /// <summary>Par catégorie principale (sous-catégories comprises).</summary>
    Category,

    /// <summary>Par catégorie la plus fine (sous-catégorie s'il y en a une).</summary>
    Subcategory,

    Payee,

    /// <summary>Par tag : une ligne à plusieurs tags compte dans chacun.</summary>
    Tag,

    /// <summary>Par période budgétaire (R1).</summary>
    Period,

    Account,
}

/// <summary>Plage de dates d'un rapport, calculée selon les périodes budgétaires (R1) au moment de l'exécuter.</summary>
public enum ReportRange
{
    /// <summary>Dates saisies (From et To).</summary>
    Custom,

    CurrentPeriod,

    PreviousPeriod,

    /// <summary>Les 3 dernières périodes, celle en cours comprise.</summary>
    Last3Periods,

    /// <summary>Les 12 dernières périodes, celle en cours comprise.</summary>
    Last12Periods,

    /// <summary>Les 12 périodes qui portent le nom de l'année en cours.</summary>
    CurrentYear,

    PreviousYear,
}

/// <summary>
/// Rapport personnalisé enregistré (G8) : filtres, regroupement et plage. Commun au foyer ; un filtre qui désigne un
/// compte privé d'un autre membre ne lui montre rien (R8).
/// </summary>
public sealed class SavedReport : AuditableEntity
{
    public const int MaxNameLength = 80;

    public const int MaxFilterItems = 100;

    public required string Name { get; set; }

    public ReportGroupBy GroupBy { get; set; }

    public ReportRange Range { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public List<Guid> AccountIds { get; set; } = [];

    /// <summary>Catégories retenues ; une catégorie principale inclut ses sous-catégories.</summary>
    public List<Guid> CategoryIds { get; set; } = [];

    public List<Guid> PayeeIds { get; set; } = [];

    public List<Guid> TagIds { get; set; } = [];

    /// <summary>Types de catégorie retenus ; vide : tous, lignes sans catégorie comprises.</summary>
    public List<CategoryKind> Kinds { get; set; } = [];

    /// <summary>Inclure les lignes de virement sans catégorie (exclues par défaut, R5).</summary>
    public bool IncludeTransfers { get; set; }
}
