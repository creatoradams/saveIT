using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class OrganizationHistoryTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private readonly HashSet<Guid> created = [];
    private static readonly Guid Tenant = DevelopmentSeed.TenantA, Alice = DevelopmentSeed.Profiles[0].MembershipId, Viewer = DevelopmentSeed.Profiles[1].MembershipId;
    private sealed record Csrf(string Token);
    private static string Path(Guid id) => $"/api/v1/organizations/{id}";
    private async Task<HttpClient> Login(string profile = "alice", ApiFactory? f = null)
    {
        var c = (f ?? factory).CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile })).StatusCode); return c;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object input, bool put = false)
    {
        var csrf = (await c.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        using var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        r.Headers.Add("X-CSRF-TOKEN", csrf.Token); return await c.SendAsync(r);
    }
    private async Task<OrganizationDto> Create(HttpClient c, string? name = null)
    {
        var r = await Write(c, "/api/v1/organizations", new { name = name ?? "ZZZ history " + Guid.NewGuid().ToString("N"), actor = "forged actor", origin = "Migration" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); var row = (await r.Content.ReadFromJsonAsync<OrganizationDto>())!; created.Add(row.Id); return row;
    }
    private static async Task<CursorPage<OrganizationRevisionDto>> History(HttpClient c, Guid id, string extra = "") =>
        (await c.GetFromJsonAsync<CursorPage<OrganizationRevisionDto>>(Path(id) + "/revisions" + extra))!;
    private static void Withheld(string text)
    {
        foreach (var value in new[] { "Confidential", "Renamed", "nextCursor", "items", "actor", "Migration" }) Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_and_rename_capture_actual_names_actors_times_without_rewriting_or_exposing_names_in_events()
    {
        using var c = await Login(); var row = await Create(c, "Confidential <img src=x> 😀");
        var first = Assert.Single((await History(c, row.Id)).Items);
        Assert.Equal(row.Name, first.Name); Assert.Equal(row.UpdatedAt, first.CreatedAt); Assert.Equal("Created", first.Origin); Assert.Equal("alice", first.Actor); Assert.Equal(1, first.Version);
        var response = await Write(c, Path(row.Id), new { name = "Renamed synthetic organization", expectedVersion = 1, actor = "forged actor", origin = "Seed" }, true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var current = (await response.Content.ReadFromJsonAsync<OrganizationDto>())!;
        var rows = (await History(c, row.Id)).Items;
        Assert.Equal(new[] { 2, 1 }, rows.Select(x => x.Version)); Assert.Equal(first, rows[1]); Assert.Equal(current.Name, rows[0].Name); Assert.Equal(current.UpdatedAt, rows[0].CreatedAt);
        Assert.Equal("alice", rows[0].Actor); Assert.Equal("Renamed", rows[0].Origin);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{row.Id}'"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{row.Id}'"));
        foreach (var table in new[] { "AuditEvents", "OutboxEvents" })
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{table}\" e WHERE e.\"OrganizationId\"='{row.Id}' AND (to_jsonb(e)::text LIKE '%Confidential%' OR to_jsonb(e)::text LIKE '%Renamed synthetic%')"));
    }

    [Theory]
    [InlineData("OrganizationRevisions", false)]
    [InlineData("OrganizationRevisions", true)]
    [InlineData("AuditEvents", false)]
    [InlineData("AuditEvents", true)]
    [InlineData("OutboxEvents", false)]
    [InlineData("OutboxEvents", true)]
    [InlineData("SearchDeliveries", false)]
    [InlineData("SearchDeliveries", true)]
    public async Task Failure_at_each_boundary_rolls_back_history_head_creator_grant_audit_outbox_and_queue(string boundary, bool rename)
    {
        using var c = await Login(); var row = rename ? await Create(c) : null;
        var tables = new[] { "Organizations", "OrganizationGrants", "OrganizationRevisions", "AuditEvents", "OutboxEvents", "SearchDeliveries" };
        var before = new List<long>(); foreach (var t in tables) before.Add(await fixture.Scalar<long>($"SELECT count(*) FROM \"{t}\""));
        await fixture.Execute($"CREATE FUNCTION fail_org_history() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic organization history failure'; END $$; CREATE TRIGGER fail_org_history BEFORE INSERT ON \"{boundary}\" FOR EACH ROW EXECUTE FUNCTION fail_org_history()");
        try
        {
            var r = await Write(c, "/api/v1/organizations" + (rename ? "/" + row!.Id : ""), new { name = "Confidential failed operation", expectedVersion = 1 }, rename);
            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode);
            var after = new List<long>(); foreach (var t in tables) after.Add(await fixture.Scalar<long>($"SELECT count(*) FROM \"{t}\""));
            Assert.Equal(before, after);
            if (row is not null) Assert.Equal(row.Name, await fixture.Scalar<string>($"SELECT \"Name\" FROM \"Organizations\" WHERE \"Id\"='{row.Id}' AND \"Version\"=1"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_org_history ON \"{boundary}\"; DROP FUNCTION fail_org_history()"); }
    }

    [Fact]
    public async Task Bounded_pages_survive_concurrent_new_revisions_and_recheck_current_viewer_grants()
    {
        using var c = await Login(); var row = await Create(c);
        for (var version = 1; version < 56; version++) Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(row.Id), new { name = "ZZZ history version " + (version + 1), expectedVersion = version }, true)).StatusCode);
        var page = await History(c, row.Id); Assert.Equal(50, page.Items.Count); Assert.Equal("7", page.NextCursor); Assert.Equal(56, page.Items[0].Version);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(row.Id), new { name = "ZZZ history version 57", expectedVersion = 56 }, true)).StatusCode);
        var tail = await History(c, row.Id, "?before=" + page.NextCursor); Assert.Equal(new[] { 6, 5, 4, 3, 2, 1 }, tail.Items.Select(x => x.Version)); Assert.Null(tail.NextCursor);
        using var viewer = await Login("viewer");
        await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{Tenant}','{Viewer}','{row.Id}','Viewer')");
        Assert.Equal(50, (await History(viewer, row.Id)).Items.Count);
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{row.Id}' AND \"MembershipId\"='{Viewer}'");
        var denied = await viewer.GetAsync(Path(row.Id) + "/revisions?before=7"); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Withheld(await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reads_require_current_membership_and_org_grant_but_not_administrator_capability()
    {
        using var alice = await Login(); var row = await Create(alice, "Confidential role fixture"); using var viewer = await Login("viewer"); using var bob = await Login("bob"); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(Path(row.Id) + "/revisions")).StatusCode);
        bob.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant.ToString()); Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Path(row.Id) + "/revisions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(row.Id) + "/revisions")).StatusCode);
        await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{Tenant}','{Viewer}','{row.Id}','Viewer'); UPDATE \"Memberships\" SET \"CanManageOrganizations\"=false WHERE \"Id\"='{Alice}'");
        try
        {
            Assert.Single((await History(viewer, row.Id)).Items); Assert.Single((await History(alice, row.Id)).Items);
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(alice, Path(row.Id), new { name = "No capability", expectedVersion = 1 }, true)).StatusCode);
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{Viewer}'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync(Path(row.Id) + "/revisions")).StatusCode);
        }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"CanManageOrganizations\"=true WHERE \"Id\"='{Alice}'; UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{Viewer}'"); }
    }

    [Theory]
    [InlineData("source")]
    [InlineData("current")]
    [InlineData("audit")]
    public async Task Actual_source_current_and_audit_failures_withhold_names_and_continuation(string boundary)
    {
        using var c = await Login(); var row = await Create(c, "Confidential history source"); var table = boundary == "current" ? "Organizations" : "OrganizationRevisions";
        if (boundary == "audit") await fixture.Execute("CREATE FUNCTION fail_org_history_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='Organization.RevisionsRead' THEN RAISE EXCEPTION 'Synthetic audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_org_history_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_org_history_read()");
        else await fixture.Execute($"REVOKE SELECT ON \"{table}\" FROM saveit_app");
        try { var r = await c.GetAsync(Path(row.Id) + "/revisions"); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        finally
        {
            if (boundary == "audit") await fixture.Execute("DROP TRIGGER fail_org_history_read ON \"AuditEvents\"; DROP FUNCTION fail_org_history_read()");
            else await fixture.Execute($"GRANT SELECT ON \"{table}\" TO saveit_app");
        }
        Assert.Single((await History(c, row.Id)).Items);
    }

    [Fact]
    public async Task Concurrent_renames_append_one_winner_and_invalid_history_cursors_fail_without_fields()
    {
        using var c = await Login(); var row = await Create(c);
        var race = await Task.WhenAll(Write(c, Path(row.Id), new { name = "ZZZ first winner", expectedVersion = 1 }, true), Write(c, Path(row.Id), new { name = "ZZZ second winner", expectedVersion = 1 }, true));
        Assert.Single(race, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict);
        var rows = (await History(c, row.Id)).Items; Assert.Equal(2, rows.Count); Assert.Equal(await fixture.Scalar<string>($"SELECT \"Name\" FROM \"Organizations\" WHERE \"Id\"='{row.Id}'"), rows[0].Name);
        foreach (var cursor in new[] { "0", "-1", "bad", "2147483648" }) { var r = await c.GetAsync(Path(row.Id) + "/revisions?before=" + cursor); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        var empty = await History(c, row.Id, "?before=1"); Assert.Empty(empty.Items); Assert.Null(empty.NextCursor);
    }

    [Fact]
    public async Task Database_history_scope_constraints_and_select_only_runtime_permissions_reject_bypasses()
    {
        using var c = await Login(); var row = await Create(c);
        await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync();
        await using (var read = new NpgsqlCommand("SELECT count(*) FROM \"OrganizationRevisions\"", app)) Assert.Equal(0L, await read.ExecuteScalarAsync());
        Assert.True(await fixture.Scalar<bool>("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='\"OrganizationRevisions\"'::regclass"));
        foreach (var sql in new[] { "INSERT INTO \"OrganizationRevisions\" DEFAULT VALUES", "UPDATE \"OrganizationRevisions\" SET \"Name\"='Rewrite'", "DELETE FROM \"OrganizationRevisions\"" })
        { await using var command = new NpgsqlCommand(sql, app); Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState); }
        string Insert(Guid tenant, int version, string name, string actor, string origin) => $"INSERT INTO \"OrganizationRevisions\" (\"TenantId\",\"OrganizationId\",\"Version\",\"Name\",\"Actor\",\"Origin\",\"CreatedAt\") VALUES ('{tenant}','{row.Id}',{version},'{name}','{actor}','{origin}',CURRENT_TIMESTAMP)";
        foreach (var (sql, state) in new[] { (Insert(DevelopmentSeed.TenantB, 2, "Synthetic", "test", "Created"), "23503"), (Insert(Tenant, 0, "Synthetic", "test", "Created"), "23514"), (Insert(Tenant, 2, " ", "test", "Created"), "23514"), (Insert(Tenant, 2, "Synthetic", "", "Created"), "23514"), (Insert(Tenant, 2, "Synthetic", "test", "Unsupported"), "23514") })
            Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(() => fixture.Execute(sql))).SqlState);
        await using var tx = await app.BeginTransactionAsync();
        await using (var context = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantB}',true),set_config('app.membership_id','{Alice}',true)", app, tx)) await context.ExecuteNonQueryAsync();
        await using (var hidden = new NpgsqlCommand($"SELECT count(*) FROM \"OrganizationRevisions\" WHERE \"OrganizationId\"='{row.Id}'", app, tx)) Assert.Equal(0L, await hidden.ExecuteScalarAsync());
        await using var spoof = new NpgsqlCommand($"SELECT public.rename_organization('{row.Id}','Spoofed','{Alice}',1)", app, tx);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => spoof.ExecuteNonQueryAsync())).SqlState);
    }

    [Theory]
    [InlineData("SELECT (\"Name\")", "direct")]
    [InlineData("SELECT (\"Actor\")", "public")]
    [InlineData("SELECT (\"Name\")", "indirect")]
    [InlineData("INSERT (\"Name\")", "direct")]
    [InlineData("UPDATE (\"Name\")", "public")]
    [InlineData("UPDATE (\"Name\")", "indirect")]
    public async Task Worker_startup_rejects_reachable_history_reads_and_core_column_writes(string privilege, string mode)
    {
        var role = mode == "indirect" ? "saveit_org_history_" + Guid.NewGuid().ToString("N") : mode == "public" ? "PUBLIC" : "saveit_search_worker";
        if (mode == "indirect") await fixture.Execute($"CREATE ROLE {role} NOLOGIN NOINHERIT; GRANT {role} TO saveit_search_worker WITH INHERIT FALSE");
        await fixture.Execute($"GRANT {privilege} ON \"OrganizationRevisions\" TO {role}");
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchProcessor(fixture.WorkerConnection).ValidateAsync()); }
        finally { await fixture.Execute($"REVOKE {privilege} ON \"OrganizationRevisions\" FROM {role}"); if (mode == "indirect") await fixture.Execute($"REVOKE {role} FROM saveit_search_worker; DROP ROLE {role}"); }
        await new SearchProcessor(fixture.WorkerConnection).ValidateAsync();
    }

    [Fact]
    public async Task Api_startup_rejects_reachable_ownership_of_new_history_table()
    {
        var role = "saveit_history_owner_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE ROLE {role} NOLOGIN NOINHERIT; ALTER TABLE \"OrganizationRevisions\" OWNER TO {role}; GRANT {role} TO saveit_app WITH INHERIT FALSE");
        try { await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(app)); }
        finally { await fixture.Execute($"ALTER TABLE \"OrganizationRevisions\" OWNER TO postgres; REVOKE {role} FROM saveit_app; DROP ROLE {role}"); }
    }

    [Fact]
    public async Task Migration_records_actual_current_version_and_seed_is_idempotent_with_function_safe_downgrade()
    {
        var database = "saveit_org_history_migration_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE DATABASE \"{database}\" ENCODING 'UTF8' TEMPLATE template0 LC_COLLATE 'C' LC_CTYPE 'C'");
        var connection = new NpgsqlConnectionStringBuilder(fixture.OwnerConnection) { Database = database }.ConnectionString;
        try
        {
            var scope = new TenantScope(); await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(connection).Options, scope);
            await db.Database.MigrateAsync(); await DevelopmentSeed.SeedAsync(db, scope); scope.TenantId = Tenant;
            Assert.All(await db.OrganizationRevisions.ToListAsync(), x => { Assert.Equal("Seed", x.Origin); Assert.Equal("seed", x.Actor); });
            await db.GetService<IMigrator>().MigrateAsync("20261030125820_AssetRevisionLocationNames");
            var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero); var id = DevelopmentSeed.OrgA;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Organizations\" SET \"Version\"=7,\"Name\"='Actual historical start',\"UpdatedAt\"={at} WHERE \"Id\"={id}");
            await db.Database.MigrateAsync();
            var snapshot = await db.OrganizationRevisions.SingleAsync(x => x.OrganizationId == id);
            Assert.Equal(7, snapshot.Version); Assert.Equal("Actual historical start", snapshot.Name); Assert.Equal(at, snapshot.CreatedAt); Assert.Equal("Migration", snapshot.Origin); Assert.Equal("migration", snapshot.Actor);
            await DevelopmentSeed.SeedAsync(db, scope); scope.TenantId = Tenant;
            Assert.Equal(1, await db.OrganizationRevisions.CountAsync(x => x.OrganizationId == id));
            using var isolated = new ApiFactory(new NpgsqlConnectionStringBuilder(connection) { Username = "saveit_app", Password = "synthetic-app-only" }.ConnectionString); using var c = await Login(f: isolated);
            Assert.Equal(7, Assert.Single((await History(c, id)).Items).Version); Assert.Empty((await History(c, id, "?before=7")).Items);
            await db.GetService<IMigrator>().MigrateAsync("20261030125820_AssetRevisionLocationNames");
            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.tenant_id',{Tenant.ToString()},true),set_config('app.membership_id',{Alice.ToString()},true)");
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT public.rename_organization({id},'Works after downgrade',{Alice},7)"); await tx.CommitAsync();
            }
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            var after = await db.OrganizationRevisions.SingleAsync(x => x.OrganizationId == id); Assert.Equal(8, after.Version); Assert.Equal("Migration", after.Origin);
        }
        finally { NpgsqlConnection.ClearAllPools(); await fixture.Execute($"DROP DATABASE \"{database}\" WITH (FORCE)"); }
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        foreach (var id in created) await fixture.Execute($"DELETE FROM \"SearchDeliveries\" WHERE \"EventId\" IN (SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{id}'); DELETE FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"AuditEvents\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"OrganizationRevisions\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"Organizations\" WHERE \"Id\"='{id}'");
    }
}
