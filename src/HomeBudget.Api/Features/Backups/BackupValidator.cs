using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Periods;
using HomeBudget.Api.Domain.Recurrences;

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
        if (document.FormatVersion != BackupDocument.CurrentVersion)
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

        ValidateSettings(settings, errors);
        var accountIds = UniqueIds("compte", accounts.Select(a => a!.Id), errors);
        var categoryIds = UniqueIds("catégorie", categories.Select(c => c!.Id), errors);
        var payeeIds = UniqueIds("tiers", payees.Select(p => p!.Id), errors);
        UniqueIds("budget", budgets.Select(b => b!.Id), errors);
        var transactionIds = UniqueIds("opération", transactions.Select(t => t!.Id), errors);
        var recurrenceIds = UniqueIds("récurrence", recurrences.Select(r => r!.Id), errors);
        UniqueIds("échéance", occurrences.Select(o => o!.Id), errors);

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
        foreach (var t in transactions.OfType<BackupTransaction>())
        {
            var where = $"Opération {t.Id}";
            Reference(where, "compte", t.AccountId, accountIds, errors);
            OptionalReference(where, "tiers", t.PayeeId, payeeIds, errors);
            Text(where, "libellé", t.Label, 200, required: false, errors);
            Text(where, "notes", t.Notes, 2000, required: false, errors);
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
            if (r.Interval is < 1 or > RecurrenceRule.MaxInterval || r.Count is < 1 or > RecurrenceRule.MaxCount)
            {
                errors.Add($"{where} : intervalle ou nombre d'échéances invalide.");
            }
            Audit(where, r.Audit, errors);
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

        return errors.List;
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
