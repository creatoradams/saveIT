using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class DirectoryTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static readonly Guid Org = DevelopmentSeed.OrgA;
    private static string Root(string resource, Guid? org = null) => $"/api/v1/organizations/{org ?? Org}/{resource}";
    private sealed record Csrf(string Token);
    private static DirectoryRequest Input(string name = "Synthetic directory", int version = 0, bool archived = false) => new(name, "Synthetic details", "", "", "Synthetic notes", archived, version);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var client = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, "/api/v1/dev/login", new { profile })).StatusCode); return client;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, object body, bool put = false)
    {
        var csrf = (await client.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token); return await client.SendAsync(request);
    }
    private static async Task<DirectoryDto> Create(HttpClient client, string resource, Guid? org = null)
    {
        var response = await Write(client, Root(resource, org), Input()); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DirectoryDto>())!;
    }
    private static async Task<AssetDto> Asset(HttpClient client)
    {
        var response = await Write(client, Root("assets"), new { name = "Original asset", kind = "Network" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Persist_edit_archive_and_reactivate_directory_with_current_scope_and_versions(string resource)
    {
        using var client = await Login(); var row = await Create(client, resource); var path = Root(resource) + "/" + row.Id;
        Assert.Contains((await client.GetFromJsonAsync<CursorPage<DirectoryDto>>(Root(resource)))!.Items, x => x.Id == row.Id);
        var edited = await Write(client, path, Input("Revised name", 1, true), true); Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var dto = (await edited.Content.ReadFromJsonAsync<DirectoryDto>())!; Assert.Equal(2, dto.Version); Assert.True(dto.IsArchived);
        Assert.DoesNotContain((await client.GetFromJsonAsync<CursorPage<DirectoryDto>>(Root(resource)))!.Items, x => x.Id == row.Id);
        Assert.Contains((await client.GetFromJsonAsync<CursorPage<DirectoryDto>>(Root(resource) + "?includeArchived=true"))!.Items, x => x.Id == row.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(client, path, Input("Stale", 1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path, Input("Reactivated", 2), true)).StatusCode);
        using var fresh = await Login(); Assert.Equal("Reactivated", (await fresh.GetFromJsonAsync<DirectoryDto>(path))!.Name);
        using var viewer = await Login("viewer"); using var bob = await Login("bob");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Root(resource), Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, path, Input("No", 3), true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Root(resource, DevelopmentSeed.OrgA2) + "/" + row.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(bob, path, Input("No", 3), true)).StatusCode);
    }

    [Theory]
    [InlineData("locations", "AuditEvents")]
    [InlineData("locations", "OutboxEvents")]
    [InlineData("contacts", "AuditEvents")]
    [InlineData("contacts", "OutboxEvents")]
    public async Task Directory_changes_are_atomic_and_reads_fail_closed(string resource, string boundary)
    {
        using var client = await Login(); var row = await Create(client, resource);
        var prefix = resource == "locations" ? "Location." : "Contact."; var field = boundary == "AuditEvents" ? "Action" : "Type";
        await fixture.Execute($"CREATE FUNCTION fail_directory() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"{field}\" LIKE '{prefix}%' THEN RAISE EXCEPTION 'Synthetic directory failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_directory BEFORE INSERT ON \"{boundary}\" FOR EACH ROW EXECUTE FUNCTION fail_directory()");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Write(client, Root(resource) + "/" + row.Id, Input("Changed", 1), true)).StatusCode);
            var table = resource == "locations" ? "Locations" : "Contacts";
            Assert.Equal(1, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"{table}\" WHERE \"Id\"='{row.Id}'"));
            if (boundary == "AuditEvents") { var result = await client.GetAsync(Root(resource)); Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode); Assert.DoesNotContain(row.Name, await result.Content.ReadAsStringAsync()); }
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_directory ON \"{boundary}\"; DROP FUNCTION fail_directory()"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Concurrent_edits_have_one_winner_and_validation_and_revocation_hold(string resource)
    {
        using var client = await Login(); var row = await Create(client, resource); var path = Root(resource) + "/" + row.Id;
        var results = await Task.WhenAll(Write(client, path, Input("First", 1), true), Write(client, path, Input("Second", 1), true));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root(resource), Input(new string('x', 161)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root(resource), Input() with { Email = "invalid" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Root(resource), Input())).StatusCode);
        using var anon = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Root(resource))).StatusCode);
        var member = DevelopmentSeed.Profiles[0].MembershipId;
        try { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'"); Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode); }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'"); }
    }

    [Fact]
    public async Task Asset_edits_placement_history_restore_and_search_are_versioned_and_atomic()
    {
        using var client = await Login(); var loc = await Create(client, "locations"); var asset = await Asset(client); var path = Root("assets") + "/" + asset.Id;
        var term = "revised" + Guid.NewGuid().ToString("N");
        var response = await Write(client, path, new ReviseAssetRequest(term, "Server", loc.Id, 1), true); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<AssetDto>())!; Assert.Equal(2, updated.Version); Assert.Equal(loc.Id, updated.LocationId);
        var history = (await client.GetFromJsonAsync<CursorPage<AssetRevisionDto>>(path + "/revisions"))!;
        Assert.Equal(new[] { 2, 1 }, history.Items.Select(x => x.Version)); Assert.Equal("Original asset", history.Items[1].Name);
        var processor = new SearchProcessor(fixture.WorkerConnection); for (var i = 0; i < 3; i++) await processor.ProcessBatchAsync(DevelopmentSeed.TenantA);
        Assert.Contains((await client.GetFromJsonAsync<SearchPage>("/api/v1/search?q=" + term))!.Items, x => x.Id == asset.Id && x.Version == 2);
        response = await Write(client, path + "/restore", new RestoreDocumentRequest(1, 2, "Restore original inventory")); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var restored = (await response.Content.ReadFromJsonAsync<AssetDto>())!; Assert.Equal(3, restored.Version); Assert.Null(restored.LocationId); Assert.Equal(asset.Name, restored.Name);
        Assert.Empty((await client.GetFromJsonAsync<SearchPage>("/api/v1/search?q=" + term))!.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(client, path, new ReviseAssetRequest("Stale", "Network", null, 2), true)).StatusCode);
        history = (await client.GetFromJsonAsync<CursorPage<AssetRevisionDto>>(path + "/revisions"))!;
        Assert.Equal(1, history.Items[0].RestoredFromVersion); Assert.Equal("Restore original inventory", history.Items[0].RestoreReason);
        using var viewer = await Login("viewer"); using var bob = await Login("bob");
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, path, new ReviseAssetRequest("No", "Network", null, 3), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, path + "/restore", new RestoreDocumentRequest(1, 3, "No restore"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(path + "/revisions")).StatusCode);
        var foreign = await Create(client, "locations", DevelopmentSeed.OrgA2);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, path, new ReviseAssetRequest("No", "Network", foreign.Id, 3), true)).StatusCode);
    }

    [Theory]
    [InlineData("AuditEvents")]
    [InlineData("OutboxEvents")]
    public async Task Failed_asset_change_rolls_back_snapshot_and_audit_and_outbox(string table)
    {
        using var client = await Login(); var asset = await Asset(client); var field = table == "AuditEvents" ? "Action" : "Type";
        await fixture.Execute($"CREATE FUNCTION fail_asset_revision() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"{field}\"='Asset.Revised' THEN RAISE EXCEPTION 'Synthetic asset failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_asset_revision BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_asset_revision()");
        try { Assert.Equal(HttpStatusCode.InternalServerError, (await Write(client, Root("assets") + "/" + asset.Id, new ReviseAssetRequest("Changed", "Server", null, 1), true)).StatusCode); Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{asset.Id}'")); Assert.Equal(1, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Assets\" WHERE \"Id\"='{asset.Id}'")); }
        finally { await fixture.Execute($"DROP TRIGGER fail_asset_revision ON \"{table}\"; DROP FUNCTION fail_asset_revision()"); }
    }

    [Fact]
    public async Task Directory_raw_role_scope_fk_and_append_only_asset_history_are_enforced()
    {
        using var client = await Login(); var asset = await Asset(client); var foreign = await Create(client, "locations", DevelopmentSeed.OrgA2);
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        foreach (var table in new[] { "Locations", "Contacts", "AssetRevisions" })
        {
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection); Assert.Equal(0L, await cmd.ExecuteScalarAsync());
            Assert.True(await fixture.Scalar<bool>($"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='\"{table}\"'::regclass"));
        }
        foreach (var sql in new[] { "UPDATE \"AssetRevisions\" SET \"Name\"='No'", "DELETE FROM \"Locations\"", "DELETE FROM \"Contacts\"" })
        { await using var cmd = new NpgsqlCommand(sql, connection); Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await cmd.ExecuteNonQueryAsync())).SqlState); }
        await using var transaction = await connection.BeginTransactionAsync();
        await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantA}',true)", connection, transaction); await set.ExecuteNonQueryAsync();
        await using var bad = new NpgsqlCommand($"UPDATE \"Assets\" SET \"LocationId\"='{foreign.Id}' WHERE \"Id\"='{asset.Id}'", connection, transaction);
        Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(async () => await bad.ExecuteNonQueryAsync())).SqlState);
    }
    [Fact]
    public async Task Asset_restore_races_reject_archived_placement_and_history_reads_require_audit()
    {
        using var client = await Login(); var asset = await Asset(client); var loc = await Create(client, "locations");
        var path = Root("assets") + "/" + asset.Id;
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path, new ReviseAssetRequest("Placed", "Server", loc.Id, 1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path, new ReviseAssetRequest("Unplaced", "Server", null, 2), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Root("locations") + "/" + loc.Id, Input("Archived", 1, true), true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, path + "/restore", new RestoreDocumentRequest(2, 3, "Try archived placement"))).StatusCode);
        var race = await Task.WhenAll(
            Write(client, path + "/restore", new RestoreDocumentRequest(1, 3, "Restore original inventory")),
            Write(client, path, new ReviseAssetRequest("Concurrent inventory", "Network", null, 3), true));
        Assert.Single(race, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(4L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{asset.Id}'"));
        await fixture.Execute("CREATE FUNCTION fail_asset_history_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='Asset.RevisionsRead' THEN RAISE EXCEPTION 'Synthetic history audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_asset_history_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_asset_history_read()");
        try
        {
            var response = await client.GetAsync(path + "/revisions"); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain("Original asset", await response.Content.ReadAsStringAsync());
        }
        finally { await fixture.Execute("DROP TRIGGER fail_asset_history_read ON \"AuditEvents\"; DROP FUNCTION fail_asset_history_read()"); }
    }

    [Theory]
    [InlineData("AuditEvents")]
    [InlineData("OutboxEvents")]
    public async Task Failed_asset_restore_preserves_current_record_history_and_success_event_counts(string table)
    {
        using var client = await Login(); var asset = await Asset(client); var path = Root("assets") + "/" + asset.Id;
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path, new ReviseAssetRequest("Current inventory", "Server", null, 1), true)).StatusCode);
        var field = table == "AuditEvents" ? "Action" : "Type";
        await fixture.Execute($"CREATE FUNCTION fail_asset_restore() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"{field}\"='Asset.Restored' THEN RAISE EXCEPTION 'Synthetic restore failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_asset_restore BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_asset_restore()");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Write(client, path + "/restore", new RestoreDocumentRequest(1, 2, "Restore original inventory"))).StatusCode);
            Assert.Equal("Current inventory", await fixture.Scalar<string>($"SELECT \"Name\" FROM \"Assets\" WHERE \"Id\"='{asset.Id}'"));
            Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{asset.Id}'"));
            foreach (var (events, action) in new[] { ("AuditEvents", "Action"), ("OutboxEvents", "Type") })
                Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{events}\" WHERE \"TargetId\"='{asset.Id}' AND \"{action}\"='Asset.Restored'"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_asset_restore ON \"{table}\"; DROP FUNCTION fail_asset_restore()"); }
    }

    public void Dispose() => factory.Dispose();
}
