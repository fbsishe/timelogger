using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOvertimeTaskToMappingRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OvertimeTimelogTaskId",
                table: "MappingRules",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_MappingRules_OvertimeTimelogTaskId",
                table: "MappingRules",
                column: "OvertimeTimelogTaskId");

            migrationBuilder.AddForeignKey(
                name: "FK_MappingRules_TimelogTasks_OvertimeTimelogTaskId",
                table: "MappingRules",
                column: "OvertimeTimelogTaskId",
                principalTable: "TimelogTasks",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MappingRules_TimelogTasks_OvertimeTimelogTaskId",
                table: "MappingRules");

            migrationBuilder.DropIndex(
                name: "IX_MappingRules_OvertimeTimelogTaskId",
                table: "MappingRules");

            migrationBuilder.DropColumn(
                name: "OvertimeTimelogTaskId",
                table: "MappingRules");
        }
    }
}
