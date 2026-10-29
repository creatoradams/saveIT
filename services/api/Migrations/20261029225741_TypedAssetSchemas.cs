using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class TypedAssetSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(TypedAssetSchemaSql.Validators);
            migrationBuilder.AddColumn<Guid>(
                name: "AssetTypeId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssetTypeVersion",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldsJson",
                table: "Assets",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<Guid>(
                name: "AssetTypeId",
                table: "AssetRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AssetTypeVersion",
                table: "AssetRevisions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldsJson",
                table: "AssetRevisions",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.CreateTable(
                name: "AssetTypes",
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
                    table.PrimaryKey("PK_AssetTypes", x => x.Id);
                    table.UniqueConstraint("AK_AssetTypes_TenantId_OrganizationId_Id", x => new { x.TenantId, x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_AssetType_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AssetTypes_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTypes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssetTypeRevisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DefinitionJson = table.Column<string>(type: "jsonb", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetTypeRevisions", x => new { x.TenantId, x.OrganizationId, x.AssetTypeId, x.Version });
                    table.CheckConstraint("CK_AssetTypeRevision_Definition", "public.valid_asset_definition_v1(\"DefinitionJson\") IS TRUE");
                    table.CheckConstraint("CK_AssetTypeRevision_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_AssetTypeRevisions_AssetTypes_TenantId_OrganizationId_Asset~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.AssetTypeId },
                        principalTable: "AssetTypes",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetTypeRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_OrganizationId_AssetTypeId_AssetTypeVersion",
                table: "Assets",
                columns: new[] { "TenantId", "OrganizationId", "AssetTypeId", "AssetTypeVersion" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Asset_TypePair",
                table: "Assets",
                sql: "(\"AssetTypeId\" IS NULL AND \"AssetTypeVersion\" IS NULL AND \"FieldsJson\"='{}'::jsonb) OR (\"AssetTypeId\" IS NOT NULL AND \"AssetTypeVersion\" IS NOT NULL AND \"AssetTypeVersion\">0)");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRevisions_TenantId_OrganizationId_AssetTypeId_AssetTyp~",
                table: "AssetRevisions",
                columns: new[] { "TenantId", "OrganizationId", "AssetTypeId", "AssetTypeVersion" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssetRevision_TypePair",
                table: "AssetRevisions",
                sql: "(\"AssetTypeId\" IS NULL AND \"AssetTypeVersion\" IS NULL AND \"FieldsJson\"='{}'::jsonb) OR (\"AssetTypeId\" IS NOT NULL AND \"AssetTypeVersion\" IS NOT NULL AND \"AssetTypeVersion\">0)");

            migrationBuilder.CreateIndex(
                name: "IX_AssetTypes_TenantId_OrganizationId_IsArchived_Id",
                table: "AssetTypes",
                columns: new[] { "TenantId", "OrganizationId", "IsArchived", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_AssetRevisions_AssetTypeRevisions_TenantId_OrganizationId_A~",
                table: "AssetRevisions",
                columns: new[] { "TenantId", "OrganizationId", "AssetTypeId", "AssetTypeVersion" },
                principalTable: "AssetTypeRevisions",
                principalColumns: new[] { "TenantId", "OrganizationId", "AssetTypeId", "Version" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_AssetTypeRevisions_TenantId_OrganizationId_AssetType~",
                table: "Assets",
                columns: new[] { "TenantId", "OrganizationId", "AssetTypeId", "AssetTypeVersion" },
                principalTable: "AssetTypeRevisions",
                principalColumns: new[] { "TenantId", "OrganizationId", "AssetTypeId", "Version" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(TypedAssetSchemaSql.Enforcement);
            foreach (var table in new[] { "AssetTypes", "AssetTypeRevisions" })
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_boundary ON "{table}" FOR ALL TO saveit_app
                    USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                    WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                    """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER asset_schema ON "Assets";
                DROP TRIGGER asset_revision_schema ON "AssetRevisions";
                DROP FUNCTION public.enforce_asset_schema_v1();
                GRANT SELECT ON "Assets" TO saveit_search_worker;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_AssetRevisions_AssetTypeRevisions_TenantId_OrganizationId_A~",
                table: "AssetRevisions");

            migrationBuilder.DropForeignKey(
                name: "FK_Assets_AssetTypeRevisions_TenantId_OrganizationId_AssetType~",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "AssetTypeRevisions");

            migrationBuilder.DropTable(
                name: "AssetTypes");

            migrationBuilder.DropIndex(
                name: "IX_Assets_TenantId_OrganizationId_AssetTypeId_AssetTypeVersion",
                table: "Assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Asset_TypePair",
                table: "Assets");

            migrationBuilder.DropIndex(
                name: "IX_AssetRevisions_TenantId_OrganizationId_AssetTypeId_AssetTyp~",
                table: "AssetRevisions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssetRevision_TypePair",
                table: "AssetRevisions");

            migrationBuilder.DropColumn(
                name: "AssetTypeId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "AssetTypeVersion",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "FieldsJson",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "AssetTypeId",
                table: "AssetRevisions");

            migrationBuilder.DropColumn(
                name: "AssetTypeVersion",
                table: "AssetRevisions");

            migrationBuilder.DropColumn(
                name: "FieldsJson",
                table: "AssetRevisions");
            migrationBuilder.Sql("""
                DROP FUNCTION public.valid_asset_fields_v1(jsonb,jsonb);
                DROP FUNCTION public.valid_asset_definition_v1(jsonb);
                DROP FUNCTION public.valid_asset_value_v1(jsonb,jsonb);
                DROP FUNCTION public.valid_asset_text_v1(text,integer,boolean);
                """);
        }
    }
}
