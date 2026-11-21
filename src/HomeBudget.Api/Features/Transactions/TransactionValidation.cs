using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Transactions;

/// <summary>Règles communes aux opérations et aux virements.</summary>
internal static class TransactionValidation
{
    public static readonly DateOnly MinDate = new(1900, 1, 1);
    public static readonly DateOnly MaxDate = new(2199, 12, 31);

    public static bool HasCents(decimal amount) => decimal.Round(amount, 2) == amount;

    /// <summary>
    /// Vérifie qu'un compte existe, accepte la date, et n'est pas archivé (sauf s'il est déjà celui de l'opération).
    /// </summary>
    public static async Task CheckAccountAsync(
        AppDbContext db, Guid accountId, DateOnly date, Guid? currentAccountId, string field, Dictionary<string, string[]> errors)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);
        if (account is null)
        {
            errors[field] = ["Compte introuvable."];
        }
        else if (account.IsArchived && account.Id != currentAccountId)
        {
            errors[field] = ["Ce compte est archivé."];
        }
        else if (date < account.InitialDate)
        {
            errors["date"] = [$"Date antérieure au solde initial du compte ({account.InitialDate:dd/MM/yyyy})."];
        }
        if (date < MinDate || date > MaxDate)
        {
            errors["date"] = ["Date invalide."];
        }
    }

    public static async Task<Dictionary<Guid, Category>> LoadCategoriesAsync(AppDbContext db, IEnumerable<Guid?> ids)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return await db.Categories.AsNoTracking().Where(c => wanted.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
    }
}
