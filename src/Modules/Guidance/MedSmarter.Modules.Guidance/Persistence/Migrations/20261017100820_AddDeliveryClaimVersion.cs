using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Guidance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryClaimVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "guidance",
                table: "guidance_delivery",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                schema: "guidance",
                table: "guidance_delivery");
        }
    }
}
