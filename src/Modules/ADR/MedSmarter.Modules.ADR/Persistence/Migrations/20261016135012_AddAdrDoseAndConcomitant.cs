using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.ADR.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdrDoseAndConcomitant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "concomitant_medications_json",
                schema: "adr",
                table: "adverse_event",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "dose_and_administration_as_reported",
                schema: "adr",
                table: "adverse_event",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "concomitant_medications_json",
                schema: "adr",
                table: "adverse_event");

            migrationBuilder.DropColumn(
                name: "dose_and_administration_as_reported",
                schema: "adr",
                table: "adverse_event");
        }
    }
}
