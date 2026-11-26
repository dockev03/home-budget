using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SavingsAndNetWorth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmergencyFundAverageMonths",
                table: "HouseholdSettings",
                type: "INTEGER",
                nullable: false,
                // Ligne de paramètres existante : 6 périodes, la valeur par défaut (J3).
                defaultValue: 6);

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergencyFund",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Épargne de précaution (J3) : les comptes d'épargne existants le deviennent, comme les nouveaux.
            migrationBuilder.Sql("UPDATE \"Accounts\" SET \"IsEmergencyFund\" = 1 WHERE \"Type\" = 'Savings';");

            migrationBuilder.CreateTable(
                name: "AccountValuations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountValuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountValuations_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavingsGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TargetAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    TargetDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Icon = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavingsGoals_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SavingsGoals_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountValuations_AccountId_Date",
                table: "AccountValuations",
                columns: new[] { "AccountId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoals_AccountId",
                table: "SavingsGoals",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SavingsGoals_CategoryId",
                table: "SavingsGoals",
                column: "CategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountValuations");

            migrationBuilder.DropTable(
                name: "SavingsGoals");

            migrationBuilder.DropColumn(
                name: "EmergencyFundAverageMonths",
                table: "HouseholdSettings");

            migrationBuilder.DropColumn(
                name: "IsEmergencyFund",
                table: "Accounts");
        }
    }
}
