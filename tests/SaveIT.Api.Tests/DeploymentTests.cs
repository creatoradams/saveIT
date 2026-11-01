using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Deployment;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class DeploymentTests(DatabaseFixture fixture)
{
    private sealed class Isolated(string admin, string database, string login) : IAsyncDisposable
    {
        public string Connection => new NpgsqlConnectionStringBuilder(admin) { Database = database, Username = login, Password = "synthetic-release-test-only", Pooling = false }.ConnectionString;
        public string Admin => new NpgsqlConnectionStringBuilder(admin) { Database = database, Pooling = false }.ConnectionString;
        public string App => new NpgsqlConnectionStringBuilder(admin) { Database = database, Username = "saveit_app", Password = "synthetic-app-only" }.ConnectionString;
        public string Worker => new NpgsqlConnectionStringBuilder(admin) { Database = database, Username = "saveit_search_worker", Password = "synthetic-worker-only" }.ConnectionString;
        public async Task Execute(string sql) { await using var c = new NpgsqlConnection(Admin); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }
        public async Task<T> Scalar<T>(string sql) { await using var c = new NpgsqlConnection(Admin); await c.OpenAsync(); await using var cmd = new NpgsqlCommand(sql, c); return (T)(await cmd.ExecuteScalarAsync())!; }
        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(admin); await c.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE); DROP ROLE \"{login}\"", c); await cmd.ExecuteNonQueryAsync();
        }
    }
    private async Task<Isolated> Database()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "SaveIT.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        await fixture.Execute(await File.ReadAllTextAsync(Path.Combine(root.FullName, "infra/postgres/bootstrap-roles.sql")));
        var suffix = Guid.NewGuid().ToString("N"); var name = "saveit_release_" + suffix; var login = "saveit_release_" + suffix;
        await fixture.Execute($"CREATE ROLE \"{login}\" LOGIN INHERIT NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD 'synthetic-release-test-only'; GRANT saveit_migration_owner TO \"{login}\"");
        await fixture.Execute($"CREATE DATABASE \"{name}\" OWNER saveit_migration_owner ENCODING 'UTF8' TEMPLATE template0 LC_COLLATE 'C' LC_CTYPE 'C'");
        return new(fixture.OwnerConnection, name, login);
    }
    private static ProvisionManifest Manifest() => new(Guid.NewGuid(), "Synthetic release tenant", Guid.NewGuid(), "synthetic-subject-" + Guid.NewGuid(),
        "Synthetic release user", "https://synthetic.okta.example/oauth2/default", "external-" + Guid.NewGuid(), Guid.NewGuid(), "Synthetic release organization", "synthetic-operator");

    [Fact]
    public async Task Seed_free_migrations_run_as_non_superuser_owner_preserve_forced_rls_and_runtime_guards_and_repeat_safely()
    {
        await using var d = await Database();
        await DeploymentRunner.MigrateAsync(d.Connection, true); await DeploymentRunner.MigrateAsync(d.Connection, true);
        Assert.Equal(0L, await d.Scalar<long>("SELECT count(*) FROM \"Tenants\""));
        Assert.False(await d.Scalar<bool>("SELECT rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname='saveit_migration_owner'"));
        Assert.Equal(25L, await d.Scalar<long>("SELECT count(*) FROM pg_policy WHERE polname='saveit_deployment_owner'"));
        foreach (var connection in new[] { d.App, d.Worker })
        {
            await using var c = new NpgsqlConnection(connection); await c.OpenAsync();
            await DatabaseRoleGuard.ValidateAsync(c); await DatabaseRoleGuard.ValidateSchemaAsync(c, requireMigration: true);
            if (connection == d.Worker) await DatabaseRoleGuard.ValidateSearchWorkerAsync(c);
            await using var roles = new NpgsqlCommand("SELECT pg_has_role(current_user,'saveit_migration_owner','MEMBER')", c); Assert.False((bool)(await roles.ExecuteScalarAsync())!);
        }
        await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(d.Admin).Options, new());
        Assert.Equal(DatabaseRoleGuard.RequiredMigration, db.Database.GetMigrations().Last());
    }

    [Fact]
    public async Task Explicit_insert_only_provisioning_is_atomic_attributed_scoped_and_indexes_only_known_metadata()
    {
        await using var d = await Database(); await DeploymentRunner.MigrateAsync(d.Connection, true);
        var p = Manifest(); await DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(p), true);
        Assert.Equal(1L, await d.Scalar<long>("SELECT count(*) FROM \"Tenants\""));
        Assert.Equal(p.Subject, await d.Scalar<string>("SELECT \"Actor\" FROM \"OrganizationRevisions\""));
        Assert.Equal(p.Operator, await d.Scalar<string>("SELECT \"Actor\" FROM \"AuditEvents\" WHERE \"Action\"='Tenant.Provisioned'"));
        Assert.Equal("Editor", await d.Scalar<string>("SELECT \"Role\" FROM \"OrganizationGrants\""));
        Assert.Equal(1L, await d.Scalar<long>("SELECT count(*) FROM \"SearchDeliveries\""));
        var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(p with { TenantName = "Must not overwrite" }), true));
        Assert.Equal("23505", Assert.IsType<PostgresException>(duplicate.InnerException).SqlState);
        Assert.Equal(p.TenantName, await d.Scalar<string>("SELECT \"Name\" FROM \"Tenants\""));
        await using var c = new NpgsqlConnection(d.App); await c.OpenAsync();
        await using var hidden = new NpgsqlCommand("SELECT count(*) FROM \"Organizations\"", c); Assert.Equal(0L, await hidden.ExecuteScalarAsync());
        var result = await new SearchProcessor(d.Worker).ProcessBatchAsync(p.TenantId); Assert.Equal(1, result.Completed); Assert.Equal(0, result.DeadLettered);
        Assert.Equal(0L, await d.Scalar<long>("SELECT count(*) FROM \"SearchEntries\""));
    }

    [Theory]
    [InlineData("AuditEvents")]
    [InlineData("OutboxEvents")]
    [InlineData("SearchDeliveries")]
    public async Task Provisioning_failures_roll_back_tenant_member_grant_history_audit_outbox_and_queue(string table)
    {
        await using var d = await Database(); await DeploymentRunner.MigrateAsync(d.Connection, true);
        await d.Execute($"CREATE FUNCTION fail_release() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic failure'; END $$; CREATE TRIGGER fail_release BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_release()");
        await Assert.ThrowsAnyAsync<Exception>(() => DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(Manifest()), true));
        foreach (var t in new[] { "Tenants", "Memberships", "Organizations", "OrganizationGrants", "OrganizationRevisions", "AuditEvents", "OutboxEvents", "SearchDeliveries" })
            Assert.Equal(0L, await d.Scalar<long>($"SELECT count(*) FROM \"{t}\""));
    }

    [Fact]
    public async Task Forward_upgrade_backfills_both_tenants_under_forced_rls_without_owner_bypass()
    {
        await using var d = await Database(); await DeploymentRunner.MigrateAsync(d.Connection, true);
        var a = Manifest(); var b = Manifest();
        foreach (var p in new[] { a, b }) await DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(p), true);
        await using (var c = new NpgsqlConnection(d.Connection))
        {
            await c.OpenAsync(); await using var set = new NpgsqlCommand("SET ROLE saveit_migration_owner", c); await set.ExecuteNonQueryAsync();
            await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(c).Options, new());
            await db.GetService<IMigrator>().MigrateAsync("20261029225741_TypedAssetSchemas");
        }
        foreach (var p in new[] { a, b })
            await d.Execute($"INSERT INTO \"Locations\" (\"Id\",\"TenantId\",\"OrganizationId\",\"Name\",\"Details\",\"Notes\",\"IsArchived\",\"Version\",\"UpdatedAt\") VALUES ('{Guid.NewGuid()}','{p.TenantId}','{p.OrganizationId}','Exact location','Address','Notes',false,1,now())");
        await DeploymentRunner.MigrateAsync(d.Connection, true);
        Assert.Equal(2L, await d.Scalar<long>("SELECT count(*) FROM \"LocationRevisions\""));
        Assert.Equal(2L, await d.Scalar<long>("SELECT count(*) FROM \"OrganizationRevisions\""));
        Assert.Equal(25L, await d.Scalar<long>("SELECT count(*) FROM pg_policy WHERE polname='saveit_deployment_owner'"));
    }

    [Fact]
    public async Task Runtime_connections_cannot_migrate_or_provision_and_foreign_schema_history_is_rejected()
    {
        await using var d = await Database(); await DeploymentRunner.MigrateAsync(d.Connection, true);
        await Assert.ThrowsAsync<PostgresException>(() => DeploymentRunner.MigrateAsync(d.App, true));
        await Assert.ThrowsAsync<PostgresException>(() => DeploymentRunner.ProvisionAsync(d.Worker, JsonSerializer.Serialize(Manifest()), true));
        await d.Execute("INSERT INTO \"__EFMigrationsHistory\" VALUES ('99999999999999_Foreign','10.0.14')");
        await Assert.ThrowsAsync<InvalidOperationException>(() => DeploymentRunner.MigrateAsync(d.Connection, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(Manifest()), true));
        Assert.Equal(0L, await d.Scalar<long>("SELECT count(*) FROM \"Tenants\""));
    }

    [Theory]
    [InlineData("saveit_app", "direct")]
    [InlineData("saveit_search_worker", "direct")]
    [InlineData("saveit_app", "PUBLIC")]
    [InlineData("saveit_search_worker", "PUBLIC")]
    [InlineData("saveit_app", "reachable")]
    [InlineData("saveit_search_worker", "reachable")]
    public async Task Schema_create_privileges_fail_the_runtime_guard_even_through_noinherit_roles(string role, string kind)
    {
        const string creator = "saveit_test_schema_creator";
        var grantee = kind == "PUBLIC" ? "PUBLIC" : kind == "reachable" ? creator : role;
        if (kind == "reachable") await fixture.Execute($"CREATE ROLE {creator} NOLOGIN NOINHERIT; GRANT {creator} TO {role} WITH INHERIT FALSE");
        await fixture.Execute($"GRANT CREATE ON SCHEMA public TO {grantee}");
        try
        {
            await using var c = new NpgsqlConnection(role == "saveit_app" ? fixture.AppConnection : fixture.WorkerConnection); await c.OpenAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(c));
        }
        finally
        {
            await fixture.Execute($"REVOKE CREATE ON SCHEMA public FROM {grantee}");
            if (kind == "reachable") await fixture.Execute($"REVOKE {creator} FROM {role}; DROP ROLE {creator}");
        }
    }

    [Fact]
    public async Task Deployment_and_runtime_connections_ignore_alternate_search_paths_and_schema_failures_block_worker_writes()
    {
        await using var d = await Database();
        await d.Execute("CREATE SCHEMA shadow AUTHORIZATION saveit_migration_owner");
        var redirected = new NpgsqlConnectionStringBuilder(d.Connection) { SearchPath = "shadow,public" }.ConnectionString;
        await DeploymentRunner.MigrateAsync(redirected, true);
        Assert.Equal(0L, await d.Scalar<long>("SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='shadow' AND c.relkind='r'"));
        var app = ConnectionPolicy.RuntimeConnection(new NpgsqlConnectionStringBuilder(d.App) { SearchPath = "shadow,public" }.ConnectionString);
        await using (var c = new NpgsqlConnection(app))
        { await c.OpenAsync(); await using var cmd = new NpgsqlCommand("SELECT current_schema()", c); Assert.Equal("public", await cmd.ExecuteScalarAsync()); }
        var p = Manifest(); await DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(p), true);
        await d.Execute("INSERT INTO \"__EFMigrationsHistory\" VALUES ('99999999999999_Foreign','10.0.14')");
        var processor = new SearchProcessor(d.Worker, requireSchema: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessBatchAsync(p.TenantId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.RebuildAsync(p.TenantId));
        Assert.Equal(0L, await d.Scalar<long>("SELECT count(*) FROM \"SearchDeliveries\" WHERE \"CompletedAt\" IS NOT NULL"));
        Assert.Equal(0L, await d.Scalar<long>("SELECT count(*) FROM \"SearchEntries\""));
    }

    [Fact]
    public async Task Oversized_manifest_file_is_rejected_with_bounded_read()
    {
        var path = Path.GetTempFileName();
        try { await File.WriteAllTextAsync(path, new string('a', 100000)); await Assert.ThrowsAsync<InvalidOperationException>(() => DeploymentRunner.ReadManifestAsync(path)); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Worker_readiness_requires_a_fresh_successful_supported_snapshot()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.False(WorkerHealth.IsReady(path)); WorkerHealth.Report(path, true); Assert.True(WorkerHealth.IsReady(path));
            WorkerHealth.Report(path, false); Assert.False(WorkerHealth.IsReady(path));
            foreach (var snapshot in new[] { new WorkerHealthSnapshot(1,true,DateTimeOffset.UtcNow.AddMinutes(-1)), new WorkerHealthSnapshot(1,true,DateTimeOffset.UtcNow.AddMinutes(1)), new WorkerHealthSnapshot(2,true,DateTimeOffset.UtcNow) })
            { File.WriteAllText(path, JsonSerializer.Serialize(snapshot)); Assert.False(WorkerHealth.IsReady(path)); }
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"unexpected\":true}")]
    public async Task Invalid_manifests_do_not_contact_the_database(string json) =>
        await Assert.ThrowsAnyAsync<Exception>(() => DeploymentRunner.ProvisionAsync("Host=invalid.example;Database=invalid;SSL Mode=VerifyFull", json));

    [Fact]
    public void Connection_policy_only_allows_verified_remote_tls_or_explicit_loopback()
    {
        ConnectionPolicy.Validate("Host=db.example;SSL Mode=VerifyFull");
        ConnectionPolicy.Validate("Host=127.0.0.1;SSL Mode=Disable", true);
        ConnectionPolicy.Validate("Host=::1;SSL Mode=Disable", true);
        foreach (var host in new[] { "db.example", "127.0.0.1,evil.example", "/tmp/socket" })
            Assert.Throws<InvalidOperationException>(() => ConnectionPolicy.Validate($"Host={host};SSL Mode=Disable", true));
        Assert.Throws<InvalidOperationException>(() => ConnectionPolicy.Validate("Host=localhost;SSL Mode=Require"));
    }

    [Theory]
    [InlineData(0, 0, 0, 5, 5, 2000)]
    [InlineData(60, 60, 60000, 5, 5, 2000)]
    [InlineData(1, 2, -1, 1, 2, -1)]
    public void Production_worker_bounds_connection_command_and_cancellation_waits(int connect, int command, int cancel, int expectedConnect, int expectedCommand, int expectedCancel)
    {
        var input = new NpgsqlConnectionStringBuilder { Host = "synthetic.example", Timeout = connect, CommandTimeout = command, CancellationTimeout = cancel };
        var bounded = new NpgsqlConnectionStringBuilder(ConnectionPolicy.WorkerConnection(input.ConnectionString, true));
        Assert.Equal(expectedConnect, bounded.Timeout); Assert.Equal(expectedCommand, bounded.CommandTimeout); Assert.Equal(expectedCancel, bounded.CancellationTimeout);
        var development = new NpgsqlConnectionStringBuilder(ConnectionPolicy.WorkerConnection(input.ConnectionString, false));
        Assert.Equal(connect, development.Timeout); Assert.Equal(command, development.CommandTimeout); Assert.Equal(cancel, development.CancellationTimeout);
    }

    private sealed class DeadlineClock : TimeProvider
    {
        public sealed class Timer(TimerCallback callback, object? state) : ITimer
        {
            private int disposed;
            public void Fire() { if (Volatile.Read(ref disposed) == 0) callback(state); }
            public bool Change(TimeSpan dueTime, TimeSpan period) => Volatile.Read(ref disposed) == 0;
            public void Dispose() => Interlocked.Exchange(ref disposed, 1);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
        public TaskCompletionSource<Timer> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromSeconds(20), dueTime);
            var timer = new Timer(callback, state); Created.TrySetResult(timer); return timer;
        }
    }

    [Theory]
    [InlineData(false, 2000)]
    [InlineData(false, -1)]
    [InlineData(true, 2000)]
    public async Task Selected_event_deadlines_back_off_dead_letter_and_allow_later_work_but_host_shutdown_does_not_record_failure(bool shutdown, int cancellationTimeout)
    {
        await using var d = await Database(); await DeploymentRunner.MigrateAsync(d.Connection, true);
        var p = Manifest(); await DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(p), true);
        var first = await d.Scalar<Guid>("SELECT \"Id\" FROM \"OutboxEvents\"");
        var later = Guid.NewGuid(); var lockKey = Random.Shared.Next(1, int.MaxValue);
        await d.Execute($"INSERT INTO \"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES ('{later}','{p.TenantId}','{p.OrganizationId}','{p.OrganizationId}','Organization.Revised',1,now()); UPDATE \"SearchDeliveries\" SET \"NextAttemptAt\"=now()-interval '1 hour' WHERE \"EventId\"='{first}'");
        await d.Execute($"CREATE FUNCTION pause_completion() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"EventId\"='{first}' AND NEW.\"CompletedAt\" IS NOT NULL THEN PERFORM pg_advisory_xact_lock({lockKey}); END IF; RETURN NEW; END $$; CREATE TRIGGER pause_completion BEFORE INSERT OR UPDATE ON \"SearchDeliveries\" FOR EACH ROW EXECUTE FUNCTION pause_completion()");
        await using var blocker = new NpgsqlConnection(d.Admin); await blocker.OpenAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_lock({lockKey})", blocker)) await command.ExecuteNonQueryAsync();
        var appName = "saveit-deadline-" + Guid.NewGuid().ToString("N");
        var worker = new NpgsqlConnectionStringBuilder(d.Worker) { ApplicationName = appName, CancellationTimeout = cancellationTimeout, MaxPoolSize = 1 }.ConnectionString;
        for (var attempt = 1; attempt <= (shutdown ? 1 : 5); attempt++)
        {
            var clock = new DeadlineClock(); using var stop = new CancellationTokenSource();
            var processing = new SearchProcessor(worker, clock, true).ProcessBatchAsync(p.TenantId, stop.Token);
            try
            {
                var timer = await clock.Created.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var waiting = false;
                for (var check = 0; check < 100; check++)
                {
                    waiting = await d.Scalar<bool>($"SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE application_name='{appName}' AND wait_event='advisory')");
                    if (waiting) break; await Task.Delay(20);
                }
                Assert.True(waiting, "Worker must reach the selected-event completion lock before cancellation.");
                if (shutdown) stop.Cancel(); else timer.Fire();
                if (shutdown)
                {
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing.WaitAsync(TimeSpan.FromSeconds(10)));
                    Assert.Equal(0, await d.Scalar<int>($"SELECT \"Attempts\" FROM \"SearchDeliveries\" WHERE \"EventId\"='{first}'"));
                }
                else
                {
                    var result = await processing.WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.Equal(attempt == 5 ? 1 : 0, result.DeadLettered);
                    Assert.Equal(attempt == 5 ? 0 : 1, result.Retried);
                    Assert.True(await d.Scalar<bool>($"SELECT \"Attempts\"={attempt} AND \"CompletedAt\" IS NULL AND \"NextAttemptAt\">now() AND \"ErrorCode\"='projection_deadline' FROM \"SearchDeliveries\" WHERE \"EventId\"='{first}'"));
                    await new SearchProcessor(worker, requireSchema: true).ProcessBatchAsync(p.TenantId);
                    Assert.True(await d.Scalar<bool>($"SELECT \"CompletedAt\" IS NOT NULL FROM \"SearchDeliveries\" WHERE \"EventId\"='{later}'"));
                    await d.Execute($"UPDATE \"SearchDeliveries\" SET \"NextAttemptAt\"=now()-interval '1 second' WHERE \"EventId\"='{first}'");
                }
            }
            finally { stop.Cancel(); try { await processing.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { } }
        }
        if (!shutdown)
        {
            Assert.True(await d.Scalar<bool>($"SELECT \"DeadLetteredAt\" IS NOT NULL FROM \"SearchDeliveries\" WHERE \"EventId\"='{first}'"));
            await using (var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({lockKey})", blocker)) await command.ExecuteNonQueryAsync();
            var processor = new SearchProcessor(worker, requireSchema: true);
            Assert.True(await processor.ReplayAsync(p.TenantId, first)); Assert.Equal(1, (await processor.ProcessBatchAsync(p.TenantId)).Completed);
        }
    }

    [Fact]
    public async Task Health_checks_fail_closed_on_dependency_and_forced_rls_failure_without_exposing_details()
    {
        using var factory = new ApiFactory(fixture.AppConnection); using var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/ready")).StatusCode);
        await fixture.Execute("ALTER TABLE \"DocumentTemplates\" NO FORCE ROW LEVEL SECURITY");
        try { var r = await c.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, r.StatusCode); Assert.Equal("{\"status\":\"unavailable\"}", await r.Content.ReadAsStringAsync()); Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health/live")).StatusCode); }
        finally { await fixture.Execute("ALTER TABLE \"DocumentTemplates\" FORCE ROW LEVEL SECURITY"); }
    }

    [Fact]
    public async Task Deferred_receipt_commit_failure_records_backoff_on_single_connection_pool_and_recovers()
    {
        await using var d = await Database(); await DeploymentRunner.MigrateAsync(d.Connection, true);
        var p = Manifest(); await DeploymentRunner.ProvisionAsync(d.Connection, JsonSerializer.Serialize(p), true);
        await d.Execute("CREATE FUNCTION fail_completion_commit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"CompletedAt\" IS NOT NULL THEN RAISE EXCEPTION 'Synthetic deferred receipt failure'; END IF; RETURN NEW; END $$; CREATE CONSTRAINT TRIGGER fail_completion_commit AFTER INSERT OR UPDATE ON \"SearchDeliveries\" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION fail_completion_commit()");
        var worker = new NpgsqlConnectionStringBuilder(d.Worker) { MaxPoolSize = 1, ApplicationName = "saveit-deferred-" + Guid.NewGuid().ToString("N") }.ConnectionString;
        var processor = new SearchProcessor(worker, requireSchema: true);
        Assert.Equal(1, (await processor.ProcessBatchAsync(p.TenantId).WaitAsync(TimeSpan.FromSeconds(10))).Retried);
        Assert.True(await d.Scalar<bool>("SELECT \"Attempts\"=1 AND \"CompletedAt\" IS NULL AND \"NextAttemptAt\">now() AND \"ErrorCode\"='projection_database_failure' FROM \"SearchDeliveries\""));
        await d.Execute("DROP TRIGGER fail_completion_commit ON \"SearchDeliveries\"; DROP FUNCTION fail_completion_commit(); UPDATE \"SearchDeliveries\" SET \"NextAttemptAt\"=now()-interval '1 second'");
        Assert.Equal(1, (await processor.ProcessBatchAsync(p.TenantId)).Completed);
    }
}
