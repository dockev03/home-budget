namespace HomeBudget.Api.Domain.Rules;

/// <summary>
/// Règle apprise des corrections (I2, I3) : un libellé bancaire, réduit à sa clé (<see cref="LabelKey"/>),
/// donne un tiers et une catégorie. Elle est créée ou mise à jour quand l'utilisateur corrige une opération
/// importée, et appliquée aux imports suivants.
/// </summary>
public sealed class LabelRule : AuditableEntity
{
    public required string Key { get; set; }

    public Guid? PayeeId { get; set; }

    public Guid? CategoryId { get; set; }
}
