using Npgsql;

namespace SaveIT.Search;

public static class DatabaseRoleGuard
{
    public static async Task ValidateAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await using var command = new NpgsqlCommand("""
            SELECT rolsuper OR rolbypassrls OR EXISTS (
                SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relname IN ('Assets','SearchEntries')
                AND pg_has_role(current_user,c.relowner,'USAGE'))
            FROM pg_roles WHERE rolname=current_user
            """, connection);
        if (await command.ExecuteScalarAsync(cancellationToken) is not false)
            throw new InvalidOperationException("The database role must be a non-owner without superuser or BYPASSRLS privileges.");
    }

    public static async Task ValidateSearchWorkerAsync(NpgsqlConnection connection, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(connection, cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT
              (SELECT bool_and(has_table_privilege(current_user,t,p))
               FROM (VALUES ('"SearchEntries"'),('"SearchDeliveries"')) AS tables(t)
               CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE')) AS privileges(p))
              AND NOT EXISTS (
                SELECT 1 FROM (VALUES ('"Assets"'),('"Documents"'),('"OutboxEvents"'),('"AuditEvents"'),
                  ('"Memberships"'),('"OrganizationGrants"'),('"Organizations"'),('"Tenants"'),('"AssetRelationships"')) AS core(t)
                WHERE has_table_privilege(current_user,t,'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                  OR has_any_column_privilege(current_user,t,'INSERT,UPDATE'))
            """, connection);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Search requires its restricted worker role: projection/receipt writes and no core writes.");
    }
}
