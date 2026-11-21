namespace HomeBudget.Api.Domain.Categories;

public enum CategoryKind
{
    Income,
    Fixed,
    Variable,
    Savings,
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
}
