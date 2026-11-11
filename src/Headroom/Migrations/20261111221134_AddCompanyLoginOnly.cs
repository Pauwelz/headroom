using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headroom.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyLoginOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "CompanyDomains",
                table: "InstanceSettings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "CompanyLoginOnly",
                table: "InstanceSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompanyDomains",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "CompanyLoginOnly",
                table: "InstanceSettings");
        }
    }
}
