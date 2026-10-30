using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SaveIT.Api.Assets;
using SaveIT.Api.Data;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class AssetLocationHistoryTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static string Root(string resource, Guid? org = null) => $"/api/v1/organizations/{org ?? DevelopmentSeed.OrgA}/{resource}";
    private sealed record Csrf(string Token);
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object input, bool put = false)
    {
        var csrf = (await c.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        using var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        r.Headers.Add("X-CSRF-TOKEN", csrf.Token); return await c.SendAsync(r);
    }
    private static async Task<HttpClient> Login(ApiFactory f, string profile = "alice")
    {
        var c = f.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile })).StatusCode); return c;
    }
    private static async Task<AssetDto> Asset(HttpClient c)
    {
        var r = await Write(c, Root("assets"), new CreateAssetRequest("Pinned synthetic asset", "Server"));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetDto>())!;
    }
    private static DirectoryRequest Location(string name, int version = 0, bool archived = false) => new(name, "", "", "", "", archived, version);
    private static async Task<DirectoryDto> Place(HttpClient c, string name, Guid? org = null)
    {
        var r = await Write(c, Root("locations", org), Location(name)); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<DirectoryDto>())!;
    }
    private static string Path(AssetDto a) => Root("assets") + "/" + a.Id;
    private static async Task<AssetRevisionDto[]> History(HttpClient c, AssetDto a) =>
        (await c.GetFromJsonAsync<CursorPage<AssetRevisionDto>>(Path(a) + "/revisions"))!.Items.ToArray();
    private static Task<HttpResponseMessage> Revise(HttpClient c, AssetDto a, Guid? location, int version) =>
        Write(c, Path(a), new ReviseAssetRequest("Pinned synthetic asset", "Server", location, version), true);

    [Fact]
    public async Task Rename_archive_and_field_edits_capture_names_while_restore_copies_exact_pin_without_renaming_location()
    {
        using var c = await Login(factory); var a = await Asset(c);
        const string oldName = "Server room <img src=x> 😀"; const string newName = "Current data center";
        var p = await Place(c, oldName);
        Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Root("locations") + "/" + p.Id, Location(newName, 1, true), true)).StatusCode);
        // Keeping an archived placement during an unrelated field edit is allowed and must still capture its name.
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(a) + "/fields", new ReviseAssetFieldsRequest(2, null, null, AssetSchemas.Json("{}")), true)).StatusCode);
        var rows = await History(c, a); Assert.Equal(newName, rows[0].LocationName); Assert.Equal(oldName, rows[1].LocationName); Assert.Null(rows[2].LocationName);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(c, Path(a) + "/restore", new RestoreDocumentRequest(2, 3, "Restore original placement"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Root("locations") + "/" + p.Id, Location(newName, 2), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(a) + "/restore", new RestoreDocumentRequest(2, 3, "Restore original placement"))).StatusCode);
        rows = await History(c, a); Assert.Equal(oldName, rows[0].LocationName); Assert.Equal(2, rows[0].RestoredFromVersion); Assert.Equal(p.Id, rows[0].LocationId);
        Assert.Equal(oldName, rows.Single(x => x.Version == 2).LocationName);
        Assert.Equal(newName, (await c.GetFromJsonAsync<DirectoryDto>(Root("locations") + "/" + p.Id))!.Name);
        var comparison = (await c.GetFromJsonAsync<AssetComparisonDto>(Path(a) + "/compare?fromVersion=2&toVersion=4&changesOnly=false"))!;
        Assert.All(comparison.Rows, x => Assert.Equal("unchanged", x.Change));
        comparison = (await c.GetFromJsonAsync<AssetComparisonDto>(Path(a) + "/compare?fromVersion=2&toVersion=3"))!;
        var row = Assert.Single(comparison.Rows); Assert.Equal("locationName", row.Key); Assert.Equal(oldName, row.From.Value!.Value.GetString()); Assert.Equal(newName, row.To.Value!.Value.GetString());
        Assert.False(row.From.Unavailable); Assert.False(row.To.Unavailable);
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" e WHERE e.\"TargetId\"='{a.Id}' AND to_jsonb(e)::text LIKE '%Server room%'"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" e WHERE e.\"TargetId\"='{a.Id}' AND to_jsonb(e)::text LIKE '%Server room%'"));
        Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 4)).StatusCode);
        Assert.Equal(newName, (await History(c, a))[0].LocationName);
    }

    [Theory]
    [InlineData("AssetRevisions")]
    [InlineData("AuditEvents")]
    [InlineData("OutboxEvents")]
    [InlineData("SearchDeliveries")]
    public async Task Name_pins_and_all_related_writes_roll_back_at_each_boundary(string table)
    {
        using var c = await Login(factory); var a = await Asset(c); var p = await Place(c, "Private rollback name");
        Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, null, 2)).StatusCode);
        var totals = new List<long>(); foreach (var t in new[] { "AssetRevisions", "AuditEvents", "OutboxEvents", "SearchDeliveries" }) totals.Add(await fixture.Scalar<long>($"SELECT count(*) FROM \"{t}\""));
        await fixture.Execute($"CREATE FUNCTION fail_pin_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic pin rollback'; END $$; CREATE TRIGGER fail_pin_write BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_pin_write()");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Revise(c, a, p.Id, 3)).StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, (await Write(c, Path(a) + "/restore", new RestoreDocumentRequest(2, 3, "Rollback restored pin"))).StatusCode);
            Assert.Equal(3, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Assets\" WHERE \"Id\"='{a.Id}'"));
            var after = new List<long>(); foreach (var t in new[] { "AssetRevisions", "AuditEvents", "OutboxEvents", "SearchDeliveries" }) after.Add(await fixture.Scalar<long>($"SELECT count(*) FROM \"{t}\""));
            Assert.Equal(totals, after);
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_pin_write ON \"{table}\"; DROP FUNCTION fail_pin_write()"); }
    }

    [Fact]
    public async Task Capture_source_failure_rolls_back_and_current_scope_controls_history_and_comparison()
    {
        using var c = await Login(factory); var a = await Asset(c); var p = await Place(c, "Scoped private location");
        Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 1)).StatusCode);
        var foreign = await Place(c, "Other organization", DevelopmentSeed.OrgA2);
        Assert.Equal(HttpStatusCode.BadRequest, (await Revise(c, a, foreign.Id, 2)).StatusCode);
        await fixture.Execute("REVOKE SELECT ON \"Locations\" FROM saveit_app");
        try
        {
            // Unchanged placement passes the ordinary active-placement check and reaches the name capture.
            Assert.Equal(HttpStatusCode.InternalServerError, (await Revise(c, a, p.Id, 2)).StatusCode);
            Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Assets\" WHERE \"Id\"='{a.Id}'"));
            Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{a.Id}'"));
            Assert.Equal("Scoped private location", (await History(c, a))[0].LocationName);
            Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Path(a) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
        }
        finally { await fixture.Execute("GRANT SELECT ON \"Locations\" TO saveit_app"); }
        using var viewer = await Login(factory, "viewer"); using var bob = await Login(factory, "bob");
        Assert.Equal("Scoped private location", (await History(viewer, a))[0].LocationName);
        foreach (var suffix in new[] { "/revisions", "/compare?fromVersion=1&toVersion=2" })
        {
            var denied = await bob.GetAsync(Path(a) + suffix); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Assert.DoesNotContain("Scoped private", await denied.Content.ReadAsStringAsync());
        }
        var member = DevelopmentSeed.Profiles.Single(x => x.Id == "viewer").MembershipId;
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'");
        try { Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(Path(a) + "/revisions")).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(Path(a) + "/compare?fromVersion=1&toVersion=2")).StatusCode); }
        finally { await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{DevelopmentSeed.TenantA}','{member}','{DevelopmentSeed.OrgA}','Viewer')"); }
    }

    [Fact]
    public async Task Historical_name_reads_fail_closed_on_audit_failure()
    {
        using var c = await Login(factory); var a = await Asset(c); var p = await Place(c, "Private audited pin");
        Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 1)).StatusCode);
        await fixture.Execute("CREATE FUNCTION fail_pin_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" IN ('Asset.RevisionsRead','Asset.Compared') THEN RAISE EXCEPTION 'Synthetic pin audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_pin_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_pin_read()");
        try { foreach (var suffix in new[] { "/revisions", "/compare?fromVersion=1&toVersion=2" }) { var r = await c.GetAsync(Path(a) + suffix); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); var text = await r.Content.ReadAsStringAsync(); Assert.DoesNotContain("Private audited pin", text); Assert.DoesNotContain("nextCursor", text); Assert.DoesNotContain("nextOffset", text); } }
        finally { await fixture.Execute("DROP TRIGGER fail_pin_read ON \"AuditEvents\"; DROP FUNCTION fail_pin_read()"); }
    }

    [Fact]
    public async Task Migration_preserves_old_snapshots_as_unknown_and_restore_does_not_fill_from_current_name()
    {
        var database = "saveit_pin_migration_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE DATABASE \"{database}\" ENCODING 'UTF8' TEMPLATE template0 LC_COLLATE 'C' LC_CTYPE 'C'");
        var connection = new NpgsqlConnectionStringBuilder(fixture.OwnerConnection) { Database = database }.ConnectionString;
        try
        {
            var scope = new TenantScope(); await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(connection).Options, scope);
            await db.Database.MigrateAsync(); await DevelopmentSeed.SeedAsync(db, scope);
            var appConnection = new NpgsqlConnectionStringBuilder(connection) { Username = "saveit_app", Password = "synthetic-app-only" }.ConnectionString;
            using var isolatedFactory = new ApiFactory(appConnection); using var c = await Login(isolatedFactory); var a = await Asset(c); var p = await Place(c, "Current misleading name");
            Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 1)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, null, 2)).StatusCode);
            var before = await History(c, a);
            await db.GetService<IMigrator>().MigrateAsync("20261030034105_DirectoryRevisionHistory");
            // The predecessor schema has genuine placed history but no name column.
            await db.Database.MigrateAsync();
            var after = await History(c, a); Assert.All(after, x => Assert.Null(x.LocationName));
            Assert.Equal(JsonSerializer.Serialize(before.Select(x => x with { LocationName = null })), JsonSerializer.Serialize(after));
            var unknown = (await c.GetFromJsonAsync<AssetComparisonDto>(Path(a) + "/compare?fromVersion=2&toVersion=3"))!.Rows.Single(x => x.Key == "locationName");
            Assert.True(unknown.From.Present); Assert.True(unknown.From.Unavailable); Assert.Null(unknown.From.Value); Assert.False(unknown.To.Present); Assert.False(unknown.To.Unavailable);
            Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(a) + "/restore", new RestoreDocumentRequest(2, 3, "Preserve unknown name"))).StatusCode);
            Assert.Null((await History(c, a))[0].LocationName);
            Assert.Equal(HttpStatusCode.OK, (await Revise(c, a, p.Id, 4)).StatusCode);
            Assert.Equal("Current misleading name", (await History(c, a))[0].LocationName);
        }
        finally { NpgsqlConnection.ClearAllPools(); await fixture.Execute($"DROP DATABASE \"{database}\" WITH (FORCE)"); }
    }

    [Fact]
    public async Task Database_enforces_name_bounds_scope_append_only_and_worker_column_read_denial()
    {
        using var c = await Login(factory); var a = await Asset(c); var p = await Place(c, "Protected pin");
        async Task Denied(string sql, string code, string? connection = null)
        {
            await using var db = new NpgsqlConnection(connection ?? fixture.OwnerConnection); await db.OpenAsync();
            await using var tx = await db.BeginTransactionAsync();
            await using (var context = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantA}',true)", db, tx)) await context.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand(sql, db, tx);
            Assert.Equal(code, (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState);
        }
        string Insert(string location, string name) => $"INSERT INTO \"AssetRevisions\" (\"TenantId\",\"OrganizationId\",\"AssetId\",\"Version\",\"Name\",\"Kind\",\"LocationId\",\"LocationName\",\"Actor\",\"CreatedAt\") VALUES ('{DevelopmentSeed.TenantA}','{DevelopmentSeed.OrgA}','{a.Id}',2,'Synthetic','Server',{location},'{name}','test',CURRENT_TIMESTAMP)";
        await Denied(Insert("NULL", "Orphan name"), "23514");
        await Denied(Insert($"'{p.Id}'", "   "), "23514");
        await Denied(Insert($"'{p.Id}'", new string('x', 161)), "22001");
        var foreign = await Place(c, "Foreign scoped pin", DevelopmentSeed.OrgA2);
        await Denied(Insert($"'{foreign.Id}'", "Foreign scoped pin"), "23503");
        await Denied($"UPDATE \"AssetRevisions\" SET \"LocationName\"='Rewrite' WHERE \"AssetId\"='{a.Id}'", "42501", fixture.AppConnection);
        await Denied("SELECT \"LocationName\" FROM \"AssetRevisions\"", "42501", fixture.WorkerConnection);
        await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync(); await using var transaction = await app.BeginTransactionAsync();
        await using (var context = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantB}',true)", app, transaction)) await context.ExecuteNonQueryAsync();
        await using var read = new NpgsqlCommand($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{a.Id}'", app, transaction);
        Assert.Equal(0L, await read.ExecuteScalarAsync());
    }

    [Fact]
    public async Task History_pins_remain_bounded_and_comparison_distinguishes_unknown_unassigned_and_known_names()
    {
        using var c = await Login(factory); var a = await Asset(c); var p = await Place(c, "Paged historical name");
        await fixture.Execute($"INSERT INTO \"AssetRevisions\" (\"TenantId\",\"OrganizationId\",\"AssetId\",\"Version\",\"Name\",\"Kind\",\"LocationId\",\"LocationName\",\"Actor\",\"CreatedAt\") SELECT '{DevelopmentSeed.TenantA}','{DevelopmentSeed.OrgA}','{a.Id}',n,'Synthetic','Server','{p.Id}',CASE WHEN n=2 THEN NULL ELSE 'Paged historical name' END,'test',CURRENT_TIMESTAMP FROM generate_series(2,56) n; UPDATE \"Assets\" SET \"Version\"=56,\"LocationId\"='{p.Id}' WHERE \"Id\"='{a.Id}'");
        var page = (await c.GetFromJsonAsync<CursorPage<AssetRevisionDto>>(Path(a) + "/revisions"))!;
        Assert.Equal(50, page.Items.Count); Assert.All(page.Items, x => Assert.Equal("Paged historical name", x.LocationName)); Assert.Equal("7", page.NextCursor);
        var tail = (await c.GetFromJsonAsync<CursorPage<AssetRevisionDto>>(Path(a) + "/revisions?before=" + page.NextCursor))!;
        Assert.Equal(6, tail.Items.Count); Assert.Null(tail.NextCursor);
        async Task<AssetComparisonRowDto> Compare(int from, int to) => (await c.GetFromJsonAsync<AssetComparisonDto>(Path(a) + $"/compare?fromVersion={from}&toVersion={to}&changesOnly=false"))!.Rows.Single(x => x.Key == "locationName");
        var unknown = await Compare(2, 2); Assert.True(unknown.From.Unavailable); Assert.Equal("unchanged", unknown.Change);
        var known = await Compare(2, 3); Assert.True(known.From.Unavailable); Assert.False(known.To.Unavailable); Assert.Equal("changed", known.Change); Assert.Equal("Paged historical name", known.To.Value!.Value.GetString());
        var absent = await Compare(1, 2); Assert.False(absent.From.Present); Assert.False(absent.From.Unavailable); Assert.True(absent.To.Present); Assert.True(absent.To.Unavailable); Assert.Equal("changed", absent.Change);
    }

    public void Dispose() => factory.Dispose();
}
