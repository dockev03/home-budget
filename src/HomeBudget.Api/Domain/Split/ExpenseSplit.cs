namespace HomeBudget.Api.Domain.Split;

/// <summary>Ce qu'un membre a apporté sur la période.</summary>
/// <param name="ExpensesPaid">Dépenses communes payées depuis les comptes dont il est titulaire.</param>
/// <param name="Contributions">Versements nets vers les comptes joints.</param>
/// <param name="Income">Revenus perçus sur ses comptes (base du prorata).</param>
/// <param name="FixedPercent">Part fixe en pourcentage (mode parts fixes).</param>
public sealed record SplitMember(Guid UserId, decimal ExpensesPaid, decimal Contributions, decimal Income, decimal FixedPercent);

/// <param name="Share">Part retenue, en pourcentage (2 décimales).</param>
/// <param name="Paid">Total apporté : dépenses payées + versements nets aux comptes joints.</param>
/// <param name="Due">Ce que sa part représente du total apporté par le foyer.</param>
/// <param name="Balance">Paid − Due : positif, le foyer lui doit ; négatif, il doit au foyer.</param>
public sealed record SplitShare(Guid UserId, decimal Share, decimal Paid, decimal Due, decimal Balance);

/// <summary>Remboursement conseillé d'un membre à un autre pour équilibrer la période.</summary>
public sealed record Settlement(Guid FromUserId, Guid ToUserId, decimal Amount);

public sealed record SplitResult(SplitMode AppliedMode, decimal TotalPaid, IReadOnlyList<SplitShare> Shares, IReadOnlyList<Settlement> Settlements);

/// <summary>
/// Répartition des dépenses communes (K4). Chaque membre apporte au foyer en payant des dépenses communes depuis ses
/// comptes, ou en alimentant les comptes joints. Le total apporté est réparti selon les parts de chacun (égales, au
/// prorata des revenus, ou fixes) : l'écart entre ce qu'il a apporté et sa part donne son solde ; les soldes
/// s'annulent. Sans revenu sur la période, le prorata revient aux parts égales.
/// </summary>
public static class ExpenseSplit
{
    public static SplitResult Compute(SplitMode mode, IReadOnlyList<SplitMember> members)
    {
        if (members.Count == 0)
        {
            return new SplitResult(mode, 0m, [], []);
        }
        var applied = mode == SplitMode.IncomeProRata && members.Sum(m => Math.Max(0m, m.Income)) <= 0 ? SplitMode.Equal : mode;
        var weights = applied switch
        {
            SplitMode.IncomeProRata => members.Select(m => Math.Max(0m, m.Income)).ToList(),
            SplitMode.FixedShares => members.Select(m => Math.Max(0m, m.FixedPercent)).ToList(),
            _ => members.Select(_ => 1m).ToList(),
        };
        var totalWeight = weights.Sum();
        if (totalWeight <= 0)
        {
            applied = SplitMode.Equal;
            weights = [.. members.Select(_ => 1m)];
            totalWeight = members.Count;
        }

        var paid = members.Select(m => m.ExpensesPaid + m.Contributions).ToList();
        var total = paid.Sum();
        var shares = new List<SplitShare>(members.Count);
        var allocated = 0m;
        for (var i = 0; i < members.Count; i++)
        {
            var ratio = weights[i] / totalWeight;
            // Le dernier membre reçoit le reste des arrondis : la somme des parts dues égale le total apporté.
            var due = i == members.Count - 1 ? total - allocated : Round(total * ratio);
            allocated += due;
            shares.Add(new SplitShare(members[i].UserId, Round(ratio * 100m), paid[i], due, paid[i] - due));
        }
        return new SplitResult(applied, total, shares, Settle(shares));
    }

    /// <summary>Remboursements conseillés : le plus gros débiteur rembourse le plus gros créancier, jusqu'à équilibre.</summary>
    private static List<Settlement> Settle(IEnumerable<SplitShare> shares)
    {
        var debtors = shares.Where(s => s.Balance < 0).Select(s => (s.UserId, Amount: -s.Balance)).OrderByDescending(d => d.Amount).ToList();
        var creditors = shares.Where(s => s.Balance > 0).Select(s => (s.UserId, s.Balance)).OrderByDescending(c => c.Balance).ToList();
        var result = new List<Settlement>();
        int d = 0, c = 0;
        while (d < debtors.Count && c < creditors.Count)
        {
            var amount = Math.Min(debtors[d].Amount, creditors[c].Balance);
            if (amount > 0)
            {
                result.Add(new Settlement(debtors[d].UserId, creditors[c].UserId, amount));
            }
            debtors[d] = (debtors[d].UserId, debtors[d].Amount - amount);
            creditors[c] = (creditors[c].UserId, creditors[c].Balance - amount);
            if (debtors[d].Amount == 0)
            {
                d++;
            }
            if (creditors[c].Balance == 0)
            {
                c++;
            }
        }
        return result;
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}
