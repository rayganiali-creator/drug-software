using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.ClinicalRules.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8Monitoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "change_json",
                schema: "clinical_rules",
                table: "assessment",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "input_fingerprint",
                schema: "clinical_rules",
                table: "assessment",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "input_refs_json",
                schema: "clinical_rules",
                table: "assessment",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "locale",
                schema: "clinical_rules",
                table: "assessment",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "monitoring_job",
                schema: "clinical_rules",
                columns: table => new
                {
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_check_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_outcome = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    last_assessment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monitoring_job", x => x.patient_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_monitoring_job_state_next_check_at",
                schema: "clinical_rules",
                table: "monitoring_job",
                columns: new[] { "state", "next_check_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monitoring_job",
                schema: "clinical_rules");

            migrationBuilder.DropColumn(
                name: "change_json",
                schema: "clinical_rules",
                table: "assessment");

            migrationBuilder.DropColumn(
                name: "input_fingerprint",
                schema: "clinical_rules",
                table: "assessment");

            migrationBuilder.DropColumn(
                name: "input_refs_json",
                schema: "clinical_rules",
                table: "assessment");

            migrationBuilder.DropColumn(
                name: "locale",
                schema: "clinical_rules",
                table: "assessment");
        }
    }
}
