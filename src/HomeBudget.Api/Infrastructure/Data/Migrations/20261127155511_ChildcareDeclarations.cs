using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChildcareDeclarations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChildcareDeclarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    PayeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityDays = table.Column<int>(type: "INTEGER", nullable: false),
                    NormalHours = table.Column<long>(type: "INTEGER", nullable: false),
                    ExtraHours = table.Column<long>(type: "INTEGER", nullable: false),
                    OvertimeHours = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidLeaveDays = table.Column<long>(type: "INTEGER", nullable: false),
                    NetHourlyRate = table.Column<long>(type: "INTEGER", nullable: true),
                    NetSalary = table.Column<long>(type: "INTEGER", nullable: false),
                    MaintenanceAllowance = table.Column<long>(type: "INTEGER", nullable: false),
                    MealAllowance = table.Column<long>(type: "INTEGER", nullable: false),
                    MileageAllowance = table.Column<long>(type: "INTEGER", nullable: false),
                    Advance = table.Column<long>(type: "INTEGER", nullable: false),
                    LongCareDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DisabledChildCare = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsContractEnd = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContractEndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PrecariousnessAllowance = table.Column<long>(type: "INTEGER", nullable: false),
                    PaidLeaveCompensation = table.Column<long>(type: "INTEGER", nullable: false),
                    NoticeCompensation = table.Column<long>(type: "INTEGER", nullable: false),
                    TerminationAllowance = table.Column<long>(type: "INTEGER", nullable: false),
                    Contributions = table.Column<long>(type: "INTEGER", nullable: false),
                    CoveredContributions = table.Column<long>(type: "INTEGER", nullable: false),
                    CmgEntitlement = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChildcareDeclarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChildcareDeclarations_Payees_PayeeId",
                        column: x => x.PayeeId,
                        principalTable: "Payees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChildcareDeclarations_PayeeId",
                table: "ChildcareDeclarations",
                column: "PayeeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChildcareDeclarations_Year_Month_PayeeId",
                table: "ChildcareDeclarations",
                columns: new[] { "Year", "Month", "PayeeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChildcareDeclarations");
        }
    }
}
