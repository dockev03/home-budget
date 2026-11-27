using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class FamilyTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExpectedAmount",
                table: "RecurrenceOccurrences",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reimburser",
                table: "Payees",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<string>(
                name: "FamilyRole",
                table: "Categories",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "None");

            // Données existantes : l'attendu des échéances déjà traitées est le montant actuel de leur récurrence,
            // et les catégories par défaut (C6) reçoivent leur rôle.
            migrationBuilder.Sql(
                "UPDATE RecurrenceOccurrences SET ExpectedAmount = "
                + "(SELECT r.Amount FROM Recurrences r WHERE r.Id = RecurrenceOccurrences.RecurrenceId);");
            migrationBuilder.Sql("UPDATE Categories SET FamilyRole = 'Benefit' WHERE Name = 'Allocation CAF' AND Kind = 'Income';");
            migrationBuilder.Sql("UPDATE Categories SET FamilyRole = 'ChildcareAid' WHERE Name = 'CMG' AND Kind = 'Income';");
            migrationBuilder.Sql("UPDATE Categories SET FamilyRole = 'Childcare' WHERE Name = 'Nounou Maël' AND Kind IN ('Fixed', 'Variable');");
            migrationBuilder.Sql("UPDATE Categories SET FamilyRole = 'Health' WHERE Name = 'Santé' AND Kind IN ('Fixed', 'Variable');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedAmount",
                table: "RecurrenceOccurrences");

            migrationBuilder.DropColumn(
                name: "Reimburser",
                table: "Payees");

            migrationBuilder.DropColumn(
                name: "FamilyRole",
                table: "Categories");
        }
    }
}
