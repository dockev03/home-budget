using System.Globalization;
using System.Text;

using HomeBudget.Api.Domain.Audit;
using HomeBudget.Api.Domain.Categories;
using HomeBudget.Api.Domain.Family;
using HomeBudget.Api.Domain.Payees;
using HomeBudget.Api.Domain.Recurrences;
using HomeBudget.Api.Features.Accounts;
using HomeBudget.Api.Features.Exports;
using HomeBudget.Api.Features.Transactions;
using HomeBudget.Api.Infrastructure;
using HomeBudget.Api.Infrastructure.Data;
using HomeBudget.Api.Infrastructure.Http;

using Microsoft.EntityFrameworkCore;

namespace HomeBudget.Api.Features.Family;

/// <summary>
/// Suivis du foyer (lot L6) : aides attendues contre reçues (P1), frais de garde de l'année (P2) et reste à charge
/// des dépenses de santé (P4). Les catégories y entrent par leur rôle (<see cref="FamilyRole"/>), les remboursements
/// de santé sont classés par l'organisme de leur tiers (<see cref="Reimburser"/>). Les filtres de requête écartent
/// les comptes privés des autres membres (R8). Les années sont civiles : celles de la déclaration d'impôts et de la CAF.
/// </summary>
public static class FamilyEndpoints
{
    public static IEndpointRouteBuilder MapFamilyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/family").WithTags("Family");

        group.MapGet("/benefits/{year:int}", BenefitsAsync)
            .Produces<BenefitsDto>().ProducesValidationProblem()
            .WithSummary("Aides de l'année civile (P1) : chaque échéance attendue, le montant reçu et l'écart, plus les versements hors échéancier.");
        group.MapGet("/childcare/{year:int}", ChildcareAsync)
            .Produces<ChildcareDto>().ProducesValidationProblem()
            .WithSummary("Frais de garde de l'année civile (P2) : payé, aides à la garde reçues et montant déclarable, par mois et par tiers.");
        group.MapGet("/childcare/{year:int}/export", ExportChildcareAsync)
            .Produces(StatusCodes.Status200OK, contentType: ExportEndpoints.CsvContentType)
            .ProducesValidationProblem()
            .WithSummary("Exporte le récapitulatif des frais de garde de l'année en CSV.");
        group.MapGet("/childcare/{year:int}/declarations", DeclarationsAsync)
            .Produces<List<ChildcareDeclarationDto>>().ProducesValidationProblem()
            .WithSummary("Déclarations Pajemploi de l'année (P2 bis), par mois puis par salarié, avec leurs totaux.");
        group.MapPost("/childcare/declarations", CreateDeclarationAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ChildcareDeclarationDto>(StatusCodes.Status201Created).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Saisit la déclaration Pajemploi d'un mois pour un salarié (une seule par mois et par salarié).");
        group.MapPut("/childcare/declarations/{id:guid}", UpdateDeclarationAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces<ChildcareDeclarationDto>().ProducesValidationProblem().WithConcurrencyProblems()
            .WithSummary("Modifie une déclaration Pajemploi (If-Match requis).");
        group.MapDelete("/childcare/declarations/{id:guid}", DeleteDeclarationAsync).RequireAuthorization(AuthorizationPolicies.Write)
            .Produces(StatusCodes.Status204NoContent).WithConcurrencyProblems()
            .WithSummary("Supprime une déclaration Pajemploi (If-Match requis). Les opérations ne sont pas modifiées.");
        group.MapGet("/health", HealthAsync)
            .Produces<HealthDto>().ProducesValidationProblem()
            .WithSummary("Dépenses de santé de la plage (P4), avec les remboursements CPAM et mutuelle liés et le reste à charge (défaut : l'année en cours).");

        return app;
    }

    /// <param name="RecurrenceId">Récurrence de l'échéance ; null pour un versement hors échéancier.</param>
    /// <param name="Date">Date d'effet de l'échéance, ou date du versement.</param>
    /// <param name="Expected">Montant attendu (0 hors échéancier).</param>
    /// <param name="Received">Montant reçu ; null s'il n'y a pas d'opération.</param>
    /// <param name="Gap">Reçu − attendu ; null pour une aide à venir ou à valider.</param>
    public sealed record BenefitRowDto(
        Guid? RecurrenceId, Guid? OccurrenceId, Guid? TransactionId, Guid CategoryId, string CategoryName, string Label,
        DateOnly Date, BenefitStatus Status, decimal Expected, decimal? Received, decimal? Gap);

    /// <param name="Expected">Attendu des aides échues (à venir exclues).</param>
    /// <param name="Received">Total reçu, hors échéancier compris.</param>
    /// <param name="Gap">Somme des écarts connus.</param>
    /// <param name="Upcoming">Attendu des aides à venir dans l'année.</param>
    public sealed record BenefitTotalsDto(Guid CategoryId, string CategoryName, decimal Expected, decimal Received, decimal Gap, decimal Upcoming);

    public sealed record BenefitsDto(int Year, List<BenefitRowDto> Rows, List<BenefitTotalsDto> Categories, BenefitTotalsDto Total);

    /// <param name="Paid">Frais de garde payés (en positif).</param>
    /// <param name="Aid">Aides à la garde reçues (CMG…).</param>
    /// <param name="Net">Payé − aides.</param>
    /// <param name="Declarations">Nombre de déclarations Pajemploi saisies pour le mois.</param>
    /// <param name="DeclaredRemuneration">Rémunération déclarée (salaire net et indemnités), tous salariés.</param>
    /// <param name="DeclaredCmg">Droit CMG déclaré (hors cotisations prises en charge, qui ne passent pas par le compte).</param>
    /// <param name="PaidGap">Payé − rémunération déclarée ; null sans déclaration.</param>
    /// <param name="AidGap">Aides reçues − droit CMG déclaré ; null sans déclaration.</param>
    public sealed record ChildcareMonthDto(
        int Month, decimal Paid, decimal Aid, decimal Net,
        int Declarations = 0, decimal DeclaredRemuneration = 0m, decimal DeclaredCmg = 0m, decimal? PaidGap = null, decimal? AidGap = null);

    /// <summary>Champs saisis d'une déclaration Pajemploi (montants et heures à 2 décimales au plus, positifs).</summary>
    public sealed record ChildcareDeclarationRequest(
        int Year,
        int Month,
        Guid PayeeId,
        int ActivityDays,
        decimal NormalHours,
        decimal ExtraHours,
        decimal OvertimeHours,
        decimal PaidLeaveDays,
        decimal? NetHourlyRate,
        decimal NetSalary,
        decimal MaintenanceAllowance,
        decimal MealAllowance,
        decimal MileageAllowance,
        decimal Advance,
        int LongCareDays,
        bool DisabledChildCare,
        bool IsContractEnd,
        DateOnly? ContractEndDate,
        decimal PrecariousnessAllowance,
        decimal PaidLeaveCompensation,
        decimal NoticeCompensation,
        decimal TerminationAllowance,
        decimal Contributions,
        decimal CoveredContributions,
        decimal CmgEntitlement);

    /// <param name="Remuneration">Salaire net déclaré : salaire, indemnités et indemnités de fin de contrat.</param>
    /// <param name="TotalCost">Coût total de l'emploi : rémunération + cotisations.</param>
    /// <param name="Cmg">Complément du mode de garde : cotisations prises en charge + droit CMG.</param>
    /// <param name="Remaining">Montant restant à charge : coût total − CMG.</param>
    public sealed record ChildcareDeclarationDto(
        Guid Id,
        int Year,
        int Month,
        Guid PayeeId,
        string PayeeName,
        int ActivityDays,
        decimal NormalHours,
        decimal ExtraHours,
        decimal OvertimeHours,
        decimal PaidLeaveDays,
        decimal? NetHourlyRate,
        decimal NetSalary,
        decimal MaintenanceAllowance,
        decimal MealAllowance,
        decimal MileageAllowance,
        decimal Advance,
        int LongCareDays,
        bool DisabledChildCare,
        bool IsContractEnd,
        DateOnly? ContractEndDate,
        decimal PrecariousnessAllowance,
        decimal PaidLeaveCompensation,
        decimal NoticeCompensation,
        decimal TerminationAllowance,
        decimal Contributions,
        decimal CoveredContributions,
        decimal CmgEntitlement,
        decimal Remuneration,
        decimal TotalCost,
        decimal Cmg,
        decimal Remaining,
        int Version);

    public sealed record ChildcarePayeeDto(Guid? PayeeId, string Name, decimal Paid);

    /// <param name="Declarable">Payé − aides sur l'année, jamais négatif : le montant à reporter sur la déclaration.</param>
    public sealed record ChildcareDto(
        int Year, List<ChildcareMonthDto> Months, List<ChildcarePayeeDto> Payees, decimal Paid, decimal Aid, decimal Declarable);

    /// <param name="Expense">Montant des lignes de santé de l'opération (en positif).</param>
    /// <param name="PrimaryInsurance">Remboursements liés (B11) dont le tiers est la CPAM.</param>
    /// <param name="Mutual">Remboursements liés dont le tiers est une mutuelle.</param>
    /// <param name="Other">Autres remboursements liés.</param>
    /// <param name="Remaining">Reste à charge.</param>
    public sealed record HealthRowDto(
        Guid TransactionId, DateOnly Date, string? Payee, string? Label, string CategoryName,
        decimal Expense, decimal PrimaryInsurance, decimal Mutual, decimal Other, decimal Remaining);

    /// <param name="Unlinked">Remboursements classés en santé mais liés à aucune dépense : déjà déduits par la catégorie (R2), pas par ligne.</param>
    public sealed record HealthDto(
        DateOnly From, DateOnly To, List<HealthRowDto> Rows,
        decimal Expense, decimal PrimaryInsurance, decimal Mutual, decimal Other, decimal Remaining, decimal Unlinked);

    private static bool ValidYear(int year) => year is >= 1901 and <= 9998;

    private static IResult InvalidYear() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["year"] = ["Année invalide."] });

    /// <summary>Catégories et leur rôle effectif (une sous-catégorie sans rôle prend celui de son parent).</summary>
    private static async Task<Dictionary<Guid, (Category Category, FamilyRole Role)>> RolesAsync(AppDbContext db, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        return categories.Values.ToDictionary(c => c.Id, c => (c, c.EffectiveRole(categories)));
    }

    private static async Task<IResult> BenefitsAsync(int year, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!ValidYear(year))
        {
            return InvalidYear();
        }
        var (start, end) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        var today = Balances.Today(clock);
        var roles = await RolesAsync(db, ct);
        var benefitIds = roles.Values.Where(r => r.Role is FamilyRole.Benefit or FamilyRole.ChildcareAid).Select(r => r.Category.Id).ToList();

        var recurrences = await db.Recurrences.AsNoTracking().Include(r => r.Occurrences)
            .Where(r => r.CategoryId != null && benefitIds.Contains(r.CategoryId.Value))
            .ToListAsync(ct);
        var transactionIds = recurrences.SelectMany(r => r.Occurrences).Where(o => o.TransactionId != null).Select(o => o.TransactionId!.Value).ToList();
        var linked = await db.Transactions.AsNoTracking()
            .Where(t => transactionIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Amount, ct);

        var rows = new List<BenefitRowDto>();
        foreach (var recurrence in recurrences)
        {
            var category = roles[recurrence.CategoryId!.Value].Category;
            foreach (var o in recurrence.Occurrences.Where(o => o.DueDate >= start && o.DueDate <= end))
            {
                var expected = o.ExpectedAmount ?? recurrence.Amount;
                decimal? received = o.TransactionId is { } id && linked.TryGetValue(id, out var amount) ? amount : null;
                var status = o.Status switch
                {
                    OccurrenceStatus.Pending => BenefitStatus.Pending,
                    OccurrenceStatus.Skipped => BenefitStatus.Skipped,
                    _ => received is null ? BenefitStatus.Missing : BenefitStatus.Received,
                };
                var label = RecurrenceSchedule.Label(recurrence.Label, recurrence.IsInstallment, o.Index, recurrence.Count);
                rows.Add(new BenefitRowDto(
                    recurrence.Id, o.Id, received is null ? null : o.TransactionId, category.Id, category.Name, label, o.DueDate, status,
                    expected, received, BenefitGap.Of(status, expected, received)));
            }

            // À venir : échéances prévues dans l'année, après aujourd'hui, pas encore traitées.
            var known = recurrence.Occurrences.Select(o => o.ScheduledDate).ToHashSet();
            var from = new[] { start, recurrence.GenerateFrom, today.AddDays(1) }.Max();
            if (from <= end)
            {
                foreach (var s in recurrence.Rule.Between(from, end).Where(s => !known.Contains(s.Date)))
                {
                    var label = RecurrenceSchedule.Label(recurrence.Label, recurrence.IsInstallment, s.Index, recurrence.Count);
                    rows.Add(new BenefitRowDto(
                        recurrence.Id, null, null, category.Id, category.Name, label, s.Date, BenefitStatus.Upcoming, recurrence.Amount, null, null));
                }
            }
        }

        // Versements hors échéancier : opérations d'une catégorie d'aide qui ne viennent d'aucune échéance.
        var fromOccurrences = await db.RecurrenceOccurrences.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.TransactionId != null).Select(o => o.TransactionId!.Value).ToListAsync(ct);
        var scheduledIds = fromOccurrences.ToHashSet();
        var unscheduled = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Id, t.Date, t.Label, t.TransferId })
            .Where(x => x.Date >= start && x.Date <= end && x.CategoryId != null && benefitIds.Contains(x.CategoryId.Value))
            .ToListAsync(ct);
        foreach (var group in unscheduled.Where(x => !scheduledIds.Contains(x.Id)).GroupBy(x => new { x.Id, x.CategoryId }))
        {
            var first = group.First();
            var category = roles[first.CategoryId!.Value].Category;
            var received = group.Sum(x => x.Amount);
            rows.Add(new BenefitRowDto(
                null, null, first.Id, category.Id, category.Name, first.Label ?? category.Name, first.Date, BenefitStatus.Unscheduled,
                0m, received, BenefitGap.Of(BenefitStatus.Unscheduled, 0m, received)));
        }

        rows = [.. rows.OrderBy(r => r.Date).ThenBy(r => r.CategoryName, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Label)];
        var totals = rows.GroupBy(r => (r.CategoryId, r.CategoryName))
            .Select(g => Totals(g.Key.CategoryId, g.Key.CategoryName, g))
            .OrderBy(t => t.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return TypedResults.Ok(new BenefitsDto(year, rows, totals, Totals(Guid.Empty, "Total", rows)));
    }

    private static BenefitTotalsDto Totals(Guid categoryId, string name, IEnumerable<BenefitRowDto> rows)
    {
        var list = rows.ToList();
        return new BenefitTotalsDto(
            categoryId, name,
            list.Where(r => BenefitGap.IsDue(r.Status)).Sum(r => r.Expected),
            list.Sum(r => r.Received ?? 0m),
            list.Sum(r => r.Gap ?? 0m),
            list.Where(r => r.Status == BenefitStatus.Upcoming).Sum(r => r.Expected));
    }

    private static async Task<IResult> ChildcareAsync(int year, AppDbContext db, CancellationToken ct) =>
        ValidYear(year) ? TypedResults.Ok(await ComputeChildcareAsync(year, db, ct)) : InvalidYear();

    private static async Task<ChildcareDto> ComputeChildcareAsync(int year, AppDbContext db, CancellationToken ct)
    {
        var (start, end) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        var roles = await RolesAsync(db, ct);
        var childcareIds = roles.Values.Where(r => r.Role == FamilyRole.Childcare).Select(r => r.Category.Id).ToList();
        var aidIds = roles.Values.Where(r => r.Role == FamilyRole.ChildcareAid).Select(r => r.Category.Id).ToList();

        // Chaque opération compte dans son mois concerné s'il est renseigné, sinon dans le mois de sa date : le salaire
        // de la garde de décembre payé en janvier reste dans l'année de la garde. Ce mois est à un an au plus de la date.
        var (from, to) = (start.AddMonths(-TransactionValidation.ReferenceMonthRange), end.AddMonths(TransactionValidation.ReferenceMonthRange));
        var lines = (await db.TransactionLines.AsNoTracking()
                .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Date, t.ReferenceMonth, t.PayeeId })
                .Where(x => x.Date >= from && x.Date <= to && x.CategoryId != null
                    && (childcareIds.Contains(x.CategoryId.Value) || aidIds.Contains(x.CategoryId.Value)))
                .ToListAsync(ct))
            .Select(x => new { x.CategoryId, x.Amount, Date = x.ReferenceMonth ?? x.Date, x.PayeeId })
            .Where(x => x.Date >= start && x.Date <= end)
            .ToList();
        var childcare = lines.Where(x => childcareIds.Contains(x.CategoryId!.Value)).ToList();
        var aids = lines.Where(x => aidIds.Contains(x.CategoryId!.Value)).ToList();

        // Déclarations Pajemploi du mois : comparées au réel, payé d'un côté, CMG reçu de l'autre.
        var declared = (await db.ChildcareDeclarations.AsNoTracking().Where(d => d.Year == year).ToListAsync(ct))
            .GroupBy(d => d.Month)
            .ToDictionary(g => g.Key, g => (
                Count: g.Count(),
                Remuneration: g.Sum(d => ChildcareDeclarationTotals.Of(d).Remuneration),
                Cmg: g.Sum(d => d.CmgEntitlement)));

        // Une dépense est négative (R2) : un remboursement de la nounou dans la même catégorie la diminue.
        var months = Enumerable.Range(1, 12)
            .Select(m => new ChildcareMonth(m, -childcare.Where(x => x.Date.Month == m).Sum(x => x.Amount), aids.Where(x => x.Date.Month == m).Sum(x => x.Amount)))
            .Select(m => declared.TryGetValue(m.Month, out var d)
                ? new ChildcareMonthDto(m.Month, m.Paid, m.Aid, m.Net, d.Count, d.Remuneration, d.Cmg, m.Paid - d.Remuneration, m.Aid - d.Cmg)
                : new ChildcareMonthDto(m.Month, m.Paid, m.Aid, m.Net))
            .ToList();

        var payeeIds = childcare.Where(x => x.PayeeId != null).Select(x => x.PayeeId!.Value).Distinct().ToList();
        var names = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var payees = childcare.GroupBy(x => x.PayeeId)
            .Select(g => new ChildcarePayeeDto(g.Key, g.Key is { } id && names.TryGetValue(id, out var name) ? name : "Sans tiers", -g.Sum(x => x.Amount)))
            .OrderByDescending(p => p.Paid)
            .ToList();

        var paid = months.Sum(m => m.Paid);
        var aid = months.Sum(m => m.Aid);
        return new ChildcareDto(year, months, payees, paid, aid, ChildcareSummary.Declarable(paid, aid));
    }

    private static readonly string[] ChildcareHeaders = ["Mois", "Frais de garde", "Aides à la garde", "Reste à charge"];

    private static async Task<IResult> ExportChildcareAsync(int year, AppDbContext db, AuditTrail audit, CancellationToken ct)
    {
        if (!ValidYear(year))
        {
            return InvalidYear();
        }
        var summary = await ComputeChildcareAsync(year, db, ct);
        await audit.RecordAsync(AuditAction.Exported, "Childcare", details: $"Frais de garde {year} (CSV).");
        var csv = new StringBuilder();
        var numbers = TransactionExportWriter.FrenchNumbers;
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        TransactionExportWriter.AppendLine(csv, ChildcareHeaders);
        foreach (var m in summary.Months)
        {
            TransactionExportWriter.AppendLine(csv,
            [
                TransactionExportWriter.Text(culture.DateTimeFormat.GetMonthName(m.Month)),
                m.Paid.ToString("0.00", numbers),
                m.Aid.ToString("0.00", numbers),
                m.Net.ToString("0.00", numbers),
            ]);
        }
        TransactionExportWriter.AppendLine(csv,
        [
            "Total",
            summary.Paid.ToString("0.00", numbers),
            summary.Aid.ToString("0.00", numbers),
            (summary.Paid - summary.Aid).ToString("0.00", numbers),
        ]);
        TransactionExportWriter.AppendLine(csv, ["Montant déclarable", summary.Declarable.ToString("0.00", numbers), "", ""]);
        byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
        return TypedResults.File(bytes, ExportEndpoints.CsvContentType, $"frais-de-garde-{year}.csv");
    }

    private static async Task<IResult> DeclarationsAsync(int year, AppDbContext db, CancellationToken ct)
    {
        if (!ValidYear(year))
        {
            return InvalidYear();
        }
        var declarations = await db.ChildcareDeclarations.AsNoTracking().Where(d => d.Year == year).ToListAsync(ct);
        var payeeIds = declarations.Select(d => d.PayeeId).Distinct().ToList();
        var names = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        return TypedResults.Ok(declarations
            .Select(d => ToDto(d, names.GetValueOrDefault(d.PayeeId) ?? string.Empty))
            .OrderBy(d => d.Month)
            .ThenBy(d => d.PayeeName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    private static async Task<IResult> CreateDeclarationAsync(ChildcareDeclarationRequest request, AppDbContext db, CancellationToken ct)
    {
        if (await ValidateAsync(request, null, db, ct) is { } problem)
        {
            return problem;
        }
        var declaration = new ChildcareDeclaration();
        Apply(declaration, request);
        db.ChildcareDeclarations.Add(declaration);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/family/childcare/declarations/{declaration.Id}", await ToDtoAsync(declaration, db, ct));
    }

    private static async Task<IResult> UpdateDeclarationAsync(
        Guid id, ChildcareDeclarationRequest request, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var declaration = await db.ChildcareDeclarations.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (declaration is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, declaration) is { } precondition)
        {
            return precondition;
        }
        if (await ValidateAsync(request, id, db, ct) is { } problem)
        {
            return problem;
        }
        Apply(declaration, request);
        return await db.TrySaveAsync(ct) ? TypedResults.Ok(await ToDtoAsync(declaration, db, ct)) : Concurrency.Conflict();
    }

    private static async Task<IResult> DeleteDeclarationAsync(Guid id, HttpRequest http, AppDbContext db, CancellationToken ct)
    {
        var declaration = await db.ChildcareDeclarations.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (declaration is null)
        {
            return TypedResults.NotFound();
        }
        if (Concurrency.Check(http, declaration) is { } precondition)
        {
            return precondition;
        }
        db.ChildcareDeclarations.Remove(declaration);
        return await db.TrySaveAsync(ct) ? TypedResults.NoContent() : Concurrency.Conflict();
    }

    /// <summary>Plafond des heures et montants saisis : bien au-delà d'un mois réel, il écarte les fautes de frappe.</summary>
    public const decimal MaxDeclarationValue = 100_000m;

    private static async Task<IResult?> ValidateAsync(ChildcareDeclarationRequest r, Guid? id, AppDbContext db, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!ValidYear(r.Year))
        {
            errors["year"] = ["Année invalide."];
        }
        if (r.Month is < 1 or > 12)
        {
            errors["month"] = ["Mois invalide (1 à 12)."];
        }
        if (r.ActivityDays is < 0 or > 31)
        {
            errors["activityDays"] = ["Nombre de jours d'activité invalide (0 à 31)."];
        }
        if (r.LongCareDays is < 0 or > 31)
        {
            errors["longCareDays"] = ["Nombre de jours de garde de plus de 24 h invalide (0 à 31)."];
        }
        var values = new (string Key, decimal? Value)[]
        {
            ("normalHours", r.NormalHours), ("extraHours", r.ExtraHours), ("overtimeHours", r.OvertimeHours),
            ("paidLeaveDays", r.PaidLeaveDays), ("netHourlyRate", r.NetHourlyRate), ("netSalary", r.NetSalary),
            ("maintenanceAllowance", r.MaintenanceAllowance), ("mealAllowance", r.MealAllowance),
            ("mileageAllowance", r.MileageAllowance), ("advance", r.Advance),
            ("precariousnessAllowance", r.PrecariousnessAllowance), ("paidLeaveCompensation", r.PaidLeaveCompensation),
            ("noticeCompensation", r.NoticeCompensation), ("terminationAllowance", r.TerminationAllowance),
            ("contributions", r.Contributions), ("coveredContributions", r.CoveredContributions), ("cmgEntitlement", r.CmgEntitlement),
        };
        foreach (var (key, value) in values)
        {
            if (value is { } v && (v < 0 || v > MaxDeclarationValue || decimal.Round(v, 2) != v))
            {
                errors[key] = ["Valeur invalide : positive, 2 décimales au plus."];
            }
        }
        if (!r.IsContractEnd && (r.ContractEndDate is not null || r.PrecariousnessAllowance != 0 || r.PaidLeaveCompensation != 0
            || r.NoticeCompensation != 0 || r.TerminationAllowance != 0))
        {
            errors["isContractEnd"] = ["Date et indemnités de fin de contrat : seulement pour une fin de contrat."];
        }
        if (!await db.Payees.AnyAsync(p => p.Id == r.PayeeId, ct))
        {
            errors["payeeId"] = ["Salarié introuvable."];
        }
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }
        if (await db.ChildcareDeclarations.AnyAsync(d => d.Year == r.Year && d.Month == r.Month && d.PayeeId == r.PayeeId && d.Id != id, ct))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Déclaration déjà saisie.",
                detail: "Ce salarié a déjà une déclaration pour ce mois : modifiez-la.");
        }
        return null;
    }

    private static void Apply(ChildcareDeclaration d, ChildcareDeclarationRequest r)
    {
        d.Year = r.Year;
        d.Month = r.Month;
        d.PayeeId = r.PayeeId;
        d.ActivityDays = r.ActivityDays;
        d.NormalHours = r.NormalHours;
        d.ExtraHours = r.ExtraHours;
        d.OvertimeHours = r.OvertimeHours;
        d.PaidLeaveDays = r.PaidLeaveDays;
        d.NetHourlyRate = r.NetHourlyRate;
        d.NetSalary = r.NetSalary;
        d.MaintenanceAllowance = r.MaintenanceAllowance;
        d.MealAllowance = r.MealAllowance;
        d.MileageAllowance = r.MileageAllowance;
        d.Advance = r.Advance;
        d.LongCareDays = r.LongCareDays;
        d.DisabledChildCare = r.DisabledChildCare;
        d.IsContractEnd = r.IsContractEnd;
        d.ContractEndDate = r.IsContractEnd ? r.ContractEndDate : null;
        d.PrecariousnessAllowance = r.PrecariousnessAllowance;
        d.PaidLeaveCompensation = r.PaidLeaveCompensation;
        d.NoticeCompensation = r.NoticeCompensation;
        d.TerminationAllowance = r.TerminationAllowance;
        d.Contributions = r.Contributions;
        d.CoveredContributions = r.CoveredContributions;
        d.CmgEntitlement = r.CmgEntitlement;
    }

    private static async Task<ChildcareDeclarationDto> ToDtoAsync(ChildcareDeclaration d, AppDbContext db, CancellationToken ct) =>
        ToDto(d, await db.Payees.AsNoTracking().Where(p => p.Id == d.PayeeId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? string.Empty);

    private static ChildcareDeclarationDto ToDto(ChildcareDeclaration d, string payeeName)
    {
        var totals = ChildcareDeclarationTotals.Of(d);
        return new ChildcareDeclarationDto(
            d.Id, d.Year, d.Month, d.PayeeId, payeeName, d.ActivityDays, d.NormalHours, d.ExtraHours, d.OvertimeHours, d.PaidLeaveDays,
            d.NetHourlyRate, d.NetSalary, d.MaintenanceAllowance, d.MealAllowance, d.MileageAllowance, d.Advance, d.LongCareDays,
            d.DisabledChildCare, d.IsContractEnd, d.ContractEndDate, d.PrecariousnessAllowance, d.PaidLeaveCompensation,
            d.NoticeCompensation, d.TerminationAllowance, d.Contributions, d.CoveredContributions, d.CmgEntitlement,
            totals.Remuneration, totals.TotalCost, totals.Cmg, totals.Remaining, d.Version);
    }

    private static async Task<IResult> HealthAsync(DateOnly? from, DateOnly? to, AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var today = Balances.Today(clock);
        var start = from ?? new DateOnly(today.Year, 1, 1);
        var end = to ?? new DateOnly(today.Year, 12, 31);
        if (end < start || end.DayNumber - start.DayNumber > 3660)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["La plage va de la date de début à la date de fin, sur dix ans au plus."],
            });
        }

        var roles = await RolesAsync(db, ct);
        var healthIds = roles.Values.Where(r => r.Role == FamilyRole.Health).Select(r => r.Category.Id).ToList();
        var lines = await db.TransactionLines.AsNoTracking()
            .Join(db.Transactions, l => l.TransactionId, t => t.Id, (l, t) => new { l.CategoryId, l.Amount, t.Id, t.Date, t.Label, t.PayeeId, t.ReimbursesId })
            .Where(x => x.Date >= start && x.Date <= end && x.CategoryId != null && healthIds.Contains(x.CategoryId.Value))
            .ToListAsync(ct);
        var byTransaction = lines.GroupBy(x => x.Id).Select(g => new
        {
            g.First().Id,
            g.First().Date,
            g.First().Label,
            g.First().PayeeId,
            g.First().ReimbursesId,
            CategoryId = g.OrderBy(x => x.Amount).First().CategoryId!.Value,
            Sum = g.Sum(x => x.Amount),
        }).ToList();
        var expenses = byTransaction.Where(t => t.Sum < 0).ToList();
        var unlinked = byTransaction.Where(t => t.Sum > 0 && t.ReimbursesId is null).Sum(t => t.Sum);

        // Remboursements liés (B11), quelle que soit leur date ou leur catégorie, classés par l'organisme du tiers.
        var expenseIds = expenses.Select(e => e.Id).ToList();
        var reimbursements = await db.Transactions.AsNoTracking()
            .Where(t => t.ReimbursesId != null && expenseIds.Contains(t.ReimbursesId.Value))
            .GroupJoin(db.Payees, t => t.PayeeId, p => p.Id, (t, p) => new { t.ReimbursesId, t.Amount, Payees = p })
            .SelectMany(x => x.Payees.DefaultIfEmpty(), (x, p) => new { ExpenseId = x.ReimbursesId!.Value, x.Amount, Reimburser = p == null ? Reimburser.None : p.Reimburser })
            .ToListAsync(ct);
        var refunds = reimbursements.GroupBy(r => r.ExpenseId).ToDictionary(g => g.Key, g => g.ToList());

        var payeeIds = expenses.Where(e => e.PayeeId != null).Select(e => e.PayeeId!.Value).Distinct().ToList();
        var names = await db.Payees.AsNoTracking().Where(p => payeeIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var rows = expenses.Select(e =>
        {
            var linked = refunds.GetValueOrDefault(e.Id) ?? [];
            var balance = new HealthBalance(
                -e.Sum,
                linked.Where(r => r.Reimburser == Reimburser.PrimaryInsurance).Sum(r => r.Amount),
                linked.Where(r => r.Reimburser == Reimburser.Mutual).Sum(r => r.Amount),
                linked.Where(r => r.Reimburser == Reimburser.None).Sum(r => r.Amount));
            return new HealthRowDto(
                e.Id, e.Date, e.PayeeId is { } id ? names.GetValueOrDefault(id) : null, e.Label, roles[e.CategoryId].Category.Name,
                balance.Expense, balance.PrimaryInsurance, balance.Mutual, balance.Other, balance.Remaining);
        }).OrderByDescending(r => r.Date).ToList();

        return TypedResults.Ok(new HealthDto(
            start, end, rows,
            rows.Sum(r => r.Expense), rows.Sum(r => r.PrimaryInsurance), rows.Sum(r => r.Mutual), rows.Sum(r => r.Other),
            rows.Sum(r => r.Remaining), unlinked));
    }
}
