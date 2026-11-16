using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Headroom.Migrations
{
    /// <inheritdoc />
    public partial class EntraIdLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Expand and contract (docs/migrations.md): 0.5.0 still reads LoginCodes, Passkeys and the Smtp* columns.
            // shortcut: the model no longer maps them, so the next release drops them with a hand-written migration.

            // Sessions from a Login code or a Passkey must not outlive the switch to Entra ID.
            migrationBuilder.Sql("DELETE FROM \"Sessions\";");

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
        }
    }
}
