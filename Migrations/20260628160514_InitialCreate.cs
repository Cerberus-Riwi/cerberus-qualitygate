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
            // Idempotente a propósito: las tablas reales ya existen en el
            // schema "cerberus" (creadas antes de que el DbContext tuviera
            // HasDefaultSchema("cerberus")). Usamos IF NOT EXISTS para que
            // Migrate() no falle si ya están ahí, y para que siga
            // funcionando igual en un ambiente nuevo donde no existan.
            migrationBuilder.Sql(@"CREATE SCHEMA IF NOT EXISTS cerberus;");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS cerberus.""Findings"" (
                    ""Id"" uuid NOT NULL,
                    ""ScanId"" uuid NOT NULL,
                    ""Title"" character varying(256) NOT NULL,
                    ""Description"" character varying(2048) NOT NULL,
                    ""Severity"" character varying(32) NOT NULL,
                    ""RuleId"" character varying(256) NOT NULL,
                    ""FilePath"" character varying(1024) NOT NULL,
                    ""LineStart"" integer NULL,
                    ""LineEnd"" integer NULL,
                    ""Recommendation"" character varying(2048) NOT NULL,
                    ""CvssScore"" numeric(4,1) NOT NULL,
                    ""CvssVector"" character varying(512) NOT NULL,
                    ""CweId"" character varying(64) NOT NULL,
                    ""CveId"" character varying(64) NOT NULL,
                    ""Tool"" character varying(64) NOT NULL,
                    ""DetectedAt"" timestamp with time zone NOT NULL,
                    CONSTRAINT ""PK_Findings"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS cerberus.""QualityGateResults"" (
                    ""Id"" uuid NOT NULL,
                    ""ScanId"" uuid NOT NULL,
                    ""Verdict"" character varying(32) NOT NULL,
                    ""Summary_Critical"" integer NOT NULL,
                    ""Summary_High"" integer NOT NULL,
                    ""Summary_Medium"" integer NOT NULL,
                    ""Summary_Low"" integer NOT NULL,
                    ""Summary_Info"" integer NOT NULL,
                    ""RollbackTriggered"" boolean NOT NULL,
                    ""IssuedAt"" timestamp with time zone NOT NULL,
                    ""DeploymentId"" character varying(256) NULL,
                    CONSTRAINT ""PK_QualityGateResults"" PRIMARY KEY (""Id"")
                );
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_Findings_ScanId""
                ON cerberus.""Findings"" (""ScanId"");
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_QualityGateResults_DeploymentId""
                ON cerberus.""QualityGateResults"" (""DeploymentId"");
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ""IX_QualityGateResults_ScanId""
                ON cerberus.""QualityGateResults"" (""ScanId"");
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS cerberus.""Findings"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS cerberus.""QualityGateResults"";");
        }
    }
}