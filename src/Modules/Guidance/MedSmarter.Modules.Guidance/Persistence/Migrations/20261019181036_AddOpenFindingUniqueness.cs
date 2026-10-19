using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Guidance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOpenFindingUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_guidance_message_open_finding",
                schema: "guidance",
                table: "guidance_message",
                columns: new[] { "patient_id", "origin_finding_key" },
                unique: true,
                filter: "origin_finding_key IS NOT NULL AND status <> 'Resolved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_guidance_message_open_finding",
                schema: "guidance",
                table: "guidance_message");
        }
    }
}
