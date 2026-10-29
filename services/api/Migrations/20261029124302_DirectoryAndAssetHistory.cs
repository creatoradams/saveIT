using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class DirectoryAndAssetHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                table: "Assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Assets",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Assets",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AssetRevisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Kind = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RestoredFromVersion = table.Column<int>(type: "integer", nullable: true),
                    RestoreReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetRevisions", x => new { x.TenantId, x.AssetId, x.Version });
                    table.CheckConstraint("CK_AssetRevision_Provenance", "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)");
                    table.ForeignKey(
                        name: "FK_AssetRevisions_AssetRevisions_TenantId_AssetId_RestoredFrom~",
                        columns: x => new { x.TenantId, x.AssetId, x.RestoredFromVersion },
                        principalTable: "AssetRevisions",
                        principalColumns: new[] { "TenantId", "AssetId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetRevisions_Assets_TenantId_OrganizationId_AssetId",
                        columns: x => new { x.TenantId, x.OrganizationId, x.AssetId },
                        principalTable: "Assets",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssetRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contacts", x => x.Id);
                    table.UniqueConstraint("AK_Contacts_TenantId_OrganizationId_Id", x => new { x.TenantId, x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Contact_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Contacts_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contacts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.UniqueConstraint("AK_Locations_TenantId_OrganizationId_Id", x => new { x.TenantId, x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Location_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Locations_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_TenantId_OrganizationId_LocationId",
                table: "Assets",
                columns: new[] { "TenantId", "OrganizationId", "LocationId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Asset_Version",
                table: "Assets",
                sql: "\"Version\" > 0");

            migrationBuilder.CreateIndex(
                name: "IX_AssetRevisions_TenantId_AssetId_RestoredFromVersion",
                table: "AssetRevisions",
                columns: new[] { "TenantId", "AssetId", "RestoredFromVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_AssetRevisions_TenantId_OrganizationId_AssetId",
                table: "AssetRevisions",
                columns: new[] { "TenantId", "OrganizationId", "AssetId" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_TenantId_OrganizationId_IsArchived_Id",
                table: "Contacts",
                columns: new[] { "TenantId", "OrganizationId", "IsArchived", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Locations_TenantId_OrganizationId_IsArchived_Id",
                table: "Locations",
                columns: new[] { "TenantId", "OrganizationId", "IsArchived", "Id" });

            migrationBuilder.AddForeignKey(
                name: "FK_Assets_Locations_TenantId_OrganizationId_LocationId",
                table: "Assets",
                columns: new[] { "TenantId", "OrganizationId", "LocationId" },
                principalTable: "Locations",
                principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql("""
                UPDATE "Assets" SET "UpdatedAt"="CreatedAt";
                INSERT INTO "AssetRevisions" ("TenantId","OrganizationId","AssetId","Version","Name","Kind","LocationId","Actor","CreatedAt")
                SELECT "TenantId","OrganizationId","Id",1,"Name","Kind","LocationId",'migration',"CreatedAt" FROM "Assets";
                CREATE OR REPLACE VIEW "CoreSearchSources" WITH (security_invoker=true) AS
                SELECT "TenantId",'asset'::text AS "RecordType","Id","OrganizationId","Id" AS "AssetId",
                  "Name"::text AS "Title","Kind"::text AS "Body","Version","UpdatedAt" FROM "Assets"
                UNION ALL
                SELECT "TenantId",'document'::text,"Id","OrganizationId","AssetId","Title"::text,"Content"::text,"Version","UpdatedAt" FROM "Documents";
                GRANT SELECT, INSERT, UPDATE ON "Locations","Contacts" TO saveit_app;
                GRANT SELECT, INSERT ON "AssetRevisions" TO saveit_app;
                """);
            foreach (var table in new[] { "Locations", "Contacts", "AssetRevisions" })
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
                CREATE OR REPLACE VIEW "CoreSearchSources" WITH (security_invoker=true) AS
                SELECT "TenantId",'asset'::text AS "RecordType","Id","OrganizationId","Id" AS "AssetId",
                  "Name"::text AS "Title","Kind"::text AS "Body",1 AS "Version","CreatedAt" AS "UpdatedAt" FROM "Assets"
                UNION ALL
                SELECT "TenantId",'document'::text,"Id","OrganizationId","AssetId","Title"::text,"Content"::text,"Version","UpdatedAt" FROM "Documents";
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Assets_Locations_TenantId_OrganizationId_LocationId",
                table: "Assets");

            migrationBuilder.DropTable(
                name: "AssetRevisions");

            migrationBuilder.DropTable(
                name: "Contacts");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Assets_TenantId_OrganizationId_LocationId",
                table: "Assets");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Asset_Version",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "LocationId",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Assets");
        }
    }
}
