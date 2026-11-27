namespace HomeBudget.Api.Domain.Categories;

public enum CategoryKind
{
    Income,
    Fixed,
    Variable,
    Savings,
}

/// <summary>Rôle d'une catégorie dans les suivis du foyer (P1, P2, P4) ; une sous-catégorie sans rôle prend celui de son parent.</summary>
public enum FamilyRole
{
    None,

    /// <summary>Aide attendue (CAF…) : ses échéances sont suivies, attendu contre reçu (P1). Réservé aux revenus.</summary>
    Benefit,

    /// <summary>Aide à la garde (CMG) : suivie comme une aide (P1) et déduite des frais de garde (P2). Réservé aux revenus.</summary>
    ChildcareAid,

    /// <summary>Frais de garde d'enfant (P2). Réservé aux dépenses.</summary>
    Childcare,

    /// <summary>Dépenses de santé, avec leurs remboursements (P4). Réservé aux dépenses.</summary>
    Health,
}

/// <summary>Catégorie sur deux niveaux au plus (C2) ; une sous-catégorie a le type de son parent.</summary>
public sealed class Category : AuditableEntity
{
    public Guid? ParentId { get; set; }

    public required string Name { get; set; }

    public CategoryKind Kind { get; set; }

    public string? Color { get; set; }

    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public bool IsArchived { get; set; }

    /// <summary>Dépenses personnelles : exclues de la répartition des dépenses communes (K4), sous-catégories comprises.</summary>
    public bool ExcludeFromSplit { get; set; }

    /// <summary>
    /// Report du reste activé (D6) : clé <c>AAAA-MM</c> de la première période dont le reste est reporté sur la
    /// suivante ; null si le report est désactivé. Réservé aux dépenses (fixes et variables).
    /// </summary>
    public string? CarryOverSince { get; set; }

    /// <summary>Rôle dans les suivis du foyer (P1, P2, P4).</summary>
    public FamilyRole FamilyRole { get; set; }

    /// <summary>Le rôle convient au type : les aides sont des revenus, la garde et la santé des dépenses.</summary>
    public static bool Fits(FamilyRole role, CategoryKind kind) => role switch
    {
        FamilyRole.None => true,
        FamilyRole.Benefit or FamilyRole.ChildcareAid => kind == CategoryKind.Income,
        _ => kind is CategoryKind.Fixed or CategoryKind.Variable,
    };

    /// <summary>Rôle effectif : le sien, sinon celui du parent.</summary>
    public FamilyRole EffectiveRole(IReadOnlyDictionary<Guid, Category> categories) =>
        FamilyRole != FamilyRole.None || ParentId is not { } parentId || !categories.TryGetValue(parentId, out var parent)
            ? FamilyRole
            : parent.FamilyRole;
}
