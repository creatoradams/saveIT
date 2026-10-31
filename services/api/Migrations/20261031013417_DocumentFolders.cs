using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class DocumentFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentFolders", x => new { x.TenantId, x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_DocumentFolder_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 160");
                    table.CheckConstraint("CK_DocumentFolder_Parent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
                    table.CheckConstraint("CK_DocumentFolder_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_DocumentFolders_DocumentFolders_TenantId_OrganizationId_Par~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.ParentId },
                        principalTable: "DocumentFolders",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFolders_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentFolders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentPlacements",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentPlacements", x => new { x.TenantId, x.OrganizationId, x.DocumentId });
                    table.CheckConstraint("CK_DocumentPlacement_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_DocumentPlacements_DocumentFolders_TenantId_OrganizationId_~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.FolderId },
                        principalTable: "DocumentFolders",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentPlacements_Documents_TenantId_OrganizationId_Docume~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.DocumentId },
                        principalTable: "Documents",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentPlacements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentFolders_TenantId_OrganizationId_ParentId_Id",
                table: "DocumentFolders",
                columns: new[] { "TenantId", "OrganizationId", "ParentId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentPlacements_TenantId_OrganizationId_FolderId_Documen~",
                table: "DocumentPlacements",
                columns: new[] { "TenantId", "OrganizationId", "FolderId", "DocumentId" });
            migrationBuilder.Sql(DocumentFolderSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DocumentFolderSql.Down);
            migrationBuilder.DropTable(
                name: "DocumentPlacements");

            migrationBuilder.DropTable(
                name: "DocumentFolders");
        }
    }
}
