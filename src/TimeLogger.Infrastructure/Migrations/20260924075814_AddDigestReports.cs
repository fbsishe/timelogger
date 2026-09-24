using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDigestReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DigestReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromDay = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDay = table.Column<DateOnly>(type: "date", nullable: false),
                    HandledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Posted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DigestReports_ToDay",
                table: "DigestReports",
                column: "ToDay",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigestReports");
        }
    }
}
