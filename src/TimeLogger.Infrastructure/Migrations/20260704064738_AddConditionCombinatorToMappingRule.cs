using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConditionCombinatorToMappingRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default 1 = ConditionCombinator.And, the pre-existing behaviour
            migrationBuilder.AddColumn<int>(
                name: "Combinator",
                table: "MappingRules",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Combinator",
                table: "MappingRules");
        }
    }
}
