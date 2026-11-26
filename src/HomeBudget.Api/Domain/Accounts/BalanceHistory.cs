namespace HomeBudget.Api.Domain.Accounts;

/// <summary>Montant daté : valorisation (A7) ou somme des opérations d'un jour.</summary>
public readonly record struct DatedAmount(DateOnly Date, decimal Amount);

/// <summary>
/// Historique d'un compte, pour calculer son solde à une date passée (G5). Avant sa date d'ouverture, un compte ne
/// compte pas son solde initial. Une valorisation (A7) remplace tout ce qui la précède : le solde est alors la
/// dernière valeur saisie, plus les opérations datées après elle.
/// </summary>
/// <param name="Valuations">Valorisations du compte, dans n'importe quel ordre.</param>
/// <param name="Movements">Opérations du compte, éventuellement regroupées par jour, dans n'importe quel ordre.</param>
public sealed record BalanceHistory(
    decimal InitialBalance, DateOnly InitialDate, IReadOnlyList<DatedAmount> Valuations, IReadOnlyList<DatedAmount> Movements)
{
    /// <summary>Solde à la fin du jour <paramref name="date"/>.</summary>
    public decimal At(DateOnly date)
    {
        var baseline = date >= InitialDate ? InitialBalance : 0m;
        DateOnly? after = null;
        foreach (var v in Valuations)
        {
            if (v.Date <= date && (after is null || v.Date > after))
            {
                after = v.Date;
                baseline = v.Amount;
            }
        }
        return baseline + Movements.Where(m => m.Date <= date && (after is null || m.Date > after)).Sum(m => m.Amount);
    }
}
