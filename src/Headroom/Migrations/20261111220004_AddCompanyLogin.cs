using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headroom.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CompanyLoginIssuer",
                table: "People",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyLoginSubject",
                table: "People",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyLoginClientId",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyLoginClientSecret",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyLoginIssuer",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyLoginKind",
                table: "InstanceSettings",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_CompanyLoginIssuer_CompanyLoginSubject",
                table: "People",
                columns: new[] { "CompanyLoginIssuer", "CompanyLoginSubject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_People_CompanyLoginIssuer_CompanyLoginSubject",
                table: "People");

            migrationBuilder.DropColumn(
                name: "CompanyLoginIssuer",
                table: "People");

            migrationBuilder.DropColumn(
                name: "CompanyLoginSubject",
                table: "People");

            migrationBuilder.DropColumn(
                name: "CompanyLoginClientId",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "CompanyLoginClientSecret",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "CompanyLoginIssuer",
                table: "InstanceSettings");

            migrationBuilder.DropColumn(
                name: "CompanyLoginKind",
                table: "InstanceSettings");
        }
    }
}
