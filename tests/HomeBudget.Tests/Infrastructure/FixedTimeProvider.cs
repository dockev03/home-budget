namespace HomeBudget.Tests.Infrastructure;

/// <summary>Horloge figée à midi (UTC) du jour donné : les calculs « au jour J » deviennent reproductibles.</summary>
public sealed class FixedTimeProvider(DateOnly today) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
