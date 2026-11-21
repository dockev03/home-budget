namespace HomeBudget.Api.Domain;

/// <summary>
/// Base des entités métier : identifiant UUID v7, généré à la création (côté client ou serveur),
/// triable chronologiquement et compatible avec la saisie hors ligne.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
