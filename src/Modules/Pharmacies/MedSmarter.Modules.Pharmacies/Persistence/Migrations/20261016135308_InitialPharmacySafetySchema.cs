using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Pharmacies.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPharmacySafetySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pharmacy");

            migrationBuilder.CreateTable(
                name: "product_concern",
                schema: "pharmacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    observed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    link_state = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    linked_subject_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_product_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_record_version = table.Column<int>(type: "integer", nullable: true),
                    linked_verification_at_linking = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    linked_product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    linked_batch_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    linked_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    linked_manufacturer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    linked_gtin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    as_reported_product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    as_reported_batch_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    as_reported_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    as_reported_manufacturer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    as_reported_gtin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    linked_adverse_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_safety_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manufacturer_report_proposed = table.Column<bool>(type: "boolean", nullable: false),
                    manufacturer_report_proposed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_concern", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "safety_entry",
                schema: "pharmacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    from_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    to_status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_safety_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "safety_event",
                schema: "pharmacy",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    client_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reached_patient = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    error_examples_json = table.Column<string>(type: "jsonb", nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: true),
                    medicine_as_reported = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    how_detected_or_intercepted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    immediate_action_taken = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    linked_adverse_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    linked_product_concern_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_safety_event", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_concern_organization_id_reported_at",
                schema: "pharmacy",
                table: "product_concern",
                columns: new[] { "organization_id", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "ux_product_concern_organization_id_reporter_user_id_client_request_id",
                schema: "pharmacy",
                table: "product_concern",
                columns: new[] { "organization_id", "reporter_user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_safety_entry_record_kind_record_id_at",
                schema: "pharmacy",
                table: "safety_entry",
                columns: new[] { "record_kind", "record_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_safety_event_organization_id_reported_at",
                schema: "pharmacy",
                table: "safety_event",
                columns: new[] { "organization_id", "reported_at" });

            migrationBuilder.CreateIndex(
                name: "ux_safety_event_organization_id_reporter_user_id_client_request_id",
                schema: "pharmacy",
                table: "safety_event",
                columns: new[] { "organization_id", "reporter_user_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "product_concern",
                schema: "pharmacy");

            migrationBuilder.DropTable(
                name: "safety_entry",
                schema: "pharmacy");

            migrationBuilder.DropTable(
                name: "safety_event",
                schema: "pharmacy");
        }
    }
}
