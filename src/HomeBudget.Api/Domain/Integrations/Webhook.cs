namespace HomeBudget.Api.Domain.Integrations;

/// <summary>
/// Webhook (I5) : à chaque événement choisi, un message JSON signé en HMAC-SHA256 est envoyé à l'adresse.
/// Créé par un administrateur, il ne reçoit que ce que voit son propriétaire (R8) : une opération d'un compte
/// privé d'un autre membre n'y apparaît jamais.
/// </summary>
public sealed class Webhook : AuditableEntity
{
    public const int MaxNameLength = 60;
    public const int MaxUrlLength = 500;

    /// <summary>Envois définitivement en échec à la suite, au-delà desquels le webhook est désactivé.</summary>
    public const int MaxConsecutiveFailures = 5;

    public required string Name { get; set; }

    public required string Url { get; set; }

    /// <summary>Secret de signature, chiffré par la protection des données ASP.NET (clés dans /data/keys).</summary>
    public required string ProtectedSecret { get; set; }

    /// <summary>Événements abonnés (<see cref="WebhookEvents"/>).</summary>
    public List<string> Events { get; set; } = [];

    public bool IsActive { get; set; } = true;

    /// <summary>Administrateur qui a créé le webhook : ses droits de lecture filtrent les événements (R8).</summary>
    public Guid OwnerId { get; set; }

    public int ConsecutiveFailures { get; set; }
}

/// <summary>Événements des webhooks.</summary>
public static class WebhookEvents
{
    public const string TransactionCreated = "transaction.created";
    public const string TransactionUpdated = "transaction.updated";
    public const string TransactionDeleted = "transaction.deleted";
    public const string ImportCompleted = "import.completed";

    /// <summary>Envoi de test, déclenché depuis l'interface.</summary>
    public const string Ping = "ping";

    public static readonly IReadOnlyList<string> All = [TransactionCreated, TransactionUpdated, TransactionDeleted, ImportCompleted];
}

public enum WebhookDeliveryStatus
{
    Pending,
    Succeeded,
    Failed,
}

/// <summary>
/// Envoi d'un événement à un webhook (file d'attente) : enregistré avec la modification qui le produit, puis envoyé
/// par <c>WebhookWorker</c>, avec de nouvelles tentatives espacées en cas d'échec.
/// </summary>
public sealed class WebhookDelivery : Entity
{
    public const int MaxErrorLength = 300;

    /// <summary>Délais avant chaque nouvelle tentative : 1 min, 5 min, 30 min, 2 h, 12 h.</summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(12)];

    public Guid WebhookId { get; set; }

    public required string Event { get; set; }

    /// <summary>Corps JSON envoyé, figé à la création de l'événement.</summary>
    public required string Payload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public WebhookDeliveryStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }
}
