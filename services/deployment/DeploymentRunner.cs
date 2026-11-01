using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;

namespace SaveIT.Deployment;

public sealed record ProvisionManifest(Guid TenantId, string TenantName, Guid MembershipId, string Subject,
    string DisplayName, string Issuer, string ExternalSubject, Guid OrganizationId, string OrganizationName, string Operator);

// An offline deployment boundary: never registered in the API and never accepts runtime credentials.
public static class DeploymentRunner
{
    public const string OwnerRole = "saveit_migration_owner";
    private const long DeploymentLock = 0x534156454954;
    public static async Task<string> ReadManifestAsync(string path, CancellationToken ct = default)
    {
        using var reader = new StreamReader(path);
        var buffer = new char[8193];
        var length = await reader.ReadBlockAsync(buffer.AsMemory(), ct);
        if (length > 8192) throw new InvalidOperationException("Manifest is too large.");
        return new string(buffer, 0, length);
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connection, bool allowLoopback, CancellationToken ct)
    {
        ConnectionPolicy.Validate(connection, allowLoopback);
        // Closing this dedicated physical session releases its advisory lock even after a failed migration.
        var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionPolicy.RuntimeConnection(connection)) { Pooling = false }.ConnectionString);
        try
        {
            await c.OpenAsync(ct);
            await using var role = new NpgsqlCommand("SET ROLE saveit_migration_owner", c);
            await role.ExecuteNonQueryAsync(ct);
            await using var path = new NpgsqlCommand("SET search_path=public", c);
            await path.ExecuteNonQueryAsync(ct);
            await using var guard = new NpgsqlCommand("""
                SELECT current_user='saveit_migration_owner'
                  AND NOT EXISTS (SELECT 1 FROM pg_roles r WHERE pg_has_role(current_user,r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb OR r.rolreplication))
                  AND (SELECT datdba=(SELECT oid FROM pg_roles WHERE rolname=current_user) FROM pg_database WHERE datname=current_database())
                  AND NOT pg_has_role('saveit_app',current_user,'MEMBER')
                  AND NOT pg_has_role('saveit_search_worker',current_user,'MEMBER')
                  AND NOT EXISTS (SELECT 1 FROM pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace
                    WHERE n.nspname='public' AND t.relkind IN ('r','p') AND t.relowner<>(SELECT oid FROM pg_roles WHERE rolname=current_user))
                """, c);
            if (await guard.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Invalid deployment owner boundary.");
            await using var locked = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", c);
            locked.Parameters.AddWithValue("key", DeploymentLock);
            await locked.ExecuteNonQueryAsync(ct);
            return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }

    private static SaveItDb Db(NpgsqlConnection c, TenantScope? scope = null) =>
        new(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(c).Options, scope ?? new());

    public static async Task MigrateAsync(string connection, bool allowLoopback = false, CancellationToken ct = default)
    {
        await using var c = await OpenAsync(connection, allowLoopback, ct);
        await using var db = Db(c);
        var known = db.Database.GetMigrations().ToArray();
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (!applied.SequenceEqual(known.Take(applied.Length))) throw new InvalidOperationException("Schema history is ahead of or differs from this release.");
        await OwnerPolicies(c, ct);
        // Install owner-only access after EACH migration: later all-tenant backfills must not silently see zero rows under forced RLS.
        // Historical backfills populate new tables before enabling RLS; existing sources already have owner access.
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        foreach (var migration in known.Skip(applied.Length))
        {
            await migrator.MigrateAsync(migration, ct);
            await OwnerPolicies(c, ct);
        }
        await ValidateSchema(c, ct);
        await using var readinessGrant = new NpgsqlCommand("GRANT SELECT (\"MigrationId\") ON public.\"__EFMigrationsHistory\" TO saveit_app,saveit_search_worker", c);
        await readinessGrant.ExecuteNonQueryAsync(ct);
    }

    private static async Task OwnerPolicies(NpgsqlConnection c, CancellationToken ct)
    {
        await using var tx = await c.BeginTransactionAsync(ct);
        await using var cmd = new NpgsqlCommand("""
            DO $body$ DECLARE t record; BEGIN
              FOR t IN SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                WHERE n.nspname='public' AND c.relkind IN ('r','p') AND c.relrowsecurity
                  AND c.relowner=(SELECT oid FROM pg_roles WHERE rolname=current_user)
              LOOP
                EXECUTE format('DROP POLICY IF EXISTS saveit_deployment_owner ON public.%I',t.relname);
                EXECUTE format('CREATE POLICY saveit_deployment_owner ON public.%I TO saveit_migration_owner USING (true) WITH CHECK (true)',t.relname);
              END LOOP;
            END $body$;
            """, c, tx);
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }

    private static async Task ValidateSchema(NpgsqlConnection c, CancellationToken ct)
    {
        await DatabaseRoleGuard.ValidateSchemaAsync(c, ct, requireMigration: true);
        await using var cmd = new NpgsqlCommand("""
            SELECT NOT EXISTS (SELECT 1 FROM pg_roles r WHERE r.rolname IN ('saveit_app','saveit_search_worker')
              AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication))
              AND NOT pg_has_role('saveit_app','saveit_migration_owner','MEMBER')
              AND NOT pg_has_role('saveit_search_worker','saveit_migration_owner','MEMBER')
            """, c);
        if (await cmd.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Unsafe runtime role.");
    }

    public static async Task ProvisionAsync(string connection, string json, bool allowLoopback = false, CancellationToken ct = default)
    {
        if (json.Length > 8192) throw new InvalidOperationException("Manifest is too large.");
        var p = JsonSerializer.Deserialize<ProvisionManifest>(json, new JsonSerializerOptions {
            PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidOperationException("Manifest required.");
        static bool Text(string? s, int max) => !string.IsNullOrWhiteSpace(s) && s.Length <= max && !s.Any(char.IsControl) && s == s.Trim();
        if (p.TenantId == Guid.Empty || p.MembershipId == Guid.Empty || p.OrganizationId == Guid.Empty
            || !Text(p.TenantName, 160) || !Text(p.OrganizationName, 160) || !Text(p.Subject, 256) || !Text(p.DisplayName, 160)
            || !Text(p.ExternalSubject, 256) || !Text(p.Operator, 160) || !Text(p.Issuer, 500)
            || !Uri.TryCreate(p.Issuer, UriKind.Absolute, out var issuer) || issuer.Scheme != "https"
            || issuer.UserInfo.Length != 0 || issuer.Query.Length != 0 || issuer.Fragment.Length != 0 || p.Issuer.EndsWith('/'))
            throw new InvalidOperationException("Invalid provisioning manifest.");
        await using var c = await OpenAsync(connection, allowLoopback, ct);
        await using var db = Db(c, new() { TenantId = p.TenantId, MembershipId = p.MembershipId, Subject = p.Subject });
        if (!db.Database.GetMigrations().SequenceEqual(await db.Database.GetAppliedMigrationsAsync(ct))) throw new InvalidOperationException("Provisioning requires this release's exact schema history.");
        await ValidateSchema(c, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{p.TenantId.ToString()},true),set_config('app.membership_id',{p.MembershipId.ToString()},true)", ct);
        // INSERT-only bootstrap, never an upsert that changes existing authority on retry.
        db.Tenants.Add(new() { Id = p.TenantId, Name = p.TenantName });
        db.Memberships.Add(new() { Id = p.MembershipId, TenantId = p.TenantId, Subject = p.Subject, DisplayName = p.DisplayName,
            ExternalIssuer = p.Issuer, ExternalSubject = p.ExternalSubject, Active = true, CanManageOrganizations = true });
        await db.SaveChangesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.create_organization({p.OrganizationId},{p.OrganizationName},{p.MembershipId})", ct);
        var now = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new() { TenantId = p.TenantId, OrganizationId = p.OrganizationId, TargetId = p.TenantId,
            Action = "Tenant.Provisioned", Actor = p.Operator, CreatedAt = now, CorrelationId = Guid.NewGuid().ToString() });
        db.OutboxEvents.Add(new() { Id = Guid.NewGuid(), TenantId = p.TenantId, OrganizationId = p.OrganizationId,
            TargetId = p.OrganizationId, Type = "Organization.Created", SchemaVersion = 1, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
