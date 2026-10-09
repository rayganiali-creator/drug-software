using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Patients.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPatientsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "patients");

            migrationBuilder.CreateTable(
                name: "patient",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "patient_record_version",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_record_version", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "care_relationship",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    initiated_by = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    patient_consented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    provider_consented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by = table.Column<Guid>(type: "uuid", nullable: true),
                    end_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_relationship", x => x.id);
                    table.CheckConstraint("ck_care_relationship_has_provider", "provider_user_id IS NOT NULL OR provider_organization_id IS NOT NULL");
                    table.CheckConstraint("ck_care_relationship_not_self", "patient_user_id <> provider_user_id");
                    table.ForeignKey(
                        name: "fk_care_relationship_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "external_patient_identifier",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheme = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    value_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: false),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    linked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_patient_identifier", x => x.id);
                    table.ForeignKey(
                        name: "fk_external_patient_identifier_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_allergy",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    substance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reaction = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_allergy", x => x.id);
                    table.CheckConstraint("ck_patient_allergy_medication_kind", "medication_id IS NULL OR kind = 'Medication'");
                    table.ForeignKey(
                        name: "fk_patient_allergy_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_condition",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    onset_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_condition", x => x.id);
                    table.CheckConstraint("ck_patient_condition_name", "length(name) BETWEEN 1 AND 200");
                    table.ForeignKey(
                        name: "fk_patient_condition_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_data_snapshot_meta",
                schema: "patients",
                columns: table => new
                {
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    last_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    record_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_data_snapshot_meta", x => new { x.patient_id, x.category });
                    table.ForeignKey(
                        name: "fk_patient_data_snapshot_meta_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_medication",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    unregistered_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    dose_amount = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    dose_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    dose_text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    frequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    frequency_value = table.Column<int>(type: "integer", nullable: true),
                    route = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    prescriber_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stop_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_medication", x => x.id);
                    table.CheckConstraint("ck_patient_medication_dates", "end_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_patient_medication_dose", "(dose_amount IS NULL AND dose_unit IS NULL) OR (dose_amount > 0 AND dose_unit IS NOT NULL)");
                    table.CheckConstraint("ck_patient_medication_identity", "medication_id IS NOT NULL OR (unregistered_name IS NOT NULL AND length(unregistered_name) > 0)");
                    table.ForeignKey(
                        name: "fk_patient_medication_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_profile",
                schema: "patients",
                columns: table => new
                {
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year_of_birth = table.Column<int>(type: "integer", nullable: true),
                    sex = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    weight_kg = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    height_cm = table.Column<decimal>(type: "numeric(5,1)", precision: 5, scale: 1, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_profile", x => x.patient_id);
                    table.CheckConstraint("ck_patient_profile_height", "height_cm IS NULL OR (height_cm > 0 AND height_cm <= 260)");
                    table.CheckConstraint("ck_patient_profile_weight", "weight_kg IS NULL OR (weight_kg > 0 AND weight_kg <= 500)");
                    table.CheckConstraint("ck_patient_profile_year_of_birth", "year_of_birth IS NULL OR (year_of_birth BETWEEN 1900 AND 2200)");
                    table.ForeignKey(
                        name: "fk_patient_profile_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "patient_symptom_report",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    onset_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    patient_medication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patient_symptom_report", x => x.id);
                    table.ForeignKey(
                        name: "fk_patient_symptom_report_patient_patient_id",
                        column: x => x.patient_id,
                        principalSchema: "patients",
                        principalTable: "patient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "care_relationship_event",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relationship_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_care_relationship_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_care_relationship_event_care_relationship_relationship_id",
                        column: x => x.relationship_id,
                        principalSchema: "patients",
                        principalTable: "care_relationship",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_schedule_entry",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    time_of_day = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    days_mask = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_schedule_entry", x => x.id);
                    table.CheckConstraint("ck_medication_schedule_entry_days", "days_mask BETWEEN 1 AND 127");
                    table.ForeignKey(
                        name: "fk_medication_schedule_entry_patient_medication_patient_medication_id",
                        column: x => x.patient_medication_id,
                        principalSchema: "patients",
                        principalTable: "patient_medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_intake_log",
                schema: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    schedule_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_intake_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_medication_intake_log_medication_schedule_entry_schedule_entry_id",
                        column: x => x.schedule_entry_id,
                        principalSchema: "patients",
                        principalTable: "medication_schedule_entry",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_intake_log_patient_medication_patient_medication_id",
                        column: x => x.patient_medication_id,
                        principalSchema: "patients",
                        principalTable: "patient_medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_care_relationship_patient_id",
                schema: "patients",
                table: "care_relationship",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_care_relationship_patient_user_id_status",
                schema: "patients",
                table: "care_relationship",
                columns: new[] { "patient_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_care_relationship_provider_user_id_status",
                schema: "patients",
                table: "care_relationship",
                columns: new[] { "provider_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_care_relationship_patient_user_id_provider_organization_id_kind",
                schema: "patients",
                table: "care_relationship",
                columns: new[] { "patient_user_id", "provider_organization_id", "kind" },
                unique: true,
                filter: "provider_organization_id IS NOT NULL AND status IN ('PendingProvider', 'PendingPatient', 'Active')");

            migrationBuilder.CreateIndex(
                name: "ux_care_relationship_patient_user_id_provider_user_id_kind",
                schema: "patients",
                table: "care_relationship",
                columns: new[] { "patient_user_id", "provider_user_id", "kind" },
                unique: true,
                filter: "provider_user_id IS NOT NULL AND status IN ('PendingProvider', 'PendingPatient', 'Active')");

            migrationBuilder.CreateIndex(
                name: "ix_care_relationship_event_relationship_id",
                schema: "patients",
                table: "care_relationship_event",
                column: "relationship_id");

            migrationBuilder.CreateIndex(
                name: "ix_external_patient_identifier_patient_id",
                schema: "patients",
                table: "external_patient_identifier",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ux_external_patient_identifier_scheme_value_hash",
                schema: "patients",
                table: "external_patient_identifier",
                columns: new[] { "scheme", "value_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medication_intake_log_patient_id_recorded_at",
                schema: "patients",
                table: "medication_intake_log",
                columns: new[] { "patient_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_intake_log_patient_medication_id",
                schema: "patients",
                table: "medication_intake_log",
                column: "patient_medication_id");

            migrationBuilder.CreateIndex(
                name: "ux_medication_intake_log_schedule_entry_id_scheduled_for",
                schema: "patients",
                table: "medication_intake_log",
                columns: new[] { "schedule_entry_id", "scheduled_for" },
                unique: true,
                filter: "schedule_entry_id IS NOT NULL AND scheduled_for IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_medication_schedule_entry_patient_id_patient_medication_id",
                schema: "patients",
                table: "medication_schedule_entry",
                columns: new[] { "patient_id", "patient_medication_id" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_schedule_entry_patient_medication_id",
                schema: "patients",
                table: "medication_schedule_entry",
                column: "patient_medication_id");

            migrationBuilder.CreateIndex(
                name: "ux_patient_user_id",
                schema: "patients",
                table: "patient",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_allergy_patient_id_deleted_at",
                schema: "patients",
                table: "patient_allergy",
                columns: new[] { "patient_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_condition_patient_id_deleted_at",
                schema: "patients",
                table: "patient_condition",
                columns: new[] { "patient_id", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_medication_medication_id",
                schema: "patients",
                table: "patient_medication",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "ix_patient_medication_patient_id_status_deleted_at",
                schema: "patients",
                table: "patient_medication",
                columns: new[] { "patient_id", "status", "deleted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_patient_record_version_patient_id",
                schema: "patients",
                table: "patient_record_version",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ux_patient_record_version_record_type_record_id_version_number",
                schema: "patients",
                table: "patient_record_version",
                columns: new[] { "record_type", "record_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_symptom_report_patient_id_onset_at",
                schema: "patients",
                table: "patient_symptom_report",
                columns: new[] { "patient_id", "onset_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "care_relationship_event",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "external_patient_identifier",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "medication_intake_log",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_allergy",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_condition",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_data_snapshot_meta",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_profile",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_record_version",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_symptom_report",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "care_relationship",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "medication_schedule_entry",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient_medication",
                schema: "patients");

            migrationBuilder.DropTable(
                name: "patient",
                schema: "patients");
        }
    }
}
