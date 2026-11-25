using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Imports;

namespace HomeBudget.Api.Features.Imports;

public static class ImportLimits
{
    /// <summary>Taille maximale d'un relevé (5 Mio).</summary>
    public const int MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>Longueur maximale du relevé encodé en Base64.</summary>
    public const int MaxContentChars = 7_000_000;

    /// <summary>Taille maximale d'une requête d'import (relevé en Base64 et choix des lignes).</summary>
    public const long MaxRequestBytes = 8L * 1024 * 1024;
}

/// <param name="Content">Contenu du fichier, encodé en Base64.</param>
/// <param name="ProfileId">Profil de lecture, obligatoire pour un CSV ou un TSV.</param>
public sealed record ImportFileRequest(
    Guid AccountId,
    [property: Required, StringLength(200, MinimumLength = 1)] string FileName,
    [property: Required, StringLength(ImportLimits.MaxContentChars, MinimumLength = 1)] string Content,
    Guid? ProfileId);

/// <param name="Line">Numéro de ligne (ou rang de l'opération) donné par l'aperçu.</param>
/// <param name="AcceptMatch">Remplacer l'échéance proposée par cette opération (E6) ; sinon, opération distincte.</param>
public sealed record ImportLineChoice(int Line, bool AcceptMatch);

/// <param name="Lines">Opérations à importer ; les doublons certains sont écartés même s'ils figurent ici.</param>
public sealed record ImportCommitRequest(
    Guid AccountId,
    [property: Required, StringLength(200, MinimumLength = 1)] string FileName,
    [property: Required, StringLength(ImportLimits.MaxContentChars, MinimumLength = 1)] string Content,
    Guid? ProfileId,
    [property: Required, MaxLength(StatementReader.MaxLines)] IReadOnlyList<ImportLineChoice> Lines);

public enum ImportLineStatus
{
    /// <summary>Opération nouvelle.</summary>
    New,

    /// <summary>Déjà importée (même FITID ou même empreinte) : elle sera écartée.</summary>
    Duplicate,

    /// <summary>Une opération du compte a le même montant à quelques jours près : non cochée par défaut.</summary>
    ProbableDuplicate,

    /// <summary>Correspond à une échéance de récurrence, qu'elle remplacera (E6).</summary>
    Match,
}

/// <param name="Date">Date prévue de l'échéance (ou de l'opération déjà générée).</param>
/// <param name="Amount">Montant prévu.</param>
public sealed record ImportOccurrenceDto(Guid RecurrenceId, string Label, DateOnly Date, decimal Amount, Guid? CategoryId);

/// <param name="DuplicateOfId">Opération existante, pour un doublon certain ou probable.</param>
public sealed record ImportPreviewLineDto(
    int Line,
    DateOnly Date,
    decimal Amount,
    string Label,
    ImportLineStatus Status,
    Guid? DuplicateOfId,
    string? DuplicateOfLabel,
    DateOnly? DuplicateOfDate,
    ImportOccurrenceDto? Occurrence);

public sealed record ImportPreviewDto(
    ImportFormat Format, string? ProfileName, IReadOnlyList<ImportPreviewLineDto> Lines, IReadOnlyList<StatementError> Errors);

/// <param name="Created">Opérations créées ou rapprochées.</param>
/// <param name="Matched">Dont opérations ayant remplacé une échéance.</param>
/// <param name="Duplicates">Opérations déjà présentes, écartées.</param>
public sealed record ImportResultDto(Guid BatchId, int Created, int Matched, int Duplicates);

public sealed record ImportBatchDto(
    Guid Id, Guid AccountId, string FileName, ImportFormat Format, int Created, int Matched, int Duplicates, DateTimeOffset ImportedAt);

/// <param name="Delimiter">Séparateur de colonnes : « ; », « , », « | » ou tabulation (« \t »).</param>
/// <param name="DateFormat">Format des dates, par exemple « dd/MM/yyyy ».</param>
/// <param name="DecimalSeparator">« , » ou « . ».</param>
public sealed record ImportProfileRequest(
    [property: Required, StringLength(60, MinimumLength = 1)] string Name,
    [property: Required(AllowEmptyStrings = true), StringLength(1, MinimumLength = 1)] string Delimiter,
    [property: Required, StringLength(16, MinimumLength = 1)] string DateFormat,
    [property: Required, StringLength(1, MinimumLength = 1)] string DecimalSeparator,
    int DateColumn,
    int LabelColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn);

public sealed record ImportProfileDto(
    Guid Id,
    string Name,
    string Delimiter,
    string DateFormat,
    string DecimalSeparator,
    int DateColumn,
    int LabelColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    int Version);
