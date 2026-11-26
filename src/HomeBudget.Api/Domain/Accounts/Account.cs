namespace HomeBudget.Api.Domain.Accounts;

public enum AccountType
{
    Checking,
    Savings,
    Investment,
    Cash,
    Credit,
}

public enum AccountVisibility
{
    Shared,
    Private,
}

public sealed class Account : AuditableEntity
{
    public required string Name { get; set; }

    public AccountType Type { get; set; }

    /// <summary>Solde du compte au début du jour <see cref="InitialDate"/>.</summary>
    public decimal InitialBalance { get; set; }

    public DateOnly InitialDate { get; set; }

    /// <summary>Propriétaire d'un compte privé, seul à le voir (R8, filtre global de AppDbContext).</summary>
    public Guid? OwnerId { get; set; }

    public AccountVisibility Visibility { get; set; } = AccountVisibility.Shared;

    public string? Color { get; set; }

    public string? Icon { get; set; }

    public bool IncludeInForecast { get; set; }

    public bool IncludeInNetWorth { get; set; } = true;

    /// <summary>Seuil d'alerte de la prévision (F3).</summary>
    public decimal OverdraftThreshold { get; set; }

    public bool IsArchived { get; set; }

    public int SortOrder { get; set; }
}
