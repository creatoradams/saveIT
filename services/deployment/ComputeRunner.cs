using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using SaveIT.Search;

namespace SaveIT.Deployment;

public sealed record ComputeChange(long ExpectedVersion, Guid OperationId, string Action, Guid? Generation, string? PlanSha256, string Operator);

// Records protected controller decisions; this is not an AWS observation or fence verifier.
public static class ComputeRunner
{
    private static async Task<NpgsqlConnection> OpenAsync(string connection, bool allowLoopback, CancellationToken ct)
    {
        ConnectionPolicy.Validate(connection,allowLoopback);
        var c = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(ConnectionPolicy.RuntimeConnection(connection)) { Pooling=false,Timeout=3,CommandTimeout=5,CancellationTimeout=2000 }.ConnectionString);
        try
        {
            await c.OpenAsync(ct);
            await using (var role = new NpgsqlCommand("SET ROLE saveit_compute_operator",c)) await role.ExecuteNonQueryAsync(ct);
            await using var guard = new NpgsqlCommand("""
                SELECT current_user='saveit_compute_operator' AND session_user<>current_user
                  AND NOT (SELECT rolcanlogin FROM pg_roles WHERE rolname=current_user)
                  AND NOT EXISTS (SELECT 1 FROM pg_roles r WHERE pg_has_role(session_user,r.oid,'MEMBER')
                    AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR has_schema_privilege(r.oid,'public','CREATE') OR r.rolname='saveit_access_operator'))
                  AND NOT EXISTS (SELECT 1 FROM pg_roles r CROSS JOIN pg_class t JOIN pg_namespace n ON n.oid=t.relnamespace
                    WHERE n.nspname='public' AND t.relrowsecurity AND pg_has_role(session_user,r.oid,'MEMBER')
                    AND (pg_has_role(r.oid,t.relowner,'MEMBER') OR has_table_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                      OR has_any_column_privilege(r.oid,t.oid,'SELECT,INSERT,UPDATE')))
                  AND NOT EXISTS (SELECT 1 FROM pg_roles r JOIN pg_proc p ON p.proname IN ('access_change_v1','access_inspect_v1','register_response_core_v1','register_response_v1','complete_response_v1')
                    JOIN pg_namespace n ON n.oid=p.pronamespace AND n.nspname='public'
                    WHERE pg_has_role(session_user,r.oid,'MEMBER') AND has_function_privilege(r.oid,p.oid,'EXECUTE'))
                """,c);
            if (await guard.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Unsafe compute operator credential boundary.");
            await DatabaseRoleGuard.ValidateSchemaAsync(c,ct,requireMigration:true);
            return c;
        }
        catch { await c.DisposeAsync(); throw; }
    }

    public static async Task<long> ChangeAsync(string connection, string json, bool allowLoopback=false, CancellationToken ct=default)
    {
        if (json.Length>8192) throw new InvalidOperationException("Compute manifest is too large.");
        using var document = JsonDocument.Parse(json,new JsonDocumentOptions { MaxDepth=4 });
        var names = new[] { "expectedVersion","operationId","action","generation","planSha256","operator" };
        if (document.RootElement.ValueKind!=JsonValueKind.Object || document.RootElement.EnumerateObject().Count()!=names.Length || names.Any(n=>!document.RootElement.TryGetProperty(n,out _))) throw new InvalidOperationException("Invalid compute manifest.");
        var p = JsonSerializer.Deserialize<ComputeChange>(json,new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase,UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidOperationException();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await using var c = await OpenAsync(connection,allowLoopback,deadline.Token);
        await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,deadline.Token);
        await using var command = new NpgsqlCommand("SELECT public.compute_change_v1(@v,@i,@a,@g,@p,@o)",c,tx);
        command.Parameters.AddWithValue("v",p.ExpectedVersion); command.Parameters.AddWithValue("i",p.OperationId);
        command.Parameters.AddWithValue("a",NpgsqlDbType.Text,(object?)p.Action ?? DBNull.Value);
        command.Parameters.AddWithValue("g",NpgsqlDbType.Uuid,(object?)p.Generation ?? DBNull.Value);
        command.Parameters.AddWithValue("p",NpgsqlDbType.Text,(object?)p.PlanSha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("o",NpgsqlDbType.Text,(object?)p.Operator ?? DBNull.Value);
        var version = (long)(await command.ExecuteScalarAsync(deadline.Token))!;
        await tx.CommitAsync(deadline.Token); // Uncertain acknowledgement is never automatically retried.
        return version;
    }

    public static async Task<string> InspectAsync(string connection, bool allowLoopback=false, CancellationToken ct=default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        await using var c = await OpenAsync(connection,allowLoopback,deadline.Token);
        await using var command = new NpgsqlCommand("SELECT public.compute_inspect_v1()::text",c);
        return (string)(await command.ExecuteScalarAsync(deadline.Token))!;
    }

    internal static async Task<long> AdmitReleaseAsync(string connection,ComputeRelease release,string attribution,bool allowLoopback=false,CancellationToken ct=default)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await using var c=await OpenAsync(connection,allowLoopback,deadline.Token);
        await using var tx=await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted,deadline.Token);
        await using var command=new NpgsqlCommand("SELECT public.compute_admit_v1(@v,@i,@g,@p,@k,@j,@o)",c,tx);
        command.Parameters.AddWithValue("v",release.Data.ExpectedVersion);command.Parameters.AddWithValue("i",release.OperationId);
        command.Parameters.AddWithValue("g",ComputeRelease.CanonicalGuid(release.Data.Generation));command.Parameters.AddWithValue("p",release.PlanSha256);
        command.Parameters.AddWithValue("k",release.KeySha256);command.Parameters.AddWithValue("j",release.SignedToken);command.Parameters.AddWithValue("o",attribution);
        var version=(long)(await command.ExecuteScalarAsync(deadline.Token))!;
        await tx.CommitAsync(deadline.Token); // No provider side effect or retry before acknowledged durable admission.
        return version;
    }

    public static async Task<string> InspectReleaseAsync(string connection,Guid operation,bool allowLoopback=false,CancellationToken ct=default)
    {
        if(operation==Guid.Empty)throw new InvalidOperationException("An exact release operation is required.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        await using var c=await OpenAsync(connection,allowLoopback,deadline.Token);
        await using var command=new NpgsqlCommand("SELECT public.compute_release_status_v1(@i)::text",c);command.Parameters.AddWithValue("i",operation);
        return (string)(await command.ExecuteScalarAsync(deadline.Token))!;
    }
}
