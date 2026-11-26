using HomeBudget.Api.Domain.Advances;
using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Domain.Rules;
using HomeBudget.Api.Domain.Tags;

namespace HomeBudget.Api.Features.Backups;

/// <summary>
/// Validation complète d'une sauvegarde avant restauration : format, champs, et cohérence de toutes les
/// références. Aucune donnée n'est touchée tant que le fichier n'est pas valide en entier.
/// </summary>
public static class BackupValidator
{
    public const int MaxErrors = 20;

    private const decimal MaxAmount = 999_999_999m;

    public static IReadOnlyList<string> Validate(BackupDocument document)
    {
        var errors = new Errors();
        if (document.Format != BackupDocument.FormatName)
        {
            errors.Add("Ce fichier n'est pas une sauvegarde Home Budget.");
            return errors.List;
        }
        if (document.FormatVersion is < BackupDocument.MinVersion or > BackupDocument.CurrentVersion)
        {
            errors.Add($"Version de format {document.FormatVersion} non prise en charge (attendue : {BackupDocument.CurrentVersion}).");
            return errors.List;
        }
        if (document is not
            {
                Settings: { } settings, Accounts: { } accounts, Categories: { } categories, Payees: { } payees,
                Budgets: { } budgets, Transactions: { } transactions, Recurrences: { } recurrences, Occurrences: { } occurrences,
            })
        {
            errors.Add("Fichier incomplet : paramètres, comptes, catégories, tiers, budgets, opérations, récurrences et échéances sont attendus.");
            return errors.List;
        }
        if (accounts.Contains(null) || categories.Contains(null) || payees.Contains(null) || budgets.Contains(null)
            || transactions.Contains(null) || recurrences.Contains(null) || occurrences.Contains(null))
        {
            errors.Add("Fichier invalide : un élément de liste est vide (null).");
            return errors.List;
        }
        var hasScenarios = document.FormatVersion >= BackupDocument.ScenariosVersion;
        if (hasScenarios && (document.Scenarios is null || document.ScenarioEvents is null))
        {
            errors.Add("Fichier incomplet : scénarios et leurs événements attendus.");
            return errors.List;
        }
        if (hasScenarios && (document.Scenarios!.Contains(null) || document.ScenarioEvents!.Contains(null)))
        {
            errors.Add("Fichier invalide : un élément de liste est vide (null).");
            return errors.List;
        }
        var hasProvisions = document.FormatVersion >= BackupDocument.ProvisionsVersion;
        if (hasProvisions && (document.PeriodBudgets is null || document.Provisions is null || document.TransactionChanges is null))
        {
            errors.Add("Fichier incomplet : budgets par période, provisions et historique des opérations attendus.");
            return errors.List;
        }
        if (hasProvisions && (document.PeriodBudgets!.Contains(null) || document.Provisions!.Contains(null) || document.TransactionChanges!.Contains(null)))
        {
            errors.Add("Fichier invalide : un élément de liste est vide (null).");
            return errors.List;
        }
        var hasReconciliations = document.FormatVersion >= BackupDocument.ReconciliationsVersion;
        if (hasReconciliations && (document.Reconciliations is null || document.Advances is null))
        {
            errors.Add("Fichier incomplet : rapprochements et avances attendus.");
            return errors.List;
        }
        if (hasReconciliations && (document.Reconciliations!.Contains(null) || document.Advances!.Contains(null)))
        {
            errors.Add("Fichier invalide : un élément de liste est vide (null).");
            return errors.List;
        }
        var hasTags = document.FormatVersion >= BackupDocument.TagsVersion;
        if (hasTags && (document.Tags is null || document.LabelRules is null))
        {
            errors.Add("Fichier incomplet : tags et règles apprises attendus.");
            return errors.List;
        }
        if (hasTags && (document.Tags!.Contains(null) || document.LabelRules!.Contains(null)))
        {
            errors.Add("Fichier invalide : un élément de liste est vide (null).");
            return errors.List;
        }
        var hasImports = document.FormatVersion >= BackupDocument.ImportsVersion;
        if (hasImports && (document.ImportProfiles is null || document.ImportBatches is null))
        {
            errors.Add("Fichier incomplet : profils de lecture et historique des imports attendus.");
            return errors.List;
        }
        if (hasImports && (document.ImportProfiles!.Contains(null) || document.ImportBatches!.Contains(null)))
        {
            errors.Add("Fichier invalide : un élément de liste est vide (null).");
            return errors.List;
        }

        ValidateSettings(settings, errors);
        var accountIds = UniqueIds("compte", accounts.Select(a => a!.Id), errors);
        var categoryIds = UniqueIds("catégorie", categories.Select(c => c!.Id), errors);
        var payeeIds = UniqueIds("tiers", payees.Select(p => p!.Id), errors);
        UniqueIds("budget", budgets.Select(b => b!.Id), errors);
        var transactionIds = UniqueIds("opération", transactions.Select(t => t!.Id), errors);
        var recurrenceIds = UniqueIds("récurrence", recurrences.Select(r => r!.Id), errors);
        UniqueIds("échéance", occurrences.Select(o => o!.Id), errors);
        var batchIds = UniqueIds("import", (document.ImportBatches ?? []).Select(b => b!.Id), errors);
        UniqueIds("profil de lecture", (document.ImportProfiles ?? []).Select(p => p!.Id), errors);
        var tagIds = UniqueIds("tag", (document.Tags ?? []).Select(t => t!.Id), errors);
        UniqueIds("règle", (document.LabelRules ?? []).Select(r => r!.Id), errors);

        UniqueIds("rapprochement", (document.Reconciliations ?? []).Select(r => r!.Id), errors);
        var advanceIds = UniqueIds("avance", (document.Advances ?? []).Select(a => a!.Id), errors);
        var reconciliationAccounts = new Dictionary<Guid, Guid>();
        var reconciledDates = new HashSet<(Guid, DateOnly)>();
        foreach (var r in (document.Reconciliations ?? []).OfType<BackupReconciliation>())
        {
            var where = $"Rapprochement {r.Id}";
            Reference(where, "compte", r.AccountId, accountIds, errors);
            Amount(where, r.StatementBalance, errors);
            if (!reconciledDates.Add((r.AccountId, r.Date)))
            {
                errors.Add($"{where} : deux rapprochements du même compte à la même date.");
            }
            reconciliationAccounts[r.Id] = r.AccountId;
            Audit(where, r.Audit, errors);
        }
        foreach (var a in (document.Advances ?? []).OfType<BackupAdvance>())
        {
            var where = $"Avance {a.Id}";
            Text(where, "libellé", a.Label, Advance.MaxLabelLength, required: true, errors);
            Text(where, "notes", a.Notes, 2000, required: false, errors);
            if (a.ExpectedAmount <= 0)
            {
                errors.Add($"{where} : montant attendu positif requis.");
            }
            Amount(where, a.ExpectedAmount, errors);
            Audit(where, a.Audit, errors);
        }

        var tagNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in (document.Tags ?? []).OfType<BackupTag>())
        {
            var where = $"Tag {g.Id}";
            if (Text(where, "nom", g.Name, Tag.MaxNameLength, required: true, errors) && !tagNames.Add(Tag.Normalize(g.Name!)))
            {
                errors.Add($"{where} : nom en double.");
            }
            Audit(where, g.Audit, errors);
        }

        var ruleKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in (document.LabelRules ?? []).OfType<BackupLabelRule>())
        {
            var where = $"Règle {r.Id}";
            if (Text(where, "clé", r.Key, LabelKey.MaxLength, required: true, errors) && !ruleKeys.Add(r.Key!))
            {
                errors.Add($"{where} : clé en double.");
            }
            OptionalReference(where, "tiers", r.PayeeId, payeeIds, errors);
            OptionalReference(where, "catégorie", r.CategoryId, categoryIds, errors);
            Audit(where, r.Audit, errors);
        }

        foreach (var a in accounts.OfType<BackupAccount>())
        {
            var where = $"Compte {a.Id}";
            Text(where, "nom", a.Name, 100, required: true, errors);
            Text(where, "couleur", a.Color, 16, required: false, errors);
            Text(where, "icône", a.Icon, 40, required: false, errors);
            Defined(where, a.Type, errors);
            Defined(where, a.Visibility, errors);
            Amount(where, a.InitialBalance, errors);
            Amount(where, a.OverdraftThreshold, errors);
            Audit(where, a.Audit, errors);
        }

        var categoryById = categories.OfType<BackupCategory>().GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var c in categoryById.Values)
        {
            var where = $"Catégorie {c.Id}";
            Text(where, "nom", c.Name, 60, required: true, errors);
            Text(where, "couleur", c.Color, 16, required: false, errors);
            Text(where, "icône", c.Icon, 40, required: false, errors);
            Defined(where, c.Kind, errors);
            if (c.ParentId is { } parentId)
            {
                if (!categoryById.TryGetValue(parentId, out var parent))
                {
                    errors.Add($"{where} : catégorie parente introuvable.");
                }
                else if (parent.ParentId is not null)
                {
                    errors.Add($"{where} : deux niveaux de catégories au plus.");
                }
                else if (parent.Kind != c.Kind)
                {
                    errors.Add($"{where} : une sous-catégorie a le type de sa catégorie parente.");
                }
            }
            Audit(where, c.Audit, errors);
        }

        var payeeNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in payees.OfType<BackupPayee>())
        {
            var where = $"Tiers {p.Id}";
            if (Text(where, "nom", p.Name, 100, required: true, errors) && !payeeNames.Add(Payee.Normalize(p.Name!)))
            {
                errors.Add($"{where} : nom en double.");
            }
            Audit(where, p.Audit, errors);
        }

        var budgetedCategories = new HashSet<Guid>();
        foreach (var b in budgets.OfType<BackupBudget>())
        {
            var where = $"Budget {b.Id}";
            Reference(where, "catégorie", b.CategoryId, categoryIds, errors);
            if (!budgetedCategories.Add(b.CategoryId))
            {
                errors.Add($"{where} : un seul budget par catégorie.");
            }
            Defined(where, b.Mode, errors);
            if (b.Value < 0 || b.Value > MaxAmount || decimal.Round(b.Value, 2) != b.Value)
            {
                errors.Add($"{where} : valeur invalide.");
            }
            Audit(where, b.Audit, errors);
        }

        var lineIds = new HashSet<Guid>();
        var importHashes = new HashSet<(Guid, string)>();
        foreach (var t in transactions.OfType<BackupTransaction>())
        {
            var where = $"Opération {t.Id}";
            Reference(where, "compte", t.AccountId, accountIds, errors);
            OptionalReference(where, "tiers", t.PayeeId, payeeIds, errors);
            Text(where, "libellé", t.Label, 200, required: false, errors);
            Text(where, "notes", t.Notes, 2000, required: false, errors);
            Text(where, "libellé bancaire", t.RawLabel, StatementText.MaxLabelLength, required: false, errors);
            OptionalReference(where, "import", t.ImportBatchId, batchIds, errors);
            OptionalReference(where, "opération remboursée", t.ReimbursesId, transactionIds, errors);
            OptionalReference(where, "avance", t.AdvanceId, advanceIds, errors);
            if (t.ReconciliationId is { } reconciliationId
                && (!reconciliationAccounts.TryGetValue(reconciliationId, out var reconciledAccount) || reconciledAccount != t.AccountId))
            {
                errors.Add($"{where} : rapprochement introuvable dans le compte de l'opération.");
            }
            if (Text(where, "empreinte d'import", t.ImportHash, 100, required: false, errors)
                && t.ImportHash is { } hash && !importHashes.Add((t.AccountId, hash)))
            {
                errors.Add($"{where} : empreinte d'import en double dans le compte.");
            }
            Audit(where, t.Audit, errors);
            if (t.Lines is not { Count: > 0 and <= 50 } lines || lines.Contains(null))
            {
                errors.Add($"{where} : de 1 à 50 lignes de ventilation attendues.");
                continue;
            }
            foreach (var line in lines.OfType<BackupTransactionLine>())
            {
                if (line.Id == Guid.Empty || !lineIds.Add(line.Id))
                {
                    errors.Add($"{where} : identifiant de ligne vide ou en double ({line.Id}).");
                }
                OptionalReference(where, "catégorie", line.CategoryId, categoryIds, errors);
                Amount(where, line.Amount, errors);
                Text(where, "mémo", line.Memo, 200, required: false, errors);
                if (line.TagIds is { } lineTags)
                {
                    if (lineTags.Count > Tag.MaxPerLine || lineTags.Distinct().Count() != lineTags.Count)
                    {
                        errors.Add($"{where} : tags en double ou trop nombreux sur une ligne.");
                    }
                    foreach (var tagId in lineTags)
                    {
                        Reference(where, "tag", tagId, tagIds, errors);
                    }
                }
            }
        }

        foreach (var r in recurrences.OfType<BackupRecurrence>())
        {
            var where = $"Récurrence {r.Id}";
            Text(where, "libellé", r.Label, 200, required: true, errors);
            Text(where, "notes", r.Notes, 2000, required: false, errors);
            Reference(where, "compte", r.AccountId, accountIds, errors);
            OptionalReference(where, "compte de destination", r.ToAccountId, accountIds, errors);
            OptionalReference(where, "tiers", r.PayeeId, payeeIds, errors);
            OptionalReference(where, "catégorie", r.CategoryId, categoryIds, errors);
            Amount(where, r.Amount, errors);
            Defined(where, r.Mode, errors);
            Defined(where, r.Unit, errors);
            Defined(where, r.End, errors);
            Defined(where, r.BusinessDayShift, errors);
            if (!new AmountTolerance(r.Tolerance, r.ToleranceUnit).IsValid)
            {
                errors.Add($"{where} : tolérance invalide.");
            }
            if (r.Interval is < 1 or > RecurrenceRule.MaxInterval || r.Count is < 1 or > RecurrenceRule.MaxCount)
            {
                errors.Add($"{where} : intervalle ou nombre d'échéances invalide.");
            }
            Audit(where, r.Audit, errors);
        }

        var periodCalculator = new PeriodCalculator(Domain.Settings.PeriodMode.Calendar, 1);
        UniqueIds("budget de période", (document.PeriodBudgets ?? []).Select(b => b!.Id), errors);
        var periodBudgets = new HashSet<(Guid, string)>();
        foreach (var b in (document.PeriodBudgets ?? []).OfType<BackupPeriodBudget>())
        {
            var where = $"Budget de période {b.Id}";
            Reference(where, "catégorie", b.CategoryId, categoryIds, errors);
            if (!periodCalculator.TryParse(b.PeriodKey, out _))
            {
                errors.Add($"{where} : période invalide.");
            }
            else if (!periodBudgets.Add((b.CategoryId, b.PeriodKey!)))
            {
                errors.Add($"{where} : un seul budget par catégorie et par période.");
            }
            Defined(where, b.Mode, errors);
            if (b.Value < 0 || b.Value > MaxAmount || decimal.Round(b.Value, 2) != b.Value)
            {
                errors.Add($"{where} : valeur invalide.");
            }
            Audit(where, b.Audit, errors);
        }

        UniqueIds("provision", (document.Provisions ?? []).Select(p => p!.Id), errors);
        foreach (var p in (document.Provisions ?? []).OfType<BackupProvision>())
        {
            var where = $"Provision {p.Id}";
            Text(where, "libellé", p.Label, Domain.Budgets.Provision.MaxLabelLength, required: true, errors);
            Reference(where, "catégorie", p.CategoryId, categoryIds, errors);
            if (p.Amount <= 0)
            {
                errors.Add($"{where} : montant positif requis.");
            }
            Amount(where, p.Amount, errors);
            Audit(where, p.Audit, errors);
        }

        var scenarioIds = UniqueIds("scénario", (document.Scenarios ?? []).Select(sc => sc!.Id), errors);
        foreach (var sc in (document.Scenarios ?? []).OfType<BackupScenario>())
        {
            var where = $"Scénario {sc.Id}";
            Text(where, "nom", sc.Name, Domain.Scenarios.Scenario.MaxNameLength, required: true, errors);
            Audit(where, sc.Audit, errors);
        }
        UniqueIds("événement de scénario", (document.ScenarioEvents ?? []).Select(e => e!.Id), errors);
        foreach (var e in (document.ScenarioEvents ?? []).OfType<BackupScenarioEvent>())
        {
            var where = $"Événement de scénario {e.Id}";
            Reference(where, "scénario", e.ScenarioId, scenarioIds, errors);
            Text(where, "libellé", e.Label, Domain.Scenarios.ScenarioEvent.MaxLabelLength, required: true, errors);
            Reference(where, "compte", e.AccountId, accountIds, errors);
            OptionalReference(where, "catégorie", e.CategoryId, categoryIds, errors);
            if (e.Amount == 0)
            {
                errors.Add($"{where} : montant non nul requis.");
            }
            Amount(where, e.Amount, errors);
            Defined(where, e.Frequency, errors);
            if (e.EndDate < e.Date)
            {
                errors.Add($"{where} : date de fin avant la première date.");
            }
            Audit(where, e.Audit, errors);
        }

        UniqueIds("modification d'opération", (document.TransactionChanges ?? []).Select(c => c!.Id), errors);
        foreach (var c in (document.TransactionChanges ?? []).OfType<BackupTransactionChange>())
        {
            var where = $"Modification d'opération {c.Id}";
            Reference(where, "opération", c.TransactionId, transactionIds, errors);
            if (Text(where, "détail", c.Changes, 1_000_000, required: true, errors) && !IsJsonArray(c.Changes!))
            {
                errors.Add($"{where} : détail illisible.");
            }
        }

        var scheduled = new HashSet<(Guid, DateOnly)>();
        foreach (var o in occurrences.OfType<BackupOccurrence>())
        {
            var where = $"Échéance {o.Id}";
            Reference(where, "récurrence", o.RecurrenceId, recurrenceIds, errors);
            OptionalReference(where, "opération", o.TransactionId, transactionIds, errors);
            Defined(where, o.Status, errors);
            if (!scheduled.Add((o.RecurrenceId, o.ScheduledDate)))
            {
                errors.Add($"{where} : échéance en double pour la même date.");
            }
            if (o.Index < 0)
            {
                errors.Add($"{where} : rang négatif.");
            }
            Audit(where, o.Audit, errors);
        }

        var profileNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in (document.ImportProfiles ?? []).OfType<BackupImportProfile>())
        {
            var where = $"Profil de lecture {p.Id}";
            if (Text(where, "nom", p.Name, 60, required: true, errors) && !profileNames.Add(p.Name!.Trim()))
            {
                errors.Add($"{where} : nom en double.");
            }
            var options = new CsvOptions(
                p.Delimiter, p.DateFormat ?? string.Empty, p.DecimalSeparator, p.DateColumn, p.LabelColumn, p.AmountColumn, p.DebitColumn, p.CreditColumn);
            if (options.Validate().Count > 0)
            {
                errors.Add($"{where} : réglages invalides.");
            }
            Audit(where, p.Audit, errors);
        }

        foreach (var b in (document.ImportBatches ?? []).OfType<BackupImportBatch>())
        {
            var where = $"Import {b.Id}";
            Reference(where, "compte", b.AccountId, accountIds, errors);
            Text(where, "nom de fichier", b.FileName, 200, required: true, errors);
            Defined(where, b.Format, errors);
            if (b.Created < 0 || b.Matched < 0 || b.Duplicates < 0 || b.Matched > b.Created)
            {
                errors.Add($"{where} : compteurs invalides.");
            }
            Audit(where, b.Audit, errors);
        }

        return errors.List;
    }

    private static bool IsJsonArray(string json)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<Domain.Transactions.TransactionFieldChange>>(
                json, Infrastructure.Data.TransactionHistoryInterceptor.Json) is not null;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static void ValidateSettings(BackupSettings s, Errors errors)
    {
        const string where = "Paramètres";
        Defined(where, s.PeriodMode, errors);
        if (s.PeriodStartDay is < 1 or > PeriodCalculator.MaxStartDay)
        {
            errors.Add($"{where} : jour de début de période invalide.");
        }
        if (s.BudgetWarningPercent is < 1 or > 100 || s.BudgetAlertPercent is < 1 or > 200 || s.BudgetWarningPercent >= s.BudgetAlertPercent)
        {
            errors.Add($"{where} : seuils des jauges invalides.");
        }
        Amount(where, s.SafetyThreshold, errors);
        Defined(where, s.ForecastStrategy, errors);
        if (s.ForecastAverageMonths is < 1 or > Domain.Settings.HouseholdSettings.MaxForecastAverageMonths)
        {
            errors.Add($"{where} : nombre de périodes de la moyenne invalide.");
        }
        Audit(where, s.Audit, errors);
    }

    private static HashSet<Guid> UniqueIds(string label, IEnumerable<Guid> ids, Errors errors)
    {
        var set = new HashSet<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty || !set.Add(id))
            {
                errors.Add($"Identifiant de {label} vide ou en double ({id}).");
            }
        }
        return set;
    }

    private static void Reference(string where, string label, Guid id, HashSet<Guid> known, Errors errors)
    {
        if (!known.Contains(id))
        {
            errors.Add($"{where} : {label} introuvable ({id}).");
        }
    }

    private static void OptionalReference(string where, string label, Guid? id, HashSet<Guid> known, Errors errors)
    {
        if (id is { } value)
        {
            Reference(where, label, value, known, errors);
        }
    }

    private static bool Text(string where, string label, string? value, int maxLength, bool required, Errors errors)
    {
        if (required && string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{where} : {label} obligatoire.");
            return false;
        }
        if (value is { Length: var length } && length > maxLength)
        {
            errors.Add($"{where} : {label} trop long ({maxLength} caractères au plus).");
            return false;
        }
        return true;
    }

    private static void Amount(string where, decimal amount, Errors errors)
    {
        if (Math.Abs(amount) > MaxAmount || decimal.Round(amount, 2) != amount)
        {
            errors.Add($"{where} : montant invalide ({amount}).");
        }
    }

    private static void Defined<TEnum>(string where, TEnum value, Errors errors)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add($"{where} : valeur inconnue ({value}).");
        }
    }

    private static void Audit(string where, BackupAudit? audit, Errors errors)
    {
        if (audit is null || audit.Version < 1)
        {
            errors.Add($"{where} : informations de traçabilité manquantes.");
        }
    }

    /// <summary>Liste d'erreurs bornée : un fichier très abîmé ne produit pas une réponse démesurée.</summary>
    private sealed class Errors
    {
        public List<string> List { get; } = [];

        private int _total;

        public void Add(string message)
        {
            _total++;
            if (List.Count < MaxErrors)
            {
                List.Add(message);
            }
            else if (_total == MaxErrors + 1)
            {
                List.Add("Autres erreurs non affichées.");
            }
        }
    }
}
