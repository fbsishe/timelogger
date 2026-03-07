using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EntraObjectId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EmployeeMappingId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUsers_EmployeeMappings_EmployeeMappingId",
                        column: x => x.EmployeeMappingId,
                        principalTable: "EmployeeMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AppUserProjects",
                columns: table => new
                {
                    AppUserId = table.Column<int>(type: "int", nullable: false),
                    TimelogProjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUserProjects", x => new { x.AppUserId, x.TimelogProjectId });
                    table.ForeignKey(
                        name: "FK_AppUserProjects_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppUserProjects_TimelogProjects_TimelogProjectId",
                        column: x => x.TimelogProjectId,
                        principalTable: "TimelogProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUserProjects_TimelogProjectId",
                table: "AppUserProjects",
                column: "TimelogProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EmployeeMappingId",
                table: "AppUsers",
                column: "EmployeeMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EntraObjectId",
                table: "AppUsers",
                column: "EntraObjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppUserProjects");

            migrationBuilder.DropTable(
                name: "AppUsers");
        }
    }
}
