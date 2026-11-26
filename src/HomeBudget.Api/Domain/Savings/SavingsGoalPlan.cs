using HomeBudget.Api.Domain.Periods;

namespace HomeBudget.Api.Domain.Savings;

public enum SavingsGoalStatus
{
    /// <summary>En cours : la cible n'est pas atteinte, et la date cible n'est pas passée (ou n'existe pas).</summary>
    InProgress,

    /// <summary>L'épargne réunie atteint la cible.</summary>
    Reached,

    /// <summary>La date cible est passée sans que la cible soit atteinte.</summary>
    Overdue,
}

/// <param name="Saved">Épargne réunie.</param>
/// <param name="Remaining">Reste à réunir (0 si la cible est atteinte).</param>
/// <param name="Percent">Avancement, de 0 à 100 (arrondi à l'entier inférieur).</param>
/// <param name="RemainingPeriods">Périodes restantes jusqu'à la date cible, la période en cours comprise (null sans date cible).</param>
/// <param name="MonthlyContribution">Versement conseillé par période (J2) : null sans date cible, ou si elle est passée.</param>
public sealed record SavingsGoalProgress(
    decimal Saved, decimal Remaining, int Percent, SavingsGoalStatus Status, int? RemainingPeriods, decimal? MonthlyContribution);

/// <summary>Avancement d'un objectif d'épargne (J1) et versement mensuel conseillé (J2).</summary>
public static class SavingsGoalPlan
{
    /// <remarks>
    /// Le versement conseillé répartit le reste à réunir à parts égales sur les périodes qui restent jusqu'à celle de
    /// la date cible, la période en cours comprise, arrondi au centime supérieur pour ne jamais finir en dessous.
    /// </remarks>
    public static SavingsGoalProgress Compute(
        decimal target, decimal saved, DateOnly? targetDate, DateOnly today, PeriodCalculator calculator)
    {
        var remaining = Math.Max(0m, target - saved);
        var percent = target <= 0 ? 100 : (int)Math.Clamp(Math.Floor(saved / target * 100), 0, 100);
        int? periods = null;
        if (targetDate is { } date)
        {
            periods = date < today ? 0 : PeriodsBetween(calculator, calculator.For(today), calculator.For(date));
        }
        var status = remaining == 0 ? SavingsGoalStatus.Reached
            : targetDate < today ? SavingsGoalStatus.Overdue
            : SavingsGoalStatus.InProgress;
        decimal? monthly = periods is > 0 ? Math.Ceiling(remaining / periods.Value * 100) / 100 : null;
        return new SavingsGoalProgress(saved, remaining, percent, status, periods, monthly);
    }

    /// <summary>Nombre de périodes de <paramref name="from"/> à <paramref name="to"/>, toutes deux comprises.</summary>
    private static int PeriodsBetween(PeriodCalculator calculator, Period from, Period to)
    {
        var count = 1;
        for (var p = from; p.Start < to.Start; p = calculator.Next(p))
        {
            count++;
        }
        return count;
    }
}
