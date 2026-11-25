using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Recurrences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Recurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PayeeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    End = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Count = table.Column<int>(type: "INTEGER", nullable: true),
                    IsInstallment = table.Column<bool>(type: "INTEGER", nullable: false),
                    GenerateFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recurrences_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recurrences_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recurrences_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recurrences_Payees_PayeeId",
                        column: x => x.PayeeId,
                        principalTable: "Payees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecurrenceOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecurrenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    TransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurrenceOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurrenceOccurrences_Recurrences_RecurrenceId",
                        column: x => x.RecurrenceId,
                        principalTable: "Recurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurrenceOccurrences_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceOccurrences_RecurrenceId_ScheduledDate",
                table: "RecurrenceOccurrences",
                columns: new[] { "RecurrenceId", "ScheduledDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceOccurrences_Status_DueDate",
                table: "RecurrenceOccurrences",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceOccurrences_TransactionId",
                table: "RecurrenceOccurrences",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Recurrences_AccountId",
                table: "Recurrences",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Recurrences_CategoryId",
                table: "Recurrences",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Recurrences_PayeeId",
                table: "Recurrences",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Recurrences_ToAccountId",
                table: "Recurrences",
                column: "ToAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurrenceOccurrences");

            migrationBuilder.DropTable(
                name: "Recurrences");
        }
    }
}
