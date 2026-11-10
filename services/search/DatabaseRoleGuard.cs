using Npgsql;

namespace SaveIT.Search;

public static class DatabaseRoleGuard
{
    public const string RequiredMigration = "20261109140000_ComputeReleaseAdmission";
    private static readonly string[] ProtectedTables = ["Tenants", "Memberships", "Organizations", "OrganizationRevisions", "OrganizationGrants", "Assets", "AssetRelationships",
        "Documents", "DocumentRevisions", "DocumentFolders", "DocumentPlacements", "DocumentTemplates", "DocumentTemplateRevisions", "DocumentTemplateOrigins", "AuditEvents", "OutboxEvents", "SearchEntries", "SearchDeliveries", "Locations", "Contacts", "LocationRevisions", "ContactRevisions", "AssetRevisions", "AssetTypes", "AssetTypeRevisions", "AccessResponses", "AccessAudits", "AccessControlEvents"];
    private static readonly string[] CoreTables = ProtectedTables.Where(x => x is not ("SearchEntries" or "SearchDeliveries")).ToArray();
    public static async Task ValidateAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand("""
            SELECT current_user<>session_user OR current_setting('server_encoding')<>'UTF8' OR EXISTS (SELECT 1 FROM pg_roles r WHERE (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR has_schema_privilege(r.oid,'public','CREATE'))
                AND pg_has_role(current_user,r.oid,'MEMBER')) OR EXISTS (
                SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relname=ANY(@tables)
            AND pg_has_role(current_user,c.relowner,'MEMBER'))
            OR EXISTS (SELECT 1 FROM pg_roles r WHERE r.rolname IN ('saveit_access_operator','saveit_compute_operator') AND pg_has_role(current_user,r.oid,'MEMBER'))
            OR EXISTS (SELECT 1 FROM pg_roles r JOIN pg_proc p ON p.proname IN ('access_change_v1','access_inspect_v1','compute_change_v1','compute_inspect_v1','compute_admit_v1','compute_release_status_v1','compute_release_v1','register_response_core_v1')
                JOIN pg_namespace n ON n.oid=p.pronamespace AND n.nspname='public'
                WHERE pg_has_role(current_user,r.oid,'MEMBER') AND has_function_privilege(r.oid,p.oid,'EXECUTE'))
            OR EXISTS (SELECT 1 FROM pg_roles r CROSS JOIN unnest(@protocol) a(t)
                WHERE pg_has_role(current_user,r.oid,'MEMBER')
                  AND (has_table_privilege(r.oid,format('%I',a.t),'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                    OR has_any_column_privilege(r.oid,format('%I',a.t),'SELECT,INSERT,UPDATE')))
            """, connection);
        command.Parameters.AddWithValue("tables", ProtectedTables.Concat(["ComputeControl", "ComputeOperations"]).ToArray());
        command.Parameters.AddWithValue("protocol", new[] { "AccessResponses", "AccessAudits", "AccessControlEvents", "ComputeControl", "ComputeOperations" });
        if (await command.ExecuteScalarAsync(cancellationToken) is not false)
            throw new InvalidOperationException("A UTF8 database and a non-owner role without superuser or BYPASSRLS privileges are required.");
    }

    public static async Task ValidateSchemaAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default, bool requireMigration = false)
    {
        await using var schema = new NpgsqlCommand("""
            SELECT count(*)=@count AND bool_and(c.relrowsecurity AND c.relforcerowsecurity)
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relname=ANY(@tables) AND c.relkind IN ('r','p')
            """, connection);
        schema.Parameters.AddWithValue("tables", ProtectedTables);
        schema.Parameters.AddWithValue("count", ProtectedTables.Length);
        if (await schema.ExecuteScalarAsync(cancellationToken) is not true) throw new InvalidOperationException("The required forced-RLS schema is unavailable.");
        if (requireMigration)
        {
            await using var compute = new NpgsqlCommand("SELECT count(*)=2 AND bool_and(c.relrowsecurity AND c.relforcerowsecurity) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r' AND c.relname IN ('ComputeControl','ComputeOperations')", connection);
            if (await compute.ExecuteScalarAsync(cancellationToken) is not true) throw new InvalidOperationException("The required compute coordination schema is unavailable.");
            await using var version = new NpgsqlCommand("SELECT max(\"MigrationId\")=@version FROM public.\"__EFMigrationsHistory\"", connection);
            version.Parameters.AddWithValue("version", RequiredMigration);
            if (await version.ExecuteScalarAsync(cancellationToken) is not true) throw new InvalidOperationException("Database schema does not match this release.");
        }
    }

    public static async Task ValidateSearchWorkerAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(connection, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
              (SELECT bool_and(has_table_privilege(current_user,t,p))
               FROM (VALUES ('"SearchEntries"')) AS tables(t)
               CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE')) AS privileges(p))
              AND has_table_privilege(current_user,'"SearchDeliveries"','SELECT')
              AND has_table_privilege(current_user,'"SearchDeliveries"','INSERT')
              AND has_table_privilege(current_user,'"SearchDeliveries"','UPDATE')
              AND NOT EXISTS (
                SELECT 1 FROM unnest(@core) AS core(t) CROSS JOIN pg_roles r
                WHERE pg_has_role(current_user,r.oid,'MEMBER')
                  AND (has_table_privilege(r.oid,format('%I',t),'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                  OR has_any_column_privilege(r.oid,format('%I',t),'INSERT,UPDATE')))
              AND NOT EXISTS (
                SELECT 1 FROM pg_roles r JOIN pg_proc p
                  ON p.oid IN (to_regprocedure('public.create_organization(uuid,text,uuid)'),to_regprocedure('public.rename_organization(uuid,text,uuid,integer)'))
                WHERE pg_has_role(current_user,r.oid,'MEMBER') AND has_function_privilege(r.oid,p.oid,'EXECUTE'))
              AND NOT EXISTS (
                SELECT 1 FROM pg_roles r WHERE pg_has_role(current_user,r.oid,'MEMBER')
                AND (has_column_privilege(r.oid,'"Assets"','FieldsJson','SELECT')
                  OR has_any_column_privilege(r.oid,'"AssetRevisions"','SELECT')
                  OR has_any_column_privilege(r.oid,'"AssetTypes"','SELECT')
                  OR has_any_column_privilege(r.oid,'"AssetTypeRevisions"','SELECT')
                  OR has_any_column_privilege(r.oid,'"Locations"','SELECT')
                  OR has_any_column_privilege(r.oid,'"Contacts"','SELECT')
                  OR has_any_column_privilege(r.oid,'"LocationRevisions"','SELECT')
                  OR has_any_column_privilege(r.oid,'"ContactRevisions"','SELECT')
                  OR has_any_column_privilege(r.oid,'"OrganizationRevisions"','SELECT')
                  OR has_any_column_privilege(r.oid,'"DocumentFolders"','SELECT')
                  OR has_any_column_privilege(r.oid,'"DocumentPlacements"','SELECT')
                  OR has_any_column_privilege(r.oid,'"DocumentTemplates"','SELECT')
                  OR has_any_column_privilege(r.oid,'"DocumentTemplateRevisions"','SELECT')
                  OR has_any_column_privilege(r.oid,'"DocumentTemplateOrigins"','SELECT')))
              AND NOT EXISTS (SELECT 1 FROM pg_roles r CROSS JOIN (VALUES ('AccessResponses'),('AccessAudits'),('AccessControlEvents')) AS a(t)
                WHERE pg_has_role(current_user,r.oid,'MEMBER') AND has_any_column_privilege(r.oid,format('%I',a.t),'SELECT'))
              AND NOT EXISTS (SELECT 1 FROM pg_roles r JOIN pg_proc p ON p.proname IN ('register_response_v1','complete_response_v1')
                JOIN pg_namespace n ON n.oid=p.pronamespace AND n.nspname='public'
                WHERE pg_has_role(current_user,r.oid,'MEMBER') AND has_function_privilege(r.oid,p.oid,'EXECUTE'))
            """, connection);
        command.Parameters.AddWithValue("core", CoreTables);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Search requires its restricted worker role: projection/receipt writes and no core writes.");
    }
}
