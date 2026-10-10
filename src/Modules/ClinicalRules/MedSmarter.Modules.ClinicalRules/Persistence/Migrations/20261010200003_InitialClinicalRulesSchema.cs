using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.ClinicalRules.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialClinicalRulesSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "clinical_rules");

            migrationBuilder.CreateTable(
                name: "assessment",
                schema: "clinical_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_patient = table.Column<bool>(type: "boolean", nullable: false),
                    trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    engine_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    data_as_of = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finding_count = table.Column<int>(type: "integer", nullable: false),
                    actionable_finding_count = table.Column<int>(type: "integer", nullable: false),
                    contains_demonstration = table.Column<bool>(type: "boolean", nullable: false),
                    guidance_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    guidance_links_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assessment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rule",
                schema: "clinical_rules",
                columns: table => new
                {
                    rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false),
                    author = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    authored_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    row_version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule", x => new { x.rule_id, x.version });
                });

            migrationBuilder.CreateTable(
                name: "rule_event",
                schema: "clinical_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rule_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assessment_patient_id_created_at",
                schema: "clinical_rules",
                table: "assessment",
                columns: new[] { "patient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_rule_status",
                schema: "clinical_rules",
                table: "rule",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_rule_event_rule_id_version",
                schema: "clinical_rules",
                table: "rule_event",
                columns: new[] { "rule_id", "version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment",
                schema: "clinical_rules");

            migrationBuilder.DropTable(
                name: "rule",
                schema: "clinical_rules");

            migrationBuilder.DropTable(
                name: "rule_event",
                schema: "clinical_rules");
        }
    }
}
