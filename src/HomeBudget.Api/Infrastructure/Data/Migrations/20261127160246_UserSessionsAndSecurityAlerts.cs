using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeBudget.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserSessionsAndSecurityAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LockoutEmailAlert",
                table: "NotificationPreferences",
                type: "INTEGER",
                nullable: false,
                // Préférences existantes : alerte de verrouillage activée, comme pour une nouvelle ligne.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "LoginEmailAlert",
                table: "NotificationPreferences",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Origin = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    RevokedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId_LastSeenAt",
                table: "UserSessions",
                columns: new[] { "UserId", "LastSeenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropColumn(
                name: "LockoutEmailAlert",
                table: "NotificationPreferences");

            migrationBuilder.DropColumn(
                name: "LoginEmailAlert",
                table: "NotificationPreferences");
        }
    }
}
