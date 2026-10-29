using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class SearchProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OutboxEvents_TenantId_Id",
                table: "OutboxEvents",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "SearchDeliveries",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchDeliveries", x => new { x.TenantId, x.EventId });
                    table.ForeignKey(
                        name: "FK_SearchDeliveries_OutboxEvents_TenantId_EventId",
                        columns: x => new { x.TenantId, x.EventId },
                        principalTable: "OutboxEvents",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SearchDeliveries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SearchEntries",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Body = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IndexedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SearchVector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false)
                        .Annotation("Npgsql:TsVectorConfig", "simple")
                        .Annotation("Npgsql:TsVectorProperties", new[] { "Title", "Body" })
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchEntries", x => new { x.TenantId, x.RecordType, x.Id });
                    table.CheckConstraint("CK_SearchEntry_Type", "\"RecordType\" IN ('asset', 'document')");
                    table.ForeignKey(
                        name: "FK_SearchEntries_Assets_TenantId_OrganizationId_AssetId",
                        columns: x => new { x.TenantId, x.OrganizationId, x.AssetId },
                        principalTable: "Assets",
                        principalColumns: new[] { "TenantId", "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SearchEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchEntries_SearchVector",
                table: "SearchEntries",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            migrationBuilder.CreateIndex(
                name: "IX_SearchEntries_TenantId_OrganizationId",
                table: "SearchEntries",
                columns: new[] { "TenantId", "OrganizationId" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchEntries_TenantId_OrganizationId_AssetId",
                table: "SearchEntries",
                columns: new[] { "TenantId", "OrganizationId", "AssetId" });

            // Core owns this read contract. The invoker's base-table RLS remains in force.
            migrationBuilder.Sql("""
                CREATE VIEW "CoreSearchSources" WITH (security_invoker=true) AS
                SELECT "TenantId",'asset'::text AS "RecordType","Id","OrganizationId","Id" AS "AssetId",
                  "Name"::text AS "Title","Kind"::text AS "Body",1 AS "Version","CreatedAt" AS "UpdatedAt" FROM "Assets"
                UNION ALL
                SELECT "TenantId",'document'::text,"Id","OrganizationId","AssetId","Title"::text,"Content"::text,"Version","UpdatedAt" FROM "Documents";
                GRANT USAGE ON SCHEMA public TO saveit_search_worker;
                GRANT SELECT ON "Assets","Documents","OutboxEvents","CoreSearchSources" TO saveit_search_worker;
                GRANT SELECT ON "CoreSearchSources","SearchEntries" TO saveit_app;
                GRANT SELECT,INSERT,UPDATE,DELETE ON "SearchEntries","SearchDeliveries" TO saveit_search_worker;
                """);
            foreach (var table in new[] { "Assets", "Documents", "OutboxEvents" })
                migrationBuilder.Sql($"""
                    CREATE POLICY search_worker_read ON "{table}" FOR SELECT TO saveit_search_worker
                    USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                    """);
            foreach (var table in new[] { "SearchEntries", "SearchDeliveries" })
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY search_worker_boundary ON "{table}" FOR ALL TO saveit_search_worker
                    USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
                    WITH CHECK ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                    """);
            migrationBuilder.Sql("""
                CREATE POLICY search_app_read ON "SearchEntries" FOR SELECT TO saveit_app
                USING ("TenantId"=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW \"CoreSearchSources\"");
            foreach (var table in new[] { "Assets", "Documents", "OutboxEvents" })
                migrationBuilder.Sql($"DROP POLICY search_worker_read ON \"{table}\"");
            migrationBuilder.DropTable(
                name: "SearchDeliveries");

            migrationBuilder.DropTable(
                name: "SearchEntries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OutboxEvents_TenantId_Id",
                table: "OutboxEvents");
        }
    }
}
