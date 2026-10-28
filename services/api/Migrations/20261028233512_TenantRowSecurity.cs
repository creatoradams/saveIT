using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveIT.Api.Migrations
{
    /// <inheritdoc />
    public partial class TenantRowSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Tenants", "Memberships", "Organizations", "OrganizationGrants", "Assets", "Documents", "DocumentRevisions", "AuditEvents", "OutboxEvents" })
            {
                var column = table == "Tenants" ? "Id" : "TenantId";
                migrationBuilder.Sql($"""
                    ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_boundary ON "{table}" FOR ALL TO saveit_app
                    USING ("{column}" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("{column}" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                    """);
            }
            migrationBuilder.Sql("""
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                GRANT USAGE ON SCHEMA public TO saveit_app;
                GRANT SELECT ON "Tenants", "Memberships", "Organizations", "OrganizationGrants" TO saveit_app;
                GRANT SELECT, INSERT ON "AuditEvents", "DocumentRevisions", "OutboxEvents" TO saveit_app;
                GRANT SELECT, INSERT, UPDATE ON "Assets", "Documents" TO saveit_app;
                GRANT USAGE, SELECT ON SEQUENCE "AuditEvents_Id_seq" TO saveit_app;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "Tenants", "Memberships", "Organizations", "OrganizationGrants", "Assets", "Documents", "DocumentRevisions", "AuditEvents", "OutboxEvents" })
            {
                migrationBuilder.Sql($"DROP POLICY tenant_boundary ON \"{table}\"; ALTER TABLE \"{table}\" DISABLE ROW LEVEL SECURITY;");
            }
        }
    }
}
