using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class DocumentTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplates", x => new { x.TenantId, x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_DocumentTemplate_Name", "public.valid_asset_text_v1(\"Name\",160,true) IS TRUE");
                    table.CheckConstraint("CK_DocumentTemplate_Version", "\"Version\">0");
                    table.ForeignKey(
                        name: "FK_DocumentTemplates_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTemplateRevisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Content = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplateRevisions", x => new { x.TenantId, x.OrganizationId, x.TemplateId, x.Version });
                    table.CheckConstraint("CK_DocumentTemplateRevision_Text", "public.valid_asset_text_v1(\"Name\",160,true) IS TRUE AND public.valid_asset_text_v1(\"Title\",160,true) IS TRUE AND public.valid_asset_text_v1(\"Content\",50000,true) IS TRUE AND length(btrim(\"Actor\"))>0");
                    table.CheckConstraint("CK_DocumentTemplateRevision_Version", "\"Version\">0");
                    table.ForeignKey(
                        name: "FK_DocumentTemplateRevisions_DocumentTemplates_TenantId_Organi~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.TemplateId },
                        principalTable: "DocumentTemplates",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentTemplateRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTemplateOrigins",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTemplateOrigins", x => new { x.TenantId, x.OrganizationId, x.DocumentId });
                    table.CheckConstraint("CK_DocumentTemplateOrigin_InitialVersion", "\"DocumentVersion\"=1 AND \"TemplateVersion\">0");
                    table.ForeignKey(
                        name: "FK_DocumentTemplateOrigins_DocumentRevisions_TenantId_Document~",
                        columns: x => new { x.TenantId, x.DocumentId, x.DocumentVersion },
                        principalTable: "DocumentRevisions",
                        principalColumns: new[] { "TenantId", "DocumentId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentTemplateOrigins_DocumentTemplateRevisions_TenantId_~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.TemplateId, x.TemplateVersion },
                        principalTable: "DocumentTemplateRevisions",
                        principalColumns: new[] { "TenantId", "OrganizationId", "TemplateId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentTemplateOrigins_Documents_TenantId_OrganizationId_D~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.DocumentId },
                        principalTable: "Documents",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentTemplateOrigins_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplateOrigins_TenantId_DocumentId_DocumentVersion",
                table: "DocumentTemplateOrigins",
                columns: new[] { "TenantId", "DocumentId", "DocumentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplateOrigins_TenantId_OrganizationId_TemplateId_~",
                table: "DocumentTemplateOrigins",
                columns: new[] { "TenantId", "OrganizationId", "TemplateId", "TemplateVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTemplates_TenantId_OrganizationId_IsArchived_Id",
                table: "DocumentTemplates",
                columns: new[] { "TenantId", "OrganizationId", "IsArchived", "Id" });
            migrationBuilder.Sql(DocumentTemplateSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(DocumentTemplateSql.Down);
            migrationBuilder.DropTable(
                name: "DocumentTemplateOrigins");

            migrationBuilder.DropTable(
                name: "DocumentTemplateRevisions");

            migrationBuilder.DropTable(
                name: "DocumentTemplates");
        }
    }
}
