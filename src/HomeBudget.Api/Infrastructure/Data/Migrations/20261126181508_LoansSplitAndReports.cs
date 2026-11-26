using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class LoansSplitAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SplitMode",
                table: "HouseholdSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                // Ligne de paramètres existante : répartition à parts égales (K4).
                defaultValue: "Equal");

            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromSplit",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "HolderId",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Principal = table.Column<long>(type: "INTEGER", nullable: false),
                    AnnualRate = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationMonths = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstPaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MonthlyInsurance = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Loans_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemberShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Percent = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemberShares_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SavedReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    GroupBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Range = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    From = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    To = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AccountIds = table.Column<string>(type: "TEXT", nullable: false),
                    CategoryIds = table.Column<string>(type: "TEXT", nullable: false),
                    PayeeIds = table.Column<string>(type: "TEXT", nullable: false),
                    TagIds = table.Column<string>(type: "TEXT", nullable: false),
                    Kinds = table.Column<string>(type: "TEXT", nullable: false),
                    IncludeTransfers = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavedReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_HolderId",
                table: "Accounts",
                column: "HolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_AccountId",
                table: "Loans",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberShares_UserId",
                table: "MemberShares",
                column: "UserId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_AspNetUsers_HolderId",
                table: "Accounts",
                column: "HolderId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_AspNetUsers_HolderId",
                table: "Accounts");

            migrationBuilder.DropTable(
                name: "Loans");

            migrationBuilder.DropTable(
                name: "MemberShares");

            migrationBuilder.DropTable(
                name: "SavedReports");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_HolderId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SplitMode",
                table: "HouseholdSettings");

            migrationBuilder.DropColumn(
                name: "ExcludeFromSplit",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "HolderId",
                table: "Accounts");
        }
    }
}
