using System.ComponentModel.DataAnnotations;

using HomeBudget.Api.Domain.Transactions;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Sync;

public enum SyncItemStatus
{
    /// <summary>Opération créée.</summary>
    Created,

    /// <summary>Déjà reçue lors d'un envoi précédent (réponse perdue) : rien n'est créé une seconde fois.</summary>
    AlreadyApplied,

    /// <summary>Refusée : données invalides (compte archivé, catégorie supprimée…) ou identifiant déjà pris.</summary>
    Rejected,
}

/// <summary>
/// Rejeu de la file d'attente hors ligne (L4) : seules les créations d'opérations sont mises en file. Chaque élément
/// porte l'identifiant UUID v7 choisi par l'appareil, qui rend l'envoi idempotent : renvoyer la file après une
/// coupure ne crée pas de doublon. Les éléments sont traités un à un : un refus métier (compte archivé, catégorie
/// supprimée) n'empêche pas les suivants. Un élément mal formé (longueur, format) fait refuser toute la requête
/// en 400 ; l'appareil renvoie alors les éléments un par un pour isoler le fautif.
/// </summary>
public static class SyncEndpoints
{
    public const int MaxItems = 100;

    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/sync", SyncAsync).WithTags("Sync").RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<SyncResponse>().ProducesValidationProblem()
            .WithSummary("Rejoue les opérations saisies hors ligne (identifiants fournis par l'appareil, envoi idempotent).");
        return app;
    }

    /// <param name="Id">Identifiant UUID v7 attribué par l'appareil à la saisie.</param>
    public sealed record SyncItem(Guid Id, [property: Required] TransactionRequest Transaction);

    public sealed record SyncRequest([property: Required, MinLength(1), MaxLength(MaxItems)] IReadOnlyList<SyncItem> Items);

    /// <param name="Errors">Motifs du refus, par champ.</param>
    /// <param name="Transaction">Opération créée ou déjà reçue.</param>
    public sealed record SyncItemResult(Guid Id, SyncItemStatus Status, IDictionary<string, string[]>? Errors, TransactionDto? Transaction);

    public sealed record SyncResponse(IReadOnlyList<SyncItemResult> Items);

    private static async Task<IResult> SyncAsync(SyncRequest request, AppDbContext db, CancellationToken ct)
    {
        if (request.Items.Any(i => i.Id == Guid.Empty) || request.Items.Select(i => i.Id).Distinct().Count() != request.Items.Count)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["items"] = ["Chaque élément doit avoir un identifiant unique."],
            });
        }

        var results = new List<SyncItemResult>(request.Items.Count);
        foreach (var item in request.Items)
        {
            results.Add(await ApplyAsync(item, db, ct));
            db.ChangeTracker.Clear();
        }
        return TypedResults.Ok(new SyncResponse(results));
    }

    private static async Task<SyncItemResult> ApplyAsync(SyncItem item, AppDbContext db, CancellationToken ct)
    {
        var existing = await db.Transactions.AsNoTracking().Include(t => t.Lines).ThenInclude(l => l.Tags)
            .FirstOrDefaultAsync(t => t.Id == item.Id, ct);
        if (existing is not null)
        {
            return new SyncItemResult(item.Id, SyncItemStatus.AlreadyApplied, null, (await TransactionEndpoints.ToDtosAsync(db, [existing]))[0]);
        }
        // Identifiant pris par une opération invisible (compte privé d'un autre membre) : refus, sans rien en dire.
        if (await db.Transactions.IgnoreQueryFilters().AnyAsync(t => t.Id == item.Id, ct))
        {
            return Rejected(item.Id, "id", "Identifiant déjà utilisé.");
        }
        if (await TransactionEndpoints.ValidateAsync(item.Transaction, db, null) is { } problem)
        {
            return new SyncItemResult(item.Id, SyncItemStatus.Rejected, problem.ProblemDetails.Errors, null);
        }

        var transaction = new Transaction { Id = item.Id };
        await TransactionEndpoints.ApplyAsync(transaction, item.Transaction, db);
        db.Transactions.Add(transaction);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Deux envois simultanés de la même file : le second trouve l'opération créée par le premier.
            db.ChangeTracker.Clear();
            var created = await db.Transactions.AsNoTracking().Include(t => t.Lines).ThenInclude(l => l.Tags)
                .FirstOrDefaultAsync(t => t.Id == item.Id, ct);
            return created is null
                ? Rejected(item.Id, "id", "Enregistrement impossible.")
                : new SyncItemResult(item.Id, SyncItemStatus.AlreadyApplied, null, (await TransactionEndpoints.ToDtosAsync(db, [created]))[0]);
        }
        return new SyncItemResult(item.Id, SyncItemStatus.Created, null, (await TransactionEndpoints.ToDtosAsync(db, [transaction]))[0]);
    }

    private static SyncItemResult Rejected(Guid id, string field, string message) =>
        new(id, SyncItemStatus.Rejected, new Dictionary<string, string[]> { [field] = [message] }, null);
}
