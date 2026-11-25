namespace HomeBudget.Api.Domain.Imports;

/// <summary>
/// Correspondance des colonnes d'un relevé CSV ou TSV (H1), enregistrée pour être réutilisée
/// (par exemple « La Banque Postale »). Les colonnes sont numérotées à partir de 1.
/// </summary>
/// <remarks>
/// Les lignes situées avant la première date lisible (en-tête du relevé, titres des colonnes) sont ignorées :
/// il n'y a pas de nombre de lignes à sauter à régler. L'encodage est détecté (UTF-8, sinon Windows-1252).
/// </remarks>
public sealed class ImportProfile : AuditableEntity
{
    public required string Name { get; set; }

    /// <summary>Séparateur de colonnes : point-virgule, virgule, tabulation (TSV) ou barre verticale.</summary>
    public char Delimiter { get; set; } = ';';

    /// <summary>Format des dates, parmi <see cref="CsvOptions.DateFormats"/>.</summary>
    public string DateFormat { get; set; } = "dd/MM/yyyy";

    public char DecimalSeparator { get; set; } = ',';

    public int DateColumn { get; set; }

    public int LabelColumn { get; set; }

    /// <summary>Colonne du montant signé ; sinon, les colonnes débit et crédit.</summary>
    public int? AmountColumn { get; set; }

    public int? DebitColumn { get; set; }

    public int? CreditColumn { get; set; }

    public CsvOptions Options => new(Delimiter, DateFormat, DecimalSeparator, DateColumn, LabelColumn, AmountColumn, DebitColumn, CreditColumn);

    /// <summary>Profil créé à l'installation, d'après l'export CSV de La Banque Postale.</summary>
    public static readonly Guid BanquePostaleId = new("019ab7a0-0000-7000-8000-000000000001");
}

/// <summary>Réglages de lecture d'un fichier CSV ou TSV ; les colonnes sont numérotées à partir de 1.</summary>
public sealed record CsvOptions(
    char Delimiter,
    string DateFormat,
    char DecimalSeparator,
    int DateColumn,
    int LabelColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn)
{
    public const int MaxColumn = 50;

    public static readonly IReadOnlyList<string> DateFormats = ["dd/MM/yyyy", "dd/MM/yy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy", "MM/dd/yyyy"];

    public static readonly IReadOnlyList<char> Delimiters = [';', ',', '\t', '|'];

    public static readonly IReadOnlyList<char> DecimalSeparators = [',', '.'];

    /// <summary>Erreurs de réglage, par champ ; vide si les réglages sont utilisables.</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (!Delimiters.Contains(Delimiter))
        {
            errors["delimiter"] = ["Séparateur de colonnes non pris en charge."];
        }
        if (!DateFormats.Contains(DateFormat))
        {
            errors["dateFormat"] = ["Format de date non pris en charge."];
        }
        if (!DecimalSeparators.Contains(DecimalSeparator))
        {
            errors["decimalSeparator"] = ["Séparateur décimal non pris en charge."];
        }
        if (DecimalSeparator == Delimiter)
        {
            errors["decimalSeparator"] = ["Le séparateur décimal doit différer du séparateur de colonnes."];
        }
        var columns = new List<int> { DateColumn, LabelColumn };
        if (AmountColumn is { } amount)
        {
            columns.Add(amount);
            if (DebitColumn is not null || CreditColumn is not null)
            {
                errors["amountColumn"] = ["Indiquez soit la colonne du montant, soit les colonnes débit et crédit."];
            }
        }
        else if (DebitColumn is { } debit && CreditColumn is { } credit)
        {
            columns.Add(debit);
            columns.Add(credit);
        }
        else
        {
            errors["amountColumn"] = ["Indiquez la colonne du montant, ou les colonnes débit et crédit."];
        }
        if (columns.Any(c => c is < 1 or > MaxColumn))
        {
            errors["columns"] = [$"Les colonnes sont numérotées de 1 à {MaxColumn}."];
        }
        else if (columns.Distinct().Count() != columns.Count)
        {
            errors["columns"] = ["Chaque colonne ne peut servir qu'une fois."];
        }
        return errors;
    }
}
