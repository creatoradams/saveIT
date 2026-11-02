using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using SaveIT.Search;

namespace SaveIT.Deployment;

public sealed record AccessManifest(Guid TenantId, Guid MembershipId, int ExpectedVersion, string Operation, string Operator,
    Guid? OrganizationId = null, string? Role = null, bool? Enabled = null, string? DisplayName = null, string? Issuer = null, string? ExternalSubject = null);
public sealed record AccessInspection(Guid TenantId, Guid MembershipId, string Operator, Guid? AfterOrganizationId = null);

// Deliberately separate credentials/role from migration ownership and all web/worker services.
public static class AccessRunner
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static T Read<T>(string json)
    {
        if (json.Length > 8192) throw new InvalidOperationException("Access manifest is too large.");
        return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException("Access manifest is required.");
    }
    private static bool Text(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && text == text.Trim() && !text.Any(char.IsControl);
    private static void Source(Guid tenant, Guid member, string label)
    {
        if (tenant == Guid.Empty || member == Guid.Empty || !Text(label, 160)) throw new InvalidOperationException("Invalid access source/attribution.");
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connection, bool allowLoopback, CancellationToken ct)
    {
        ConnectionPolicy.Validate(connection, allowLoopback);
        var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionPolicy.RuntimeConnection(connection)) {
            Pooling = false, Timeout = 5, CommandTimeout = 8, CancellationTimeout = 2000
        }.ConnectionString);
        try
        {
            await c.OpenAsync(ct);
            await using (var role = new NpgsqlCommand("SET ROLE saveit_access_operator", c)) await role.ExecuteNonQueryAsync(ct);
            await using var guard = new NpgsqlCommand("""
                SELECT current_user='saveit_access_operator' AND session_user<>current_user
                  AND NOT (SELECT rolcanlogin FROM pg_roles WHERE rolname=current_user)
                  AND NOT EXISTS (SELECT 1 FROM pg_roles r WHERE pg_has_role(session_user,r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR has_schema_privilege(r.oid,'public','CREATE')))
                  AND NOT EXISTS (SELECT 1 FROM pg_roles r CROSS JOIN pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace
                    WHERE n.nspname='public' AND t.relrowsecurity AND pg_has_role(session_user,r.oid,'MEMBER')
                      AND (pg_has_role(r.oid,t.relowner,'MEMBER') OR has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                        OR has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE')))
                """, c);
            if (await guard.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Unsafe access operator credential boundary.");
            await DatabaseRoleGuard.ValidateSchemaAsync(c, ct, requireMigration: true);
            return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }

    public static async Task<int> ChangeAsync(string connection, string json, bool allowLoopback = false, CancellationToken ct = default)
    {
        var p = Read<AccessManifest>(json); Source(p.TenantId, p.MembershipId, p.Operator);
        if (p.Operation == "create")
        {
            if (p.ExpectedVersion != 0 || p.OrganizationId is null || p.OrganizationId == Guid.Empty || p.Role is not ("Viewer" or "Editor") || p.Enabled is not null
                || !Text(p.DisplayName, 160) || !Text(p.ExternalSubject, 256) || !Text(p.Issuer, 500)
                || !Uri.TryCreate(p.Issuer, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || p.Issuer!.EndsWith('/'))
                throw new InvalidOperationException("Invalid mapped membership manifest.");
        }
        else if (p.ExpectedVersion < 1 || p.DisplayName is not null || p.Issuer is not null || p.ExternalSubject is not null
            || p.Operation is not ("active" or "grant" or "capability")
            || (p.Operation == "grant" && (p.OrganizationId is null || p.OrganizationId == Guid.Empty || p.Enabled is not null || p.Role is not (null or "Viewer" or "Editor")))
            || (p.Operation is "active" or "capability" && (p.Enabled is null || p.OrganizationId is not null || p.Role is not null)))
            throw new InvalidOperationException("Invalid access change manifest.");
        await using var c = await OpenAsync(connection, allowLoopback, ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var cmd = new NpgsqlCommand("SELECT public.access_change_v1(@t,@m,@expected,@operation,@org,@role,@enabled,@display,@issuer,@external,@operator)", c, tx);
        cmd.Parameters.AddWithValue("t", p.TenantId); cmd.Parameters.AddWithValue("m", p.MembershipId); cmd.Parameters.AddWithValue("expected", p.ExpectedVersion);
        cmd.Parameters.AddWithValue("operation", p.Operation); cmd.Parameters.AddWithValue("operator", p.Operator);
        cmd.Parameters.AddWithValue("org", NpgsqlDbType.Uuid, (object?)p.OrganizationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role", NpgsqlDbType.Text, (object?)p.Role ?? DBNull.Value);
        cmd.Parameters.AddWithValue("enabled", NpgsqlDbType.Boolean, (object?)p.Enabled ?? DBNull.Value);
        cmd.Parameters.AddWithValue("display", NpgsqlDbType.Text, (object?)p.DisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("issuer", NpgsqlDbType.Text, (object?)p.Issuer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("external", NpgsqlDbType.Text, (object?)p.ExternalSubject ?? DBNull.Value);
        var version = (int)(await cmd.ExecuteScalarAsync(ct))!;
        // Never retry an uncertain commit automatically or print success before acknowledgement.
        await tx.CommitAsync(ct); return version;
    }

    public static async Task<string> InspectAsync(string connection, string json, bool allowLoopback = false, CancellationToken ct = default)
    {
        var p = Read<AccessInspection>(json); Source(p.TenantId, p.MembershipId, p.Operator);
        await using var c = await OpenAsync(connection, allowLoopback, ct);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        await using var cmd = new NpgsqlCommand("SELECT public.access_inspect_v1(@t,@m,@after,@operator)::text", c, tx);
        cmd.Parameters.AddWithValue("t", p.TenantId); cmd.Parameters.AddWithValue("m", p.MembershipId); cmd.Parameters.AddWithValue("operator", p.Operator);
        cmd.Parameters.AddWithValue("after", NpgsqlDbType.Uuid, (object?)p.AfterOrganizationId ?? DBNull.Value);
        var result = (string)(await cmd.ExecuteScalarAsync(ct))!;
        await tx.CommitAsync(ct); return result;
    }
}
