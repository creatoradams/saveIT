using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class DocumentRestoreProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RestoreReason",
                table: "DocumentRevisions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RestoredFromVersion",
                table: "DocumentRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceVersion",
                table: "AuditEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TargetVersion",
                table: "AuditEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRevisions_TenantId_DocumentId_RestoredFromVersion",
                table: "DocumentRevisions",
                columns: new[] { "TenantId", "DocumentId", "RestoredFromVersion" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Revision_RestoreProvenance",
                table: "DocumentRevisions",
                sql: "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentRevisions_DocumentRevisions_TenantId_DocumentId_Res~",
                table: "DocumentRevisions",
                columns: new[] { "TenantId", "DocumentId", "RestoredFromVersion" },
                principalTable: "DocumentRevisions",
                principalColumns: new[] { "TenantId", "DocumentId", "Version" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DocumentRevisions_DocumentRevisions_TenantId_DocumentId_Res~",
                table: "DocumentRevisions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentRevisions_TenantId_DocumentId_RestoredFromVersion",
                table: "DocumentRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Revision_RestoreProvenance",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "RestoreReason",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "RestoredFromVersion",
                table: "DocumentRevisions");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetVersion",
                table: "AuditEvents");
        }
    }
}
