namespace HomeBudget.Api.Domain.Transactions;

/// <summary>
/// Opération : un en-tête et une ou plusieurs lignes (DM3). La ventilation (B5) est un ensemble
/// de lignes ; un virement (A4) est un couple d'opérations partageant le même <see cref="TransferId"/>.
/// </summary>
public sealed class Transaction : AuditableEntity
{
    public Guid AccountId { get; set; }

    public DateOnly Date { get; set; }

    public Guid? PayeeId { get; set; }

    public string? Label { get; set; }

    public string? Notes { get; set; }

    /// <summary>Somme des lignes, dénormalisée pour filtrer et totaliser en SQL.</summary>
    public decimal Amount { get; private set; }

    public bool IsCleared { get; set; }

    public Guid? TransferId { get; set; }

    public List<TransactionLine> Lines { get; private set; } = [];

    public void SetLines(IEnumerable<TransactionLine> lines)
    {
        Lines.Clear();
        var order = 0;
        foreach (var line in lines)
        {
            line.TransactionId = Id;
            line.SortOrder = order++;
            Lines.Add(line);
        }
        Amount = Lines.Sum(l => l.Amount);
    }
}

public sealed class TransactionLine : Entity
{
    public Guid TransactionId { get; set; }

    public Guid? CategoryId { get; set; }

    public decimal Amount { get; set; }

    public string? Memo { get; set; }

    public int SortOrder { get; set; }
}
