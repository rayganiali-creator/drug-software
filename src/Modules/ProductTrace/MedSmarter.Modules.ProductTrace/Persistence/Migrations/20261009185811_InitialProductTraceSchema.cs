using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.ProductTrace.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialProductTraceSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "product_trace");

            migrationBuilder.CreateTable(
                name: "dispensed_product_record",
                schema: "product_trace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_medication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    generic_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manufacturer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    batch_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    batch_normalized = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    product_key = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: false),
                    manufacture_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    gtin = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    pharmacy_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    verification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    consistency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    findings_json = table.Column<string>(type: "jsonb", nullable: false),
                    recorded_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispensed_product_record", x => x.id);
                    table.CheckConstraint("ck_dispensed_product_record_batch", "length(batch_number) BETWEEN 1 AND 40");
                    table.CheckConstraint("ck_dispensed_product_record_dates", "manufacture_date IS NULL OR manufacture_date < expiry_date");
                    table.CheckConstraint("ck_dispensed_product_record_identity", "medication_id IS NOT NULL OR length(product_name) > 0");
                });

            migrationBuilder.CreateTable(
                name: "dispensed_product_record_version",
                schema: "product_trace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispensed_product_record_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispensed_product_record_version_dispensed_product_record_product_record_id",
                        column: x => x.product_record_id,
                        principalSchema: "product_trace",
                        principalTable: "dispensed_product_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "manufacturer_safety_report",
                schema: "product_trace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    issue_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    duration_of_use_days = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    include_concomitant = table.Column<bool>(type: "boolean", nullable: false),
                    review_required = table.Column<bool>(type: "boolean", nullable: false),
                    reviewer_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    review_decision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    consent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_mock_delivery = table.Column<bool>(type: "boolean", nullable: false),
                    payload_json = table.Column<string>(type: "text", nullable: true),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    client_request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manufacturer_safety_report", x => x.id);
                    table.CheckConstraint("ck_manufacturer_safety_report_duration", "duration_of_use_days IS NULL OR duration_of_use_days BETWEEN 0 AND 36500");
                    table.ForeignKey(
                        name: "fk_manufacturer_safety_report_dispensed_product_record_product_record_id",
                        column: x => x.product_record_id,
                        principalSchema: "product_trace",
                        principalTable: "dispensed_product_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manufacturer_report_event",
                schema: "product_trace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manufacturer_report_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_manufacturer_report_event_manufacturer_safety_report_report_id",
                        column: x => x.report_id,
                        principalSchema: "product_trace",
                        principalTable: "manufacturer_safety_report",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "manufacturer_report_outbox",
                schema: "product_trace",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manufacturer_report_outbox", x => x.id);
                    table.ForeignKey(
                        name: "fk_manufacturer_report_outbox_manufacturer_safety_report_report_id",
                        column: x => x.report_id,
                        principalSchema: "product_trace",
                        principalTable: "manufacturer_safety_report",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dispensed_product_record_batch_normalized",
                schema: "product_trace",
                table: "dispensed_product_record",
                column: "batch_normalized");

            migrationBuilder.CreateIndex(
                name: "ix_dispensed_product_record_patient_id_deleted_at",
                schema: "product_trace",
                table: "dispensed_product_record",
                columns: new[] { "patient_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_dispensed_product_record_patient_id_batch_normalized_product_key_expiry_date",
                schema: "product_trace",
                table: "dispensed_product_record",
                columns: new[] { "patient_id", "batch_normalized", "product_key", "expiry_date" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ux_dispensed_product_record_version_product_record_id_version_number",
                schema: "product_trace",
                table: "dispensed_product_record_version",
                columns: new[] { "product_record_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_report_event_report_id",
                schema: "product_trace",
                table: "manufacturer_report_event",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_report_outbox_report_id",
                schema: "product_trace",
                table: "manufacturer_report_outbox",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_report_outbox_state_next_attempt_at",
                schema: "product_trace",
                table: "manufacturer_report_outbox",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ux_manufacturer_report_outbox_idempotency_key",
                schema: "product_trace",
                table: "manufacturer_report_outbox",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_safety_report_patient_id_status",
                schema: "product_trace",
                table: "manufacturer_safety_report",
                columns: new[] { "patient_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_safety_report_product_record_id",
                schema: "product_trace",
                table: "manufacturer_safety_report",
                column: "product_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_manufacturer_safety_report_status",
                schema: "product_trace",
                table: "manufacturer_safety_report",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_manufacturer_safety_report_patient_id_client_request_id",
                schema: "product_trace",
                table: "manufacturer_safety_report",
                columns: new[] { "patient_id", "client_request_id" },
                unique: true,
                filter: "client_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_manufacturer_safety_report_reference",
                schema: "product_trace",
                table: "manufacturer_safety_report",
                column: "reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispensed_product_record_version",
                schema: "product_trace");

            migrationBuilder.DropTable(
                name: "manufacturer_report_event",
                schema: "product_trace");

            migrationBuilder.DropTable(
                name: "manufacturer_report_outbox",
                schema: "product_trace");

            migrationBuilder.DropTable(
                name: "manufacturer_safety_report",
                schema: "product_trace");

            migrationBuilder.DropTable(
                name: "dispensed_product_record",
                schema: "product_trace");
        }
    }
}
