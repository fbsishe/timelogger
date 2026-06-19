using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConflictFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ConflictHoursInTimelog",
                table: "ImportedEntries",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictTimelogRegistrationId",
                table: "ImportedEntries",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictHoursInTimelog",
                table: "ImportedEntries");

            migrationBuilder.DropColumn(
                name: "ConflictTimelogRegistrationId",
                table: "ImportedEntries");
        }
    }
}
