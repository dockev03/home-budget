namespace HomeBudget.Api.Domain.Savings;

/// <summary>Épargne de précaution (J3) : combien de mois de dépenses elle couvre.</summary>
public static class EmergencyFund
{
    /// <param name="amount">Solde des comptes d'épargne de précaution.</param>
    /// <param name="monthlyExpenses">Dépenses (fixes et variables) moyennes par période, en positif.</param>
    /// <returns>Mois couverts, à une décimale (arrondi inférieur), ou null sans dépense moyenne.</returns>
    public static decimal? MonthsCovered(decimal amount, decimal monthlyExpenses) =>
        monthlyExpenses <= 0 ? null : Math.Max(0m, Math.Floor(amount / monthlyExpenses * 10) / 10);
}
