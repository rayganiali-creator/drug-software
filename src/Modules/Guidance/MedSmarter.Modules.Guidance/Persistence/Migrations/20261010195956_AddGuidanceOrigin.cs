using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedSmarter.Modules.Guidance.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuidanceOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "origin_assessment_id",
                schema: "guidance",
                table: "guidance_message",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "origin_data_as_of",
                schema: "guidance",
                table: "guidance_message",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_finding_key",
                schema: "guidance",
                table: "guidance_message",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_kind",
                schema: "guidance",
                table: "guidance_message",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "origin_rule_id",
                schema: "guidance",
                table: "guidance_message",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "origin_rule_version",
                schema: "guidance",
                table: "guidance_message",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_guidance_message_patient_id_origin_finding_key",
                schema: "guidance",
                table: "guidance_message",
                columns: new[] { "patient_id", "origin_finding_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_guidance_message_patient_id_origin_finding_key",
                schema: "guidance",
                table: "guidance_message");

            migrationBuilder.DropColumn(
                name: "origin_assessment_id",
                schema: "guidance",
                table: "guidance_message");

            migrationBuilder.DropColumn(
                name: "origin_data_as_of",
                schema: "guidance",
                table: "guidance_message");

            migrationBuilder.DropColumn(
                name: "origin_finding_key",
                schema: "guidance",
                table: "guidance_message");

            migrationBuilder.DropColumn(
                name: "origin_kind",
                schema: "guidance",
                table: "guidance_message");

            migrationBuilder.DropColumn(
                name: "origin_rule_id",
                schema: "guidance",
                table: "guidance_message");

            migrationBuilder.DropColumn(
                name: "origin_rule_version",
                schema: "guidance",
                table: "guidance_message");
        }
    }
}
