using System.Globalization;
using System.Text.Json;

using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Transactions;

/// <summary>Historique des modifications d'une opération (B13).</summary>
public static class TransactionHistoryEndpoints
{
    /// <summary>Nom affiché pour un compte privé d'un autre membre (R8).</summary>
    public const string HiddenAccount = "Compte privé";

    /// <summary>Nom affiché pour un élément supprimé depuis.</summary>
    public const string Deleted = "(supprimé)";

    public static IEndpointRouteBuilder MapTransactionHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/transactions").WithTags("Transactions")
            .MapGet("/{id:guid}/history", GetAsync)
            .WithSummary("Modifications de l'opération, de la plus récente à la plus ancienne : qui, quand, avant et après.");
        return app;
    }

    public sealed record LineChangeDto(string? Category, decimal Amount, string? Memo, IReadOnlyList<string> Tags);

    /// <param name="Field">date, account, payee, label, notes, cleared, reconciled, reimburses, advance ou lines.</param>
    /// <param name="Before">Valeur avant, lisible : nom du compte, du tiers ou de l'avance, date ISO, true/false.</param>
    /// <param name="BeforeLines">Ventilation avant (champ lines).</param>
    public sealed record FieldChangeDto(
        string Field, string? Before, string? After, IReadOnlyList<LineChangeDto>? BeforeLines, IReadOnlyList<LineChangeDto>? AfterLines);

    /// <param name="UserName">Nom de l'auteur ; null pour un traitement automatique (récurrence, import planifié).</param>
    public sealed record TransactionChangeDto(Guid Id, DateTimeOffset At, Guid? UserId, string? UserName, IReadOnlyList<FieldChangeDto> Changes);

    private static async Task<Results<Ok<List<TransactionChangeDto>>, NotFound>> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        // Le filtre des comptes privés (R8) rend une opération invisible introuvable, historique compris.
        if (!await db.Transactions.AnyAsync(t => t.Id == id, ct))
        {
            return TypedResults.NotFound();
        }
        var entries = await db.TransactionChanges.AsNoTracking()
            .Where(c => c.TransactionId == id)
            .OrderByDescending(c => c.Id) // UUID v7 : ordre chronologique (SQLite ne trie pas les DateTimeOffset)
            .ToListAsync(ct);
        var parsed = entries.Select(e => (Entry: e, Changes: JsonSerializer.Deserialize<List<TransactionFieldChange>>(e.Changes, TransactionHistoryInterceptor.Json) ?? []))
            .ToList();
        var all = parsed.SelectMany(p => p.Changes).ToList();

        HashSet<Guid> Ids(params string[] fields) =>
            [.. all.Where(c => fields.Contains(c.Field)).SelectMany(c => new[] { c.Before, c.After })
                .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null).OfType<Guid>()];
        var accountIds = Ids("account");
        var payeeIds = Ids("payee");
        var advanceIds = Ids("advance");
        var expenseIds = Ids("reimburses");
        var categoryIds = all.SelectMany(c => (c.BeforeLines ?? []).Concat(c.AfterLines ?? []))
            .Select(l => l.CategoryId).OfType<Guid>().ToHashSet();
        var userIds = entries.Select(e => e.UserId).OfType<Guid>().ToHashSet();

        var accounts = await db.Accounts.AsNoTracking().Where(a => accountIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var hiddenAccounts = await db.Accounts.AsNoTracking().IgnoreQueryFilters()
            .Where(a => accountIds.Contains(a.Id)).Select(a => a.Id).ToListAsync(ct);
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var advances = await db.Advances.AsNoTracking().Where(a => advanceIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Label, ct);
        var expenses = await db.Transactions.AsNoTracking().Where(t => expenseIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Date, t.Label, t.Amount }).ToDictionaryAsync(t => t.Id, ct);
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        string? Name(string field, string? value)
        {
            if (value is null || !Guid.TryParse(value, out var key))
            {
                return value;
            }
            return field switch
            {
                "account" => accounts.GetValueOrDefault(key) ?? (hiddenAccounts.Contains(key) ? HiddenAccount : Deleted),
                "payee" => payees.GetValueOrDefault(key) ?? Deleted,
                "advance" => advances.GetValueOrDefault(key) ?? Deleted,
                "reimburses" => expenses.TryGetValue(key, out var e)
                    ? $"{e.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)} · {e.Label ?? "Dépense"} · {e.Amount.ToString("0.00", CultureInfo.GetCultureInfo("fr-FR"))} €"
                    : Deleted,
                _ => value,
            };
        }

        List<LineChangeDto>? Lines(IReadOnlyList<TransactionLineSnapshot>? lines) =>
            lines?.Select(l => new LineChangeDto(
                l.CategoryId is { } c ? categories.GetValueOrDefault(c) ?? Deleted : null, l.Amount, l.Memo, l.Tags)).ToList();

        return TypedResults.Ok(parsed.Select(p => new TransactionChangeDto(
            p.Entry.Id,
            p.Entry.At,
            p.Entry.UserId,
            p.Entry.UserId is { } u && users.TryGetValue(u, out var name) ? name : p.Entry.UserName,
            [.. p.Changes.Select(c => new FieldChangeDto(c.Field, Name(c.Field, c.Before), Name(c.Field, c.After), Lines(c.BeforeLines), Lines(c.AfterLines)))]))
            .ToList());
    }
}
