using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMappingRuleConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MappingRuleConditions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MappingRuleId = table.Column<int>(type: "int", nullable: false),
                    MatchField = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MatchOperator = table.Column<int>(type: "int", nullable: false),
                    MatchValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingRuleConditions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingRuleConditions_MappingRules_MappingRuleId",
                        column: x => x.MappingRuleId,
                        principalTable: "MappingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MappingRuleConditions_MappingRuleId",
                table: "MappingRuleConditions",
                column: "MappingRuleId");

            // Migrate existing single-condition rules into the new table
            migrationBuilder.Sql(@"
                INSERT INTO MappingRuleConditions (MappingRuleId, MatchField, MatchOperator, MatchValue)
                SELECT Id, MatchField, MatchOperator, MatchValue FROM MappingRules
            ");

            migrationBuilder.DropColumn(name: "MatchField", table: "MappingRules");
            migrationBuilder.DropColumn(name: "MatchOperator", table: "MappingRules");
            migrationBuilder.DropColumn(name: "MatchValue", table: "MappingRules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchField",
                table: "MappingRules",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "MatchOperator",
                table: "MappingRules",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MatchValue",
                table: "MappingRules",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            // Copy back the first condition for each rule
            migrationBuilder.Sql(@"
                UPDATE mr
                SET mr.MatchField = c.MatchField,
                    mr.MatchOperator = c.MatchOperator,
                    mr.MatchValue = c.MatchValue
                FROM MappingRules mr
                INNER JOIN (
                    SELECT MappingRuleId,
                           MatchField, MatchOperator, MatchValue,
                           ROW_NUMBER() OVER (PARTITION BY MappingRuleId ORDER BY Id) AS rn
                    FROM MappingRuleConditions
                ) c ON c.MappingRuleId = mr.Id AND c.rn = 1
            ");

            migrationBuilder.DropTable(name: "MappingRuleConditions");
        }
    }
}
