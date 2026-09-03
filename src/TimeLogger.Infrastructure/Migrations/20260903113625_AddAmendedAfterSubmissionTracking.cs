using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAmendedAfterSubmissionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AmendedAfterSubmissionAt",
                table: "ImportedEntries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AmendedSourceDescription",
                table: "ImportedEntries",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AmendedSourceSeconds",
                table: "ImportedEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AmendmentReportedAt",
                table: "ImportedEntries",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEntries_AmendedAfterSubmissionAt",
                table: "ImportedEntries",
                column: "AmendedAfterSubmissionAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ImportedEntries_AmendedAfterSubmissionAt",
                table: "ImportedEntries");

            migrationBuilder.DropColumn(
                name: "AmendedAfterSubmissionAt",
                table: "ImportedEntries");

            migrationBuilder.DropColumn(
                name: "AmendedSourceDescription",
                table: "ImportedEntries");

            migrationBuilder.DropColumn(
                name: "AmendedSourceSeconds",
                table: "ImportedEntries");

            migrationBuilder.DropColumn(
                name: "AmendmentReportedAt",
                table: "ImportedEntries");
        }
    }
}
