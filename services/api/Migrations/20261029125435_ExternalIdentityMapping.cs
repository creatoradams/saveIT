using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExternalIdentityMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalIssuer",
                table: "Memberships",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubject",
                table: "Memberships",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_ExternalIssuer_ExternalSubject",
                table: "Memberships",
                columns: new[] { "TenantId", "ExternalIssuer", "ExternalSubject" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Membership_ExternalIdentity",
                table: "Memberships",
                sql: "(\"ExternalIssuer\" IS NULL AND \"ExternalSubject\" IS NULL) OR (\"ExternalIssuer\" IS NOT NULL AND \"ExternalSubject\" IS NOT NULL AND length(\"ExternalIssuer\") > 0 AND length(\"ExternalSubject\") > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Memberships_TenantId_ExternalIssuer_ExternalSubject",
                table: "Memberships");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Membership_ExternalIdentity",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "ExternalIssuer",
                table: "Memberships");

            migrationBuilder.DropColumn(
                name: "ExternalSubject",
                table: "Memberships");
        }
    }
}
