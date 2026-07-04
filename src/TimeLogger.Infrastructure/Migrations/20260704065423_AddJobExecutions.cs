using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Succeeded = table.Column<bool>(type: "bit", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobExecutions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobExecutions_JobName_ExecutedAt",
                table: "JobExecutions",
                columns: new[] { "JobName", "ExecutedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobExecutions");
        }
    }
}
