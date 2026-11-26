namespace HomeBudget.Api.Domain.Accounts;

/// <summary>Une mensualité du tableau d'amortissement.</summary>
/// <param name="Number">Rang, à partir de 1.</param>
/// <param name="Payment">Mensualité hors assurance : capital + intérêts.</param>
/// <param name="Remaining">Capital restant dû après cette mensualité.</param>
public readonly record struct LoanInstallment(
    int Number, DateOnly Date, decimal Payment, decimal Interest, decimal Principal, decimal Insurance, decimal Remaining);

/// <summary>
/// Tableau d'amortissement d'un crédit à mensualités constantes (A8). La mensualité hors assurance vaut
/// C × t / (1 − (1 + t)^−n), avec t le taux mensuel (taux annuel / 12) ; elle est arrondie au centime. Les intérêts
/// de chaque mois portent sur le capital restant dû, arrondis au centime ; la dernière mensualité solde le capital.
/// La k-ième échéance est calculée depuis la première (comme R6) : un départ le 31 donne le 28 février, puis le 31 mars.
/// </summary>
public sealed class LoanSchedule
{
    private LoanSchedule(decimal principal, decimal payment, IReadOnlyList<LoanInstallment> installments)
    {
        Principal = principal;
        Payment = payment;
        Installments = installments;
    }

    public decimal Principal { get; }

    /// <summary>Mensualité constante hors assurance (la dernière peut différer de quelques centimes).</summary>
    public decimal Payment { get; }

    public IReadOnlyList<LoanInstallment> Installments { get; }

    public decimal TotalInterest => Installments.Sum(i => i.Interest);

    public decimal TotalInsurance => Installments.Sum(i => i.Insurance);

    public DateOnly EndDate => Installments[^1].Date;

    public static LoanSchedule Compute(decimal principal, decimal annualRate, int durationMonths, DateOnly firstPaymentDate, decimal monthlyInsurance)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(principal);
        ArgumentOutOfRangeException.ThrowIfNegative(annualRate);
        ArgumentOutOfRangeException.ThrowIfNegative(monthlyInsurance);
        ArgumentOutOfRangeException.ThrowIfLessThan(durationMonths, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(durationMonths, Loan.MaxDurationMonths);

        var rate = annualRate / 100m / 12m;
        var payment = Round(rate == 0 ? principal / durationMonths : principal * rate / (1m - 1m / Power(1m + rate, durationMonths)));
        var installments = new List<LoanInstallment>(durationMonths);
        var remaining = principal;
        for (var number = 1; number <= durationMonths; number++)
        {
            var interest = Round(remaining * rate);
            var amortized = number == durationMonths ? remaining : Math.Min(remaining, payment - interest);
            remaining -= amortized;
            installments.Add(new LoanInstallment(
                number, firstPaymentDate.AddMonths(number - 1), amortized + interest, interest, amortized, monthlyInsurance, remaining));
        }
        return new LoanSchedule(principal, payment, installments);
    }

    public static LoanSchedule For(Loan loan) =>
        Compute(loan.Principal, loan.AnnualRate, loan.DurationMonths, loan.FirstPaymentDate, loan.MonthlyInsurance);

    /// <summary>Capital restant dû à la fin du jour <paramref name="date"/> : les mensualités échues ce jour-là sont payées.</summary>
    public decimal RemainingAt(DateOnly date) =>
        Installments.LastOrDefault(i => i.Date <= date) is { Number: > 0 } last ? last.Remaining : Principal;

    /// <summary>Nombre de mensualités échues à la fin du jour <paramref name="date"/>.</summary>
    public int PaidCountAt(DateOnly date) => Installments.Count(i => i.Date <= date);

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal Power(decimal value, int exponent)
    {
        var result = 1m;
        for (var i = 0; i < exponent; i++)
        {
            result *= value;
        }
        return result;
    }
}
