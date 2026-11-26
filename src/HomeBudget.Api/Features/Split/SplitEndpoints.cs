using HomeBudget.Api.Domain.Accounts;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Split;
using HomeBudget.Api.Features.Periods;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Split;

/// <summary>
/// Répartition des dépenses communes (K4) entre les membres actifs du foyer, sur une période. Seuls les comptes partagés
/// comptent, pour que chacun voie le même résultat sans jamais lire les comptes privés d'un autre (R8) :
/// <list type="bullet">
/// <item>une dépense commune (fixe ou variable, hors catégories exclues) payée depuis un compte qui a un titulaire est
/// apportée par ce titulaire ;</item>
/// <item>un virement vers un compte joint (sans titulaire) est un apport du membre à qui appartient le compte d'origine
/// (son titulaire, ou le propriétaire d'un compte privé : seul ce lien est lu, jamais ses opérations) ; un retrait vers
/// son compte réduit son apport ;</item>
/// <item>les revenus de la base du prorata sont ceux des comptes partagés dont il est titulaire.</item>
/// </list>
/// </summary>
public static class SplitEndpoints
{
    public static IEndpointRouteBuilder MapSplitEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/split").WithTags("Split");

        group.MapGet("/settings", SettingsAsync)
            .Produces<SplitSettingsDto>()
            .WithSummary("Mode de répartition et parts fixes des membres.");
        group.MapPut("/settings", UpdateSettingsAsync).RequireAuthorization(AuthorizationPolicies.Admin)
            .Produces<SplitSettingsDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie le mode de répartition et les parts fixes (administrateurs, If-Match : version des paramètres du foyer).");
        group.MapGet("/{periodKey}", GetAsync)
            .Produces<SplitDto>().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Répartition des dépenses communes de la période (clé AAAA-MM) : apports, parts, soldes et remboursements conseillés.");

        return app;
    }

    public sealed record MemberShareDto(Guid UserId, string Name, decimal Percent);

    /// <param name="Version">Version des paramètres du foyer, attendue dans If-Match.</param>
    public sealed record SplitSettingsDto(SplitMode Mode, List<MemberShareDto> Shares, int Version);

    public sealed record MemberShareRequest(Guid UserId, decimal Percent);

    /// <param name="Shares">Parts fixes en pourcentage (mode FixedShares) : 100 % au total.</param>
    public sealed record SplitSettingsRequest(SplitMode Mode, List<MemberShareRequest>? Shares);

    /// <param name="Share">Part retenue, en pourcentage.</param>
    /// <param name="Income">Revenus de la période sur ses comptes partagés (base du prorata).</param>
    /// <param name="ExpensesPaid">Dépenses communes payées depuis ses comptes.</param>
    /// <param name="Contributions">Versements nets vers les comptes joints.</param>
    /// <param name="Paid">Total apporté.</param>
    /// <param name="Due">Sa part du total apporté par le foyer.</param>
    /// <param name="Balance">Apporté − part : positif, le foyer lui doit ; négatif, il doit au foyer.</param>
    public sealed record SplitMemberDto(
        Guid UserId, string Name, decimal Share, decimal Income, decimal ExpensesPaid, decimal Contributions, decimal Paid, decimal Due, decimal Balance);

    public sealed record SettlementDto(Guid FromUserId, string FromName, Guid ToUserId, string ToName, decimal Amount);

    /// <param name="AppliedMode">Mode réellement appliqué : le prorata revient aux parts égales sans revenu sur la période.</param>
    /// <param name="CommonExpenses">Dépenses communes de la période sur les comptes partagés.</param>
    /// <param name="JointExpenses">Part de ces dépenses payée depuis les comptes joints.</param>
    /// <param name="TotalPaid">Total apporté par les membres, réparti selon leurs parts.</param>
    public sealed record SplitDto(
        PeriodEndpoints.PeriodDto Period,
        SplitMode Mode,
        SplitMode AppliedMode,
        decimal CommonExpenses,
        decimal JointExpenses,
        decimal TotalPaid,
        List<SplitMemberDto> Members,
        List<SettlementDto> Settlements);

    private static async Task<IResult> SettingsAsync(AppDbContext db, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        return TypedResults.Ok(await SettingsDtoAsync(db, settings.SplitMode, settings.Version, ct));
    }

    private static async Task<IResult> UpdateSettingsAsync(SplitSettingsRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.SingleAsync(ct);
        if (Concurrency.Check(http, settings) is { } precondition)
        {
            return precondition;
        }
        var errors = new Dictionary<string, string[]>();
        var shares = request.Shares ?? [];
        if (!Enum.IsDefined(request.Mode))
        {
            errors["mode"] = ["Mode de répartition inconnu."];
        }
        var users = (await db.Users.Where(u => u.IsActive).Select(u => u.Id).ToListAsync(ct)).ToHashSet();
        if (shares.Any(s => !users.Contains(s.UserId)) || shares.Select(s => s.UserId).Distinct().Count() != shares.Count)
        {
            errors["shares"] = ["Une part désigne un membre inconnu, désactivé ou cité deux fois."];
        }
        else if (shares.Any(s => s.Percent is < 0 or > 100 || decimal.Round(s.Percent, 2) != s.Percent))
        {
            errors["shares"] = ["Une part va de 0 à 100 %, avec 2 décimales au plus."];
        }
        else if (request.Mode == SplitMode.FixedShares && shares.Sum(s => s.Percent) != 100m)
        {
            errors["shares"] = ["Les parts fixes totalisent 100 %."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        settings.SplitMode = request.Mode;
        var existing = await db.MemberShares.ToListAsync(ct);
        db.MemberShares.RemoveRange(existing.Where(e => shares.All(s => s.UserId != e.UserId)));
        foreach (var share in shares)
        {
            if (existing.FirstOrDefault(e => e.UserId == share.UserId) is { } current)
            {
                current.Percent = share.Percent;
            }
            else
            {
                db.MemberShares.Add(new MemberShare { UserId = share.UserId, Percent = share.Percent });
            }
        }
        if (!await db.TrySaveAsync(ct))
        {
            return Concurrency.Conflict();
        }
        return TypedResults.Ok(await SettingsDtoAsync(db, settings.SplitMode, settings.Version, ct));
    }

    private static async Task<SplitSettingsDto> SettingsDtoAsync(AppDbContext db, SplitMode mode, int version, CancellationToken ct)
    {
        var members = await MembersAsync(db, ct);
        var shares = await db.MemberShares.AsNoTracking().ToDictionaryAsync(s => s.UserId, s => s.Percent, ct);
        return new SplitSettingsDto(mode, [.. members.Select(m => new MemberShareDto(m.Id, m.Name, shares.GetValueOrDefault(m.Id)))], version);
    }

    /// <summary>Membres actifs du foyer, par nom.</summary>
    private static async Task<List<(Guid Id, string Name)>> MembersAsync(AppDbContext db, CancellationToken ct) =>
        [.. (await db.Users.AsNoTracking().Where(u => u.IsActive).ToListAsync(ct))
            .Select(u => (u.Id, u.Name))
            .OrderBy(u => u.Name, StringComparer.CurrentCultureIgnoreCase)];

    private static async Task<IResult> GetAsync(string periodKey, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var settings = await db.HouseholdSettings.AsNoTracking().SingleAsync(ct);
        var calculator = Domain.Periods.PeriodCalculator.From(settings);
        if (!calculator.TryParse(periodKey, out var period))
        {
            return PeriodEndpoints.UnknownPeriod();
        }
        var (start, end) = (period.Start, period.End);

        // Comptes partagés seulement : le résultat est le même pour chaque membre (R8).
        var shared = await db.Accounts.AsNoTracking()
            .Where(a => a.Visibility == AccountVisibility.Shared)
            .Select(a => new { a.Id, a.HolderId })
            .ToListAsync(ct);
        var sharedIds = shared.Select(a => a.Id).ToList();
        var holders = shared.ToDictionary(a => a.Id, a => a.HolderId);
        var jointIds = shared.Where(a => a.HolderId is null).Select(a => a.Id).ToHashSet();

        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        bool Excluded(Category c) => c.ExcludeFromSplit || (c.ParentId is { } p && categories.TryGetValue(p, out var parent) && parent.ExcludeFromSplit);
        var expenseIds = categories.Values.Where(c => c.Kind is CategoryKind.Fixed or CategoryKind.Variable && !Excluded(c)).Select(c => c.Id).ToList();
        var incomeIds = categories.Values.Where(c => c.Kind == CategoryKind.Income).Select(c => c.Id).ToList();

        var lines = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Date, t.AccountId })
            .Where(x => x.Date >= start && x.Date <= end && sharedIds.Contains(x.AccountId) && x.CategoryId != null
                && (expenseIds.Contains(x.CategoryId.Value) || incomeIds.Contains(x.CategoryId.Value)))
            .GroupBy(x => new { x.AccountId, IsIncome = incomeIds.Contains(x.CategoryId!.Value) })
            .Select(g => new { g.Key.AccountId, g.Key.IsIncome, Sum = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var expensesPaid = new Dictionary<Guid, decimal>();
        var incomes = new Dictionary<Guid, decimal>();
        var commonExpenses = 0m;
        var jointExpenses = 0m;
        foreach (var line in lines)
        {
            var holder = holders[line.AccountId];
            if (line.IsIncome)
            {
                if (holder is { } h)
                {
                    incomes[h] = incomes.GetValueOrDefault(h) + line.Sum;
                }
                continue;
            }
            commonExpenses -= line.Sum;
            if (holder is { } payer)
            {
                expensesPaid[payer] = expensesPaid.GetValueOrDefault(payer) - line.Sum;
            }
            else
            {
                jointExpenses -= line.Sum;
            }
        }

        // Apports aux comptes joints : virements dont l'autre côté est un compte d'un membre.
        var jointList = jointIds.ToList();
        var transfers = await db.Transactions.AsNoTracking()
            .Where(t => jointList.Contains(t.AccountId) && t.TransferId != null && t.Date >= start && t.Date <= end)
            .Select(t => new { TransferId = t.TransferId!.Value, t.Amount })
            .ToListAsync(ct);
        var transferIds = transfers.Select(t => t.TransferId).ToList();
        // Seul le compte d'origine est lu, même privé : jamais son opération ni son solde (R8).
        var counterparts = await db.Transactions.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.TransferId != null && transferIds.Contains(t.TransferId.Value) && !jointList.Contains(t.AccountId))
            .Join(db.Accounts.IgnoreQueryFilters(), t => t.AccountId, a => a.Id, (t, a) => new
            {
                TransferId = t.TransferId!.Value,
                Member = a.Visibility == AccountVisibility.Private ? a.OwnerId : a.HolderId,
            })
            .ToListAsync(ct);
        var memberByTransfer = counterparts.Where(c => c.Member != null).ToDictionary(c => c.TransferId, c => c.Member!.Value);
        var contributions = new Dictionary<Guid, decimal>();
        foreach (var t in transfers)
        {
            if (memberByTransfer.TryGetValue(t.TransferId, out var member))
            {
                contributions[member] = contributions.GetValueOrDefault(member) + t.Amount;
            }
        }

        var members = await MembersAsync(db, ct);
        var fixedShares = await db.MemberShares.AsNoTracking().ToDictionaryAsync(s => s.UserId, s => s.Percent, ct);
        var result = ExpenseSplit.Compute(settings.SplitMode, [.. members.Select(m => new SplitMember(
            m.Id, expensesPaid.GetValueOrDefault(m.Id), contributions.GetValueOrDefault(m.Id),
            incomes.GetValueOrDefault(m.Id), fixedShares.GetValueOrDefault(m.Id)))]);
        var names = members.ToDictionary(m => m.Id, m => m.Name);

        return TypedResults.Ok(new SplitDto(
            PeriodEndpoints.ToDto(calculator, period, clock),
            settings.SplitMode,
            result.AppliedMode,
            commonExpenses,
            jointExpenses,
            result.TotalPaid,
            [.. result.Shares.Select(s => new SplitMemberDto(
                s.UserId, names[s.UserId], s.Share, incomes.GetValueOrDefault(s.UserId), expensesPaid.GetValueOrDefault(s.UserId),
                contributions.GetValueOrDefault(s.UserId), s.Paid, s.Due, s.Balance))],
            [.. result.Settlements.Select(s => new SettlementDto(s.FromUserId, names[s.FromUserId], s.ToUserId, names[s.ToUserId], s.Amount))]));
    }
}
