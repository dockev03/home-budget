using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Imports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportHash",
                table: "Transactions",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RawLabel",
                table: "Transactions",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Tolerance",
                table: "Recurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ToleranceUnit",
                table: "Recurrences",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Amount");

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Created = table.Column<int>(type: "INTEGER", nullable: false),
                    Matched = table.Column<int>(type: "INTEGER", nullable: false),
                    Duplicates = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Delimiter = table.Column<char>(type: "TEXT", nullable: false),
                    DateFormat = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DecimalSeparator = table.Column<char>(type: "TEXT", nullable: false),
                    DateColumn = table.Column<int>(type: "INTEGER", nullable: false),
                    LabelColumn = table.Column<int>(type: "INTEGER", nullable: false),
                    AmountColumn = table.Column<int>(type: "INTEGER", nullable: true),
                    DebitColumn = table.Column<int>(type: "INTEGER", nullable: true),
                    CreditColumn = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportProfiles", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ImportProfiles",
                columns: new[] { "Id", "AmountColumn", "CreatedAt", "CreatedBy", "CreditColumn", "DateColumn", "DateFormat", "DebitColumn", "DecimalSeparator", "Delimiter", "LabelColumn", "Name", "UpdatedAt", "UpdatedBy", "Version" },
                values: new object[] { new Guid("019ab7a0-0000-7000-8000-000000000001"), 3, new DateTimeOffset(new DateTime(2026, 11, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, null, 1, "dd/MM/yyyy", null, ',', ';', 2, "La Banque Postale (CSV)", new DateTimeOffset(new DateTime(2026, 11, 25, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_ImportHash",
                table: "Transactions",
                columns: new[] { "AccountId", "ImportHash" },
                unique: true,
                filter: "\"ImportHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_AccountId",
                table: "ImportBatches",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportProfiles_Name",
                table: "ImportProfiles",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_ImportBatches_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_ImportBatches_ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "ImportProfiles");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_ImportHash",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ImportHash",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "RawLabel",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Tolerance",
                table: "Recurrences");

            migrationBuilder.DropColumn(
                name: "ToleranceUnit",
                table: "Recurrences");
        }
    }
}
