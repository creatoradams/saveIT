using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssetRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AssetRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedBy = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetRelationships", x => x.Id);
                    table.CheckConstraint("CK_Relationship_Endpoints", "\"SourceAssetId\" <> \"TargetAssetId\"");
                    table.CheckConstraint("CK_Relationship_Kind", "\"Kind\" IN ('DependsOn','Contains')");
                    table.CheckConstraint("CK_Relationship_Lifecycle", "COALESCE((\"Version\"=1 AND \"RemovedAt\" IS NULL AND \"RemovedBy\" IS NULL) OR (\"Version\"=2 AND \"RemovedAt\" IS NOT NULL AND length(btrim(\"RemovedBy\")) > 0),false)");
                    table.ForeignKey(
                        name: "FK_AssetRelationships_Assets_TenantId_OrganizationId_SourceAss~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.SourceAssetId },
                        principalTable: "Assets",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetRelationships_Assets_TenantId_OrganizationId_TargetAss~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.TargetAssetId },
                        principalTable: "Assets",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetRelationships_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssetRelationships_TenantId_OrganizationId_SourceAssetId_Id",
                table: "AssetRelationships",
                columns: new[] { "TenantId", "OrganizationId", "SourceAssetId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetRelationships_TenantId_OrganizationId_SourceAssetId_Ta~",
                table: "AssetRelationships",
                columns: new[] { "TenantId", "OrganizationId", "SourceAssetId", "TargetAssetId", "Kind" },
                unique: true,
                filter: "\"RemovedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRelationships_TenantId_OrganizationId_TargetAssetId_Id",
                table: "AssetRelationships",
                columns: new[] { "TenantId", "OrganizationId", "TargetAssetId", "Id" });

            migrationBuilder.Sql("""
                ALTER TABLE "AssetRelationships" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "AssetRelationships" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_boundary ON "AssetRelationships" FOR ALL TO saveit_app
                USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                GRANT SELECT, INSERT ON "AssetRelationships" TO saveit_app;
                GRANT UPDATE ("Version", "RemovedAt", "RemovedBy") ON "AssetRelationships" TO saveit_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetRelationships");
        }
    }
}
