namespace HomeBudget.Api.Domain;

/// <summary>
/// Entité tracée (exigence K2) et protégée par concurrence optimiste. Les champs sont remplis
/// par <c>AuditInterceptor</c> à l'enregistrement : le code métier ne les touche jamais.
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Version incrémentée à chaque modification ; attendue dans l'en-tête If-Match.</summary>
    public int Version { get; set; }
}
