using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReconciliationsAndAdvances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AdvanceId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReimbursesId",
                table: "Transactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSubscription",
                table: "Recurrences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Advances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExpectedAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    StatementBalance = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reconciliations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AdvanceId",
                table: "Transactions",
                column: "AdvanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReconciliationId",
                table: "Transactions",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ReimbursesId",
                table: "Transactions",
                column: "ReimbursesId");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliations_AccountId_Date",
                table: "Reconciliations",
                columns: new[] { "AccountId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Advances_AdvanceId",
                table: "Transactions",
                column: "AdvanceId",
                principalTable: "Advances",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Reconciliations_ReconciliationId",
                table: "Transactions",
                column: "ReconciliationId",
                principalTable: "Reconciliations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Transactions_ReimbursesId",
                table: "Transactions",
                column: "ReimbursesId",
                principalTable: "Transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Advances_AdvanceId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Reconciliations_ReconciliationId",
                table: "Transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Transactions_ReimbursesId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "Advances");

            migrationBuilder.DropTable(
                name: "Reconciliations");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AdvanceId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ReconciliationId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ReimbursesId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AdvanceId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReconciliationId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ReimbursesId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsSubscription",
                table: "Recurrences");
        }
    }
}
