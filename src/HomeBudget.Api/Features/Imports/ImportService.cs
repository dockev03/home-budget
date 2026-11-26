using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Features.Recurrences;
using HomeBudget.Api.Features.Rules;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Imports;

using Account = HomeBudget.Api.Domain.Accounts.Account;

/// <summary>Échéance de récurrence qu'une opération importée peut remplacer (E6).</summary>
/// <param name="OccurrenceId">Échéance déjà traitée (à valider ou générée) ; null si elle n'a pas encore été traitée.</param>
/// <param name="TransactionId">Opération générée par l'échéance, mise à jour par l'import.</param>
/// <param name="Expected">Montant attendu (celui de l'opération générée, s'il y en a une).</param>
public sealed record OccurrenceCandidate(
    Recurrence Recurrence, int Index, DateOnly ScheduledDate, DateOnly Date, decimal Expected, Guid? OccurrenceId, Guid? TransactionId)
{
    public string Key => OccurrenceId?.ToString() ?? $"{Recurrence.Id}|{ScheduledDate:yyyy-MM-dd}";
}

/// <summary>Opération existante du compte, telle que vue par l'analyse.</summary>
public sealed record ExistingTransaction(Guid Id, DateOnly Date, decimal Amount, string? Label, string? ImportHash, bool IsSimple);

/// <param name="Suggestion">Tiers et catégorie donnés par une règle apprise (I2, I3), appliqués à l'opération créée.</param>
public sealed record AnalyzedLine(
    FingerprintedLine Source, ImportLineStatus Status, ExistingTransaction? Duplicate, OccurrenceCandidate? Match, LabelSuggestion? Suggestion = null);

public sealed record ImportAnalysis(
    Account Account, string FileName, ImportFormat Format, ImportProfile? Profile, IReadOnlyList<AnalyzedLine> Lines, IReadOnlyList<StatementError> Errors);

/// <summary>Deux imports simultanés du même relevé : le second se heurte à l'unicité des empreintes.</summary>
public sealed class ImportConflictException(Exception inner) : Exception("Import concurrent.", inner);

/// <summary>
/// Import d'un relevé (H1 à H3, E6) : lecture, repérage des doublons et des échéances correspondantes,
/// puis création des opérations choisies dans une seule transaction.
/// </summary>
public sealed class ImportService(AppDbContext db, RecurrenceGenerator generator)
{
    /// <summary>Écart de date admis pour signaler un doublon probable.</summary>
    public const int ProbableDuplicateDays = 3;

    public async Task<(ImportAnalysis? Analysis, Dictionary<string, string[]> Errors)> AnalyzeAsync(
        Guid accountId, string fileName, string content, Guid? profileId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            errors["accountId"] = ["Compte introuvable."];
        }
        else if (account.IsArchived)
        {
            errors["accountId"] = ["Ce compte est archivé."];
        }

        var bytes = new byte[ImportLimits.MaxFileBytes];
        if (!Convert.TryFromBase64String(content, bytes, out var length))
        {
            errors["content"] = content.Length > ImportLimits.MaxFileBytes / 3 * 4
                ? [$"Fichier trop volumineux ({ImportLimits.MaxFileBytes / (1024 * 1024)} Mio au plus)."]
                : ["Contenu du fichier illisible."];
        }
        else if (length == 0)
        {
            errors["content"] = ["Le fichier est vide."];
        }
        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var text = StatementText.Decode(bytes.AsSpan(0, length));
        var format = StatementText.DetectFormat(fileName, text);
        ImportProfile? profile = null;
        if (format == ImportFormat.Csv)
        {
            profile = profileId is { } id ? await db.ImportProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) : null;
            if (profile is null)
            {
                errors["profileId"] = [profileId is null ? "Choisissez le profil de lecture de ce fichier CSV." : "Profil introuvable."];
                return (null, errors);
            }
        }

        var read = StatementReader.Read(format, text, profile?.Options);
        if (read.Lines.Count > StatementReader.MaxLines)
        {
            errors["content"] = [$"Fichier trop long ({StatementReader.MaxLines} opérations au plus)."];
            return (null, errors);
        }

        var readErrors = read.Errors.ToList();
        var lines = new List<FingerprintedLine>();
        foreach (var line in StatementReader.Fingerprint(read.Lines))
        {
            if (line.Line.Date < account!.InitialDate)
            {
                readErrors.Add(new(line.Line.Line, $"Date antérieure au solde initial du compte ({account.InitialDate:dd/MM/yyyy}) : opération ignorée."));
            }
            else
            {
                lines.Add(line);
            }
        }
        var analyzed = await AnalyzeLinesAsync(account!.Id, lines, ct);
        return (new ImportAnalysis(account!, fileName.Trim(), format, profile, await SuggestAsync(analyzed, ct), readErrors), errors);
    }

    /// <summary>Tiers et catégorie des règles apprises, pour les opérations qui ne sont pas des doublons certains.</summary>
    private async Task<List<AnalyzedLine>> SuggestAsync(List<AnalyzedLine> lines, CancellationToken ct)
    {
        var open = lines.Where(l => l.Status != ImportLineStatus.Duplicate).ToList();
        var suggestions = await LabelRules.SuggestByLabelAsync(db, open.Select(l => l.Source.Line.Label), ct);
        if (suggestions.Count == 0)
        {
            return lines;
        }
        return
        [
            .. lines.Select(l => l.Status != ImportLineStatus.Duplicate
                && suggestions.TryGetValue(LabelKey.From(l.Source.Line.Label), out var suggestion)
                    ? l with { Suggestion = suggestion }
                    : l),
        ];
    }

    private async Task<List<AnalyzedLine>> AnalyzeLinesAsync(Guid accountId, List<FingerprintedLine> lines, CancellationToken ct)
    {
        if (lines.Count == 0)
        {
            return [];
        }
        var window = Math.Max(AmountTolerance.MatchWindowDays, ProbableDuplicateDays);
        var from = lines.Min(l => l.Line.Date).AddDays(-window);
        var to = lines.Max(l => l.Line.Date).AddDays(window);

        var hashes = lines.Select(l => l.Hash).ToList();
        var byHash = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.ImportHash != null && hashes.Contains(t.ImportHash))
            .Select(t => new ExistingTransaction(t.Id, t.Date, t.Amount, t.Label, t.ImportHash, t.TransferId == null && t.ReconciliationId == null && t.Lines.Count == 1))
            .ToDictionaryAsync(t => t.ImportHash!, ct);
        var nearby = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == accountId && t.Date >= from && t.Date <= to)
            .Select(t => new ExistingTransaction(t.Id, t.Date, t.Amount, t.Label, t.ImportHash, t.TransferId == null && t.ReconciliationId == null && t.Lines.Count == 1))
            .ToListAsync(ct);
        var nearbyById = nearby.ToDictionary(t => t.Id);
        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.AccountId == accountId && r.ToAccountId == null)
            .ToListAsync(ct);

        // Une opération existante ou une échéance ne sert qu'une fois.
        var usedTransactions = new HashSet<Guid>(byHash.Values.Select(t => t.Id));
        var usedOccurrences = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<AnalyzedLine>(lines.Count);
        foreach (var line in lines)
        {
            if (byHash.TryGetValue(line.Hash, out var duplicate))
            {
                result.Add(new(line, ImportLineStatus.Duplicate, duplicate, null));
                continue;
            }
            var match = Candidates(line.Line, recurrences, nearbyById)
                .Where(c => !usedOccurrences.Contains(c.Key) && (c.TransactionId is not { } t || !usedTransactions.Contains(t)))
                .OrderBy(c => Math.Abs(c.Date.DayNumber - line.Line.Date.DayNumber))
                .ThenBy(c => Math.Abs(c.Expected - line.Line.Amount))
                .FirstOrDefault();
            if (match is not null)
            {
                usedOccurrences.Add(match.Key);
                if (match.TransactionId is { } transactionId)
                {
                    usedTransactions.Add(transactionId);
                }
                result.Add(new(line, ImportLineStatus.Match, null, match));
                continue;
            }
            var probable = nearby
                .Where(t => !usedTransactions.Contains(t.Id) && t.Amount == line.Line.Amount
                    && Math.Abs(t.Date.DayNumber - line.Line.Date.DayNumber) <= ProbableDuplicateDays)
                .OrderBy(t => Math.Abs(t.Date.DayNumber - line.Line.Date.DayNumber))
                .FirstOrDefault();
            if (probable is not null)
            {
                usedTransactions.Add(probable.Id);
                result.Add(new(line, ImportLineStatus.ProbableDuplicate, probable, null));
                continue;
            }
            result.Add(new(line, ImportLineStatus.New, null, null));
        }
        return result;
    }

    /// <summary>
    /// Échéances proches de l'opération (± <see cref="AmountTolerance.MatchWindowDays"/> jours) dont le montant est
    /// dans la tolérance : à valider, déjà générées (opération non encore rapprochée d'un relevé), ou à venir.
    /// </summary>
    private static IEnumerable<OccurrenceCandidate> Candidates(
        StatementLine line, List<Recurrence> recurrences, Dictionary<Guid, ExistingTransaction> nearby)
    {
        var window = AmountTolerance.MatchWindowDays;
        bool Near(DateOnly date) => Math.Abs(date.DayNumber - line.Date.DayNumber) <= window;
        foreach (var recurrence in recurrences)
        {
            var tolerance = recurrence.AmountTolerance;
            foreach (var o in recurrence.Occurrences)
            {
                if (o.Status == OccurrenceStatus.Pending && Near(o.DueDate) && tolerance.Accepts(recurrence.Amount, line.Amount))
                {
                    yield return new(recurrence, o.Index, o.ScheduledDate, o.DueDate, recurrence.Amount, o.Id, null);
                }
                else if (o.Status == OccurrenceStatus.Generated && o.TransactionId is { } id
                    && nearby.TryGetValue(id, out var generated) && generated is { ImportHash: null, IsSimple: true }
                    && Near(generated.Date) && tolerance.Accepts(generated.Amount, line.Amount))
                {
                    yield return new(recurrence, o.Index, o.ScheduledDate, generated.Date, generated.Amount, o.Id, id);
                }
            }
            var known = recurrence.Occurrences.Select(o => o.ScheduledDate).ToHashSet();
            var start = line.Date.AddDays(-window);
            foreach (var s in recurrence.Rule.Between(start < recurrence.GenerateFrom ? recurrence.GenerateFrom : start, line.Date.AddDays(window)))
            {
                if (!known.Contains(s.Date) && tolerance.Accepts(recurrence.Amount, line.Amount))
                {
                    yield return new(recurrence, s.Index, s.Date, s.Date, recurrence.Amount, null, null);
                }
            }
        }
    }

    /// <summary>
    /// Crée les opérations choisies (<paramref name="choices"/> : numéro de ligne → remplacer l'échéance proposée)
    /// dans une seule transaction. Les doublons certains sont toujours écartés.
    /// </summary>
    public async Task<ImportBatch> CommitAsync(ImportAnalysis analysis, IReadOnlyDictionary<int, bool> choices, CancellationToken ct)
    {
        var batch = new ImportBatch
        {
            AccountId = analysis.Account.Id,
            FileName = analysis.FileName.Length > 200 ? analysis.FileName[..200] : analysis.FileName,
            Format = analysis.Format,
            Duplicates = analysis.Lines.Count(l => l.Status == ImportLineStatus.Duplicate),
        };
        db.ImportBatches.Add(batch);
        var accounts = new Dictionary<Guid, Account> { [analysis.Account.Id] = analysis.Account };

        foreach (var line in analysis.Lines)
        {
            if (line.Status == ImportLineStatus.Duplicate || !choices.TryGetValue(line.Source.Line.Line, out var acceptMatch))
            {
                continue;
            }
            var transaction = acceptMatch && line.Match is { } match
                ? await ApplyMatchAsync(match, line.Source.Line, accounts, ct)
                : null;
            if (transaction is not null)
            {
                batch.Matched++;
            }
            else
            {
                // Règle apprise (I3) : le tiers remplace le libellé bancaire à l'affichage, la catégorie est posée.
                transaction = new Transaction
                {
                    AccountId = analysis.Account.Id,
                    Date = line.Source.Line.Date,
                    Label = TransactionLabel(line.Source.Line.Label),
                    PayeeId = line.Suggestion?.PayeeId,
                };
                transaction.SetLines([new TransactionLine { Amount = line.Source.Line.Amount, CategoryId = line.Suggestion?.CategoryId }]);
                db.Transactions.Add(transaction);
            }
            transaction.RawLabel = string.IsNullOrEmpty(line.Source.Line.Label) ? null : line.Source.Line.Label;
            transaction.ImportHash = line.Source.Hash;
            transaction.ImportBatchId = batch.Id;
            // L'opération figure sur le relevé de la banque : elle est pointée.
            transaction.IsCleared = true;
            batch.Created++;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e)
        {
            db.ChangeTracker.Clear();
            throw new ImportConflictException(e);
        }
        return batch;
    }

    /// <summary>
    /// Remplace l'échéance par l'opération du relevé : l'opération générée est mise à jour (date, montant), sinon
    /// une opération est créée avec le tiers et la catégorie de la récurrence. Renvoie null si l'échéance a changé
    /// depuis l'aperçu : l'opération est alors importée seule.
    /// </summary>
    private async Task<Transaction?> ApplyMatchAsync(
        OccurrenceCandidate match, StatementLine line, Dictionary<Guid, Account> accounts, CancellationToken ct)
    {
        RecurrenceOccurrence? occurrence;
        if (match.OccurrenceId is { } occurrenceId)
        {
            occurrence = await db.RecurrenceOccurrences.FirstOrDefaultAsync(o => o.Id == occurrenceId, ct);
            if (occurrence is null)
            {
                return null;
            }
        }
        else
        {
            occurrence = new RecurrenceOccurrence
            {
                RecurrenceId = match.Recurrence.Id,
                Index = match.Index,
                ScheduledDate = match.ScheduledDate,
                DueDate = match.ScheduledDate,
                Status = OccurrenceStatus.Pending,
            };
            db.RecurrenceOccurrences.Add(occurrence);
        }

        Transaction? transaction = null;
        if (match.TransactionId is { } transactionId)
        {
            if (occurrence.Status != OccurrenceStatus.Generated || occurrence.TransactionId != transactionId)
            {
                return null;
            }
            transaction = await db.Transactions.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (transaction is not { ImportHash: null, TransferId: null, ReconciliationId: null, Lines.Count: 1 })
            {
                return null;
            }
            var previous = transaction.Lines[0];
            transaction.Date = line.Date;
            transaction.SetLines([new TransactionLine { CategoryId = previous.CategoryId, Amount = line.Amount, Memo = previous.Memo }]);
        }
        else
        {
            if (occurrence.Status != OccurrenceStatus.Pending)
            {
                return null;
            }
            transaction = generator.CreateTransactions(match.Recurrence, occurrence, line.Date, line.Amount, accounts);
            occurrence.TransactionId = transaction.Id;
        }
        occurrence.Status = OccurrenceStatus.Matched;
        return transaction;
    }

    private static string? TransactionLabel(string label) =>
        string.IsNullOrEmpty(label) ? null : label.Length > 200 ? label[..200].TrimEnd() : label;
}
