using Npgsql;

namespace SaveIT.Search;

public static class DatabaseRoleGuard
{
    private static readonly string[] ProtectedTables = ["Tenants", "Memberships", "Organizations", "OrganizationGrants", "Assets", "AssetRelationships",
        "Documents", "DocumentRevisions", "AuditEvents", "OutboxEvents", "SearchEntries", "SearchDeliveries", "Locations", "Contacts", "LocationRevisions", "ContactRevisions", "AssetRevisions", "AssetTypes", "AssetTypeRevisions"];
    private static readonly string[] CoreTables = ProtectedTables.Where(x => x is not ("SearchEntries" or "SearchDeliveries")).ToArray();
    public static async Task ValidateAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand("""
            SELECT current_setting('server_encoding')<>'UTF8' OR EXISTS (SELECT 1 FROM pg_roles r WHERE (r.rolsuper OR r.rolbypassrls)
                AND pg_has_role(current_user,r.oid,'MEMBER')) OR EXISTS (
                SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relname=ANY(@tables)
                AND pg_has_role(current_user,c.relowner,'MEMBER'))
            """, connection);
        command.Parameters.AddWithValue("tables", ProtectedTables);
        if (await command.ExecuteScalarAsync(cancellationToken) is not false)
            throw new InvalidOperationException("A UTF8 database and a non-owner role without superuser or BYPASSRLS privileges are required.");
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
                  OR has_any_column_privilege(r.oid,'"ContactRevisions"','SELECT')))
            """, connection);
        command.Parameters.AddWithValue("core", CoreTables);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Search requires its restricted worker role: projection/receipt writes and no core writes.");
    }
}
