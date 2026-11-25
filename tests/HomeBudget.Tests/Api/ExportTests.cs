using System.Net;
using System.Text;

using ClosedXML.Excel;

using HomeBudget.Tests.Infrastructure;

namespace HomeBudget.Tests.Api;

public sealed class ExportTests
{
    private static readonly DateOnly Day = new(2026, 3, 5);

    private static async Task<(ApiClient Admin, Guid Checking)> SeedAsync(HomeBudgetFactory factory)
    {
        var admin = await factory.CreateAdminClientAsync();
        var checking = await admin.CreateAccountAsync("Courant", 0m);
        var other = await admin.CreateAccountAsync("Joint", 0m);
        var groceries = await admin.CategoryIdAsync("Courses");
        var leisure = await admin.CreateCategoryAsync("Cinéma", "Variable", await admin.CategoryIdAsync("Restaurants"));
        await admin.CreateAsync("/api/transactions", new
        {
            accountId = checking,
            date = TestScenario.Iso(Day),
            payeeName = "=HYPER; \"Marché\"",
            label = "Courses et sortie",
            lines = new object[]
            {
                new { categoryId = groceries, amount = -1234.5m, memo = "Alimentation" },
                new { categoryId = leisure, amount = -20m, memo = "-cinéma" },
            },
        });
        await admin.CreateTransactionAsync(checking, 3000m, await admin.CategoryIdAsync("Salaire Kévin"), Day.AddDays(-4));
        await admin.CreateTransactionAsync(other, -99m, groceries, Day);
        return (admin, checking);
    }

    [Fact]
    public async Task Csv_export_is_ready_for_french_excel_with_one_row_per_split_line()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, checking) = await SeedAsync(factory);
        var reader = await factory.CreateUserClientAsync("lecteur", "ReadOnly");

        var response = await reader.GetAsync($"/api/exports/transactions?format=csv&accountId={checking}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldEndWith(".csv");
        var bytes = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        bytes.Take(3).ShouldBe(Encoding.UTF8.GetPreamble());
        var lines = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldStartWith("Date;Compte;Tiers;Libellé;Catégorie;Sous-catégorie;Type;Montant;");
        lines.Length.ShouldBe(4); // en-tête + salaire + 2 lignes de ventilation ; le compte Joint est filtré
        lines[1].ShouldStartWith("01/03/2026;Courant;;;Salaire Kévin;;Revenu;3000,00;");
        // Texte entre guillemets (point-virgule, guillemets doublés) et neutralisé s'il commence comme une formule.
        lines[2].ShouldStartWith("05/03/2026;Courant;\"'=HYPER; \"\"Marché\"\"\";Courses et sortie;Courses;;Dépense variable;-1234,50;Alimentation;-1254,50;Non;");
        lines[3].ShouldContain(";Restaurants;Cinéma;Dépense variable;-20,00;'-cinéma;-1254,50;");
    }

    [Fact]
    public async Task Xlsx_export_has_typed_dates_and_amounts()
    {
        await using var factory = new HomeBudgetFactory();
        var (admin, _) = await SeedAsync(factory);

        var response = await admin.GetAsync("/api/exports/transactions?format=xlsx&search=sortie");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        sheet.LastRowUsed()!.RowNumber().ShouldBe(3);
        sheet.Cell(1, 8).GetString().ShouldBe("Montant");
        sheet.Cell(2, 1).GetDateTime().ShouldBe(new DateTime(2026, 3, 5));
        sheet.Cell(2, 3).GetString().ShouldBe("=HYPER; \"Marché\"");
        sheet.Cell(2, 3).HasFormula.ShouldBeFalse();
        sheet.Cell(2, 8).GetDouble().ShouldBe(-1234.5);
        sheet.Cell(3, 8).GetDouble().ShouldBe(-20);
    }

    [Fact]
    public async Task Unknown_format_is_rejected()
    {
        await using var factory = new HomeBudgetFactory();
        var admin = await factory.CreateAdminClientAsync();

        (await admin.GetAsync("/api/exports/transactions?format=pdf")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await factory.CreateApiClient().GetAsync("/api/exports/transactions")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
