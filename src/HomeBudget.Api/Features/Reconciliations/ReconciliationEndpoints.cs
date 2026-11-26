using System.ComponentModel.DataAnnotations;
using System.Globalization;

using HomeBudget.Api.Domain.Reconciliations;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Reconciliations;

/// <summary>
/// Rapprochement bancaire (A5, R7) : le solde du relevé est comparé au solde des opérations pointées à la même date ;
/// sans écart, ces opérations sont rapprochées et verrouillées. Seul le dernier rapprochement d'un compte s'annule.
/// </summary>
public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts/{accountId:guid}/reconciliations").WithTags("Reconciliations");

        group.MapGet("/", ListAsync)
            .WithSummary("Rapprochements du compte, du plus récent au plus ancien.");
        group.MapGet("/preview", PreviewAsync)
            .Produces<ReconciliationPreviewDto>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Solde pointé du compte à la date du relevé, et écart avec le solde du relevé. Rien n'est enregistré.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ReconciliationDto>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Rapproche le compte : sans écart, les opérations pointées jusqu'à la date du relevé sont verrouillées.");
        group.MapDelete("/{id:guid}", CancelAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Annule le dernier rapprochement du compte (If-Match requis) : ses opérations sont déverrouillées et restent pointées.");

        return app;
    }

    public sealed record ReconciliationRequest(DateOnly Date, [property: Range(-999_999_999, 999_999_999)] decimal StatementBalance);

    /// <param name="TransactionCount">Opérations rapprochées.</param>
    public sealed record ReconciliationDto(
        Guid Id, Guid AccountId, DateOnly Date, decimal StatementBalance, int TransactionCount, DateTimeOffset CreatedAt, int Version);

    /// <param name="ClearedBalance">Solde initial plus les opérations pointées (ou déjà rapprochées) jusqu'à la date.</param>
    /// <param name="Difference">Solde du relevé moins solde pointé : 0 pour pouvoir rapprocher.</param>
    /// <param name="ToReconcileCount">Opérations pointées qui seront rapprochées.</param>
    /// <param name="UnclearedCount">Opérations non pointées jusqu'à la date : à pointer si elles figurent sur le relevé.</param>
    /// <param name="LastDate">Date du dernier rapprochement du compte.</param>
    public sealed record ReconciliationPreviewDto(
        DateOnly Date, decimal StatementBalance, decimal ClearedBalance, decimal Difference, int ToReconcileCount, int UnclearedCount, DateOnly? LastDate);

    private static async Task<Results<Ok<List<ReconciliationDto>>, NotFound>> ListAsync(Guid accountId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Accounts.AnyAsync(a => a.Id == accountId, ct))
        {
            return TypedResults.NotFound();
        }
        var list = await db.Reconciliations.AsNoTracking()
            .Where(r => r.AccountId == accountId)
            .OrderByDescending(r => r.Date)
            .Select(r => new ReconciliationDto(
                r.Id, r.AccountId, r.Date, r.StatementBalance, db.Transactions.Count(t => t.ReconciliationId == r.Id), r.CreatedAt, r.Version))
            .ToListAsync(ct);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> PreviewAsync(
        Guid accountId, DateOnly date, decimal statementBalance, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var (preview, problem) = await ComputeAsync(accountId, new ReconciliationRequest(date, statementBalance), db, clock, ct);
        return problem ?? TypedResults.Ok(preview);
    }

    private static async Task<IResult> CreateAsync(
        Guid accountId, ReconciliationRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var (preview, problem) = await ComputeAsync(accountId, request, db, clock, ct);
        if (problem is not null)
        {
            return problem;
        }
        if (preview!.Difference != 0)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["statementBalance"] =
                [
                    $"Écart de {preview.Difference.ToString("N2", CultureInfo.GetCultureInfo("fr-FR"))} € avec le solde pointé : pointez les opérations du relevé ou corrigez le solde.",
                ],
            });
        }

        var reconciliation = new Reconciliation { AccountId = accountId, Date = request.Date, StatementBalance = request.StatementBalance };
        db.Reconciliations.Add(reconciliation);
        await db.SaveChangesAsync(ct);
        var count = await db.Transactions
            .Where(t => t.AccountId == accountId && t.Date <= request.Date && t.IsCleared && t.ReconciliationId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReconciliationId, reconciliation.Id), ct);
        await transaction.CommitAsync(ct);

        return TypedResults.Created(
            $"/api/accounts/{accountId}/reconciliations/{reconciliation.Id}",
            new ReconciliationDto(reconciliation.Id, accountId, reconciliation.Date, reconciliation.StatementBalance, count, reconciliation.CreatedAt, reconciliation.Version));
    }

    private static async Task<IResult> CancelAsync(Guid accountId, Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var reconciliation = await db.Reconciliations.FirstOrDefaultAsync(r => r.Id == id && r.AccountId == accountId, ct);
        if (reconciliation is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, reconciliation) is { } precondition)
        {
            return precondition;
        }
        if (await db.Reconciliations.AnyAsync(r => r.AccountId == accountId && r.Date > reconciliation.Date, ct))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Ce n'est pas le dernier rapprochement du compte.",
                detail: "Annulez d'abord les rapprochements plus récents.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Transactions.Where(t => t.ReconciliationId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReconciliationId, (Guid?)null), ct);
        db.Reconciliations.Remove(reconciliation);
        if (!await db.TrySaveAsync(ct))
        {
            return Concurrency.Conflict();
        }
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Solde pointé et écart à la date du relevé, après validation du compte et de la date.</summary>
    private static async Task<(ReconciliationPreviewDto? Preview, IResult? Problem)> ComputeAsync(
        Guid accountId, ReconciliationRequest request, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return (null, TypedResults.NotFound());
        }
        var lastDate = await db.Reconciliations.Where(r => r.AccountId == accountId)
            .MaxAsync(r => (DateOnly?)r.Date, ct);
        var errors = new Dictionary<string, string[]>();
        if (request.Date < account.InitialDate)
        {
            errors["date"] = [$"Date antérieure au solde initial du compte ({account.InitialDate:dd/MM/yyyy})."];
        }
        else if (request.Date > Balances.Today(clock))
        {
            errors["date"] = ["La date du relevé ne peut pas être dans le futur."];
        }
        else if (lastDate is { } last && request.Date <= last)
        {
            errors["date"] = [$"Le compte est déjà rapproché au {last:dd/MM/yyyy} : choisissez une date ultérieure."];
        }
        if (!TransactionValidation.HasCents(request.StatementBalance))
        {
            errors["statementBalance"] = ["Le montant a au plus 2 décimales."];
        }
        if (errors.Count > 0)
        {
            return (null, TypedResults.ValidationProblem(errors));
        }

        var until = db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId && t.Date <= request.Date);
        var cleared = await until.Where(t => t.IsCleared || t.ReconciliationId != null).SumAsync(t => t.Amount, ct);
        var toReconcile = await until.CountAsync(t => t.IsCleared && t.ReconciliationId == null, ct);
        var uncleared = await until.CountAsync(t => !t.IsCleared && t.ReconciliationId == null, ct);
        var clearedBalance = account.InitialBalance + cleared;
        return (new ReconciliationPreviewDto(
            request.Date, request.StatementBalance, clearedBalance, request.StatementBalance - clearedBalance, toReconcile, uncleared, lastDate), null);
    }
}
