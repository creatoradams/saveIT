using System.Data;
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
public sealed class DocumentFolderTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private readonly Guid tenant = DevelopmentSeed.TenantA;
    private Guid org;
    private HttpClient client = null!;
    private string Base => $"/api/v1/organizations/{org}";
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
    private async Task<FolderDto> Folder(Guid? parent = null, string name = "Confidential folder <img src=x> 😀")
    {
        var r = await Write(client, Base + "/folders", new { name, parentId = parent });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<FolderDto>())!;
    }
    private Task<HttpResponseMessage> Revise(FolderDto row, Guid? parent, int? version = null, string? name = null) =>
        Write(client, Base + "/folders/" + row.Id, new { name = name ?? row.Name, parentId = parent, expectedVersion = version ?? row.Version }, true);
    private async Task<DocumentDto> Document()
    {
        var asset = await Write(client, Base + "/assets", new { name = "Synthetic library asset", kind = "Server" });
        var a = (await asset.Content.ReadFromJsonAsync<AssetDto>())!;
        var r = await Write(client, Base + "/documents", new { assetId = a.Id, title = "Confidential document", content = "Confidential body" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<DocumentDto>())!;
    }
    private Task<HttpResponseMessage> File(Guid doc, Guid? folder, int version = 0, HttpClient? c = null) =>
        Write(c ?? client, Base + $"/documents/{doc}/placement", new { folderId = folder, expectedVersion = version }, true);
    private static string Query(Guid? folder, string? after, string key) => "?" + string.Join("&", new[] { folder is null ? null : key + "=" + folder, after is null ? null : "after=" + after }.Where(x => x is not null));
    private Task<CursorPage<FolderDto>?> Folders(Guid? parent = null, string? after = null) => client.GetFromJsonAsync<CursorPage<FolderDto>>(Base + "/folders" + Query(parent, after, "parentId"));
    private Task<CursorPage<LibraryDocumentDto>?> Documents(Guid? folder = null, string? after = null) => client.GetFromJsonAsync<CursorPage<LibraryDocumentDto>>(Base + "/library/documents" + Query(folder, after, "folderId"));
    private async Task<long[]> Counts()
    {
        var result = new List<long>();
        foreach (var table in new[] { "DocumentFolders", "DocumentPlacements", "Documents", "DocumentRevisions", "AuditEvents", "OutboxEvents", "SearchDeliveries" }) result.Add(await fixture.Scalar<long>($"SELECT count(*) FROM \"{table}\""));
        return result.ToArray();
    }
    private static void Withheld(string text)
    {
        foreach (var canary in new[] { "Confidential", "items", "nextCursor", "folderId", "placementVersion", "\"path\":" }) Assert.DoesNotContain(canary, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Folder_paths_and_placement_persist_without_changing_content_history_asset_or_search_and_restore_retains_placement()
    {
        var root = await Folder(); var child = await Folder(root.Id); var doc = await Document();
        var detail = (await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + child.Id))!;
        Assert.Equal(new[] { root.Id, child.Id }, detail.Path.Select(x => x.Id));
        Assert.Equal(0, (await client.GetFromJsonAsync<DocumentPlacementDto>(Base + $"/documents/{doc.Id}/placement"))!.Version);
        Assert.Single((await Documents())!.Items);
        var persisted = await client.GetFromJsonAsync<DocumentDto>(Base + "/documents/" + doc.Id);
        Assert.Equal(HttpStatusCode.OK, (await File(doc.Id, child.Id)).StatusCode);
        Assert.Empty((await Documents())!.Items); var filed = Assert.Single((await Documents(child.Id))!.Items); Assert.Equal(doc.Id, filed.Id); Assert.Equal(1, filed.PlacementVersion);
        Assert.Equal(persisted, await client.GetFromJsonAsync<DocumentDto>(Base + "/documents/" + doc.Id));
        var renamed = await Revise(root, null, name: "Renamed folder"); Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Renamed folder", (await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + child.Id))!.Path[0].Name);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Base + "/documents/" + doc.Id, new { title = "Revised title", content = "Revised body", expectedVersion = 1 }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Base + $"/documents/{doc.Id}/restore", new { sourceVersion = 1, expectedVersion = 2, reason = "Private restore reason" })).StatusCode);
        var current = (await client.GetFromJsonAsync<DocumentDto>(Base + "/documents/" + doc.Id))!;
        Assert.Equal(doc.AssetId, current.AssetId); Assert.Equal(doc.Content, current.Content); Assert.Equal(3, current.Version);
        var placement = (await client.GetFromJsonAsync<DocumentPlacementDto>(Base + $"/documents/{doc.Id}/placement"))!; Assert.Equal(child.Id, placement.FolderId); Assert.Equal(1, placement.Version);
        Assert.Equal(HttpStatusCode.OK, (await File(doc.Id, null, 1)).StatusCode); Assert.Single((await Documents())!.Items);
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'"));
        foreach (var table in new[] { "AuditEvents", "OutboxEvents" }) Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{table}\" e WHERE e.\"OrganizationId\"='{org}' AND to_jsonb(e)::text LIKE '%Confidential%'"));
    }

    [Fact]
    public async Task Current_roles_scope_membership_and_csrf_control_every_path_and_folders_grant_no_access()
    {
        var folder = await Folder(); var doc = await Document(); using var viewer = await Login("viewer"); using var bob = await Login("bob"); using var anonymous = factory.CreateClient();
        var paths = new[] { "/folders", "/folders/" + folder.Id, "/library/documents", $"/documents/{doc.Id}/placement" };
        foreach (var path in paths) { Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(Base + path)).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Base + path)).StatusCode); }
        bob.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.ToString()); foreach (var path in paths) Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Base + path)).StatusCode);
        var member = DevelopmentSeed.Profiles[1].MembershipId;
        await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{tenant}','{member}','{org}','Viewer')");
        foreach (var path in paths) Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Base + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Base + "/folders", new { name = "No", parentId = folder.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Base + "/folders/" + folder.Id, new { name = "No", expectedVersion = 1 }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await File(doc.Id, folder.Id, c: viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Base + "/folders", new { name = "No CSRF" })).StatusCode);
        await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
        try { foreach (var path in paths) Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync(Base + path)).StatusCode); }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'"); }
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{org}' AND \"MembershipId\"='{member}'");
        foreach (var path in paths) { var r = await viewer.GetAsync(Base + path); Assert.Equal(HttpStatusCode.NotFound, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        Assert.Equal(HttpStatusCode.NotFound, (await Write(client, Base + "/folders", new { name = "Foreign", parentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await File(Guid.NewGuid(), folder.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await File(doc.Id, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Folder_and_document_pages_are_bounded_disjoint_metadata_only_and_recheck_grants()
    {
        var parent = await Folder(); var doc = await Document();
        await fixture.Execute($"INSERT INTO \"DocumentFolders\" (\"TenantId\",\"OrganizationId\",\"Id\",\"ParentId\",\"Name\",\"Version\",\"UpdatedAt\") SELECT '{tenant}','{org}',gen_random_uuid(),'{parent.Id}','Confidential child',1,now() FROM generate_series(1,55); INSERT INTO \"Documents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Content\",\"Version\",\"UpdatedAt\") SELECT gen_random_uuid(),'{tenant}','{org}','{doc.AssetId}','Confidential metadata','Content canary must never be listed',1,now() FROM generate_series(1,54)");
        var first = (await Folders(parent.Id))!; var tail = (await Folders(parent.Id, first.NextCursor))!; Assert.Equal(50, first.Items.Count); Assert.Equal(5, tail.Items.Count); Assert.Null(tail.NextCursor); Assert.Empty(first.Items.Select(x => x.Id).Intersect(tail.Items.Select(x => x.Id)));
        await fixture.Execute("REVOKE SELECT ON \"Documents\" FROM saveit_app; GRANT SELECT (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Version\",\"UpdatedAt\") ON \"Documents\" TO saveit_app");
        try
        {
            var page = (await Documents())!; var rest = (await Documents(after: page.NextCursor))!; Assert.Equal(50, page.Items.Count); Assert.Equal(5, rest.Items.Count); Assert.Null(rest.NextCursor); Assert.Empty(page.Items.Select(x => x.Id).Intersect(rest.Items.Select(x => x.Id)));
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync(Base + "/documents/" + doc.Id)).StatusCode);
        }
        finally { await fixture.Execute("REVOKE SELECT (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Version\",\"UpdatedAt\") ON \"Documents\" FROM saveit_app; GRANT SELECT ON \"Documents\" TO saveit_app"); }
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{org}'");
        foreach (var path in new[] { $"/folders?parentId={parent.Id}&after={first.NextCursor}", "/library/documents?after=" + doc.Id }) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Base + path)).StatusCode);
    }

    [Fact]
    public async Task Self_cycles_descendant_cycles_depth_and_moved_subtree_height_are_rejected_without_partial_changes()
    {
        var chain = new List<FolderDto>(); for (var i = 0; i < 8; i++) chain.Add(await Folder(i == 0 ? null : chain[^1].Id));
        Assert.Equal(8, (await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + chain[^1].Id))!.Path.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Base + "/folders", new { name = "Too deep", parentId = chain[^1].Id })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Revise(chain[0], chain[0].Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Revise(chain[0], chain[3].Id)).StatusCode);
        var subtree = await Folder(); var leaf = await Folder(subtree.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await Revise(subtree, chain[6].Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Revise(subtree, chain[5].Id)).StatusCode);
        Assert.Equal(8, (await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + leaf.Id))!.Path.Count);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT \"Version\"::bigint FROM \"DocumentFolders\" WHERE \"Id\"='{chain[0].Id}'"));
    }

    [Fact]
    public async Task Simultaneous_opposite_moves_and_stale_folder_versions_have_one_valid_winner()
    {
        var a = await Folder(); var b = await Folder();
        var results = await Task.WhenAll(Revise(a, b.Id), Revise(b, a.Id));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
        foreach (var id in new[] { a.Id, b.Id }) Assert.InRange((await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + id))!.Path.Count, 1, 2);
        var row = (await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + a.Id))!.Folder;
        var edits = await Task.WhenAll(Revise(row, row.ParentId, name: "First"), Revise(row, row.ParentId, name: "Second"));
        Assert.Single(edits, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(edits, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Concurrent_first_and_later_placements_conflict_while_content_edits_preserve_assignment()
    {
        var a = await Folder(); var b = await Folder(); var doc = await Document();
        var first = await Task.WhenAll(File(doc.Id, a.Id), File(doc.Id, b.Id)); Assert.Single(first, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(first, x => x.StatusCode == HttpStatusCode.Conflict);
        var move = File(doc.Id, null, 1); var edit = Write(client, Base + "/documents/" + doc.Id, new { title = "Concurrent content", content = "Concurrent body", expectedVersion = 1 }, true);
        foreach (var r in await Task.WhenAll(move, edit)) Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var later = await Task.WhenAll(File(doc.Id, a.Id, 2), File(doc.Id, b.Id, 2)); Assert.Single(later, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(later, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("Concurrent body", (await client.GetFromJsonAsync<DocumentDto>(Base + "/documents/" + doc.Id))!.Content);
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'"));
    }

    [Theory]
    [InlineData("DocumentFolders", "create")]
    [InlineData("AuditEvents", "create")]
    [InlineData("OutboxEvents", "create")]
    [InlineData("SearchDeliveries", "create")]
    [InlineData("DocumentFolders", "reparent")]
    [InlineData("AuditEvents", "reparent")]
    [InlineData("OutboxEvents", "reparent")]
    [InlineData("SearchDeliveries", "reparent")]
    [InlineData("DocumentPlacements", "file")]
    [InlineData("AuditEvents", "file")]
    [InlineData("OutboxEvents", "file")]
    [InlineData("SearchDeliveries", "file")]
    public async Task Each_write_boundary_rolls_back_all_business_audit_event_and_queue_changes(string table, string operation)
    {
        var folder = await Folder(); var parent = await Folder(); var doc = await Document(); var before = await Counts();
        await fixture.Execute($"CREATE FUNCTION fail_folder_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic failure'; END $$; CREATE TRIGGER fail_folder_write BEFORE INSERT OR UPDATE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_folder_write()");
        try
        {
            var r = operation switch { "create" => await Write(client, Base + "/folders", new { name = "Failed" }), "reparent" => await Revise(folder, parent.Id), _ => await File(doc.Id, folder.Id) };
            Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); Assert.Equal(before, await Counts());
            Assert.True(await fixture.Scalar<bool>($"SELECT \"ParentId\" IS NULL AND \"Version\"=1 FROM \"DocumentFolders\" WHERE \"Id\"='{folder.Id}'"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_folder_write ON \"{table}\"; DROP FUNCTION fail_folder_write()"); }
    }

    [Theory]
    [InlineData("DocumentFolders")]
    [InlineData("DocumentPlacements")]
    [InlineData("Documents")]
    [InlineData("audit")]
    public async Task Actual_source_or_read_audit_failure_withholds_metadata_and_cursors(string table)
    {
        var folder = await Folder(); var doc = await Document();
        if (table == "audit") await fixture.Execute("CREATE FUNCTION fail_folder_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic read audit failure'; END $$; CREATE TRIGGER fail_folder_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_folder_read()");
        else await fixture.Execute($"REVOKE SELECT ON \"{table}\" FROM saveit_app");
        try
        {
            var paths = table switch { "DocumentFolders" => new[] { "/folders", "/folders/" + folder.Id, "/library/documents?folderId=" + folder.Id }, "DocumentPlacements" or "Documents" => new[] { "/library/documents", $"/documents/{doc.Id}/placement" }, _ => new[] { "/folders", "/folders/" + folder.Id, "/library/documents", $"/documents/{doc.Id}/placement" } };
            foreach (var path in paths) { var r = await client.GetAsync(Base + path); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        }
        finally { if (table == "audit") await fixture.Execute("DROP TRIGGER fail_folder_read ON \"AuditEvents\"; DROP FUNCTION fail_folder_read()"); else await fixture.Execute($"GRANT SELECT ON \"{table}\" TO saveit_app"); }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Base + "/folders")).StatusCode);
    }

    [Fact]
    public async Task Capacity_is_bounded_and_two_last_slot_creations_cannot_both_win()
    {
        await fixture.Execute($"INSERT INTO \"DocumentFolders\" (\"TenantId\",\"OrganizationId\",\"Id\",\"Name\",\"Version\",\"UpdatedAt\") SELECT '{tenant}','{org}',gen_random_uuid(),'Capacity fixture',1,now() FROM generate_series(1,999)");
        var results = await Task.WhenAll(Write(client, Base + "/folders", new { name = "Last slot A" }), Write(client, Base + "/folders", new { name = "Last slot B" }));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created); Assert.Single(results, x => x.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(1000L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentFolders\" WHERE \"OrganizationId\"='{org}'"));
    }

    [Fact]
    public async Task Raw_rls_scoped_fks_versions_column_permissions_and_non_read_committed_writes_fail_closed()
    {
        var folder = await Folder(); var doc = await Document(); await File(doc.Id, folder.Id);
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        await using (var noContext = new NpgsqlCommand("SELECT count(*) FROM \"DocumentFolders\"", connection)) Assert.Equal(0L, await noContext.ExecuteScalarAsync());
        var folderInsert = $"INSERT INTO \"DocumentFolders\" (\"TenantId\",\"OrganizationId\",\"Id\",\"ParentId\",\"Name\",\"Version\",\"UpdatedAt\") VALUES ('{tenant}','{DevelopmentSeed.OrgA2}',gen_random_uuid(),'{folder.Id}','Bad',1,now())";
        foreach (var (sql, state) in new[] {
            (folderInsert,"23503"),
            ($"INSERT INTO \"DocumentPlacements\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"FolderId\",\"Version\",\"UpdatedAt\") VALUES ('{tenant}','{DevelopmentSeed.OrgA2}','{doc.Id}',NULL,1,now())","23503"),
            ($"UPDATE \"DocumentFolders\" SET \"Name\"='Bad' WHERE \"Id\"='{folder.Id}'","23514"),
            ($"UPDATE \"DocumentFolders\" SET \"OrganizationId\"='{DevelopmentSeed.OrgA2}' WHERE \"Id\"='{folder.Id}'","42501"),
            ($"DELETE FROM \"DocumentFolders\" WHERE \"Id\"='{folder.Id}'","42501"),
            ($"UPDATE \"DocumentPlacements\" SET \"Version\"=99 WHERE \"DocumentId\"='{doc.Id}'","23514"),
            ($"UPDATE \"DocumentPlacements\" SET \"DocumentId\"=gen_random_uuid() WHERE \"DocumentId\"='{doc.Id}'","42501"),
            ($"DELETE FROM \"DocumentPlacements\" WHERE \"DocumentId\"='{doc.Id}'","42501") })
        { await using var tx = await connection.BeginTransactionAsync(); await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true)", connection, tx)) await set.ExecuteNonQueryAsync(); await using var command = new NpgsqlCommand(sql, connection, tx); Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState); }
        foreach (var table in new[] { "DocumentFolders", "DocumentPlacements" })
        {
            await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
            await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true)", connection, tx)) await set.ExecuteNonQueryAsync();
            await using var update = new NpgsqlCommand($"UPDATE \"{table}\" SET \"Version\"=\"Version\"+1 WHERE \"OrganizationId\"='{org}'", connection, tx);
            Assert.Equal("25000", (await Assert.ThrowsAsync<PostgresException>(async () => await update.ExecuteNonQueryAsync())).SqlState);
        }
        foreach (var table in new[] { "DocumentFolders", "DocumentPlacements" }) Assert.True(await fixture.Scalar<bool>($"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname='{table}'"));
    }

    [Theory]
    [InlineData("DocumentFolders", "SELECT (\"Name\")", "direct")]
    [InlineData("DocumentFolders", "SELECT (\"Name\")", "PUBLIC")]
    [InlineData("DocumentFolders", "UPDATE (\"ParentId\")", "role")]
    [InlineData("DocumentPlacements", "SELECT (\"FolderId\")", "role")]
    [InlineData("DocumentPlacements", "INSERT (\"DocumentId\")", "direct")]
    [InlineData("DocumentPlacements", "UPDATE (\"FolderId\")", "PUBLIC")]
    public async Task Worker_rejects_direct_public_and_reachable_new_table_column_privileges(string table, string privilege, string target)
    {
        var role = "folder_worker_" + Guid.NewGuid().ToString("N"); var grantee = target == "direct" ? "saveit_search_worker" : target == "PUBLIC" ? "PUBLIC" : role;
        if (target == "role") await fixture.Execute($"CREATE ROLE {role}; GRANT {role} TO saveit_search_worker WITH INHERIT FALSE");
        await fixture.Execute($"GRANT {privilege} ON \"{table}\" TO {grantee}");
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchProcessor(fixture.WorkerConnection).ValidateAsync()); }
        finally { await fixture.Execute($"REVOKE {privilege} ON \"{table}\" FROM {grantee}"); if (target == "role") await fixture.Execute($"REVOKE {role} FROM saveit_search_worker; DROP ROLE {role}"); }
        await new SearchProcessor(fixture.WorkerConnection).ValidateAsync();
    }

    [Fact]
    public async Task Search_acknowledges_known_schema1_metadata_events_without_projection_changes_and_keeps_unknowns_dead_lettered()
    {
        var doc = await Document(); var folder = await Folder(); var processor = new SearchProcessor(fixture.WorkerConnection);
        for (var i = 0; i < 20; i++) { var r = await processor.ProcessBatchAsync(tenant); if (r.Completed + r.Retried + r.DeadLettered == 0) break; }
        var before = await fixture.Scalar<string>($"SELECT row_to_json(s)::text FROM \"SearchEntries\" s WHERE \"Id\"='{doc.Id}'");
        await File(doc.Id, folder.Id); await Revise(folder, null, name: "Not searchable folder label");
        for (var i = 0; i < 20; i++) { var r = await processor.ProcessBatchAsync(tenant); if (r.Completed + r.Retried + r.DeadLettered == 0) break; }
        Assert.Equal(before, await fixture.Scalar<string>($"SELECT row_to_json(s)::text FROM \"SearchEntries\" s WHERE \"Id\"='{doc.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" e JOIN \"SearchDeliveries\" d ON d.\"EventId\"=e.\"Id\" WHERE e.\"OrganizationId\"='{org}' AND e.\"Type\" IN ('DocumentFolder.Created','DocumentFolder.Revised','Document.Filed') AND d.\"CompletedAt\" IS NOT NULL AND d.\"DeadLetteredAt\" IS NULL"));
        var replay = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{org}' AND \"Type\"='Document.Filed'"); Assert.True(await processor.ReplayAsync(tenant, replay)); await processor.ProcessBatchAsync(tenant);
        foreach (var (type, schema) in new[] { ("Document.Filed", 2), ("DocumentFolder.Deleted", 1) })
        {
            var id = Guid.NewGuid(); await fixture.Execute($"INSERT INTO \"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES ('{id}','{tenant}','{org}','{folder.Id}','{type}',{schema},now())"); await processor.ProcessBatchAsync(tenant);
            Assert.True(await fixture.Scalar<bool>($"SELECT \"ErrorCode\"='unsupported_event' AND \"DeadLetteredAt\" IS NOT NULL FROM \"SearchDeliveries\" WHERE \"EventId\"='{id}'"));
        }
    }

    [Fact]
    public async Task Concrete_folder_pages_use_scoped_placements_with_unrelated_document_noise()
    {
        var folder = await Folder(); var doc = await Document();
        await fixture.Execute($"INSERT INTO \"Documents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Content\",\"Version\",\"UpdatedAt\") SELECT gen_random_uuid(),'{tenant}','{org}','{doc.AssetId}','Unrelated metadata','Private body',1,now() FROM generate_series(1,120); WITH docs AS (INSERT INTO \"Documents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Content\",\"Version\",\"UpdatedAt\") SELECT gen_random_uuid(),'{tenant}','{org}','{doc.AssetId}','Filed metadata','Private filed body',1,now() FROM generate_series(1,55) RETURNING \"Id\") INSERT INTO \"DocumentPlacements\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"FolderId\",\"Version\",\"UpdatedAt\") SELECT '{tenant}','{org}',\"Id\",'{folder.Id}',1,now() FROM docs");
        var first = (await Documents(folder.Id))!; var second = (await Documents(folder.Id, first.NextCursor))!;
        Assert.Equal(50, first.Items.Count); Assert.Equal(5, second.Items.Count); Assert.Null(second.NextCursor); Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.All(first.Items.Concat(second.Items), x => { Assert.Equal("Filed metadata", x.Title); Assert.Equal(folder.Id, x.FolderId); Assert.Equal(1, x.PlacementVersion); });
    }

    [Fact]
    public async Task Hidden_middle_ancestor_and_raw_multirow_cycle_fail_without_changing_the_graph()
    {
        var a = await Folder(); var middle = await Folder(a.Id); var b = await Folder(middle.Id);
        await fixture.Execute($"CREATE POLICY synthetic_hidden_ancestor ON \"DocumentFolders\" AS RESTRICTIVE FOR ALL TO saveit_app USING (\"Id\"<>'{middle.Id}') WITH CHECK (\"Id\"<>'{middle.Id}')");
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await Revise(a, b.Id)).StatusCode);
            var read = await client.GetAsync(Base + "/folders/" + b.Id); Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode); Withheld(await read.Content.ReadAsStringAsync());
        }
        finally { await fixture.Execute("DROP POLICY synthetic_hidden_ancestor ON \"DocumentFolders\""); }
        var c = await Folder(); var d = await Folder();
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        await using (var tx = await connection.BeginTransactionAsync())
        {
            await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true)", connection, tx)) await set.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand($"UPDATE \"DocumentFolders\" SET \"ParentId\"=CASE WHEN \"Id\"='{c.Id}' THEN '{d.Id}'::uuid ELSE '{c.Id}'::uuid END,\"Version\"=\"Version\"+1 WHERE \"TenantId\"='{tenant}' AND \"OrganizationId\"='{org}' AND \"Id\" IN ('{c.Id}','{d.Id}')", connection, tx);
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState);
        }
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentFolders\" WHERE \"OrganizationId\"='{org}' AND \"Id\" IN ('{c.Id}','{d.Id}') AND \"ParentId\" IS NULL AND \"Version\"=1"));
    }

    [Fact]
    public async Task Raw_opposite_moves_wait_on_the_same_lock_then_validate_the_committed_graph()
    {
        var a = await Folder(); var b = await Folder();
        await using var blocker = new NpgsqlConnection(fixture.OwnerConnection); await blocker.OpenAsync(); await using var tx = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('{tenant}:{org}',190019))", blocker, tx)) await command.ExecuteNonQueryAsync();
        async Task<string> Move(Guid id, Guid parent)
        {
            await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync(); await using var t = await c.BeginTransactionAsync();
            await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true)", c, t)) await set.ExecuteNonQueryAsync();
            try { await using var update = new NpgsqlCommand($"UPDATE \"DocumentFolders\" SET \"ParentId\"='{parent}',\"Version\"=\"Version\"+1 WHERE \"TenantId\"='{tenant}' AND \"OrganizationId\"='{org}' AND \"Id\"='{id}'", c, t); await update.ExecuteNonQueryAsync(); await t.CommitAsync(); return "committed"; }
            catch (PostgresException e) { await t.RollbackAsync(); return e.SqlState; }
        }
        var first = Move(a.Id, b.Id); var second = Move(b.Id, a.Id); var observed = false;
        try { for (var i = 0; i < 500; i++) { if (await fixture.Scalar<long>("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND usename='saveit_app' AND wait_event='advisory'") >= 2) { observed = true; break; } await Task.Delay(20); } }
        finally { await tx.CommitAsync(); }
        var results = await Task.WhenAll(first, second); Assert.True(observed, "Both real application-role updates must have waited on the hierarchy lock."); Assert.Single(results, x => x == "committed"); Assert.Single(results, x => x == "23514");
        foreach (var id in new[] { a.Id, b.Id }) Assert.InRange((await client.GetFromJsonAsync<FolderDetailDto>(Base + "/folders/" + id))!.Path.Count, 1, 2);
    }

    [Fact]
    public async Task Cross_tenant_rows_and_correct_document_with_foreign_folder_are_denied_by_rls_and_scoped_fks()
    {
        var doc = await Document(); var foreign = Guid.NewGuid();
        await fixture.Execute($"INSERT INTO \"DocumentFolders\" (\"TenantId\",\"OrganizationId\",\"Id\",\"Name\",\"Version\",\"UpdatedAt\") VALUES ('{tenant}','{DevelopmentSeed.OrgA2}','{foreign}','Foreign scope',1,now())");
        try
        {
            Assert.Equal(HttpStatusCode.NotFound, (await File(doc.Id, foreign)).StatusCode);
            await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync();
            foreach (var (sql, state) in new[] {
                ($"INSERT INTO \"DocumentPlacements\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"FolderId\",\"Version\",\"UpdatedAt\") VALUES ('{tenant}','{org}','{doc.Id}','{foreign}',1,now())","23503"),
                ($"INSERT INTO \"DocumentFolders\" (\"TenantId\",\"OrganizationId\",\"Id\",\"Name\",\"Version\",\"UpdatedAt\") VALUES ('{DevelopmentSeed.TenantB}','{DevelopmentSeed.OrgB}',gen_random_uuid(),'Wrong tenant',1,now())","42501"),
                ($"INSERT INTO \"DocumentPlacements\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"FolderId\",\"Version\",\"UpdatedAt\") VALUES ('{DevelopmentSeed.TenantB}','{DevelopmentSeed.OrgB}','{doc.Id}',NULL,1,now())","42501") })
            { await using var t = await c.BeginTransactionAsync(); await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true)", c, t)) await set.ExecuteNonQueryAsync(); await using var command = new NpgsqlCommand(sql, c, t); Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState); }
        }
        finally { await fixture.Execute($"DELETE FROM \"DocumentFolders\" WHERE \"Id\"='{foreign}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA2}'"); }
    }

    [Theory]
    [InlineData("DocumentFolders")]
    [InlineData("DocumentPlacements")]
    public async Task Reachable_new_table_ownership_fails_the_runtime_guard(string table)
    {
        var role = "folder_owner_" + Guid.NewGuid().ToString("N");
        await fixture.Execute($"CREATE ROLE {role}; GRANT CREATE ON SCHEMA public TO {role}; GRANT {role} TO saveit_app WITH INHERIT FALSE; ALTER TABLE \"{table}\" OWNER TO {role}");
        try { await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(c)); }
        finally { await fixture.Execute($"ALTER TABLE \"{table}\" OWNER TO postgres; REVOKE {role} FROM saveit_app; REVOKE CREATE ON SCHEMA public FROM {role}; DROP ROLE {role}"); }
    }

    [Fact]
    public async Task Predecessor_migration_preserves_existing_documents_and_starts_them_unfiled_without_invented_placements()
    {
        await using var isolated = new DatabaseFixture(); await isolated.InitializeAsync();
        var scope = new TenantScope(); await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(isolated.OwnerConnection).Options, scope);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261031001255_OrganizationRevisionHistory");
        var oldDoc = Guid.NewGuid(); var oldAsset = await isolated.Scalar<Guid>($"SELECT \"Id\" FROM \"Assets\" WHERE \"OrganizationId\"='{DevelopmentSeed.OrgA}' LIMIT 1");
        await isolated.Execute($"INSERT INTO \"Documents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Content\",\"Version\",\"UpdatedAt\") VALUES ('{oldDoc}','{tenant}','{DevelopmentSeed.OrgA}','{oldAsset}','Existing version seven','Preserved exact body',7,now()); INSERT INTO \"DocumentRevisions\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"Version\",\"Title\",\"Content\",\"Actor\",\"CreatedAt\") VALUES ('{tenant}','{DevelopmentSeed.OrgA}','{oldDoc}',7,'Existing version seven','Preserved exact body','fixture',now())");
        await isolated.Execute($"INSERT INTO \"SearchEntries\" (\"TenantId\",\"RecordType\",\"Id\",\"OrganizationId\",\"AssetId\",\"Title\",\"Body\",\"Version\",\"UpdatedAt\",\"IndexedAt\") VALUES ('{tenant}','document','{oldDoc}','{DevelopmentSeed.OrgA}','{oldAsset}','Existing version seven','Preserved exact body',7,now(),now())");
        var before = await isolated.Scalar<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY d.\"Id\")::text FROM \"Documents\" d");
        var revisions = await isolated.Scalar<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY d.\"DocumentId\",d.\"Version\")::text FROM \"DocumentRevisions\" d");
        var projection = await isolated.Scalar<string>("SELECT row_to_json(s)::text FROM \"SearchEntries\" s LIMIT 1");
        await migrator.MigrateAsync();
        Assert.Equal(before, await isolated.Scalar<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY d.\"Id\")::text FROM \"Documents\" d"));
        Assert.Equal(revisions, await isolated.Scalar<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY d.\"DocumentId\",d.\"Version\")::text FROM \"DocumentRevisions\" d"));
        Assert.Equal(projection, await isolated.Scalar<string>("SELECT row_to_json(s)::text FROM \"SearchEntries\" s LIMIT 1"));
        Assert.Equal(0L, await isolated.Scalar<long>("SELECT count(*) FROM \"DocumentFolders\"")); Assert.Equal(0L, await isolated.Scalar<long>("SELECT count(*) FROM \"DocumentPlacements\""));
        using var f = new ApiFactory(isolated.AppConnection); using var c = f.CreateClient(new() { HandleCookies = true });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile = "alice" })).StatusCode);
        var page = (await c.GetFromJsonAsync<CursorPage<LibraryDocumentDto>>($"/api/v1/organizations/{DevelopmentSeed.OrgA}/library/documents"))!;
        Assert.NotEmpty(page.Items); Assert.All(page.Items, x => { Assert.Null(x.FolderId); Assert.Equal(0, x.PlacementVersion); });
        await migrator.MigrateAsync("20261031001255_OrganizationRevisionHistory");
        Assert.False(await isolated.Scalar<bool>("SELECT to_regprocedure('public.validate_document_folder_v1()') IS NOT NULL"));
        Assert.Equal(before, await isolated.Scalar<string>("SELECT jsonb_agg(to_jsonb(d) ORDER BY d.\"Id\")::text FROM \"Documents\" d"));
        await migrator.MigrateAsync();
    }

    [Fact]
    public async Task Invalid_names_versions_ids_and_noop_placements_are_rejected()
    {
        var f = await Folder(); var doc = await Document();
        foreach (var name in new[] { "", " ", new string('x',161), "Bad\0name" }) Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Base + "/folders", new { name })).StatusCode);
        foreach (var path in new[] { "/folders?parentId=bad", "/folders?after=bad", "/library/documents?folderId=bad", "/library/documents?after=bad" }) Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Base + path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Revise(f, null, 0)).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await Revise(f, null, 99)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await File(doc.Id, null)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await File(doc.Id, f.Id, -1)).StatusCode);
    }

    public async Task InitializeAsync()
    {
        client = await Login(); var r = await Write(client, "/api/v1/organizations", new { name = "ZZZ folder tests " + Guid.NewGuid() }); Assert.Equal(HttpStatusCode.Created, r.StatusCode); org = (await r.Content.ReadFromJsonAsync<OrganizationDto>())!.Id;
    }
    public async Task DisposeAsync()
    {
        await fixture.Execute($"DELETE FROM \"DocumentPlacements\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"DocumentRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"Documents\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"SearchEntries\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"AssetRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"Assets\" WHERE \"OrganizationId\"='{org}'");
        for (var i = 0; i < 8; i++) await fixture.Execute($"DELETE FROM \"DocumentFolders\" f WHERE f.\"OrganizationId\"='{org}' AND NOT EXISTS (SELECT 1 FROM \"DocumentFolders\" c WHERE c.\"TenantId\"=f.\"TenantId\" AND c.\"OrganizationId\"=f.\"OrganizationId\" AND c.\"ParentId\"=f.\"Id\")");
        await fixture.Execute($"DELETE FROM \"SearchDeliveries\" d USING \"OutboxEvents\" e WHERE d.\"EventId\"=e.\"Id\" AND e.\"OrganizationId\"='{org}'; DELETE FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"AuditEvents\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"OrganizationRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"Organizations\" WHERE \"Id\"='{org}'");
        client.Dispose(); await factory.DisposeAsync();
    }
}
