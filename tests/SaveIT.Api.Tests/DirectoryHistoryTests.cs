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
public sealed class DirectoryHistoryTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static string Root(string resource, Guid? org = null) => $"/api/v1/organizations/{org ?? DevelopmentSeed.OrgA}/{resource}";
    private static string Table(string resource) => resource == "contacts" ? "ContactRevisions" : "LocationRevisions";
    private static string Prefix(string resource) => resource == "contacts" ? "Contact." : "Location.";
    private static DirectoryRequest Input(string resource, string name = "Historical <img src=x>", int expected = 0, bool archived = false) =>
        new(name, "Synthetic address / role", resource == "contacts" ? "synthetic@example.invalid" : "", resource == "contacts" ? "+1 555 0100" : "", "Exact\r\nnotes 😀\n", archived, expected);
    private sealed record Csrf(string Token);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var c = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile })).StatusCode); return c;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object input, bool put = false)
    {
        var csrf = (await c.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        using var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token); return await c.SendAsync(request);
    }
    private static async Task<DirectoryDto> Create(HttpClient c, string resource, Guid? org = null)
    {
        var r = await Write(c, Root(resource, org), Input(resource)); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<DirectoryDto>())!;
    }
    private static void Same(DirectoryDto expected, DirectoryRevisionDto actual)
    {
        Assert.Equal(expected.Name, actual.Name); Assert.Equal(expected.Details, actual.Details); Assert.Equal(expected.Email, actual.Email);
        Assert.Equal(expected.Phone, actual.Phone); Assert.Equal(expected.Notes, actual.Notes); Assert.Equal(expected.IsArchived, actual.IsArchived);
    }
    private static void NoContent(string text)
    {
        foreach (var privateValue in new[] { "Historical", "Exact", "synthetic@example.invalid", "555", "private restore reason", "nextCursor", "items", "notes" })
            Assert.DoesNotContain(privateValue, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Exact_snapshots_include_lifecycle_and_restore_appends_without_rewriting_history(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        var initial = (await c.GetFromJsonAsync<DirectoryRevisionDto>(path + "/revisions/1"))!; Same(row, initial); Assert.Equal("alice", initial.Actor);
        Guid? assetId = null;
        if (resource == "locations")
        {
            var asset = (await (await Write(c, Root("assets"), new CreateAssetRequest("Placement remains", "Server"))).Content.ReadFromJsonAsync<AssetDto>())!;
            assetId = asset.Id; Assert.Equal(HttpStatusCode.OK, (await Write(c, Root("assets") + "/" + asset.Id, new ReviseAssetRequest(asset.Name, asset.Kind, row.Id, 1), true)).StatusCode);
        }
        var archived = (await (await Write(c, path, Input(resource, "Archived historical", 1, true) with { Notes = "Archived exact\r\n", Email = resource == "contacts" ? "archived@example.invalid" : "" }, true)).Content.ReadFromJsonAsync<DirectoryDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path, Input(resource, "Current active", 2), true)).StatusCode);
        var result = await Write(c, path + "/restore", new RestoreDocumentRequest(2, 3, "private restore reason")); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var restored = (await result.Content.ReadFromJsonAsync<DirectoryDto>())!; Assert.Equal(4, restored.Version); Assert.Equal(row.Id, restored.Id); Assert.True(restored.IsArchived);
        var revision = (await c.GetFromJsonAsync<DirectoryRevisionDto>(path + "/revisions/4"))!; Same(archived, revision);
        Assert.Equal(2, revision.RestoredFromVersion); Assert.Equal("private restore reason", revision.RestoreReason); Assert.Equal("alice", revision.Actor);
        Assert.True(revision.CreatedAt >= initial.CreatedAt); Assert.Equal(initial, await c.GetFromJsonAsync<DirectoryRevisionDto>(path + "/revisions/1"));
        Assert.DoesNotContain((await c.GetFromJsonAsync<CursorPage<DirectoryDto>>(Root(resource)))!.Items, x => x.Id == row.Id);
        var history = (await c.GetFromJsonAsync<CursorPage<DirectoryRevisionSummaryDto>>(path + "/revisions"))!;
        Assert.Equal(new[] { 4, 3, 2, 1 }, history.Items.Select(x => x.Version)); Assert.Equal(2, history.Items[0].RestoredFromVersion);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path + "/restore", new RestoreDocumentRequest(1, 4, "Reactivate exact original"))).StatusCode);
        Same(row, (await c.GetFromJsonAsync<DirectoryRevisionDto>(path + "/revisions/5"))!);
        if (assetId != null) Assert.Equal(row.Id, (await c.GetFromJsonAsync<AssetDto>(Root("assets") + "/" + assetId))!.LocationId);
        Assert.Equal(5L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}'"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{row.Id}' AND \"Action\"='{Prefix(resource)}Restored' AND \"SourceVersion\"=2 AND \"TargetVersion\"=4"));
        var events = await fixture.Scalar<string>($"SELECT json_agg(e)::text FROM \"OutboxEvents\" e WHERE \"TargetId\"='{row.Id}'");
        Assert.DoesNotContain(row.Name, events); Assert.DoesNotContain("private restore reason", events); Assert.DoesNotContain("Notes", events);
        var audits = await fixture.Scalar<string>($"SELECT json_agg(e)::text FROM \"AuditEvents\" e WHERE \"TargetId\"='{row.Id}'");
        Assert.DoesNotContain(row.Name, audits); Assert.DoesNotContain("private restore reason", audits); Assert.DoesNotContain("Notes", audits);
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task History_and_restore_recheck_current_authority_and_validate_source_version_reason_and_csrf(string resource)
    {
        using var c = await Login(); using var viewer = await Login("viewer"); using var bob = await Login("bob"); using var anonymous = factory.CreateClient();
        var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path, Input(resource, "Current", 1), true)).StatusCode);
        foreach (var suffix in new[] { "/revisions", "/revisions/1" })
        {
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path + suffix)).StatusCode);
            foreach (var denied in new[] { await bob.GetAsync(path + suffix), await c.GetAsync(Root(resource, DevelopmentSeed.OrgA2) + "/" + row.Id + suffix), await c.GetAsync(Root(resource == "contacts" ? "locations" : "contacts") + "/" + row.Id + suffix) })
            { Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); NoContent(await denied.Content.ReadAsStringAsync()); }
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path + suffix)).StatusCode);
        }
        var restore = new RestoreDocumentRequest(1, 2, "Valid private reason");
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, path + "/restore", restore)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(bob, path + "/restore", restore)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Write(c, Root(resource, DevelopmentSeed.OrgA2) + "/" + row.Id + "/restore", restore)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(path + "/restore", restore)).StatusCode);
        foreach (var input in new[] { restore with { SourceVersion = 0 }, restore with { ExpectedVersion = 0 }, restore with { Reason = null! }, restore with { Reason = "  " }, restore with { Reason = new string('x', 501) }, restore with { Reason = "bad\u0001reason" }, restore with { SourceVersion = 2 }, restore with { SourceVersion = 3 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(c, path + "/restore", input)).StatusCode);
        foreach (var suffix in new[] { "/revisions?before=0", "/revisions?before=-1", "/revisions/0" }) Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(path + suffix)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(path + "/revisions/999")).StatusCode);
        var member = DevelopmentSeed.Profiles[0].MembershipId;
        try
        {
            await fixture.Execute($"UPDATE \"OrganizationGrants\" SET \"Role\"='Viewer' WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'");
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(c, path + "/restore", restore)).StatusCode);
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(path + "/revisions/1")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await Write(c, path + "/restore", restore)).StatusCode);
        }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'; UPDATE \"OrganizationGrants\" SET \"Role\"='Editor' WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'"); }
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}'"));
    }

    [Theory]
    [InlineData("locations", "snapshot")]
    [InlineData("locations", "audit")]
    [InlineData("locations", "outbox")]
    [InlineData("locations", "queue")]
    [InlineData("contacts", "snapshot")]
    [InlineData("contacts", "audit")]
    [InlineData("contacts", "outbox")]
    [InlineData("contacts", "queue")]
    public async Task Restore_rolls_back_head_snapshot_audit_outbox_and_receipt_at_every_boundary(string resource, string boundary)
    {
        using var c = await Login(); var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path, Input(resource, "Retain current", 1), true)).StatusCode);
        var table = boundary switch { "snapshot" => Table(resource), "audit" => "AuditEvents", "outbox" => "OutboxEvents", _ => "SearchDeliveries" };
        var condition = boundary switch { "snapshot" => $"NEW.\"RecordId\"='{row.Id}' AND NEW.\"Version\"=3", "audit" => $"NEW.\"Action\"='{Prefix(resource)}Restored'", "outbox" => $"NEW.\"Type\"='{Prefix(resource)}Restored'", _ => $"EXISTS(SELECT 1 FROM public.\"OutboxEvents\" e WHERE e.\"Id\"=NEW.\"EventId\" AND e.\"Type\"='{Prefix(resource)}Restored')" };
        await fixture.Execute($"CREATE FUNCTION fail_directory_restore() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION 'Synthetic restore failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_directory_restore BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_directory_restore()");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Write(c, path + "/restore", new RestoreDocumentRequest(1, 2, "private restore reason"))).StatusCode);
            Assert.Equal("Retain current", (await c.GetFromJsonAsync<DirectoryDto>(path))!.Name);
            Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}'"));
            Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"{(resource == "contacts" ? "Contacts" : "Locations")}\" WHERE \"Id\"='{row.Id}'"));
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{row.Id}' AND \"Action\"='{Prefix(resource)}Restored'"));
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{row.Id}' AND \"Type\"='{Prefix(resource)}Restored'"));
            Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchDeliveries\" d JOIN \"OutboxEvents\" e ON e.\"TenantId\"=d.\"TenantId\" AND e.\"Id\"=d.\"EventId\" WHERE e.\"TargetId\"='{row.Id}'"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_directory_restore ON \"{table}\"; DROP FUNCTION fail_directory_restore()"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task History_insert_failure_rolls_back_regular_create_and_edit_and_read_audit_or_source_failure_withholds_content(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        await fixture.Execute($"CREATE FUNCTION fail_directory_snapshot() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic snapshot failure'; END $$; CREATE TRIGGER fail_directory_snapshot BEFORE INSERT ON \"{Table(resource)}\" FOR EACH ROW EXECUTE FUNCTION fail_directory_snapshot()");
        var name = "No orphan " + Guid.NewGuid();
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Write(c, Root(resource), Input(resource, name))).StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError, (await Write(c, path, Input(resource, "No change", 1), true)).StatusCode);
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{(resource == "contacts" ? "Contacts" : "Locations")}\" WHERE \"Name\"='{name}'"));
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}'"));
            Assert.Equal(1, (await c.GetFromJsonAsync<DirectoryDto>(path))!.Version);
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_directory_snapshot ON \"{Table(resource)}\"; DROP FUNCTION fail_directory_snapshot()"); }
        await fixture.Execute($"CREATE FUNCTION fail_directory_history_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" IN ('{Prefix(resource)}RevisionsRead','{Prefix(resource)}RevisionRead') THEN RAISE EXCEPTION 'Synthetic audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_directory_history_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_directory_history_read()");
        try { foreach (var suffix in new[] { "/revisions", "/revisions/1" }) { var r = await c.GetAsync(path + suffix); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); NoContent(await r.Content.ReadAsStringAsync()); } }
        finally { await fixture.Execute("DROP TRIGGER fail_directory_history_read ON \"AuditEvents\"; DROP FUNCTION fail_directory_history_read()"); }
        await fixture.Execute($"REVOKE SELECT ON \"{Table(resource)}\" FROM saveit_app");
        try { foreach (var suffix in new[] { "/revisions", "/revisions/1" }) { var r = await c.GetAsync(path + suffix); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); NoContent(await r.Content.ReadAsStringAsync()); } }
        finally { await fixture.Execute($"GRANT SELECT ON \"{Table(resource)}\" TO saveit_app"); }
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(path + "/revisions/1")).StatusCode);
    }

    [Theory]
    [InlineData("locations", false)]
    [InlineData("locations", true)]
    [InlineData("contacts", false)]
    [InlineData("contacts", true)]
    public async Task Restore_racing_restore_or_edit_has_one_winner_and_one_atomic_change(string resource, bool edit)
    {
        using var c = await Login(); var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path, Input(resource, "Current", 1), true)).StatusCode);
        var results = await Task.WhenAll(Write(c, path + "/restore", new RestoreDocumentRequest(1, 2, "Restore original")),
            edit ? Write(c, path, Input(resource, "Other writer", 2), true) : Write(c, path + "/restore", new RestoreDocumentRequest(1, 2, "Other restore")));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(3, (await c.GetFromJsonAsync<DirectoryDto>(path))!.Version);
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{row.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{row.Id}' AND \"Action\" IN ('{Prefix(resource)}Created','{Prefix(resource)}Revised','{Prefix(resource)}Restored')"));
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task History_pages_are_bounded_metadata_only_and_recheck_grants_between_pages(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        for (var version = 1; version < 55; version++) Assert.Equal(HttpStatusCode.OK, (await Write(c, path, Input(resource, "Page " + version, version), true)).StatusCode);
        using var viewer = await Login("viewer");
        var response = await viewer.GetAsync(path + "/revisions"); var text = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "notes", "email", "phone", "details", "restoreReason", "Exact" }) Assert.DoesNotContain(field, text, StringComparison.OrdinalIgnoreCase);
        var first = (await response.Content.ReadFromJsonAsync<CursorPage<DirectoryRevisionSummaryDto>>())!;
        Assert.Equal(Enumerable.Range(6, 50).Reverse(), first.Items.Select(x => x.Version)); Assert.Equal("6", first.NextCursor);
        var last = (await c.GetFromJsonAsync<CursorPage<DirectoryRevisionSummaryDto>>(path + "/revisions?before=6"))!;
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, last.Items.Select(x => x.Version)); Assert.Null(last.NextCursor);
        var member = DevelopmentSeed.Profiles[1].MembershipId;
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'");
        try { response = await viewer.GetAsync(path + "/revisions?before=6"); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); NoContent(await response.Content.ReadAsStringAsync()); }
        finally { await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{DevelopmentSeed.TenantA}','{member}','{DevelopmentSeed.OrgA}','Viewer')"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Raw_roles_enforce_rls_scoped_parent_and_source_fks_provenance_and_append_only_history(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); var table = Table(resource);
        await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync();
        await using (var read = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", app)) Assert.Equal(0L, await read.ExecuteScalarAsync());
        Assert.True(await fixture.Scalar<bool>($"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='\"{table}\"'::regclass"));
        foreach (var sql in new[] { $"UPDATE \"{table}\" SET \"Notes\"='No'", $"DELETE FROM \"{table}\"" })
        { await using var command = new NpgsqlCommand(sql, app); Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState); }
        var extras = resource == "contacts" ? ",\"Email\",\"Phone\"" : ""; var values = resource == "contacts" ? ",'',''" : "";
        string Insert(Guid tenant, Guid org, int version, string source, string reason) => $"INSERT INTO \"{table}\" (\"TenantId\",\"OrganizationId\",\"RecordId\",\"Version\",\"Name\",\"Details\",\"Notes\",\"IsArchived\",\"Actor\",\"CreatedAt\",\"RestoredFromVersion\",\"RestoreReason\"{extras}) VALUES ('{tenant}','{org}','{row.Id}',{version},'Synthetic','','',false,'synthetic',CURRENT_TIMESTAMP,{source},{reason}{values})";
        foreach (var (sql, state) in new[] {
            (Insert(DevelopmentSeed.TenantB, DevelopmentSeed.OrgA, 2, "NULL", "NULL"), "42501"),
            (Insert(DevelopmentSeed.TenantA, DevelopmentSeed.OrgA2, 2, "NULL", "NULL"), "23503"),
            (Insert(DevelopmentSeed.TenantA, DevelopmentSeed.OrgA, 2, "1", "NULL"), "23514"),
            (Insert(DevelopmentSeed.TenantA, DevelopmentSeed.OrgA, 2, "NULL", "'Reason'"), "23514"),
            (Insert(DevelopmentSeed.TenantA, DevelopmentSeed.OrgA, 2, "2", "'Reason'"), "23514"),
            (Insert(DevelopmentSeed.TenantA, DevelopmentSeed.OrgA, 100, "99", "'Missing source'"), "23503") })
        {
            await using var tx = await app.BeginTransactionAsync();
            await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantA}',true)", app, tx)) await set.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand(sql, app, tx); Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync())).SqlState);
            await tx.RollbackAsync();
        }
        await using var worker = new NpgsqlConnection(fixture.WorkerConnection); await worker.OpenAsync();
        await using var denied = new NpgsqlCommand($"SELECT \"Notes\" FROM \"{table}\"", worker);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync())).SqlState);
        await DatabaseRoleGuard.ValidateSearchWorkerAsync(worker);
    }

    [Theory]
    [InlineData("LocationRevisions", "SELECT (\"Notes\")", "saveit_search_worker")]
    [InlineData("ContactRevisions", "SELECT (\"Notes\")", "PUBLIC")]
    [InlineData("LocationRevisions", "UPDATE (\"Notes\")", "saveit_search_worker")]
    [InlineData("ContactRevisions", "INSERT (\"Notes\")", "PUBLIC")]
    public async Task Worker_guard_rejects_new_history_content_and_column_write_grants(string table, string privilege, string grantee)
    {
        await fixture.Execute($"GRANT {privilege} ON \"{table}\" TO {grantee}");
        try { await using var worker = new NpgsqlConnection(fixture.WorkerConnection); await worker.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateSearchWorkerAsync(worker)); }
        finally { await fixture.Execute($"REVOKE {privilege} ON \"{table}\" FROM {grantee}"); }
    }

    [Fact]
    public async Task Worker_guard_rejects_reachable_history_read_and_api_guard_rejects_reachable_history_owner()
    {
        await fixture.Execute("CREATE ROLE saveit_directory_history_review NOLOGIN NOINHERIT; GRANT saveit_directory_history_review TO saveit_search_worker WITH INHERIT FALSE; GRANT SELECT (\"Email\") ON \"ContactRevisions\" TO saveit_directory_history_review");
        try { await using var worker = new NpgsqlConnection(fixture.WorkerConnection); await worker.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateSearchWorkerAsync(worker)); }
        finally { await fixture.Execute("REVOKE saveit_directory_history_review FROM saveit_search_worker; REVOKE SELECT (\"Email\") ON \"ContactRevisions\" FROM saveit_directory_history_review; DROP ROLE saveit_directory_history_review"); }
        await fixture.Execute("CREATE ROLE saveit_directory_owner_review NOLOGIN NOINHERIT; GRANT saveit_directory_owner_review TO saveit_app WITH INHERIT FALSE; ALTER TABLE \"LocationRevisions\" OWNER TO saveit_directory_owner_review");
        try { await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(app)); }
        finally { await fixture.Execute("ALTER TABLE \"LocationRevisions\" OWNER TO postgres; REVOKE saveit_directory_owner_review FROM saveit_app; DROP ROLE saveit_directory_owner_review"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Restored_events_are_schema_one_metadata_acknowledgements_and_never_directory_projections(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); var path = Root(resource) + "/" + row.Id;
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path, Input(resource, "Updated", 1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, path + "/restore", new RestoreDocumentRequest(1, 2, "Restore synthetic directory"))).StatusCode);
        var processor = new SearchProcessor(fixture.WorkerConnection);
        for (var i = 0; i < 40; i++)
        {
            await processor.ProcessBatchAsync(DevelopmentSeed.TenantA);
            if (await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" e JOIN \"SearchDeliveries\" d ON e.\"Id\"=d.\"EventId\" AND e.\"TenantId\"=d.\"TenantId\" WHERE e.\"TargetId\"='{row.Id}' AND d.\"CompletedAt\" IS NOT NULL") == 3) break;
        }
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" e JOIN \"SearchDeliveries\" d ON e.\"Id\"=d.\"EventId\" AND e.\"TenantId\"=d.\"TenantId\" WHERE e.\"TargetId\"='{row.Id}' AND d.\"CompletedAt\" IS NOT NULL"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{row.Id}'"));
        var bad = Guid.NewGuid();
        await fixture.Execute($"INSERT INTO \"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES ('{bad}','{DevelopmentSeed.TenantA}','{DevelopmentSeed.OrgA}','{row.Id}','{Prefix(resource)}Restored',2,CURRENT_TIMESTAMP)");
        await processor.ProcessBatchAsync(DevelopmentSeed.TenantA);
        Assert.Equal("unsupported_event", await fixture.Scalar<string>($"SELECT \"ErrorCode\" FROM \"SearchDeliveries\" WHERE \"EventId\"='{bad}'"));
    }

    [Fact]
    public async Task Migration_backfills_only_the_actual_current_directory_versions_with_exact_fields()
    {
        var database = "saveit_directory_migration_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE DATABASE \"{database}\" ENCODING 'UTF8' TEMPLATE template0 LC_COLLATE 'C' LC_CTYPE 'C'");
        var connection = new NpgsqlConnectionStringBuilder(fixture.OwnerConnection) { Database = database }.ConnectionString;
        try
        {
            var scope = new TenantScope(); await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(connection).Options, scope);
            await db.GetService<IMigrator>().MigrateAsync("20261029225741_TypedAssetSchemas"); await DevelopmentSeed.SeedAsync(db, scope); scope.TenantId = DevelopmentSeed.TenantA;
            var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            var place = new Location { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = DevelopmentSeed.OrgA, Version = 7, Name = "Migration location", Details = "Exact address", Notes = "Exact\r\n😀", IsArchived = true, UpdatedAt = at };
            var person = new Contact { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = DevelopmentSeed.OrgA, Version = 9, Name = "Migration contact", Email = "migration@example.invalid", Phone = "555", Notes = "Exact\r\ncontact", UpdatedAt = at };
            db.Locations.Add(place); db.Contacts.Add(person); await db.SaveChangesAsync(); await db.Database.MigrateAsync();
            var oldPlace = await db.LocationRevisions.SingleAsync(); var oldPerson = await db.ContactRevisions.SingleAsync();
            Assert.Equal(7, oldPlace.Version); Assert.Equal(9, oldPerson.Version); Assert.Equal("migration", oldPlace.Actor); Assert.Equal("migration", oldPerson.Actor);
            Assert.Equal(place.Notes, oldPlace.Notes); Assert.Equal(person.Email, oldPerson.Email); Assert.Equal(person.Phone, oldPerson.Phone); Assert.Equal(person.Notes, oldPerson.Notes);
            Assert.Equal(at, oldPlace.CreatedAt); Assert.Equal(at, oldPerson.CreatedAt); Assert.True(oldPlace.IsArchived); Assert.Null(oldPlace.RestoredFromVersion);
            Assert.Equal(1, await db.LocationRevisions.CountAsync()); Assert.Equal(1, await db.ContactRevisions.CountAsync());
            using var isolatedFactory = new ApiFactory(new NpgsqlConnectionStringBuilder(connection) { Username = "saveit_app", Password = "synthetic-app-only" }.ConnectionString);
            using var client = isolatedFactory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
            Assert.Equal(HttpStatusCode.NoContent, (await Write(client, "/api/v1/dev/login", new { profile = "alice" })).StatusCode);
            var path = Root("locations") + "/" + place.Id;
            var history = (await client.GetFromJsonAsync<CursorPage<DirectoryRevisionSummaryDto>>(path + "/revisions"))!;
            Assert.Equal(7, Assert.Single(history.Items).Version);
            Assert.Equal(HttpStatusCode.NotFound, (await Write(client, path + "/restore", new RestoreDocumentRequest(1, 7, "Unavailable earlier history"))).StatusCode);
            Assert.Equal(7, (await client.GetFromJsonAsync<DirectoryDto>(path))!.Version);
        }
        finally { NpgsqlConnection.ClearAllPools(); await fixture.Execute($"DROP DATABASE \"{database}\" WITH (FORCE)"); }
    }
    public void Dispose() => factory.Dispose();
}
