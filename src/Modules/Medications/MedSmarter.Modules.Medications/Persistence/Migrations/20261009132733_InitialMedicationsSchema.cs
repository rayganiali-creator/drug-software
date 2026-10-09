using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Medications.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMedicationsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "medications");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "active_ingredient",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_fa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    atc_code = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    lifecycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    validation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_ingredient", x => x.id);
                    table.CheckConstraint("ck_active_ingredient_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "knowledge_source",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    publisher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    license_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    redistribution_allowed = table.Column<bool>(type: "boolean", nullable: true),
                    usage_restrictions = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    validation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_source", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "manufacturer",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_fa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    country = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: true),
                    manufacturer_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_manufacturer", x => x.id);
                    table.CheckConstraint("ck_manufacturer_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "reference_term",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_fa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reference_term", x => x.id);
                    table.CheckConstraint("ck_reference_term_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "ingredient_synonym",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingredient_synonym", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingredient_synonym_active_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "medications",
                        principalTable: "active_ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "knowledge_revision",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_knowledge_revision", x => x.id);
                    table.ForeignKey(
                        name: "fk_knowledge_revision_knowledge_source_source_id",
                        column: x => x.source_id,
                        principalSchema: "medications",
                        principalTable: "knowledge_source",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "brand",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_fa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_brand", x => x.id);
                    table.CheckConstraint("ck_brand_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_brand_manufacturer_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalSchema: "medications",
                        principalTable: "manufacturer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "drug_interaction",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    mechanism_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    mechanism_fa = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    management_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    management_fa = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drug_interaction", x => x.id);
                    table.CheckConstraint("ck_drug_interaction_order", "ingredient_a_id < ingredient_b_id");
                    table.ForeignKey(
                        name: "fk_drug_interaction_active_ingredient_ingredient_a_id",
                        column: x => x.ingredient_a_id,
                        principalSchema: "medications",
                        principalTable: "active_ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_drug_interaction_active_ingredient_ingredient_b_id",
                        column: x => x.ingredient_b_id,
                        principalSchema: "medications",
                        principalTable: "active_ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_drug_interaction_knowledge_revision_revision_id",
                        column: x => x.revision_id,
                        principalSchema: "medications",
                        principalTable: "knowledge_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    name_en = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    name_fa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: true),
                    manufacturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    dosage_form_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lifecycle = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    validation = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication", x => x.id);
                    table.CheckConstraint("ck_medication_demo_not_validated", "NOT (is_demo AND validation = 'Validated')");
                    table.CheckConstraint("ck_medication_has_name", "name_en IS NOT NULL OR name_fa IS NOT NULL");
                    table.CheckConstraint("ck_medication_version_positive", "version >= 1");
                    table.ForeignKey(
                        name: "fk_medication_brand_brand_id",
                        column: x => x.brand_id,
                        principalSchema: "medications",
                        principalTable: "brand",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_manufacturer_manufacturer_id",
                        column: x => x.manufacturer_id,
                        principalSchema: "medications",
                        principalTable: "manufacturer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_reference_term_dosage_form_id",
                        column: x => x.dosage_form_id,
                        principalSchema: "medications",
                        principalTable: "reference_term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_classification",
                schema: "medications",
                columns: table => new
                {
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_classification", x => new { x.medication_id, x.term_id });
                    table.ForeignKey(
                        name: "fk_medication_classification_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_medication_classification_reference_term_term_id",
                        column: x => x.term_id,
                        principalSchema: "medications",
                        principalTable: "reference_term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_identifier",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheme = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_revision_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_identifier", x => x.id);
                    table.ForeignKey(
                        name: "fk_medication_identifier_knowledge_revision_source_revision_id",
                        column: x => x.source_revision_id,
                        principalSchema: "medications",
                        principalTable: "knowledge_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_identifier_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medication_ingredient",
                schema: "medications",
                columns: table => new
                {
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingredient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    strength_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    strength_unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    per_unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_ingredient", x => new { x.medication_id, x.ingredient_id });
                    table.CheckConstraint("ck_medication_ingredient_strength", "(strength_value IS NULL AND strength_unit IS NULL) OR (strength_value > 0 AND strength_unit IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_medication_ingredient_active_ingredient_ingredient_id",
                        column: x => x.ingredient_id,
                        principalSchema: "medications",
                        principalTable: "active_ingredient",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_ingredient_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medication_route",
                schema: "medications",
                columns: table => new
                {
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    term_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_route", x => new { x.medication_id, x.term_id });
                    table.ForeignKey(
                        name: "fk_medication_route_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_medication_route_reference_term_term_id",
                        column: x => x.term_id,
                        principalSchema: "medications",
                        principalTable: "reference_term",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "medication_search_term",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_search_term", x => x.id);
                    table.ForeignKey(
                        name: "fk_medication_search_term_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medication_statement",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    text_en = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    text_fa = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    frequency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    population = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_statement", x => x.id);
                    table.CheckConstraint("ck_medication_statement_has_text", "text_en IS NOT NULL OR text_fa IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_medication_statement_knowledge_revision_revision_id",
                        column: x => x.revision_id,
                        principalSchema: "medications",
                        principalTable: "knowledge_revision",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_medication_statement_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medication_synonym",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_synonym", x => x.id);
                    table.ForeignKey(
                        name: "fk_medication_synonym_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medication_version",
                schema: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    medication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    change_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medication_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_medication_version_medication_medication_id",
                        column: x => x.medication_id,
                        principalSchema: "medications",
                        principalTable: "medication",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_active_ingredient_atc_code",
                schema: "medications",
                table: "active_ingredient",
                column: "atc_code");

            migrationBuilder.CreateIndex(
                name: "ix_brand_manufacturer_id",
                schema: "medications",
                table: "brand",
                column: "manufacturer_id");

            migrationBuilder.CreateIndex(
                name: "ix_drug_interaction_ingredient_b_id",
                schema: "medications",
                table: "drug_interaction",
                column: "ingredient_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_drug_interaction_revision_id",
                schema: "medications",
                table: "drug_interaction",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ux_drug_interaction_ingredient_a_id_ingredient_b_id",
                schema: "medications",
                table: "drug_interaction",
                columns: new[] { "ingredient_a_id", "ingredient_b_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ingredient_synonym_ingredient_id",
                schema: "medications",
                table: "ingredient_synonym",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_revision_source_id_label",
                schema: "medications",
                table: "knowledge_revision",
                columns: new[] { "source_id", "label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_knowledge_source_name_version",
                schema: "medications",
                table: "knowledge_source",
                columns: new[] { "name", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_manufacturer_manufacturer_code",
                schema: "medications",
                table: "manufacturer",
                column: "manufacturer_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medication_brand_id",
                schema: "medications",
                table: "medication",
                column: "brand_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_dosage_form_id",
                schema: "medications",
                table: "medication",
                column: "dosage_form_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_lifecycle_validation",
                schema: "medications",
                table: "medication",
                columns: new[] { "lifecycle", "validation" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_manufacturer_id",
                schema: "medications",
                table: "medication",
                column: "manufacturer_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_classification_term_id",
                schema: "medications",
                table: "medication_classification",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_identifier_medication_id",
                schema: "medications",
                table: "medication_identifier",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_identifier_source_revision_id",
                schema: "medications",
                table: "medication_identifier",
                column: "source_revision_id");

            migrationBuilder.CreateIndex(
                name: "ux_medication_identifier_scheme_value",
                schema: "medications",
                table: "medication_identifier",
                columns: new[] { "scheme", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_medication_ingredient_ingredient_id",
                schema: "medications",
                table: "medication_ingredient",
                column: "ingredient_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_route_term_id",
                schema: "medications",
                table: "medication_route",
                column: "term_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_search_term_medication_id",
                schema: "medications",
                table: "medication_search_term",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_search_term_trgm",
                schema: "medications",
                table: "medication_search_term",
                column: "normalized")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_statement_medication_id_kind",
                schema: "medications",
                table: "medication_statement",
                columns: new[] { "medication_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_medication_statement_revision_id",
                schema: "medications",
                table: "medication_statement",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_medication_synonym_medication_id",
                schema: "medications",
                table: "medication_synonym",
                column: "medication_id");

            migrationBuilder.CreateIndex(
                name: "ux_medication_version_medication_id_version_number",
                schema: "medications",
                table: "medication_version",
                columns: new[] { "medication_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_reference_term_kind_code",
                schema: "medications",
                table: "reference_term",
                columns: new[] { "kind", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "drug_interaction",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "ingredient_synonym",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_classification",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_identifier",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_ingredient",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_route",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_search_term",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_statement",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_synonym",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication_version",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "active_ingredient",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "knowledge_revision",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "medication",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "knowledge_source",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "brand",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "reference_term",
                schema: "medications");

            migrationBuilder.DropTable(
                name: "manufacturer",
                schema: "medications");
        }
    }
}
