using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QualityGateService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    LineStart = table.Column<int>(type: "integer", nullable: true),
                    LineEnd = table.Column<int>(type: "integer", nullable: true),
                    Recommendation = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    CvssScore = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: false),
                    CvssVector = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CweId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CveId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Tool = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QualityGateResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Verdict = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary_Critical = table.Column<int>(type: "integer", nullable: false),
                    Summary_High = table.Column<int>(type: "integer", nullable: false),
                    Summary_Medium = table.Column<int>(type: "integer", nullable: false),
                    Summary_Low = table.Column<int>(type: "integer", nullable: false),
                    Summary_Info = table.Column<int>(type: "integer", nullable: false),
                    RollbackTriggered = table.Column<bool>(type: "boolean", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeploymentId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityGateResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_ScanId",
                table: "Findings",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityGateResults_DeploymentId",
                table: "QualityGateResults",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityGateResults_ScanId",
                table: "QualityGateResults",
                column: "ScanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "QualityGateResults");
        }
    }
}
