using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Audit.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entry",
                schema: "audit",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    resource_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reason_code = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    previous_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_entry", x => x.sequence);
                    table.CheckConstraint("ck_audit_entry_result", "result IN (0, 1, 2)");
                    table.CheckConstraint("ck_audit_entry_sequence_positive", "sequence >= 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_action_sequence",
                schema: "audit",
                table: "audit_entry",
                columns: new[] { "action", "sequence" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_actor_user_id_sequence",
                schema: "audit",
                table: "audit_entry",
                columns: new[] { "actor_user_id", "sequence" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entry_subject_user_id_sequence",
                schema: "audit",
                table: "audit_entry",
                columns: new[] { "subject_user_id", "sequence" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_audit_entry_hash",
                schema: "audit",
                table: "audit_entry",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_audit_entry_id",
                schema: "audit",
                table: "audit_entry",
                column: "id",
                unique: true);

            // Append-only: the application can add entries but nobody can change or remove one through SQL without dropping these triggers.
            migrationBuilder.Sql(@"CREATE FUNCTION audit.audit_entry_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'audit.audit_entry is append-only (% is not allowed)', TG_OP USING ERRCODE = 'restrict_violation';
END;
$$;");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_entry_no_change BEFORE UPDATE OR DELETE ON audit.audit_entry FOR EACH ROW EXECUTE FUNCTION audit.audit_entry_append_only();");
            migrationBuilder.Sql("CREATE TRIGGER trg_audit_entry_no_truncate BEFORE TRUNCATE ON audit.audit_entry FOR EACH STATEMENT EXECUTE FUNCTION audit.audit_entry_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_entry_no_truncate ON audit.audit_entry;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_entry_no_change ON audit.audit_entry;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit.audit_entry_append_only();");
            migrationBuilder.DropTable(
                name: "audit_entry",
                schema: "audit");
        }
    }
}
