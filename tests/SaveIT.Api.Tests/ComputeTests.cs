using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
using NpgsqlTypes;
using SaveIT.Api.Data;
using SaveIT.Api.Security;
using SaveIT.Deployment;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[CollectionDefinition("compute",DisableParallelization=true)]
public sealed class ComputeDatabaseCollection : ICollectionFixture<DatabaseFixture>;

[Collection("compute")]
public sealed class ComputeTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly string login = "saveit_compute_test_"+Guid.NewGuid().ToString("N");
    private readonly List<(Guid Request,Guid Instance)> requests=[];
    private string OperatorConnection => new NpgsqlConnectionStringBuilder(fixture.OwnerConnection) { Username=login,Password="synthetic-compute-test-only",Pooling=false }.ConnectionString;
    private static readonly Guid Tenant=DevelopmentSeed.TenantA;
    private static readonly Guid Member=DevelopmentSeed.Profiles.Single(x=>x.Id=="alice").MembershipId;
    public Task InitializeAsync() => fixture.Execute($"CREATE ROLE \"{login}\" LOGIN NOINHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-compute-test-only'; GRANT saveit_compute_operator TO \"{login}\"");
    public async Task DisposeAsync()
    {
        foreach(var request in requests) await fixture.Execute($"DELETE FROM public.\"AccessResponses\" WHERE \"RequestId\"='{request.Request}'");
        // Owner-only cleanup of this isolated test fixture; no runtime/operator bypass is exposed.
        await fixture.Execute($"DELETE FROM public.\"ComputeOperations\"; DELETE FROM public.\"ComputeControl\"; DROP ROLE \"{login}\"");
    }
    private Task<long> Change(long expected,string action,Guid? generation=null,string? plan=null,Guid? operation=null,string? connection=null) => ComputeRunner.ChangeAsync(connection??OperatorConnection,JsonSerializer.Serialize(new ComputeChange(expected,operation??Guid.NewGuid(),action,generation,plan,"synthetic-compute-operator"),new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.CamelCase }),true);
    private async Task<Guid> Running()
    {
        var generation=Guid.NewGuid(); Assert.Equal(1,await Change(0,"initialize")); Assert.Equal(2,await Change(1,"prepare",generation,new string('a',64))); Assert.Equal(3,await Change(2,"activate")); return generation;
    }
    private async Task<bool> Check(Guid? generation,bool startup=false)
    {
        await using var c=new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync(); return await ComputeAdmission.CheckAsync(c,null,generation,CancellationToken.None,startup);
    }
    private static async Task Context(NpgsqlConnection c,NpgsqlTransaction tx,Guid? generation)
    {
        await using var command=new NpgsqlCommand("SELECT set_config('app.tenant_id',@t,true),set_config('app.membership_id',@m,true),set_config('app.compute_generation',@g,true)",c,tx);
        command.Parameters.AddWithValue("t",Tenant.ToString()); command.Parameters.AddWithValue("m",Member.ToString()); command.Parameters.AddWithValue("g",generation?.ToString()??""); await command.ExecuteNonQueryAsync();
    }
    private async Task<(Guid Request,Guid Instance)> Register(Guid generation, bool expected=true)
    {
        var ids=(Request:Guid.NewGuid(),Instance:Guid.NewGuid());
        await using var c=new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync(); await using var tx=await c.BeginTransactionAsync(); await Context(c,tx,generation);
        await using var command=new NpgsqlCommand("SELECT public.register_response_v1(@t,@m,'alice',1,@r,@i)",c,tx);
        command.Parameters.AddWithValue("t",Tenant);command.Parameters.AddWithValue("m",Member);command.Parameters.AddWithValue("r",ids.Request);command.Parameters.AddWithValue("i",ids.Instance);
        Assert.Equal(expected,await command.ExecuteScalarAsync() is true); await tx.CommitAsync(); requests.Add(ids);return ids;
    }
    private async Task Complete((Guid Request,Guid Instance) ids)
    {
        await using var c=new NpgsqlConnection(fixture.AppConnection);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();await Context(c,tx,null);
        await using var command=new NpgsqlCommand("SELECT public.complete_response_v1(@t,@m,@r,@i,false)",c,tx);
        command.Parameters.AddWithValue("t",Tenant);command.Parameters.AddWithValue("m",Member);command.Parameters.AddWithValue("r",ids.Request);command.Parameters.AddWithValue("i",ids.Instance);
        await command.ExecuteNonQueryAsync();await tx.CommitAsync();
    }
    private async Task WaitFor(Func<Task<bool>> predicate)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));while(!await predicate())await Task.Delay(20,timeout.Token);
    }

    [Fact]
    public async Task Seed_free_state_transitions_are_atomic_versioned_attributed_and_generations_never_rejoin()
    {
        Assert.True(await Check(null));Assert.False(await Check(Guid.NewGuid()));
        var generation=await Running();Assert.True(await Check(generation));Assert.False(await Check(null));Assert.False(await Check(Guid.NewGuid()));
        Assert.Equal(4,await Change(3,"quiesce"));Assert.False(await Check(generation));
        Assert.Equal(5,await Change(4,"drained"));Assert.Equal(6,await Change(5,"fencing"));Assert.Equal(7,await Change(6,"park"));
        Assert.Equal("22023",(await Assert.ThrowsAsync<PostgresException>(()=>Change(7,"prepare",generation,new string('b',64)))).SqlState);
        var next=Guid.NewGuid();Assert.Equal(8,await Change(7,"prepare",next,new string('b',64)));Assert.True(await Check(next,true));Assert.False(await Check(next));
        Assert.Equal(9,await Change(8,"activate"));Assert.True(await Check(next));Assert.False(await Check(generation));
        Assert.Equal(9L,await fixture.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
        Assert.Equal(login,await fixture.Scalar<string>("SELECT \"DatabaseLogin\"::text FROM public.\"ComputeOperations\" WHERE \"Version\"=9"));
    }

    [Fact]
    public async Task Concurrent_expected_versions_have_one_winner_and_duplicate_intents_do_not_retry_uncertain_decisions()
    {
        await Running();var operation=Guid.NewGuid();
        async Task<bool> Attempt(){try{await Change(3,"quiesce",operation:operation);return true;}catch(PostgresException e)when(e.SqlState=="40001"){return false;}}
        Assert.Single(await Task.WhenAll(Attempt(),Attempt()),x=>x);
        Assert.Equal(4L,await fixture.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
        Assert.Equal("23505",(await Assert.ThrowsAsync<PostgresException>(()=>Change(4,"drained",operation:operation))).SqlState);
        Assert.Equal("Quiescing",await fixture.Scalar<string>("SELECT \"Phase\" FROM public.\"ComputeControl\""));
    }

    [Fact]
    public async Task Lost_COMMIT_acknowledgement_is_uncertain_but_inspection_retains_the_exact_durable_decision()
    {
        await Running();var operation=Guid.NewGuid();await using var proxy=new AccessCommitProxy(OperatorConnection,1);
        await Assert.ThrowsAnyAsync<NpgsqlException>(()=>Change(3,"quiesce",operation:operation,connection:proxy.Connection));
        await proxy.CommitLost.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var state=JsonDocument.Parse(await ComputeRunner.InspectAsync(OperatorConnection,true));Assert.Equal(4,state.RootElement.GetProperty("version").GetInt64());Assert.Equal(operation,state.RootElement.GetProperty("operationId").GetGuid());Assert.Equal("Quiescing",state.RootElement.GetProperty("phase").GetString());
        Assert.Equal("40001",(await Assert.ThrowsAsync<PostgresException>(()=>Change(3,"quiesce",operation:operation))).SqlState);
        Assert.Equal(4L,await fixture.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\""));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Journal_statement_or_deferred_commit_failure_rolls_back_control_state(bool deferred)
    {
        await Running();const string name="compute_journal_failure";
        await fixture.Execute($"CREATE FUNCTION public.{name}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='quiesce' THEN RAISE EXCEPTION 'Synthetic journal failure'; END IF; RETURN NEW; END $$; CREATE {(deferred?"CONSTRAINT ":"")}TRIGGER {name} {(deferred?"AFTER":"BEFORE")} INSERT ON public.\"ComputeOperations\" {(deferred?"DEFERRABLE INITIALLY DEFERRED ":"")}FOR EACH ROW EXECUTE FUNCTION public.{name}()");
        try { await Assert.ThrowsAsync<PostgresException>(()=>Change(3,"quiesce"));Assert.Equal("Running",await fixture.Scalar<string>("SELECT \"Phase\" FROM public.\"ComputeControl\""));Assert.Equal(3L,await fixture.Scalar<long>("SELECT count(*) FROM public.\"ComputeOperations\"")); }
        finally {await fixture.Execute($"DROP TRIGGER {name} ON public.\"ComputeOperations\";DROP FUNCTION public.{name}()");}
    }

    [Fact]
    public async Task Orphan_pending_responses_block_drain_and_aging_cannot_force_close_but_real_completion_remains_ungated()
    {
        var generation=await Running();var pending=await Register(generation);Assert.Equal(4,await Change(3,"quiesce"));
        await fixture.Execute($"UPDATE public.\"AccessResponses\" SET \"StartedAt\"=clock_timestamp()-interval '90 days' WHERE \"RequestId\"='{pending.Request}'");
        Assert.Equal("55P03",(await Assert.ThrowsAsync<PostgresException>(()=>Change(4,"drained"))).SqlState);
        using var inspected=JsonDocument.Parse(await ComputeRunner.InspectAsync(OperatorConnection,true));Assert.Equal(1,inspected.RootElement.GetProperty("pendingResponses").GetInt32());
        await Complete(pending);Assert.Equal(5,await Change(4,"drained"));
        Assert.Equal(1L,await fixture.Scalar<long>($"SELECT count(*) FROM public.\"AccessResponses\" WHERE \"RequestId\"='{pending.Request}' AND \"CompletedAt\" IS NOT NULL"));
    }

    [Fact]
    public async Task Exclusive_change_waits_for_worker_transaction_then_post_wait_registration_sees_quiescence()
    {
        var generation=await Running();
        await using var worker=new NpgsqlConnection(fixture.WorkerConnection);await worker.OpenAsync();await using var held=await worker.BeginTransactionAsync();
        Assert.True(await ComputeAdmission.CheckAsync(worker,held,generation,CancellationToken.None));
        var changing=Change(3,"quiesce");
        await WaitFor(()=>fixture.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.compute_change_v1%' AND wait_event='PgSleep')"));
        Assert.False(changing.IsCompleted);await held.CommitAsync();Assert.Equal(4,await changing);Assert.False(await Check(generation));
        await using var op=new NpgsqlConnection(OperatorConnection);await op.OpenAsync();await using(var role=new NpgsqlCommand("SET ROLE saveit_compute_operator",op))await role.ExecuteNonQueryAsync();
        await using var gate=await op.BeginTransactionAsync();await using(var lockCommand=new NpgsqlCommand("SELECT pg_advisory_xact_lock(91555333526868::bigint)",op,gate))await lockCommand.ExecuteNonQueryAsync();
        var admission=Check(generation);
        await WaitFor(()=>fixture.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.compute_enter_v1%' AND wait_event='PgSleep')"));
        Assert.False(admission.IsCompleted);await gate.CommitAsync();Assert.False(await admission);
    }

    [Fact]
    public async Task Operator_post_wait_snapshot_includes_newly_committed_registration()
    {
        var generation=await Running();var ids=(Request:Guid.NewGuid(),Instance:Guid.NewGuid());requests.Add(ids);
        await using var c=new NpgsqlConnection(fixture.AppConnection);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();await Context(c,tx,generation);
        await using(var command=new NpgsqlCommand("SELECT public.register_response_v1(@t,@m,'alice',1,@r,@i)",c,tx)) {
            command.Parameters.AddWithValue("t",Tenant);command.Parameters.AddWithValue("m",Member);command.Parameters.AddWithValue("r",ids.Request);command.Parameters.AddWithValue("i",ids.Instance);
            Assert.True(await command.ExecuteScalarAsync() is true);
        }
        var change=Change(3,"quiesce");await WaitFor(()=>fixture.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.compute_change_v1%' AND wait_event='PgSleep')"));
        Assert.False(change.IsCompleted);await tx.CommitAsync();Assert.Equal(4,await change);
        Assert.Equal("55P03",(await Assert.ThrowsAsync<PostgresException>(()=>Change(4,"drained"))).SqlState);
        await Complete(ids);Assert.Equal(5,await Change(4,"drained"));
    }

    [Fact]
    public async Task Registration_waiting_behind_exclusive_change_rechecks_the_committed_state()
    {
        var generation=await Running();await using var op=new NpgsqlConnection(OperatorConnection);await op.OpenAsync();
        await using(var role=new NpgsqlCommand("SET ROLE saveit_compute_operator",op))await role.ExecuteNonQueryAsync();
        await using var tx=await op.BeginTransactionAsync();
        await using(var command=new NpgsqlCommand("SELECT public.compute_change_v1(3,@id,'quiesce',NULL,NULL,'synthetic')",op,tx)){command.Parameters.AddWithValue("id",Guid.NewGuid());Assert.Equal(4L,await command.ExecuteScalarAsync());}
        var registration=Register(generation,false);
        await WaitFor(()=>fixture.Scalar<bool>("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND query LIKE 'SELECT public.register_response_v1%' AND wait_event='PgSleep')"));
        Assert.False(registration.IsCompleted);await tx.CommitAsync();await registration;Assert.Equal(5,await Change(4,"drained"));
    }

    [Fact]
    public async Task Real_Kestrel_completion_finishes_existing_body_while_quiescence_refuses_new_registration()
    {
        var generation=await Running();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder=WebApplication.CreateSlimBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app=builder.Build();var registry=new ResponseRegistry(ConnectionPolicy.RuntimeConnection(fixture.AppConnection),NullLogger<ResponseRegistry>.Instance,generation);
        app.Use(async(context,next)=>{
            registry.Initialize(context);var user=new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier,Member.ToString()),new("tenant",Tenant.ToString()),new("subject","alice"),new(ResponseRegistry.EpochClaim,"1")],"synthetic"));
            if(!await registry.RegisterAsync(context,user)){context.Response.StatusCode=503;return;}await next(context);
        });
        app.MapGet("/held",async(HttpContext context)=>{await context.Response.WriteAsync("held:");await context.Response.Body.FlushAsync();started.TrySetResult();await release.Task.WaitAsync(context.RequestAborted);await context.Response.WriteAsync("finished");});
        await app.StartAsync();using var client=new HttpClient(new SocketsHttpHandler { UseProxy=false }) { BaseAddress=new Uri(app.Urls.Single()),Timeout=TimeSpan.FromSeconds(15) };
        var first=client.GetAsync("/held");await started.Task.WaitAsync(TimeSpan.FromSeconds(8));
        try {
            Assert.Equal(4,await Change(3,"quiesce"));Assert.False(first.IsCompleted);
            Assert.Equal("55P03",(await Assert.ThrowsAsync<PostgresException>(()=>Change(4,"drained"))).SqlState);
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/held")).StatusCode);
            release.TrySetResult();Assert.Equal("held:finished",await(await first).Content.ReadAsStringAsync());
            await WaitFor(()=>fixture.Scalar<bool>("SELECT NOT EXISTS(SELECT 1 FROM public.\"AccessResponses\" WHERE \"CompletedAt\" IS NULL)"));
            Assert.Equal(5,await Change(4,"drained"));
        }
        finally{release.TrySetResult();using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));await app.StopAsync(stop.Token);}
    }

    [Theory]
    [InlineData(System.Data.IsolationLevel.RepeatableRead)] [InlineData(System.Data.IsolationLevel.Serializable)]
    public async Task Admission_refuses_snapshot_isolation_before_any_compute_lock(System.Data.IsolationLevel isolation)
    {
        var generation=await Running();await using var c=new NpgsqlConnection(fixture.AppConnection);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync(isolation);
        Assert.Equal("25000",(await Assert.ThrowsAsync<PostgresException>(()=>ComputeAdmission.CheckAsync(c,tx,generation,CancellationToken.None))).SqlState);
    }

    [Fact]
    public async Task Worker_batch_rebuild_replay_and_legacy_generation_refuse_quiesced_work_without_receipt_failures()
    {
        var generation=await Running();var worker=new SearchProcessor(fixture.WorkerConnection,computeGeneration:generation);
        await worker.ValidateAsync();Assert.Equal(4,await Change(3,"quiesce"));
        var before=await fixture.Scalar<long>("SELECT COALESCE(sum(\"Attempts\"),0)::bigint FROM public.\"SearchDeliveries\"");
        await Assert.ThrowsAsync<ComputeUnavailableException>(()=>worker.ProcessBatchAsync(Tenant));
        await Assert.ThrowsAsync<ComputeUnavailableException>(()=>worker.RebuildAsync(Tenant));
        await Assert.ThrowsAsync<ComputeUnavailableException>(()=>worker.ReplayAsync(Tenant,Guid.NewGuid()));
        await Assert.ThrowsAsync<ComputeUnavailableException>(()=>new SearchProcessor(fixture.WorkerConnection).ProcessBatchAsync(Tenant));
        Assert.Equal(before,await fixture.Scalar<long>("SELECT COALESCE(sum(\"Attempts\"),0)::bigint FROM public.\"SearchDeliveries\""));
    }

    [Fact]
    public async Task Failed_selected_event_cannot_write_retry_receipt_when_quiescence_commits_before_replacement_transaction()
    {
        var generation=await Running();var id=Guid.NewGuid();var org=DevelopmentSeed.OrgA;
        await fixture.Execute($"INSERT INTO public.\"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES ('{id}','{Tenant}','{org}','{org}','Organization.Created',1,now());CREATE FUNCTION public.compute_receipt_failure() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"EventId\"='{id}' AND NEW.\"CompletedAt\" IS NOT NULL THEN RAISE EXCEPTION 'Synthetic receipt failure'; END IF;RETURN NEW;END $$;CREATE TRIGGER compute_receipt_failure BEFORE INSERT OR UPDATE ON public.\"SearchDeliveries\" FOR EACH ROW EXECUTE FUNCTION public.compute_receipt_failure()");
        var before=await fixture.Scalar<string>($"SELECT to_jsonb(d)::text FROM public.\"SearchDeliveries\" d WHERE \"EventId\"='{id}'");
        await using var proxy=new AccessCommitProxy(fixture.WorkerConnection,int.MaxValue,holdRollback:true);
        var processing=new SearchProcessor(proxy.Connection,computeGeneration:generation).ProcessBatchAsync(Tenant);
        try {
            await proxy.RollbackObserved.Task.WaitAsync(TimeSpan.FromSeconds(8));Assert.Equal(4,await Change(3,"quiesce"));proxy.ReleaseRollback.TrySetResult();
            await Assert.ThrowsAsync<ComputeUnavailableException>(()=>processing);
            Assert.Equal(before,await fixture.Scalar<string>($"SELECT to_jsonb(d)::text FROM public.\"SearchDeliveries\" d WHERE \"EventId\"='{id}'"));
        }
        finally {
            proxy.ReleaseRollback.TrySetResult();try{await processing.WaitAsync(TimeSpan.FromSeconds(5));}catch(Exception){}
            await fixture.Execute($"DROP TRIGGER compute_receipt_failure ON public.\"SearchDeliveries\";DROP FUNCTION public.compute_receipt_failure();DELETE FROM public.\"SearchDeliveries\" WHERE \"EventId\"='{id}';DELETE FROM public.\"OutboxEvents\" WHERE \"Id\"='{id}'");
        }
    }

    [Fact]
    public async Task Bound_API_has_distinct_startup_readiness_and_live_checks_and_refuses_new_work_after_quiesce()
    {
        var generation=Guid.NewGuid();await Change(0,"initialize");await Change(1,"prepare",generation,new string('a',64));
        await using var factory=fixture.Factory.WithWebHostBuilder(builder=>builder.UseSetting("Deployment:ComputeGuard","true").UseSetting("Deployment:ComputeGeneration",generation.ToString()));
        using var client=factory.CreateClient();Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/startup")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/health/ready")).StatusCode);Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/api/v1/auth/options")).StatusCode);
        await Change(2,"activate");Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/v1/csrf")).StatusCode);
        await Change(3,"quiesce");Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/api/v1/csrf")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/health/ready")).StatusCode);Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Legacy_API_and_worker_compatibility_ends_when_control_state_is_initialized()
    {
        await using var factory=new ApiFactory(fixture.AppConnection);using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/health/ready")).StatusCode);await new SearchProcessor(fixture.WorkerConnection).ValidateAsync();
        await Running();
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/health/startup")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync("/api/v1/csrf")).StatusCode);
        await Assert.ThrowsAsync<ComputeUnavailableException>(()=>new SearchProcessor(fixture.WorkerConnection).ValidateAsync());
        await using var unbound=new ApiFactory(fixture.AppConnection);Assert.Throws<InvalidOperationException>(()=>unbound.CreateClient());
    }

    [Fact]
    public async Task Offline_downgrade_restores_original_registration_before_dropping_coordination_and_upgrade_is_seed_free()
    {
        var generation=await Running();
        await using var db=new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(fixture.OwnerConnection).Options,new());
        var migrator=db.GetService<IMigrator>();await migrator.MigrateAsync("20261102004553_OperatorAccess");
        try {
            Assert.True(await fixture.Scalar<bool>("SELECT to_regprocedure('public.register_response_core_v1(uuid,uuid,text,integer,uuid,uuid)') IS NULL AND to_regclass('public.\"ComputeControl\"') IS NULL"));
            var request=await Register(generation);await Complete(request);
        }
        finally{await migrator.MigrateAsync(DatabaseRoleGuard.RequiredMigration);}
        Assert.Equal(0L,await fixture.Scalar<long>("SELECT count(*) FROM public.\"ComputeControl\""));Assert.True(await Check(null));Assert.False(await Check(generation));
    }

    [Theory]
    [InlineData("direct")] [InlineData("public")] [InlineData("membership")]
    public async Task Operator_guard_rejects_any_reachable_response_completion_authority(string source)
    {
        var bridge="compute_protocol_bridge_"+Guid.NewGuid().ToString("N");
        var target=source=="public"?"PUBLIC":source=="direct"?login:bridge;
        if(source=="membership")await fixture.Execute($"CREATE ROLE {bridge} NOLOGIN NOINHERIT;GRANT {bridge} TO {login}");
        await fixture.Execute($"GRANT EXECUTE ON FUNCTION public.complete_response_v1(uuid,uuid,uuid,uuid,boolean) TO {target}");
        try{await Assert.ThrowsAsync<InvalidOperationException>(()=>ComputeRunner.InspectAsync(OperatorConnection,true));}
        finally{await fixture.Execute($"REVOKE EXECUTE ON FUNCTION public.complete_response_v1(uuid,uuid,uuid,uuid,boolean) FROM {target}");if(source=="membership")await fixture.Execute($"DROP ROLE {bridge}");}
    }

    [Fact]
    public async Task Compute_and_access_operator_credential_separation_is_checked_in_both_directions()
    {
        await fixture.Execute($"GRANT saveit_access_operator TO {login}");
        try {
            await Assert.ThrowsAsync<InvalidOperationException>(()=>ComputeRunner.InspectAsync(OperatorConnection,true));
            await Assert.ThrowsAsync<InvalidOperationException>(()=>AccessRunner.InspectAsync(OperatorConnection,JsonSerializer.Serialize(new AccessInspection(Tenant,Member,"synthetic")),true));
        }
        finally{await fixture.Execute($"REVOKE saveit_access_operator FROM {login}");}
    }

    [Theory]
    [InlineData("saveit_app")] [InlineData("saveit_search_worker")]
    public async Task Runtime_roles_cannot_reach_operator_state_privileges_and_column_grant_poison_fails_startup(string role)
    {
        var connection=role=="saveit_app"?fixture.AppConnection:fixture.WorkerConnection;
        await using var c=new NpgsqlConnection(connection);await c.OpenAsync();await DatabaseRoleGuard.ValidateAsync(c);
        foreach(var sql in new[]{"SELECT public.compute_inspect_v1()","SELECT * FROM public.\"ComputeControl\"","SET ROLE saveit_compute_operator","SELECT public.register_response_core_v1(NULL,NULL,NULL,NULL,NULL,NULL)"}) {
            await using var forbidden=new NpgsqlCommand(sql,c);Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(()=>forbidden.ExecuteNonQueryAsync())).SqlState);
        }
        foreach(var table in new[]{"ComputeControl","ComputeOperations"}) {
            await fixture.Execute($"GRANT SELECT (\"Generation\") ON public.\"{table}\" TO {role}");
            try{await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseRoleGuard.ValidateAsync(c));}
            finally{await fixture.Execute($"REVOKE SELECT (\"Generation\") ON public.\"{table}\" FROM {role}");}
        }
        foreach(var unsafeConnection in new[]{fixture.OwnerConnection,fixture.AppConnection,fixture.WorkerConnection})await Assert.ThrowsAnyAsync<Exception>(()=>ComputeRunner.InspectAsync(unsafeConnection,true));
    }
}
