using HomeBudget.Api.Domain.Imports;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Imports;

/// <summary>Profils de lecture des relevés CSV et TSV (H1) : séparateurs, format des dates, colonnes.</summary>
public static class ImportProfileEndpoints
{
    public static IEndpointRouteBuilder MapImportProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/import-profiles").WithTags("Imports");

        group.MapGet("/", ListAsync)
            .WithSummary("Profils de lecture CSV et TSV, par nom.");
        group.MapPost("/", CreateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .WithSummary("Crée un profil de lecture.");
        group.MapPut("/{id:guid}", UpdateAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ImportProfileDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie un profil de lecture (If-Match requis).");
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime un profil de lecture (If-Match requis).");

        return app;
    }

    private static async Task<Ok<List<ImportProfileDto>>> ListAsync(AppDbContext db, CancellationToken ct)
    {
        var profiles = await db.ImportProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        return TypedResults.Ok(profiles.ConvertAll(ToDto));
    }

    private static async Task<Results<Created<ImportProfileDto>, ValidationProblem>> CreateAsync(
        ImportProfileRequest request, AppDbContext db, CancellationToken ct)
    {
        if (await ValidateAsync(request, db, null, ct) is { } problem)
        {
            return problem;
        }
        var profile = new ImportProfile { Name = request.Name.Trim() };
        Apply(profile, request);
        db.ImportProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/import-profiles/{profile.Id}", ToDto(profile));
    }

    private static async Task<IResult> UpdateAsync(Guid id, ImportProfileRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.ImportProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, profile) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, db, id, ct) is { } problem)
        {
            return problem;
        }
        Apply(profile, request);
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(ToDto(profile)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var profile = await db.ImportProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (profile is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, profile) is { } precondition)
        {
            return precondition;
        }
        db.ImportProfiles.Remove(profile);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    private static async Task<ValidationProblem?> ValidateAsync(ImportProfileRequest request, AppDbContext db, Guid? id, CancellationToken ct)
    {
        var errors = Options(request).Validate();
        var name = request.Name.Trim();
        if (name.Length == 0)
        {
            errors["name"] = ["Le nom est obligatoire."];
        }
        else if (await db.ImportProfiles.AnyAsync(p => p.Name == name && p.Id != id, ct))
        {
            errors["name"] = ["Un profil porte déjà ce nom."];
        }
        return errors.Count == 0 ? null : TypedResults.ValidationProblem(errors);
    }

    private static CsvOptions Options(ImportProfileRequest r) => new(
        r.Delimiter[0], r.DateFormat, r.DecimalSeparator[0], r.DateColumn, r.LabelColumn, r.AmountColumn, r.DebitColumn, r.CreditColumn);

    private static void Apply(ImportProfile profile, ImportProfileRequest request)
    {
        profile.Name = request.Name.Trim();
        profile.Delimiter = request.Delimiter[0];
        profile.DateFormat = request.DateFormat;
        profile.DecimalSeparator = request.DecimalSeparator[0];
        profile.DateColumn = request.DateColumn;
        profile.LabelColumn = request.LabelColumn;
        profile.AmountColumn = request.AmountColumn;
        profile.DebitColumn = request.AmountColumn is null ? request.DebitColumn : null;
        profile.CreditColumn = request.AmountColumn is null ? request.CreditColumn : null;
    }

    private static ImportProfileDto ToDto(ImportProfile p) => new(
        p.Id, p.Name, p.Delimiter.ToString(), p.DateFormat, p.DecimalSeparator.ToString(), p.DateColumn, p.LabelColumn,
        p.AmountColumn, p.DebitColumn, p.CreditColumn, p.Version);
}
