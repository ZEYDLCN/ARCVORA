using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Arcvora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Retention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnonymizedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RetentionSettings",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedRequestMonths = table.Column<int>(type: "integer", nullable: true),
                    AutoPurge = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionSettings", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_RetentionSettings_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionSettings");

            migrationBuilder.DropColumn(
                name: "AnonymizedAt",
                table: "Users");
        }
    }
}
