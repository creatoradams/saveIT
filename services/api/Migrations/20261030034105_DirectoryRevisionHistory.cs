using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class DirectoryRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContactRevisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RestoredFromVersion = table.Column<int>(type: "integer", nullable: true),
                    RestoreReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactRevisions", x => new { x.TenantId, x.OrganizationId, x.RecordId, x.Version });
                    table.CheckConstraint("CK_ContactRevision_Provenance", "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)");
                    table.CheckConstraint("CK_ContactRevision_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_ContactRevisions_ContactRevisions_TenantId_OrganizationId_R~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.RecordId, x.RestoredFromVersion },
                        principalTable: "ContactRevisions",
                        principalColumns: new[] { "TenantId", "OrganizationId", "RecordId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContactRevisions_Contacts_TenantId_OrganizationId_RecordId",
                        columns: x => new { x.TenantId, x.OrganizationId, x.RecordId },
                        principalTable: "Contacts",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContactRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationRevisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RestoredFromVersion = table.Column<int>(type: "integer", nullable: true),
                    RestoreReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationRevisions", x => new { x.TenantId, x.OrganizationId, x.RecordId, x.Version });
                    table.CheckConstraint("CK_LocationRevision_Provenance", "COALESCE((\"RestoredFromVersion\" IS NULL AND \"RestoreReason\" IS NULL) OR (\"RestoredFromVersion\" > 0 AND \"RestoredFromVersion\" < \"Version\" AND length(btrim(\"RestoreReason\")) BETWEEN 3 AND 500),false)");
                    table.CheckConstraint("CK_LocationRevision_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_LocationRevisions_LocationRevisions_TenantId_OrganizationId~",
                        columns: x => new { x.TenantId, x.OrganizationId, x.RecordId, x.RestoredFromVersion },
                        principalTable: "LocationRevisions",
                        principalColumns: new[] { "TenantId", "OrganizationId", "RecordId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LocationRevisions_Locations_TenantId_OrganizationId_RecordId",
                        columns: x => new { x.TenantId, x.OrganizationId, x.RecordId },
                        principalTable: "Locations",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LocationRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContactRevisions_TenantId_OrganizationId_RecordId_RestoredF~",
                table: "ContactRevisions",
                columns: new[] { "TenantId", "OrganizationId", "RecordId", "RestoredFromVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_LocationRevisions_TenantId_OrganizationId_RecordId_Restored~",
                table: "LocationRevisions",
                columns: new[] { "TenantId", "OrganizationId", "RecordId", "RestoredFromVersion" });
            migrationBuilder.Sql("""
                INSERT INTO "LocationRevisions" ("TenantId","OrganizationId","RecordId","Version","Name","Details","Notes","IsArchived","Actor","CreatedAt")
                SELECT "TenantId","OrganizationId","Id","Version","Name","Details","Notes","IsArchived",'migration',"UpdatedAt" FROM "Locations";
                INSERT INTO "ContactRevisions" ("TenantId","OrganizationId","RecordId","Version","Name","Details","Email","Phone","Notes","IsArchived","Actor","CreatedAt")
                SELECT "TenantId","OrganizationId","Id","Version","Name","Details","Email","Phone","Notes","IsArchived",'migration',"UpdatedAt" FROM "Contacts";
                REVOKE ALL ON "LocationRevisions","ContactRevisions" FROM PUBLIC, saveit_app, saveit_search_worker;
                GRANT SELECT, INSERT ON "LocationRevisions","ContactRevisions" TO saveit_app;
                """);
            foreach (var table in new[] { "LocationRevisions", "ContactRevisions" })
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
            migrationBuilder.DropTable(
                name: "ContactRevisions");

            migrationBuilder.DropTable(
                name: "LocationRevisions");
        }
    }
}
