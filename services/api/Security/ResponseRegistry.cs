using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Npgsql;

namespace SaveIT.Api.Security;

public sealed class AccessProtocolUnavailableException : Exception;

// A durable pending row survives connection/process loss. Only the server's OnCompleted
// callback may close it; middleware finally/RequestAborted alone are not completion proof.
public sealed class ResponseRegistry(string connection, ILogger<ResponseRegistry> logger)
{
    public const string EpochClaim = "saveit.session_epoch";
    private static readonly object StateKey = new();
    private readonly Guid instance = Guid.NewGuid();
    private int announced;
    // Reserve cleanup capacity even while new registrations wait behind an operator barrier.
    private readonly string registration = Pool(connection, "SaveIT.ResponseRegistration", 16);
    private readonly string completion = Pool(connection, "SaveIT.ResponseCompletion", 4);
    private sealed class State { public Lease? Lease; public bool Registered; public int Epoch; public string? Subject; }
    private sealed record Lease(Guid Tenant, Guid Member, Guid Request, Guid Instance);

    private static string Pool(string connection, string name, int capacity) => new NpgsqlConnectionStringBuilder(connection) {
        ApplicationName = name, MaxPoolSize = capacity, MinPoolSize = 0, Timeout = 3, CommandTimeout = 5, CancellationTimeout = 2000
    }.ConnectionString;

    public void Initialize(HttpContext context)
    {
        if (Interlocked.Exchange(ref announced, 1) == 0) logger.LogInformation("Access response instance {InstanceId} started.", instance);
        var state = new State(); context.Items.Add(StateKey, state);
        var aborted = context.RequestAborted;
        context.Response.OnCompleted(async () => {
            if (state.Lease is { } lease) await CompleteAsync(lease, aborted.IsCancellationRequested);
        });
    }

    public static bool TryAuthority(ClaimsPrincipal user, out Guid tenant, out Guid member, out int epoch)
    {
        tenant = default; member = default; epoch = default;
        return user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue("tenant"), out tenant) && tenant != Guid.Empty
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out member) && member != Guid.Empty
            && int.TryParse(user.FindFirstValue(EpochClaim), NumberStyles.None, CultureInfo.InvariantCulture, out epoch) && epoch > 0
            && user.FindFirstValue("subject") is { Length: > 0 and <= 256 };
    }

    public async Task<bool> RegisterAsync(HttpContext context, ClaimsPrincipal user)
    {
        if (!TryAuthority(user, out var tenant, out var member, out var epoch)) return false;
        if (context.Items[StateKey] is not State state) throw new InvalidOperationException("Response envelope is required.");
        if (state.Lease is { } existing)
            return state.Registered && existing.Tenant == tenant && existing.Member == member && state.Epoch == epoch && state.Subject == user.FindFirstValue("subject");
        // Assign before commit: a lost commit acknowledgement is uncertain, so completion
        // still attempts to close this exact record after the failed HTTP response finishes.
        var lease = new Lease(tenant, member, Guid.NewGuid(), instance); state.Lease = lease; state.Epoch = epoch; state.Subject = user.FindFirstValue("subject");
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            deadline.CancelAfter(TimeSpan.FromSeconds(6));
            await using var c = new NpgsqlConnection(registration); await c.OpenAsync(deadline.Token);
            await using var tx = await c.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, deadline.Token);
            await ContextAsync(c, tx, lease, deadline.Token);
            await using var cmd = new NpgsqlCommand("SELECT public.register_response_v1(@t,@m,@s,@e,@r,@i)", c, tx);
            Parameters(cmd, lease); cmd.Parameters.AddWithValue("s", user.FindFirstValue("subject")!); cmd.Parameters.AddWithValue("e", epoch);
            if (await cmd.ExecuteScalarAsync(deadline.Token) is not true) return false;
            await tx.CommitAsync(deadline.Token);
            state.Registered = true;
            return true;
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or TimeoutException)
        { throw new AccessProtocolUnavailableException(); }
    }

    public async Task AuthorizeAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();
        if (context.User.Identity?.IsAuthenticated == true && endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null
            && endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0 && !await RegisterAsync(context, context.User))
        { context.Response.StatusCode = 401; return; }
        await next(context);
    }

    private async Task CompleteAsync(Lease lease, bool aborted)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await using var c = new NpgsqlConnection(completion); await c.OpenAsync(deadline.Token);
                await using var tx = await c.BeginTransactionAsync(deadline.Token);
                await ContextAsync(c, tx, lease, deadline.Token);
                await using var cmd = new NpgsqlCommand("SELECT public.complete_response_v1(@t,@m,@r,@i,@aborted)", c, tx);
                Parameters(cmd, lease); cmd.Parameters.AddWithValue("aborted", aborted);
                await cmd.ExecuteNonQueryAsync(deadline.Token); await tx.CommitAsync(deadline.Token);
                return;
            }
            catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException or TimeoutException)
            { if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(100 * (attempt + 1))); }
        }
        // No exception details, tenant, route, token or content. A pending record remains a
        // fail-closed operator gate until explicit privileged instance fencing/recovery.
        logger.LogError("Access response completion could not be persisted; operator changes may be blocked. Inspect the protected access protocol state.");
    }

    private static async Task ContextAsync(NpgsqlConnection c, NpgsqlTransaction tx, Lease lease, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id',@t,true),set_config('app.membership_id',@m,true)", c, tx);
        cmd.Parameters.AddWithValue("t", lease.Tenant.ToString()); cmd.Parameters.AddWithValue("m", lease.Member.ToString()); await cmd.ExecuteNonQueryAsync(ct);
    }
    private static void Parameters(NpgsqlCommand cmd, Lease lease)
    {
        cmd.Parameters.AddWithValue("t", lease.Tenant); cmd.Parameters.AddWithValue("m", lease.Member);
        cmd.Parameters.AddWithValue("r", lease.Request); cmd.Parameters.AddWithValue("i", lease.Instance);
    }
}
