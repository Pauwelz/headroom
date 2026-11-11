using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headroom.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailServerSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmtpFromAddress",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "InstanceSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "InstanceSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmtpFromAddress",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "InstanceSettings");
        }
    }
}
