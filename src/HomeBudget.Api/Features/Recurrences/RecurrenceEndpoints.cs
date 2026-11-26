using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Payees;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Recurrences;

/// <summary>Récurrences et échéancier (E1 à E5) : saisie, et traitement des échéances « à valider ».</summary>
public static class RecurrenceEndpoints
{
    /// <summary>Horizon de recherche de la prochaine échéance.</summary>
    private const int NextDateHorizonYears = 10;

    public static IEndpointRouteBuilder MapRecurrenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recurrences").WithTags("Recurrences");

        group.MapGet("/", ListAsync)
            .WithSummary("Récurrences, avec leur prochaine échéance.");
        group.MapGet("/{id:guid}", GetAsync)
            .WithSummary("Une récurrence.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée une récurrence. Les échéances antérieures à aujourd'hui ne sont pas générées.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<RecurrenceDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une récurrence (If-Match requis). Les échéances déjà traitées ne changent pas.");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une récurrence (If-Match requis). Les opérations déjà créées sont conservées.");

        group.MapGet("/pending", PendingAsync)
            .WithSummary("Échéances à valider (E5), échues ou dont le report est arrivé à terme.");
        group.MapPost("/occurrences/{id:guid}/confirm", ConfirmAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<OccurrenceResultDto>().ProducesValidationProblem().WithOccurrenceProblems()
            .WithSummary("Confirme une échéance à valider : crée l'opération, avec un montant ajusté si besoin.");
        group.MapPost("/occurrences/{id:guid}/skip", SkipAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<OccurrenceResultDto>().WithOccurrenceProblems()
            .WithSummary("Saute une échéance à valider : elle n'aura pas lieu.");
        group.MapPost("/occurrences/{id:guid}/postpone", PostponeAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<OccurrenceResultDto>().ProducesValidationProblem().WithOccurrenceProblems()
            .WithSummary("Reporte une échéance à valider à une date ultérieure.");

        return app;
    }

    private static RouteHandlerBuilder WithOccurrenceProblems(this RouteHandlerBuilder builder) =>
        builder.ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed);

    private static async Task<Ok<List<RecurrenceDto>>> ListAsync(AppDbContext db, TimeProvider clock)
    {
        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .OrderBy(r => r.Label).ToListAsync();
        return TypedResults.Ok(await ToDtosAsync(db, clock, recurrences));
    }

    private static async Task<Results<Ok<RecurrenceDto>, NotFound>> GetAsync(Guid id, AppDbContext db, TimeProvider clock)
    {
        var recurrence = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences).FirstOrDefaultAsync(r => r.Id == id);
        return recurrence is null ? TypedResults.NotFound() : TypedResults.Ok((await ToDtosAsync(db, clock, [recurrence]))[0]);
    }

    private static async Task<Results<Created<RecurrenceDto>, ValidationProblem>> CreateAsync(
        RecurrenceRequest request, AppDbContext db, TimeProvider clock, RecurrenceGenerator generator)
    {
        if (await ValidateAsync(request, db, null) is { } problem)
        {
            return problem;
        }
        var recurrence = new Recurrence { Label = request.Label, GenerateFrom = Balances.Today(clock) };
        await ApplyAsync(recurrence, request, db);
        db.Recurrences.Add(recurrence);
        await db.SaveChangesAsync();
        await generator.RunAsync(recurrence.Id);
        return TypedResults.Created($"/api/recurrences/{recurrence.Id}", await LoadDtoAsync(db, clock, recurrence.Id));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id, RecurrenceRequest request, HttpRequest http, AppDbContext db, TimeProvider clock, RecurrenceGenerator generator)
    {
        var recurrence = await db.Recurrences.FirstOrDefaultAsync(r => r.Id == id);
        if (recurrence is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, recurrence) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, recurrence) is { } problem)
        {
            return problem;
        }
        var previousRule = recurrence.Rule;
        await ApplyAsync(recurrence, request, db);
        // Une nouvelle règle ne s'applique qu'à partir d'aujourd'hui : le passé n'est pas recalculé.
        var today = Balances.Today(clock);
        if (recurrence.Rule != previousRule && recurrence.GenerateFrom < today)
        {
            recurrence.GenerateFrom = today;
        }
        if (!await db.TrySaveAsync())
        {
            return Concurrency.Conflict();
        }
        await generator.RunAsync(recurrence.Id);
        return TypedResults.Ok(await LoadDtoAsync(db, clock, recurrence.Id));
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db)
    {
        var recurrence = await db.Recurrences.FirstOrDefaultAsync(r => r.Id == id);
        if (recurrence is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, recurrence) is { } precondition)
        {
            return precondition;
        }
        db.Recurrences.Remove(recurrence);
        return await db.TrySaveAsync() ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<Ok<List<PendingOccurrenceDto>>> PendingAsync(AppDbContext db, TimeProvider clock)
    {
        var today = Balances.Today(clock);
        var rows = await db.RecurrenceOccurrences.AsNoTracking()
            .Where(o => o.Status == OccurrenceStatus.Pending && o.DueDate <= today)
            .Join(db.Recurrences, o => o.RecurrenceId, r => r.Id, (o, r) => new { Occurrence = o, Recurrence = r })
            .ToListAsync();
        var payeeIds = rows.Select(r => r.Recurrence.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        return TypedResults.Ok(rows
            .OrderBy(r => r.Occurrence.DueDate).ThenBy(r => r.Recurrence.Label)
            .Select(r => new PendingOccurrenceDto(
                r.Occurrence.Id,
                r.Recurrence.Id,
                RecurrenceSchedule.Label(r.Recurrence.Label, r.Recurrence.IsInstallment, r.Occurrence.Index, r.Recurrence.Count),
                r.Recurrence.AccountId,
                r.Recurrence.ToAccountId,
                r.Recurrence.PayeeId is { } p ? payees.GetValueOrDefault(p) : null,
                r.Recurrence.CategoryId,
                r.Recurrence.Amount,
                r.Occurrence.ScheduledDate,
                r.Occurrence.DueDate))
            .ToList());
    }

    private static async Task<IResult> ConfirmAsync(
        Guid id, ConfirmOccurrenceRequest request, AppDbContext db, RecurrenceGenerator generator)
    {
        if (await LoadPendingAsync(db, id) is not ({ } occurrence, { } recurrence))
        {
            return await NotPendingAsync(db, id);
        }
        var amount = request.Amount ?? recurrence.Amount;
        var date = request.Date ?? occurrence.DueDate;
        var errors = new Dictionary<string, string[]>();
        CheckAmount(amount, recurrence.ToAccountId is not null, "amount", errors);
        await TransactionValidation.CheckAccountAsync(db, recurrence.AccountId, date, recurrence.AccountId, "accountId", errors);
        if (recurrence.ToAccountId is { } toAccountId)
        {
            await TransactionValidation.CheckAccountAsync(db, toAccountId, date, toAccountId, "toAccountId", errors);
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == recurrence.AccountId || a.Id == recurrence.ToAccountId)
            .ToDictionaryAsync(a => a.Id);
        var transaction = generator.CreateTransactions(recurrence, occurrence, date, amount, accounts);
        occurrence.Status = OccurrenceStatus.Generated;
        occurrence.TransactionId = transaction.Id;
        return await db.TrySaveAsync() ? TypedResults.Ok(ToResult(occurrence)) : Concurrency.Conflict();
    }

    private static async Task<IResult> SkipAsync(Guid id, AppDbContext db)
    {
        if (await LoadPendingAsync(db, id) is not ({ } occurrence, _))
        {
            return await NotPendingAsync(db, id);
        }
        occurrence.Status = OccurrenceStatus.Skipped;
        return await db.TrySaveAsync() ? TypedResults.Ok(ToResult(occurrence)) : Concurrency.Conflict();
    }

    private static async Task<IResult> PostponeAsync(Guid id, PostponeOccurrenceRequest request, AppDbContext db, TimeProvider clock)
    {
        if (await LoadPendingAsync(db, id) is not ({ } occurrence, _))
        {
            return await NotPendingAsync(db, id);
        }
        if (request.Date <= Balances.Today(clock) || request.Date > TransactionValidation.MaxDate)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["date"] = ["Choisissez une date à venir."],
            });
        }
        occurrence.DueDate = request.Date;
        return await db.TrySaveAsync() ? TypedResults.Ok(ToResult(occurrence)) : Concurrency.Conflict();
    }

    private static async Task<(RecurrenceOccurrence? Occurrence, Recurrence? Recurrence)> LoadPendingAsync(AppDbContext db, Guid id)
    {
        var occurrence = await db.RecurrenceOccurrences.FirstOrDefaultAsync(o => o.Id == id && o.Status == OccurrenceStatus.Pending);
        if (occurrence is null)
        {
            return (null, null);
        }
        return (occurrence, await db.Recurrences.AsNoTracking().FirstAsync(r => r.Id == occurrence.RecurrenceId));
    }

    private static async Task<IResult> NotPendingAsync(AppDbContext db, Guid id) =>
        await db.RecurrenceOccurrences.AnyAsync(o => o.Id == id)
            ? TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Échéance déjà traitée.",
                detail: "Cette échéance a déjà été confirmée ou sautée.")
            : TypedResults.NotFound();

    private static OccurrenceResultDto ToResult(RecurrenceOccurrence o) => new(o.Id, o.Status, o.DueDate, o.TransactionId);

    private static async Task<ValidationProblem?> ValidateAsync(RecurrenceRequest request, AppDbContext db, Recurrence? existing)
    {
        var rule = new RecurrenceRule(request.StartDate, request.Unit, request.Interval, request.End, request.EndDate, request.Count, request.BusinessDayShift);
        var errors = rule.Validate();
        if (!Enum.IsDefined(request.Mode))
        {
            errors["mode"] = ["Mode inconnu."];
        }
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            errors["label"] = ["Le libellé est obligatoire."];
        }
        if (!new AmountTolerance(request.Tolerance ?? 0, request.ToleranceUnit ?? ToleranceUnit.Amount).IsValid)
        {
            errors["tolerance"] = ["Tolérance invalide : de 0 à 100 % ou un montant positif (2 décimales au plus)."];
        }
        if (request.IsInstallment && request.End != RecurrenceEnd.AfterCount)
        {
            errors["end"] = ["Un paiement en plusieurs fois se termine après un nombre d'échéances."];
        }
        var isTransfer = request.ToAccountId is not null;
        CheckAmount(request.Amount, isTransfer, "amount", errors);

        await TransactionValidation.CheckAccountAsync(db, request.AccountId, request.StartDate, existing?.AccountId, "accountId", errors);
        if (request.ToAccountId is { } toAccountId)
        {
            if (toAccountId == request.AccountId)
            {
                errors["toAccountId"] = ["Choisissez deux comptes différents."];
            }
            else
            {
                await TransactionValidation.CheckAccountAsync(db, toAccountId, request.StartDate, existing?.ToAccountId, "toAccountId", errors);
            }
            if (!string.IsNullOrWhiteSpace(request.PayeeName))
            {
                errors["payeeName"] = ["Un virement n'a pas de tiers."];
            }
        }
        if (request.CategoryId is { } categoryId)
        {
            var categories = await TransactionValidation.LoadCategoriesAsync(db, [categoryId]);
            if (!categories.TryGetValue(categoryId, out var category))
            {
                errors["categoryId"] = ["Catégorie introuvable."];
            }
            else if (isTransfer && category.Kind != CategoryKind.Savings)
            {
                errors["categoryId"] = ["Un virement ne peut porter qu'une catégorie de type épargne."];
            }
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static void CheckAmount(decimal amount, bool isTransfer, string field, Dictionary<string, string[]> errors)
    {
        if (amount == 0)
        {
            errors[field] = ["Le montant ne peut pas être nul."];
        }
        else if (isTransfer && amount < 0)
        {
            errors[field] = ["Le montant d'un virement est positif."];
        }
        else if (!TransactionValidation.HasCents(amount))
        {
            errors[field] = ["Le montant a au plus 2 décimales."];
        }
    }

    private static async Task ApplyAsync(Recurrence recurrence, RecurrenceRequest request, AppDbContext db)
    {
        recurrence.Label = request.Label.Trim();
        recurrence.AccountId = request.AccountId;
        recurrence.ToAccountId = request.ToAccountId;
        recurrence.PayeeId = request.ToAccountId is null ? (await PayeeEndpoints.FindOrCreateAsync(db, request.PayeeName))?.Id : null;
        recurrence.CategoryId = request.CategoryId;
        recurrence.Amount = request.Amount;
        recurrence.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        recurrence.Mode = request.Mode;
        recurrence.StartDate = request.StartDate;
        recurrence.Unit = request.Unit;
        recurrence.Interval = request.Interval;
        recurrence.End = request.End;
        recurrence.EndDate = request.End == RecurrenceEnd.OnDate ? request.EndDate : null;
        recurrence.Count = request.End == RecurrenceEnd.AfterCount ? request.Count : null;
        recurrence.IsInstallment = request.IsInstallment;
        recurrence.Tolerance = request.Tolerance ?? 0;
        recurrence.ToleranceUnit = request.ToleranceUnit ?? ToleranceUnit.Amount;
        recurrence.IsSubscription = request.IsSubscription && request.ToAccountId is null;
        recurrence.BusinessDayShift = request.BusinessDayShift;
    }

    private static async Task<RecurrenceDto> LoadDtoAsync(AppDbContext db, TimeProvider clock, Guid id)
    {
        db.ChangeTracker.Clear();
        var recurrence = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences).FirstAsync(r => r.Id == id);
        return (await ToDtosAsync(db, clock, [recurrence]))[0];
    }

    private static async Task<List<RecurrenceDto>> ToDtosAsync(AppDbContext db, TimeProvider clock, IReadOnlyList<Recurrence> recurrences)
    {
        var today = Balances.Today(clock);
        var payeeIds = recurrences.Select(r => r.PayeeId).OfType<Guid>().Distinct().ToList();
        var payees = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        return [.. recurrences.Select(r =>
        {
            var next = RecurrenceSchedule.Remaining(r.Rule, r.GenerateFrom, r.States, today.AddYears(NextDateHorizonYears));
            return new RecurrenceDto(
                r.Id, r.Label, r.AccountId, r.ToAccountId, r.PayeeId,
                r.PayeeId is { } p ? payees.GetValueOrDefault(p) : null,
                r.CategoryId, r.Amount, r.Notes, r.Mode, r.StartDate, r.Unit, r.Interval, r.End, r.EndDate, r.Count,
                r.IsInstallment,
                r.Tolerance,
                r.ToleranceUnit,
                next.Count > 0 ? next[0].Date : null,
                r.Occurrences.Count(o => o.Status == OccurrenceStatus.Pending && o.DueDate <= today),
                r.Version,
                r.IsSubscription,
                r.BusinessDayShift);
        })];
    }
}
