using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeLogger.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    ApiToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PollSchedule = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimelogProjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelogProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TimelogTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExternalId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TimelogProjectId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelogTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TimelogTasks_TimelogProjects_TimelogProjectId",
                        column: x => x.TimelogProjectId,
                        principalTable: "TimelogProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MappingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<int>(type: "int", nullable: true),
                    MatchField = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MatchOperator = table.Column<int>(type: "int", nullable: false),
                    MatchValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TimelogProjectId = table.Column<int>(type: "int", nullable: false),
                    TimelogTaskId = table.Column<int>(type: "int", nullable: true),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MappingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MappingRules_TimelogProjects_TimelogProjectId",
                        column: x => x.TimelogProjectId,
                        principalTable: "TimelogProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MappingRules_TimelogTasks_TimelogTaskId",
                        column: x => x.TimelogTaskId,
                        principalTable: "TimelogTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ImportedEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportSourceId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeSpentSeconds = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ProjectKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IssueKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Activity = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    MetadataJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MappingRuleId = table.Column<int>(type: "int", nullable: true),
                    TimelogProjectId = table.Column<int>(type: "int", nullable: true),
                    TimelogTaskId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportedEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportedEntries_ImportSources_ImportSourceId",
                        column: x => x.ImportSourceId,
                        principalTable: "ImportSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportedEntries_MappingRules_MappingRuleId",
                        column: x => x.MappingRuleId,
                        principalTable: "MappingRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportedEntries_TimelogProjects_TimelogProjectId",
                        column: x => x.TimelogProjectId,
                        principalTable: "TimelogProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ImportedEntries_TimelogTasks_TimelogTaskId",
                        column: x => x.TimelogTaskId,
                        principalTable: "TimelogTasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubmittedEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportedEntryId = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmittedEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmittedEntries_ImportedEntries_ImportedEntryId",
                        column: x => x.ImportedEntryId,
                        principalTable: "ImportedEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEntries_ImportSourceId_ExternalId",
                table: "ImportedEntries",
                columns: new[] { "ImportSourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEntries_MappingRuleId",
                table: "ImportedEntries",
                column: "MappingRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEntries_TimelogProjectId",
                table: "ImportedEntries",
                column: "TimelogProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportedEntries_TimelogTaskId",
                table: "ImportedEntries",
                column: "TimelogTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingRules_TimelogProjectId",
                table: "MappingRules",
                column: "TimelogProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MappingRules_TimelogTaskId",
                table: "MappingRules",
                column: "TimelogTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmittedEntries_ImportedEntryId",
                table: "SubmittedEntries",
                column: "ImportedEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimelogProjects_ExternalId",
                table: "TimelogProjects",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TimelogTasks_TimelogProjectId_ExternalId",
                table: "TimelogTasks",
                columns: new[] { "TimelogProjectId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubmittedEntries");

            migrationBuilder.DropTable(
                name: "ImportedEntries");

            migrationBuilder.DropTable(
                name: "ImportSources");

            migrationBuilder.DropTable(
                name: "MappingRules");

            migrationBuilder.DropTable(
                name: "TimelogTasks");

            migrationBuilder.DropTable(
                name: "TimelogProjects");
        }
    }
}
