using System;
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
            migrationBuilder.DropTable(
                name: "LoginCodes");

            migrationBuilder.DropTable(
                name: "Passkeys");

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

            migrationBuilder.CreateTable(
                name: "LoginCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false, collation: "case_insensitive"),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginCodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Passkeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1024, nullable: false),
                    DateModified = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    Transports = table.Column<string[]>(type: "text[]", nullable: false),
                    UserHandle = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passkeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Passkeys_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoginCodes_Email",
                table: "LoginCodes",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Passkeys_CredentialId",
                table: "Passkeys",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Passkeys_PersonId",
                table: "Passkeys",
                column: "PersonId");
        }
    }
}
