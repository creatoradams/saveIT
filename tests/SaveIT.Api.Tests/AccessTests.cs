using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Api.Security;
using SaveIT.Deployment;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class AccessTests(DatabaseFixture fixture)
{
    private static readonly Guid Tenant = DevelopmentSeed.TenantA;
    private static readonly Guid Org = DevelopmentSeed.OrgA;
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private sealed class OperatorLogin(DatabaseFixture fixture, string name) : IAsyncDisposable
    {
        public string Name => name;
        public string Connection => new NpgsqlConnectionStringBuilder(fixture.OwnerConnection) {
            Username = name, Password = "synthetic-access-test-only", Pooling = false
        }.ConnectionString;
        public ValueTask DisposeAsync() => new(fixture.Execute($"DROP ROLE \"{name}\""));
    }
    private async Task<OperatorLogin> Operator()
    {
        var name = "saveit_access_test_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE ROLE \"{name}\" LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-access-test-only'; GRANT saveit_access_operator TO \"{name}\"");
        return new(fixture, name);
    }
    private static AccessManifest Create(Guid? member = null, Guid? org = null) => new(Tenant, member ?? Guid.NewGuid(), 0, "create", "synthetic-access-operator",
        org ?? Org, "Editor", DisplayName: "Private synthetic identity", Issuer: "https://synthetic.okta.example/oauth2/default", ExternalSubject: "private-external-" + Guid.NewGuid());
    private static AccessManifest Change(Guid member, int expected = 1, string operation = "active", bool? enabled = false, Guid? org = null, string? role = null) =>
        new(Tenant, member, expected, operation, "synthetic-access-operator", org, role, enabled);
    private static ClaimsPrincipal Principal(Guid member, int epoch = 1, string? subject = null) => new(new ClaimsIdentity([
        new(ClaimTypes.NameIdentifier, member.ToString()), new("tenant", Tenant.ToString()), new("subject", subject ?? member.ToString()),
        new(ResponseRegistry.EpochClaim, epoch.ToString(System.Globalization.CultureInfo.InvariantCulture))
    ], "synthetic-test"));
    private async Task Until(Func<Task<bool>> predicate, int seconds = 10)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!await predicate()) await Task.Delay(25, timeout.Token);
    }
    private Task<long> Pending(Guid member) => fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{member}' AND \"CompletedAt\" IS NULL");
    private Task<bool> OperatorWaiting() => fixture.Scalar<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.access_change_v1%' AND wait_event='PgSleep')");

    [Fact]
    public async Task Restricted_operator_onboards_and_changes_exact_scopes_with_atomic_body_free_audit_and_control_events()
    {
        await using var op = await Operator(); var p = Create();
        Assert.Equal(1, await AccessRunner.ChangeAsync(op.Connection, Json(p), true));
        Assert.Equal(p.MembershipId.ToString(), await fixture.Scalar<string>($"SELECT \"Subject\" FROM \"Memberships\" WHERE \"Id\"='{p.MembershipId}'"));
        Assert.Equal(2, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, operation: "grant", enabled: null, org: Org, role: "Viewer")), true));
        Assert.Equal(3, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, 2, "capability", true)), true));
        Assert.Equal(4, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, 3)), true));
        Assert.Equal(5, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, 4, enabled: true)), true));
        Assert.Equal(3, await fixture.Scalar<int>($"SELECT \"SessionEpoch\" FROM \"Memberships\" WHERE \"Id\"='{p.MembershipId}'"));
        Assert.Equal(6, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, 5, "grant", null, Org)), true));
        using var inspected = JsonDocument.Parse(await AccessRunner.InspectAsync(op.Connection, Json(new AccessInspection(Tenant, p.MembershipId, "synthetic-inspector")), true));
        Assert.Equal(6, inspected.RootElement.GetProperty("accessVersion").GetInt32()); Assert.Empty(inspected.RootElement.GetProperty("grants").EnumerateArray());
        Assert.Equal(7L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessAudits\" WHERE \"MembershipId\"='{p.MembershipId}' AND \"DatabaseLogin\"='{op.Name}'"));
        Assert.Equal(6L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessControlEvents\" WHERE \"MembershipId\"='{p.MembershipId}'"));
        var audit = await fixture.Scalar<string>($"SELECT jsonb_agg(to_jsonb(a))::text FROM \"AccessAudits\" a WHERE \"MembershipId\"='{p.MembershipId}'");
        Assert.DoesNotContain(p.ExternalSubject!, audit); Assert.DoesNotContain(p.DisplayName!, audit); Assert.DoesNotContain(p.Issuer!, audit);
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{p.MembershipId}'"));
    }

    [Fact]
    public async Task Cross_tenant_organization_missing_member_duplicate_mapping_and_stale_versions_fail_without_partial_authority()
    {
        await using var op = await Operator(); var p = Create();
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(p with { OrganizationId = DevelopmentSeed.OrgB }), true))).SqlState);
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"Memberships\" WHERE \"Id\"='{p.MembershipId}'"));
        await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(p with { MembershipId = Guid.NewGuid() }), true))).SqlState);
        Assert.Equal("40001", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, 2)), true))).SqlState);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(Change(Guid.NewGuid())), true))).SqlState);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId, operation: "grant", enabled: null, org: DevelopmentSeed.OrgB, role: "Viewer")), true))).SqlState);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessAudits\" WHERE \"MembershipId\"='{p.MembershipId}'"));
    }

    [Theory]
    [InlineData("AccessAudits")]
    [InlineData("AccessControlEvents")]
    public async Task Immediate_and_deferred_event_failure_roll_back_access_epoch_grants_and_audit(string table)
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var suffix = Guid.NewGuid().ToString("N"); var fn = "access_fail_" + suffix;
        var deferred = table == "AccessControlEvents";
        await fixture.Execute($"CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"MembershipId\"='{p.MembershipId}' THEN RAISE EXCEPTION 'Synthetic failure'; END IF; RETURN NEW; END $$; CREATE {(deferred ? "CONSTRAINT " : "")}TRIGGER {fn} {(deferred ? "AFTER" : "BEFORE")} INSERT ON \"{table}\" {(deferred ? "DEFERRABLE INITIALLY DEFERRED " : "")}FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        try
        {
            await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true));
            Assert.True(await fixture.Scalar<bool>($"SELECT \"Active\" AND \"SessionEpoch\"=1 AND \"AccessVersion\"=1 FROM \"Memberships\" WHERE \"Id\"='{p.MembershipId}'"));
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessAudits\" WHERE \"MembershipId\"='{p.MembershipId}'"));
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessControlEvents\" WHERE \"MembershipId\"='{p.MembershipId}'"));
        }
        finally { await fixture.Execute($"DROP TRIGGER {fn} ON \"{table}\"; DROP FUNCTION public.{fn}()"); }
    }

    [Fact]
    public async Task Operator_role_has_only_checked_execution_runtime_and_worker_cannot_read_or_mutate_protocol_state()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        foreach (var connection in new[] { op.Connection, fixture.AppConnection, fixture.WorkerConnection })
        {
            await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
            if (connection == op.Connection) { await using var role = new NpgsqlCommand("SET ROLE saveit_access_operator", c); await role.ExecuteNonQueryAsync(); }
            foreach (var table in new[] { "AccessResponses", "AccessAudits", "AccessControlEvents" })
            foreach (var sql in new[] { $"SELECT * FROM \"{table}\"", $"DELETE FROM \"{table}\"" })
            { await using var cmd = new NpgsqlCommand(sql, c); Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState); }
            if (connection == op.Connection)
            {
                await using var core = new NpgsqlCommand("SELECT * FROM \"Memberships\"", c); Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => core.ExecuteNonQueryAsync())).SqlState);
            }
            else
            {
                await using var forbidden = new NpgsqlCommand($"SELECT public.access_inspect_v1('{Tenant}','{p.MembershipId}',NULL,'forged-runtime')", c);
                Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync())).SqlState);
            }
        }
        foreach (var unsafeConnection in new[] { fixture.OwnerConnection, fixture.AppConnection, fixture.WorkerConnection })
            await Assert.ThrowsAnyAsync<Exception>(() => AccessRunner.InspectAsync(unsafeConnection, Json(new AccessInspection(Tenant, p.MembershipId, "forged")), true));
        Assert.Equal(3L, await fixture.Scalar<long>("SELECT count(*) FROM pg_class WHERE relname IN ('AccessResponses','AccessAudits','AccessControlEvents') AND relrowsecurity AND relforcerowsecurity"));
    }

    [Fact]
    public async Task Concurrent_operator_changes_use_one_expected_version_and_only_one_audit_event_commits()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        async Task<bool> Attempt() { try { await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true); return true; } catch (PostgresException e) when (e.SqlState == "40001") { return false; } }
        var results = await Task.WhenAll(Attempt(), Attempt()); Assert.Single(results, x => x);
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessAudits\" WHERE \"MembershipId\"='{p.MembershipId}'"));
    }

    [Fact]
    public async Task Reactivation_rejects_old_cookie_and_new_login_observes_epoch_and_current_grant()
    {
        await using var op = await Operator();
        await using var factory = new ApiFactory(new NpgsqlConnectionStringBuilder(fixture.AppConnection) { MaxPoolSize = 1, ApplicationName = "SaveIT.OneSlotAccessTest" }.ConnectionString);
        var member = DevelopmentSeed.Profiles.Single(x => x.Id == "alice").MembershipId;
        var version = await fixture.Scalar<int>($"SELECT \"AccessVersion\" FROM \"Memberships\" WHERE \"Id\"='{member}'");
        using var client = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        async Task<HttpResponseMessage> Login() {
            var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/csrf")).GetProperty("token").GetString();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/dev/login") { Content = JsonContent.Create(new { profile = "alice" }) };
            request.Headers.Add("X-CSRF-TOKEN", token); return await client.SendAsync(request);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Login()).StatusCode);
        try
        {
            await AccessRunner.ChangeAsync(op.Connection, Json(Change(member, version)), true); version++;
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/organizations")).StatusCode);
            await AccessRunner.ChangeAsync(op.Connection, Json(Change(member, version, enabled: true)), true); version++;
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/organizations")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Login()).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/organizations")).StatusCode);
            await AccessRunner.ChangeAsync(op.Connection, Json(Change(member, version, "grant", null, Org, "Viewer")), true); version++;
            var token = (await client.GetFromJsonAsync<JsonElement>("/api/v1/csrf")).GetProperty("token").GetString();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/organizations/{Org}/assets") { Content = JsonContent.Create(new { name = "Denied", kind = "Network" }) }; request.Headers.Add("X-CSRF-TOKEN", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/organizations/{Org}/assets")).StatusCode);
        }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'; UPDATE \"OrganizationGrants\" SET \"Role\"='Editor' WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{Org}'"); }
    }

    private sealed class HeldResponse : IResult
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("[\"synthetic-held-body\",", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted); Started.TrySetResult();
            await Release.Task.WaitAsync(context.RequestAborted);
            await JsonSerializer.SerializeAsync(context.Response.Body, "finished", cancellationToken: context.RequestAborted);
            await context.Response.WriteAsync("]", context.RequestAborted);
        }
    }
    private async Task<WebApplication> Server(Guid member, HeldResponse held, string? connection = null, bool holdFailure = false)
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build(); var registry = new ResponseRegistry(ConnectionPolicy.RuntimeConnection(connection ?? fixture.AppConnection), NullLogger<ResponseRegistry>.Instance);
        app.Use(async (context, next) => {
            registry.Initialize(context); context.User = Principal(member);
            try { if (!await registry.RegisterAsync(context, context.User)) { context.Response.StatusCode = 401; return; } }
            catch (AccessProtocolUnavailableException) when (holdFailure) { context.Response.StatusCode = 503; await held.ExecuteAsync(context); return; }
            await next(context);
        });
        app.MapGet("/held", () => held); await app.StartAsync(); return app;
    }

    [Fact]
    public async Task Real_kestrel_operator_acknowledgement_waits_for_actual_serialized_response_completion()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held); using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var response = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(1L, await Pending(p.MembershipId));
            var change = AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true);
            await Until(OperatorWaiting); Assert.False(change.IsCompleted); Assert.False(response.IsCompleted);
            held.Release.TrySetResult(); Assert.Contains("finished", await (await response).Content.ReadAsStringAsync());
            Assert.Equal(2, await change); Assert.Equal(0L, await Pending(p.MembershipId));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); }
    }

    [Fact]
    public async Task Losing_database_sessions_and_aging_pending_rows_cannot_create_false_revocation_success()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held); using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var response = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            await fixture.Execute($"SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname=current_database() AND application_name='SaveIT.ResponseRegistration'; UPDATE \"AccessResponses\" SET \"StartedAt\"=now()-interval '30 days' WHERE \"MembershipId\"='{p.MembershipId}'");
            NpgsqlConnection.ClearAllPools(); // Discard intentionally killed idle test sockets; never retries registration/commit.
            Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true))).SqlState);
            Assert.True(await fixture.Scalar<bool>($"SELECT \"Active\" AND \"AccessVersion\"=1 FROM \"Memberships\" WHERE \"Id\"='{p.MembershipId}'"));
            Assert.Equal(1L, await Pending(p.MembershipId)); held.Release.TrySetResult(); await response;
            Assert.Equal(2, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); }
    }

    [Fact]
    public async Task Aborted_http_response_closes_only_from_server_completion_and_then_allows_operator_change()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held); using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var cancel = new CancellationTokenSource(); var response = http.GetAsync("/held", cancel.Token); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Equal(1L, await Pending(p.MembershipId)); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => response);
            await Until(async () => await Pending(p.MembershipId) == 0);
            Assert.Equal("Aborted", await fixture.Scalar<string>($"SELECT \"Outcome\" FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}'"));
            Assert.Equal(2, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); }
    }

    [Fact]
    public async Task Registration_waiting_behind_operator_rechecks_epoch_after_the_exclusive_wait()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held); using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var first = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var change = AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true); await Until(OperatorWaiting);
            var second = http.GetAsync("/held");
            await Until(() => fixture.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.register_response_v1%' AND wait_event='PgSleep')"));
            Assert.False(second.IsCompleted); Assert.Equal(1L, await Pending(p.MembershipId));
            held.Release.TrySetResult(); await first; Assert.Equal(2, await change);
            Assert.Equal(HttpStatusCode.Unauthorized, (await second).StatusCode); Assert.Equal(0L, await Pending(p.MembershipId));
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}'"));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); }
    }

    private static async Task Context(NpgsqlConnection c, NpgsqlTransaction tx, Guid member)
    {
        await using var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id',@t,true),set_config('app.membership_id',@m,true)", c, tx);
        cmd.Parameters.AddWithValue("t", Tenant.ToString()); cmd.Parameters.AddWithValue("m", member.ToString()); await cmd.ExecuteNonQueryAsync();
    }
    private static NpgsqlCommand Registration(NpgsqlConnection c, NpgsqlTransaction tx, Guid member, Guid request, Guid instance) =>
        new($"SELECT public.register_response_v1('{Tenant}','{member}','{member}',1,'{request}','{instance}')", c, tx);
    private static async Task Close(NpgsqlConnection c, Guid member, Guid request, Guid instance)
    {
        await using var tx = await c.BeginTransactionAsync(); await Context(c, tx, member);
        await using var cmd = new NpgsqlCommand($"SELECT public.complete_response_v1('{Tenant}','{member}','{request}','{instance}',false)", c, tx);
        await cmd.ExecuteNonQueryAsync(); await tx.CommitAsync();
    }

    [Fact]
    public async Task Operator_waiting_for_registration_commit_sees_new_pending_record_after_lock_wait()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        await using var registering = new NpgsqlConnection(fixture.AppConnection); await registering.OpenAsync();
        var request = Guid.NewGuid(); var instance = Guid.NewGuid();
        await using var tx = await registering.BeginTransactionAsync(); await Context(registering, tx, p.MembershipId);
        await using var register = Registration(registering, tx, p.MembershipId, request, instance); Assert.True((bool)(await register.ExecuteScalarAsync())!);
        var change = AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true); await Until(OperatorWaiting);
        Assert.Equal(0L, await Pending(p.MembershipId)); // Pending INSERT has not committed yet.
        await tx.CommitAsync();
        await Until(() => fixture.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_locks l JOIN pg_stat_activity a ON a.pid=l.pid WHERE a.datname=current_database() AND a.query LIKE 'SELECT public.access_change_v1%' AND l.locktype='advisory' AND l.mode='ExclusiveLock' AND l.granted)"));
        Assert.False(change.IsCompleted); Assert.Equal(1L, await Pending(p.MembershipId));
        await using var completing = new NpgsqlConnection(fixture.AppConnection); await completing.OpenAsync(); await Close(completing, p.MembershipId, request, instance);
        Assert.Equal(2, await change);
    }

    [Fact]
    public async Task Completion_retries_known_finished_response_without_reopening_authority_or_business_work()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var suffix = Guid.NewGuid().ToString("N"); var fn = "access_retry_" + suffix;
        await fixture.Execute($"CREATE SEQUENCE public.{fn}; CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"MembershipId\"='{p.MembershipId}' AND nextval('public.{fn}')<3 THEN RAISE EXCEPTION 'Synthetic completion failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER {fn} BEFORE UPDATE ON \"AccessResponses\" FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held); using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            var response = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); held.Release.TrySetResult(); Assert.Equal(HttpStatusCode.OK, (await response).StatusCode);
            await Until(async () => await Pending(p.MembershipId) == 0); Assert.Equal(3L, await fixture.Scalar<long>($"SELECT last_value FROM public.{fn}"));
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}'"));
            Assert.Equal(2, await AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); await fixture.Execute($"DROP TRIGGER {fn} ON \"AccessResponses\"; DROP FUNCTION public.{fn}(); DROP SEQUENCE public.{fn}"); }
    }

    [Fact]
    public async Task Exhausted_completion_retries_leave_uncertain_pending_record_blocking_acknowledgement()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var suffix = Guid.NewGuid().ToString("N"); var fn = "access_uncertain_" + suffix;
        await fixture.Execute($"CREATE SEQUENCE public.{fn}; CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"MembershipId\"='{p.MembershipId}' THEN PERFORM nextval('public.{fn}'); RAISE EXCEPTION 'Synthetic completion uncertainty'; END IF; RETURN NEW; END $$; CREATE TRIGGER {fn} BEFORE UPDATE ON \"AccessResponses\" FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held); using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        try
        {
            var response = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); held.Release.TrySetResult(); Assert.Equal(HttpStatusCode.OK, (await response).StatusCode);
            await Until(() => fixture.Scalar<bool>($"SELECT last_value=3 FROM public.{fn}"));
            Assert.Equal("55P03", (await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true))).SqlState);
            Assert.Equal(1L, await Pending(p.MembershipId));
        }
        finally
        {
            held.Release.TrySetResult(); await app.StopAsync(); // Test instance is actually stopped before privileged fixture cleanup.
            await fixture.Execute($"DROP TRIGGER {fn} ON \"AccessResponses\"; DROP FUNCTION public.{fn}(); DROP SEQUENCE public.{fn}; UPDATE \"AccessResponses\" SET \"CompletedAt\"=clock_timestamp(),\"Outcome\"='Aborted' WHERE \"MembershipId\"='{p.MembershipId}' AND \"CompletedAt\" IS NULL");
        }
    }

    [Theory]
    [InlineData(System.Data.IsolationLevel.RepeatableRead)]
    [InlineData(System.Data.IsolationLevel.Serializable)]
    public async Task Registration_and_operator_functions_reject_isolation_that_could_keep_stale_snapshots(System.Data.IsolationLevel isolation)
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        await using (var app = new NpgsqlConnection(fixture.AppConnection))
        {
            await app.OpenAsync(); await using var tx = await app.BeginTransactionAsync(isolation); await Context(app, tx, p.MembershipId);
            await using var cmd = Registration(app, tx, p.MembershipId, Guid.NewGuid(), Guid.NewGuid());
            Assert.Equal("25000", (await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync())).SqlState);
        }
        await using var c = new NpgsqlConnection(op.Connection); await c.OpenAsync();
        await using (var role = new NpgsqlCommand("SET ROLE saveit_access_operator", c)) await role.ExecuteNonQueryAsync();
        await using var changeTx = await c.BeginTransactionAsync(isolation);
        await using var change = new NpgsqlCommand($"SELECT public.access_change_v1('{Tenant}','{p.MembershipId}',1,'active',NULL,NULL,false,NULL,NULL,NULL,'synthetic')", c, changeTx);
        Assert.Equal("25000", (await Assert.ThrowsAsync<PostgresException>(() => change.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task Inspection_is_bounded_audited_and_fails_closed_if_its_audit_cannot_commit()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        await fixture.Execute($"INSERT INTO \"Organizations\" (\"Id\",\"TenantId\",\"Name\",\"Version\",\"UpdatedAt\") SELECT gen_random_uuid(),'{Tenant}','Synthetic grant page',1,now() FROM generate_series(1,55); INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") SELECT '{Tenant}','{p.MembershipId}',o.\"Id\",'Viewer' FROM \"Organizations\" o WHERE o.\"TenantId\"='{Tenant}' AND o.\"Name\"='Synthetic grant page'");
        using var page = JsonDocument.Parse(await AccessRunner.InspectAsync(op.Connection, Json(new AccessInspection(Tenant, p.MembershipId, "synthetic")), true));
        var rows = page.RootElement.GetProperty("grants").EnumerateArray().ToArray(); Assert.Equal(50, rows.Length);
        var after = page.RootElement.GetProperty("nextOrganizationId").GetGuid(); Assert.Equal(after, rows[^1].GetProperty("organizationId").GetGuid());
        using var tail = JsonDocument.Parse(await AccessRunner.InspectAsync(op.Connection, Json(new AccessInspection(Tenant, p.MembershipId, "synthetic", after)), true));
        Assert.Equal(6, tail.RootElement.GetProperty("grants").GetArrayLength()); Assert.Equal(JsonValueKind.Null, tail.RootElement.GetProperty("nextOrganizationId").ValueKind);
        var fn = "access_read_fail_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE FUNCTION public.{fn}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"MembershipId\"='{p.MembershipId}' AND NEW.\"Action\"='Inspected' THEN RAISE EXCEPTION 'Synthetic read audit failure'; END IF; RETURN NEW; END $$; CREATE CONSTRAINT TRIGGER {fn} AFTER INSERT ON \"AccessAudits\" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.{fn}()");
        try { await Assert.ThrowsAsync<PostgresException>(() => AccessRunner.InspectAsync(op.Connection, Json(new AccessInspection(Tenant, p.MembershipId, "synthetic")), true)); }
        finally { await fixture.Execute($"DROP TRIGGER {fn} ON \"AccessAudits\"; DROP FUNCTION public.{fn}()"); }
    }

    [Fact]
    public async Task Normalization_suppresses_ambient_startup_options_and_guard_rejects_a_switched_privileged_login()
    {
        var previous = Environment.GetEnvironmentVariable("PGOPTIONS");
        try
        {
            Environment.SetEnvironmentVariable("PGOPTIONS", "-c application_name=SaveIT.UntrustedAmbient");
            var normalized = ConnectionPolicy.RuntimeConnection(new NpgsqlConnectionStringBuilder(fixture.AppConnection) { Pooling = false }.ConnectionString);
            var settings = new NpgsqlConnectionStringBuilder(normalized); Assert.True(settings.ContainsKey("Options")); Assert.Equal(" ", settings.Options);
            await using var c = new NpgsqlConnection(normalized); await c.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT current_setting('application_name')", c); Assert.NotEqual("SaveIT.UntrustedAmbient", await cmd.ExecuteScalarAsync());
            Environment.SetEnvironmentVariable("PGOPTIONS", "-c role=saveit_app");
            var owner = new NpgsqlConnectionStringBuilder(fixture.OwnerConnection) { Pooling = false }.ConnectionString;
            await using var ambient = new NpgsqlConnection(owner); await ambient.OpenAsync();
            await using var identity = new NpgsqlCommand("SELECT current_user='saveit_app' AND session_user<>current_user", ambient);
            Assert.True((bool)(await identity.ExecuteScalarAsync())!); // Reproduce the unsafe ambient startup path.
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(ambient));
            await using var guardedOwner = new NpgsqlConnection(ConnectionPolicy.RuntimeConnection(owner)); await guardedOwner.OpenAsync();
            await using var unswitched = new NpgsqlCommand("SELECT current_user=session_user AND current_user=@login", guardedOwner);
            unswitched.Parameters.AddWithValue("login", new NpgsqlConnectionStringBuilder(owner).Username!);
            Assert.True((bool)(await unswitched.ExecuteScalarAsync())!);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(guardedOwner));
        }
        finally { Environment.SetEnvironmentVariable("PGOPTIONS", previous); }
        await using var privileged = new NpgsqlConnection(fixture.OwnerConnection); await privileged.OpenAsync();
        await using (var switched = new NpgsqlCommand("SET ROLE saveit_app", privileged)) await switched.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(privileged));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Lost_commit_acknowledgements_never_retry_registration_and_only_retry_known_finished_completion(int cutCommit)
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        await using var proxy = new AccessCommitProxy(fixture.AppConnection, cutCommit);
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held, proxy.Connection, holdFailure: true);
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        var response = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            if (cutCommit == 1) await proxy.CommitLost.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1L, await Pending(p.MembershipId));
            var change = AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true); await Until(OperatorWaiting); Assert.False(change.IsCompleted);
            held.Release.TrySetResult(); Assert.Equal(cutCommit == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK, (await response).StatusCode);
            await proxy.CommitLost.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(2, await change);
            await Until(async () => await Pending(p.MembershipId) == 0);
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}'"));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); }
    }

    [Fact]
    public async Task Saturated_registration_pool_cannot_starve_completion_and_different_members_do_not_share_barriers()
    {
        await using var op = await Operator(); var p = Create(); var other = Create();
        await AccessRunner.ChangeAsync(op.Connection, Json(p), true); await AccessRunner.ChangeAsync(op.Connection, Json(other), true);
        var held = new HeldResponse(); await using var app = await Server(p.MembershipId, held);
        using var http = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 32 }) { BaseAddress = new Uri(app.Urls.Single()) };
        var first = http.GetAsync("/held"); await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var change = AccessRunner.ChangeAsync(op.Connection, Json(Change(p.MembershipId)), true); await Until(OperatorWaiting);
            Assert.Equal(2, await AccessRunner.ChangeAsync(op.Connection, Json(Change(other.MembershipId)), true).WaitAsync(TimeSpan.FromSeconds(2)));
            var blocked = Enumerable.Range(0, 16).Select(_ => http.GetAsync("/held")).ToArray();
            await Until(() => fixture.Scalar<bool>("SELECT count(*)>=16 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.register_response_v1%' AND wait_event='PgSleep'"));
            held.Release.TrySetResult(); await first; Assert.Equal(2, await change);
            foreach (var result in await Task.WhenAll(blocked)) Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
            Assert.Equal(0L, await Pending(p.MembershipId));
        }
        finally { held.Release.TrySetResult(); await app.StopAsync(); }
    }

    [Theory]
    [InlineData("saveit_app", "function")]
    [InlineData("saveit_search_worker", "function")]
    [InlineData("saveit_app", "role")]
    [InlineData("saveit_search_worker", "role")]
    [InlineData("saveit_app", "column")]
    [InlineData("saveit_search_worker", "column")]
    public async Task Runtime_guards_reject_reachable_operator_authority_and_protocol_column_privileges(string role, string kind)
    {
        var bridge = "saveit_access_bridge_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE ROLE {bridge} NOLOGIN NOINHERIT; GRANT {bridge} TO {role}");
        try
        {
            var grant = kind switch {
                "function" => $"GRANT EXECUTE ON FUNCTION public.access_inspect_v1(uuid,uuid,uuid,text) TO {bridge}",
                "role" => $"GRANT saveit_access_operator TO {bridge}",
                _ => $"GRANT UPDATE (\"CompletedAt\") ON \"AccessResponses\" TO {bridge}"
            };
            await fixture.Execute(grant);
            await using var c = new NpgsqlConnection(role == "saveit_app" ? fixture.AppConnection : fixture.WorkerConnection); await c.OpenAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(c));
        }
        finally { await fixture.Execute($"REVOKE {bridge} FROM {role}; DROP OWNED BY {bridge}; DROP ROLE {bridge}"); }
    }

    [Fact]
    public async Task Public_operator_execute_grant_is_unsafe_even_without_direct_or_inherited_membership()
    {
        const string signature = "public.access_inspect_v1(uuid,uuid,uuid,text)";
        await fixture.Execute($"GRANT EXECUTE ON FUNCTION {signature} TO PUBLIC");
        try
        {
            await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(c));
        }
        finally { await fixture.Execute($"REVOKE ALL ON FUNCTION {signature} FROM PUBLIC"); }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("foreign-role")]
    [InlineData("unknown-property")]
    [InlineData("large")]
    [InlineData("http")]
    public async Task Invalid_private_manifests_fail_before_authority_changes(string failure)
    {
        await using var op = await Operator(); var p = Create();
        var json = failure switch {
            "unknown" => Json(p with { Operation = "delete" }),
            "foreign-role" => Json(p with { Role = "Administrator" }),
            "unknown-property" => Json(p).Replace("{", "{\"silentElevation\":true,", StringComparison.Ordinal),
            "large" => new string(' ',8193),
            _ => Json(p with { Issuer = "http://insecure.example" })
        };
        await Assert.ThrowsAnyAsync<Exception>(() => AccessRunner.ChangeAsync(op.Connection, json, true));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"Memberships\" WHERE \"Id\"='{p.MembershipId}'"));
    }

    [Fact]
    public async Task Completion_is_exact_idempotent_and_prunes_at_most_32_closed_rows_without_expiring_pending_work()
    {
        await using var op = await Operator(); var p = Create(); await AccessRunner.ChangeAsync(op.Connection, Json(p), true);
        var request = Guid.NewGuid(); var instance = Guid.NewGuid();
        await fixture.Execute($"INSERT INTO \"AccessResponses\" (\"TenantId\",\"MembershipId\",\"RequestId\",\"InstanceId\",\"StartedAt\",\"CompletedAt\",\"Outcome\") SELECT '{Tenant}','{p.MembershipId}',gen_random_uuid(),'{instance}',now()-interval '30 days',now()-interval '20 days','Completed' FROM generate_series(1,65); INSERT INTO \"AccessResponses\" (\"TenantId\",\"MembershipId\",\"RequestId\",\"InstanceId\",\"StartedAt\") VALUES ('{Tenant}','{p.MembershipId}','{request}','{instance}',now()-interval '30 days')");
        await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync();
        await Close(c, p.MembershipId, request, Guid.NewGuid()); Assert.Equal(1L, await Pending(p.MembershipId));
        Assert.Equal(33L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}' AND \"CompletedAt\"<now()-interval '7 days'"));
        await Close(c, p.MembershipId, request, instance); Assert.Equal(0L, await Pending(p.MembershipId));
        var closed = await fixture.Scalar<DateTime>($"SELECT \"CompletedAt\" FROM \"AccessResponses\" WHERE \"RequestId\"='{request}'");
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}' AND \"CompletedAt\"<now()-interval '7 days'"));
        await Close(c, p.MembershipId, request, instance);
        Assert.Equal(closed, await fixture.Scalar<DateTime>($"SELECT \"CompletedAt\" FROM \"AccessResponses\" WHERE \"RequestId\"='{request}'"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AccessResponses\" WHERE \"MembershipId\"='{p.MembershipId}'"));
    }

    [Fact]
    public async Task Epochless_legacy_cookie_cannot_read_but_can_clear_itself_with_valid_csrf()
    {
        await using var factory = new ApiFactory(fixture.AppConnection);
        using var client = factory.CreateClient(new() { HandleCookies = false, AllowAutoRedirect = false });
        var member = DevelopmentSeed.Profiles.Single(x => x.Id == "alice").MembershipId;
        var principal = Principal(member, subject: "alice"); var claims = (ClaimsIdentity)principal.Identity!;
        claims.RemoveClaim(claims.FindFirst(ResponseRegistry.EpochClaim)!);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
        var legacy = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }, CookieAuthenticationDefaults.AuthenticationScheme));
        var cookie = "SaveIT.Development=" + legacy; client.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/session")).StatusCode);
        var csrf = await client.GetAsync("/api/v1/csrf"); Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        var csrfCookie = Assert.Single(csrf.Headers.GetValues("Set-Cookie"), x => x.StartsWith("SaveIT.Csrf=", StringComparison.Ordinal)).Split(';')[0];
        client.DefaultRequestHeaders.Remove("Cookie");
        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/logout"); logout.Headers.Add("Cookie", cookie + "; " + csrfCookie); logout.Headers.Add("X-CSRF-TOKEN", token);
        var result = await client.SendAsync(logout); Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Contains(result.Headers.GetValues("Set-Cookie"), x => x.StartsWith("SaveIT.Development=;", StringComparison.Ordinal));
    }
}
