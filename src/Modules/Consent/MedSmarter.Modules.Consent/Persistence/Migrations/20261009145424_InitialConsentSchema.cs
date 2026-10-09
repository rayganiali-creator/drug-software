using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Consent.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialConsentSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consent");

            migrationBuilder.CreateTable(
                name: "consent",
                schema: "consent",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grantee_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grantee_organization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    scope = table.Column<string[]>(type: "text[]", nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent", x => x.id);
                    table.CheckConstraint("ck_consent_expires_after_grant", "expires_at > granted_at");
                    table.CheckConstraint("ck_consent_one_grantee_at_most", "NOT (grantee_user_id IS NOT NULL AND grantee_organization_id IS NOT NULL)");
                    table.CheckConstraint("ck_consent_scope_not_empty", "cardinality(scope) > 0");
                });

            migrationBuilder.CreateTable(
                name: "consent_event",
                schema: "consent",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    consent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_consent_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_consent_event_consent_id",
                        column: x => x.consent_id,
                        principalSchema: "consent",
                        principalTable: "consent",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consent_grantee_organization_id",
                schema: "consent",
                table: "consent",
                column: "grantee_organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_grantee_user_id",
                schema: "consent",
                table: "consent",
                column: "grantee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_subject_user_id_granted_at",
                schema: "consent",
                table: "consent",
                columns: new[] { "subject_user_id", "granted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_consent_event_subject_user_id_at",
                schema: "consent",
                table: "consent_event",
                columns: new[] { "subject_user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ux_consent_event_consent_id_kind",
                schema: "consent",
                table: "consent_event",
                columns: new[] { "consent_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consent_event",
                schema: "consent");

            migrationBuilder.DropTable(
                name: "consent",
                schema: "consent");
        }
    }
}
