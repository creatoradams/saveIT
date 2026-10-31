using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrganizationRevisions",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Actor = table.Column<string>(type: "text", nullable: false),
                    Origin = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRevisions", x => new { x.TenantId, x.OrganizationId, x.Version });
                    table.CheckConstraint("CK_OrganizationRevision_Actor", "length(btrim(\"Actor\")) > 0");
                    table.CheckConstraint("CK_OrganizationRevision_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 160");
                    table.CheckConstraint("CK_OrganizationRevision_Origin", "\"Origin\" IN ('Created','Renamed','Migration','Seed')");
                    table.CheckConstraint("CK_OrganizationRevision_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_OrganizationRevisions_Organizations_TenantId_OrganizationId",
                        columns: x => new { x.TenantId, x.OrganizationId },
                        principalTable: "Organizations",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrganizationRevisions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.Sql("""
                INSERT INTO "OrganizationRevisions" ("TenantId","OrganizationId","Version","Name","Actor","CreatedAt","Origin")
                SELECT "TenantId","Id","Version","Name",'migration',"UpdatedAt",'Migration' FROM "Organizations";
                REVOKE ALL ON "OrganizationRevisions" FROM PUBLIC, saveit_app, saveit_search_worker;
                GRANT SELECT ON "OrganizationRevisions" TO saveit_app;
                ALTER TABLE "OrganizationRevisions" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "OrganizationRevisions" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_boundary ON "OrganizationRevisions" FOR ALL TO saveit_app
                USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                """);
            migrationBuilder.Sql(OrganizationHistorySql.Functions(true));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(OrganizationHistorySql.Functions(false));
            migrationBuilder.DropTable(
                name: "OrganizationRevisions");
        }
    }
}
