using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.ADR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAdrSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "adr");

            migrationBuilder.CreateTable(
                name: "adverse_event",
                schema: "adr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    event_description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    onset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome_as_stated = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    additional_details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    suspected_medications_json = table.Column<string>(type: "jsonb", nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adverse_event", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "adverse_event_entry",
                schema: "adr",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    from_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    from_category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    to_category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    supplement_onset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    supplement_resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    supplement_outcome_as_stated = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adverse_event_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_adverse_event_entry_adverse_event_event_id",
                        column: x => x.event_id,
                        principalSchema: "adr",
                        principalTable: "adverse_event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adverse_event_status_last_activity_at",
                schema: "adr",
                table: "adverse_event",
                columns: new[] { "status", "last_activity_at" });

            migrationBuilder.CreateIndex(
                name: "ix_adverse_event_subject_id_reported_at",
                schema: "adr",
                table: "adverse_event",
                columns: new[] { "subject_id", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "ux_adverse_event_subject_id_reporter_user_id_client_request_id",
                schema: "adr",
                table: "adverse_event",
                columns: new[] { "subject_id", "reporter_user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_adverse_event_entry_event_id_at",
                schema: "adr",
                table: "adverse_event_entry",
                columns: new[] { "event_id", "at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "adverse_event_entry",
                schema: "adr");

            migrationBuilder.DropTable(
                name: "adverse_event",
                schema: "adr");
        }
    }
}
