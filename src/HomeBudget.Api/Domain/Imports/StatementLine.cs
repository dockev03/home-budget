namespace HomeBudget.Api.Domain.Imports;

public enum ImportFormat
{
    Csv,
    Ofx,
    Qif,
}

/// <summary>
/// Opération lue dans un relevé bancaire, avant tout rapprochement.
/// </summary>
/// <param name="Line">Numéro de ligne du fichier (CSV, QIF) ou rang de l'opération (OFX), à partir de 1.</param>
/// <param name="Label">Libellé brut, espaces superflus retirés.</param>
/// <param name="ExternalId">Identifiant unique fourni par la banque (FITID de l'OFX), s'il existe.</param>
public sealed record StatementLine(int Line, DateOnly Date, decimal Amount, string Label, string? ExternalId);

/// <summary>Ligne du fichier qui n'a pas pu être lue.</summary>
public sealed record StatementError(int Line, string Message);

public sealed record StatementReadResult(ImportFormat Format, IReadOnlyList<StatementLine> Lines, IReadOnlyList<StatementError> Errors);
