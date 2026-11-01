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
public sealed class DocumentTemplateTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private readonly Guid tenant = DevelopmentSeed.TenantA;
    private Guid org;
    private HttpClient client = null!;
    private string Base => $"/api/v1/organizations/{org}";
    private sealed record Csrf(string Token);
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object input, bool put = false)
    {
        var token = (await c.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        using var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", token.Token); return await c.SendAsync(request);
    }
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var c = factory.CreateClient(new() { HandleCookies = true }); Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile })).StatusCode); return c;
    }
    private async Task<DocumentTemplateDto> Template()
    {
        var r = await Write(client, Base + "/document-templates", new { name = "Private starter <img src=x> 😀", title = "Private title", content = "Private starter body\r\nExact\ttext" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<DocumentTemplateDto>())!;
    }
    private async Task<AssetDto> Asset()
    {
        var r = await Write(client, Base + "/assets", new { name = "Synthetic template asset", kind = "Server" }); Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetDto>())!;
    }
    private Task<HttpResponseMessage> Publish(DocumentTemplateDto t, bool archived = false, string content = "Private revised body") =>
        Write(client, Base + $"/document-templates/{t.Id}", new { name = t.Name, title = "Private revised title", content, expectedVersion = t.Version, isArchived = archived }, true);
    private Task<HttpResponseMessage> Create(DocumentTemplateDto t, Guid asset, HttpClient? c = null) => Write(c ?? client, Base + $"/document-templates/{t.Id}/documents", new { assetId = asset, templateVersion = t.Version, title = "Reviewed title", content = "Reviewed final body\r\nChanged from starter 😀" });
    private async Task<long[]> Counts()
    {
        var counts = new List<long>(); foreach (var table in new[] { "DocumentTemplates", "DocumentTemplateRevisions", "DocumentTemplateOrigins", "Documents", "DocumentRevisions", "AuditEvents", "OutboxEvents", "SearchDeliveries" }) counts.Add(await fixture.Scalar<long>($"SELECT count(*) FROM \"{table}\"")); return counts.ToArray();
    }
    private async Task Drain()
    {
        var p = new SearchProcessor(fixture.WorkerConnection); for (var i = 0; i < 30; i++) { var n = await p.ProcessBatchAsync(tenant); if (n.Completed + n.Retried + n.DeadLettered == 0) break; }
    }
    [Fact]
    public async Task Exact_immutable_templates_reviewed_documents_provenance_restore_placement_and_normal_search_persist()
    {
        var t = await Template(); var a = await Asset();
        var first = (await client.GetFromJsonAsync<DocumentTemplateRevisionDto>(Base + $"/document-templates/{t.Id}/revisions/1"))!;
        Assert.Equal("Private starter body\r\nExact\ttext", first.Content);
        var r = await Create(t, a.Id); Assert.Equal(HttpStatusCode.Created, r.StatusCode); var doc = (await r.Content.ReadFromJsonAsync<DocumentDto>())!;
        Assert.NotEqual(first.Content, doc.Content); Assert.Equal(1, doc.Version);
        var originPath = Base + $"/documents/{doc.Id}/template-origin";
        var origin = (await client.GetFromJsonAsync<DocumentTemplateOriginDto>(originPath))!; Assert.Equal(t.Id, origin.TemplateId); Assert.Equal(1, origin.TemplateVersion); Assert.Equal(t.Name, origin.TemplateName);
        Assert.Equal(0, (await client.GetFromJsonAsync<DocumentPlacementDto>(Base + $"/documents/{doc.Id}/placement"))!.Version);
        Assert.Equal(HttpStatusCode.OK, (await Publish(t, true)).StatusCode);
        Assert.Equal(first, await client.GetFromJsonAsync<DocumentTemplateRevisionDto>(Base + $"/document-templates/{t.Id}/revisions/1"));
        Assert.Equal(HttpStatusCode.Conflict, (await Create(t, a.Id)).StatusCode);
        var current = (await client.GetFromJsonAsync<DocumentTemplateDto>(Base + $"/document-templates/{t.Id}"))!;
        Assert.Equal(HttpStatusCode.OK, (await Publish(current)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Base + $"/documents/{doc.Id}", new { title = "Edited", content = "Other body", expectedVersion = 1 }, true)).StatusCode);
        var folder = await Write(client, Base + "/folders", new { name = "Retained destination" }); var f = (await folder.Content.ReadFromJsonAsync<FolderDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Base + $"/documents/{doc.Id}/placement", new { folderId = f.Id, expectedVersion = 0 }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Base + $"/documents/{doc.Id}/restore", new { sourceVersion = 1, expectedVersion = 2, reason = "Private restoration reason" })).StatusCode);
        Assert.Equal(origin, await client.GetFromJsonAsync<DocumentTemplateOriginDto>(originPath));
        Assert.Equal(f.Id, (await client.GetFromJsonAsync<DocumentPlacementDto>(Base + $"/documents/{doc.Id}/placement"))!.FolderId);
        Assert.Equal(doc.Content, (await client.GetFromJsonAsync<DocumentDto>(Base + $"/documents/{doc.Id}"))!.Content);
        await Drain(); Assert.Equal(doc.Content, await fixture.Scalar<string>($"SELECT \"Body\" FROM \"SearchEntries\" WHERE \"Id\"='{doc.Id}'"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{t.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" e JOIN \"SearchDeliveries\" d ON d.\"EventId\"=e.\"Id\" WHERE e.\"TargetId\"='{t.Id}' AND d.\"CompletedAt\" IS NOT NULL AND d.\"DeadLetteredAt\" IS NULL"));
        foreach (var table in new[] { "AuditEvents", "OutboxEvents" }) Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{table}\" e WHERE e.\"OrganizationId\"='{org}' AND to_jsonb(e)::text ~ '(Private|Reviewed|starter body)'"));
        var manual = await Write(client, Base + "/documents", new { assetId = a.Id, title = "Manual", content = "Manual body" }); var m = (await manual.Content.ReadFromJsonAsync<DocumentDto>())!;
        Assert.Null((await client.GetFromJsonAsync<DocumentTemplateOriginDto>(Base + $"/documents/{m.Id}/template-origin"))!.TemplateId);
    }
    [Fact]
    public async Task Live_roles_scopes_revocation_payload_spoofs_and_csrf_cannot_grant_authority()
    {
        var t = await Template(); var a = await Asset(); using var viewer = await Login("viewer"); using var bob = await Login("bob");
        await fixture.Execute($"INSERT INTO \"OrganizationGrants\" VALUES ('{tenant}','{DevelopmentSeed.Profiles.Single(x => x.Id == "viewer").MembershipId}','{org}','Viewer')");
        var paths = new[] { "/document-templates", $"/document-templates/{t.Id}", $"/document-templates/{t.Id}/revisions", $"/document-templates/{t.Id}/revisions/1" };
        foreach (var path in paths) { Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Base + path)).StatusCode); Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Base + path)).StatusCode); }
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(t, a.Id, viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Base + "/document-templates", new { name = "Denied", title = "Denied", content = "Denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Base + $"/document-templates/{t.Id}", new { name = "Denied", title = "Denied", content = "Denied", expectedVersion = 1 }, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(t, a.Id, bob)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Create(t, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/organizations/{DevelopmentSeed.OrgA2}/document-templates/{t.Id}/revisions/1")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Base + "/document-templates", new { name = "Denied", title = "Denied", content = "Denied" })).StatusCode);
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{org}' AND \"MembershipId\"='{DevelopmentSeed.Profiles.Single(x => x.Id == "alice").MembershipId}'");
        client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.ToString()); Assert.Equal(HttpStatusCode.NotFound, (await Create(t, a.Id)).StatusCode);
    }
    [Fact]
    public async Task Metadata_and_history_pages_are_bounded_body_free_and_use_column_restricted_sources()
    {
        var t = await Template(); for (var i = 1; i < 55; i++) await Template();
        var p = (await client.GetFromJsonAsync<CursorPage<DocumentTemplateDto>>(Base + "/document-templates"))!;
        var q = (await client.GetFromJsonAsync<CursorPage<DocumentTemplateDto>>(Base + "/document-templates?after=" + p.NextCursor))!;
        Assert.Equal(50, p.Items.Count); Assert.Equal(5, q.Items.Count); Assert.Null(q.NextCursor); Assert.Empty(p.Items.Select(x => x.Id).Intersect(q.Items.Select(x => x.Id)));
        for (var i = 1; i < 55; i++) { Assert.Equal(HttpStatusCode.OK, (await Publish(t)).StatusCode); t = t with { Version = t.Version + 1 }; }
        await fixture.Execute("REVOKE SELECT ON \"DocumentTemplateRevisions\" FROM saveit_app; GRANT SELECT (\"TenantId\",\"OrganizationId\",\"TemplateId\",\"Version\",\"Name\",\"IsArchived\",\"Actor\",\"CreatedAt\") ON \"DocumentTemplateRevisions\" TO saveit_app");
        try {
            var h = (await client.GetFromJsonAsync<CursorPage<DocumentTemplateRevisionSummaryDto>>(Base + $"/document-templates/{t.Id}/revisions"))!;
            Assert.Equal(50, h.Items.Count); Assert.Equal(55, h.Items[0].Version);
            var tail = (await client.GetFromJsonAsync<CursorPage<DocumentTemplateRevisionSummaryDto>>(Base + $"/document-templates/{t.Id}/revisions?before={h.NextCursor}"))!; Assert.Equal(5, tail.Items.Count); Assert.Null(tail.NextCursor);
            Assert.DoesNotContain("Private starter body", await client.GetStringAsync(Base + "/document-templates"));
            Assert.Equal(HttpStatusCode.InternalServerError, (await client.GetAsync(Base + $"/document-templates/{t.Id}/revisions/1")).StatusCode);
        } finally { await fixture.Execute("REVOKE SELECT (\"TenantId\",\"OrganizationId\",\"TemplateId\",\"Version\",\"Name\",\"IsArchived\",\"Actor\",\"CreatedAt\") ON \"DocumentTemplateRevisions\" FROM saveit_app; GRANT SELECT ON \"DocumentTemplateRevisions\" TO saveit_app"); }
    }
    [Theory]
    [InlineData("create", "DocumentTemplates")][InlineData("create", "DocumentTemplateRevisions")][InlineData("create", "AuditEvents")][InlineData("create", "OutboxEvents")][InlineData("create", "SearchDeliveries")]
    [InlineData("publish", "DocumentTemplates")][InlineData("publish", "DocumentTemplateRevisions")][InlineData("publish", "AuditEvents")][InlineData("publish", "OutboxEvents")][InlineData("publish", "SearchDeliveries")]
    [InlineData("document", "Documents")][InlineData("document", "DocumentRevisions")][InlineData("document", "DocumentTemplateOrigins")][InlineData("document", "AuditEvents")][InlineData("document", "OutboxEvents")][InlineData("document", "SearchDeliveries")]
    public async Task Every_mutation_boundary_rolls_back_atomically(string operation, string table)
    {
        var t = await Template(); var a = await Asset(); var before = await Counts();
        await fixture.Execute($"CREATE FUNCTION fail_template_write() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic template failure'; END $$; CREATE TRIGGER fail_template_write BEFORE INSERT OR UPDATE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_template_write()");
        try { var r = operation switch { "create" => await Write(client, Base + "/document-templates", new { name = "Failed", title = "Failed", content = "Failed" }), "publish" => await Publish(t), _ => await Create(t, a.Id) }; Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); Assert.Equal(before, await Counts()); }
        finally { await fixture.Execute($"DROP TRIGGER fail_template_write ON \"{table}\"; DROP FUNCTION fail_template_write()"); }
    }
    [Theory]
    [InlineData("DocumentTemplateRevisions")][InlineData("DocumentTemplateOrigins")][InlineData("DocumentTemplates")][InlineData("audit")]
    public async Task Source_or_audit_failure_withholds_content_metadata_and_origin(string table)
    {
        var t = await Template(); var a = await Asset(); var r = await Create(t, a.Id); var doc = (await r.Content.ReadFromJsonAsync<DocumentDto>())!;
        if (table == "audit") await fixture.Execute("CREATE FUNCTION fail_template_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic read failure'; END $$; CREATE TRIGGER fail_template_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_template_read()");
        else await fixture.Execute($"REVOKE SELECT ON \"{table}\" FROM saveit_app");
        try { var paths = table switch { "DocumentTemplates" => new[] { "/document-templates", $"/document-templates/{t.Id}" }, "DocumentTemplateOrigins" => new[] { $"/documents/{doc.Id}/template-origin" }, "DocumentTemplateRevisions" => new[] { $"/document-templates/{t.Id}/revisions/1", $"/documents/{doc.Id}/template-origin" }, _ => new[] { "/document-templates", $"/document-templates/{t.Id}/revisions/1", $"/documents/{doc.Id}/template-origin" } }; foreach (var path in paths) { var response = await client.GetAsync(Base + path); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); var body = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("Private", body); Assert.DoesNotContain("templateId", body); Assert.DoesNotContain("items", body); } }
        finally { if (table == "audit") await fixture.Execute("DROP TRIGGER fail_template_read ON \"AuditEvents\"; DROP FUNCTION fail_template_read()"); else await fixture.Execute($"GRANT SELECT ON \"{table}\" TO saveit_app"); }
    }
    [Theory]
    [InlineData("DocumentTemplates", "Name", "direct")][InlineData("DocumentTemplateRevisions", "Content", "PUBLIC")][InlineData("DocumentTemplateOrigins", "TemplateVersion", "role")]
    [InlineData("DocumentTemplates", "Name", "PUBLIC")][InlineData("DocumentTemplates", "Name", "role")]
    [InlineData("DocumentTemplateRevisions", "Content", "direct")][InlineData("DocumentTemplateRevisions", "Content", "role")]
    [InlineData("DocumentTemplateOrigins", "TemplateVersion", "direct")][InlineData("DocumentTemplateOrigins", "TemplateVersion", "PUBLIC")]
    public async Task Worker_rejects_direct_public_and_reachable_column_reads(string table, string column, string mode)
    {
        var role = "template_worker_" + Guid.NewGuid().ToString("N"); var grantee = mode == "direct" ? "saveit_search_worker" : mode == "PUBLIC" ? "PUBLIC" : role;
        if (mode == "role") await fixture.Execute($"CREATE ROLE {role}; GRANT {role} TO saveit_search_worker WITH INHERIT FALSE");
        await fixture.Execute($"GRANT SELECT (\"{column}\") ON \"{table}\" TO {grantee}");
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchProcessor(fixture.WorkerConnection).ValidateAsync()); }
        finally { await fixture.Execute($"REVOKE SELECT (\"{column}\") ON \"{table}\" FROM {grantee}"); if (mode == "role") await fixture.Execute($"REVOKE {role} FROM saveit_search_worker; DROP ROLE {role}"); }
        await new SearchProcessor(fixture.WorkerConnection).ValidateAsync();
    }
    [Fact]
    public async Task Invalid_text_versions_concurrent_publications_archived_sources_and_unknown_schemas_fail()
    {
        var t = await Template(); var a = await Asset();
        foreach (var content in new[] { "", " \r\n", new string('x',50001), "Bad\0body" }) Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Base + "/document-templates", new { name = "Name", title = "Title", content })).StatusCode);
        var race = await Task.WhenAll(Publish(t), Publish(t)); Assert.Single(race, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.Conflict, (await Create(t, a.Id)).StatusCode);
        await fixture.Execute($"INSERT INTO \"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES (gen_random_uuid(),'{tenant}','{org}','{t.Id}','DocumentTemplate.Revised',2,now()),(gen_random_uuid(),'{tenant}','{org}','{t.Id}','DocumentTemplate.Deleted',1,now())"); await Drain();
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchDeliveries\" d JOIN \"OutboxEvents\" e ON d.\"EventId\"=e.\"Id\" WHERE e.\"OrganizationId\"='{org}' AND d.\"ErrorCode\"='unsupported_event'"));
    }
    [Fact]
    public async Task Raw_rls_immutable_columns_initial_origin_and_deferred_tip_checks_are_enforced()
    {
        var t = await Template(); var a = await Asset(); var r = await Create(t, a.Id); var doc = (await r.Content.ReadFromJsonAsync<DocumentDto>())!;
        await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync();
        foreach (var table in new[] { "DocumentTemplates", "DocumentTemplateRevisions", "DocumentTemplateOrigins" }) {
            await using var q = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", c); Assert.Equal(0L, await q.ExecuteScalarAsync());
            Assert.True(await fixture.Scalar<bool>($"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname='{table}'"));
        }
        foreach (var (sql, state) in new[] {
            ($"UPDATE \"DocumentTemplates\" SET \"Name\"='Invalid' WHERE \"Id\"='{t.Id}'", "23514"),
            ($"UPDATE \"DocumentTemplates\" SET \"OrganizationId\"='{DevelopmentSeed.OrgA2}' WHERE \"Id\"='{t.Id}'", "42501"),
            ($"UPDATE \"DocumentTemplateRevisions\" SET \"Content\"='Rewritten' WHERE \"TemplateId\"='{t.Id}'", "42501"),
            ($"UPDATE \"DocumentTemplateOrigins\" SET \"TemplateVersion\"=2 WHERE \"DocumentId\"='{doc.Id}'", "42501"),
            ($"DELETE FROM \"DocumentTemplates\" WHERE \"Id\"='{t.Id}'", "42501"),
            ($"DELETE FROM \"DocumentTemplateRevisions\" WHERE \"TemplateId\"='{t.Id}'", "42501"),
            ($"DELETE FROM \"DocumentTemplateOrigins\" WHERE \"DocumentId\"='{doc.Id}'", "42501"),
            ($"INSERT INTO \"DocumentTemplates\" VALUES (gen_random_uuid(),'{DevelopmentSeed.TenantB}','{org}','Spoof',1,false,now())", "42501"),
            ($"INSERT INTO \"DocumentTemplateOrigins\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"DocumentVersion\",\"TemplateId\",\"TemplateVersion\") VALUES ('{tenant}','{org}','{doc.Id}',2,'{t.Id}',1)", "23514")
        }) {
            await using var tx = await c.BeginTransactionAsync(); await using (var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true)", c, tx)) await set.ExecuteNonQueryAsync();
            await using var q = new NpgsqlCommand(sql, c, tx); Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(async () => await q.ExecuteNonQueryAsync())).SqlState);
        }
        await using (var tx = await c.BeginTransactionAsync()) {
            await using var q = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true); UPDATE \"DocumentTemplates\" SET \"Version\"=2,\"Name\"='Missing snapshot' WHERE \"Id\"='{t.Id}'", c, tx); await q.ExecuteNonQueryAsync();
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).SqlState);
        }
        Assert.Equal(1, (await client.GetFromJsonAsync<DocumentTemplateDto>(Base + $"/document-templates/{t.Id}"))!.Version);
    }
    [Fact]
    public async Task Every_raw_publication_requires_its_own_snapshot_even_with_multiple_versions_in_one_transaction()
    {
        await using var c = new NpgsqlConnection(fixture.AppConnection); await c.OpenAsync();
        foreach (var missing in new[] { "initial", "middle", "none" }) {
            var id = Guid.NewGuid(); await using var tx = await c.BeginTransactionAsync();
            await using (var init = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true); INSERT INTO \"DocumentTemplates\" VALUES ('{id}','{tenant}','{org}','Raw publication',1,false,now())", c, tx)) await init.ExecuteNonQueryAsync();
            for (var version = 1; version <= 3; version++) {
                if (version > 1) { await using var update = new NpgsqlCommand($"UPDATE \"DocumentTemplates\" SET \"Version\"={version},\"UpdatedAt\"=clock_timestamp() WHERE \"Id\"='{id}'", c, tx); await update.ExecuteNonQueryAsync(); }
                if (missing == "none" || version == 3 || (version == 1 && missing == "middle")) { await using var snapshot = new NpgsqlCommand($"INSERT INTO \"DocumentTemplateRevisions\" SELECT \"TenantId\",\"OrganizationId\",\"Id\",\"Version\",\"Name\",'Raw title','Raw body',\"IsArchived\",'synthetic-raw',\"UpdatedAt\" FROM \"DocumentTemplates\" WHERE \"Id\"='{id}'", c, tx); await snapshot.ExecuteNonQueryAsync(); }
            }
            if (missing == "none") await tx.CommitAsync(); else Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync())).SqlState);
        }
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateRevisions\" WHERE \"OrganizationId\"='{org}'"));
    }
    [Theory][InlineData("DocumentTemplates")][InlineData("DocumentTemplateRevisions")][InlineData("DocumentTemplateOrigins")]
    public async Task Reachable_ownership_of_each_template_table_rejects_api_and_worker_startup(string table)
    {
        var role = "template_owner_" + Guid.NewGuid().ToString("N"); await fixture.Execute($"CREATE ROLE {role} NOLOGIN NOINHERIT; ALTER TABLE \"{table}\" OWNER TO {role}; GRANT {role} TO saveit_app WITH INHERIT FALSE; GRANT {role} TO saveit_search_worker WITH INHERIT FALSE");
        try { await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseRoleGuard.ValidateAsync(app)); await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchProcessor(fixture.WorkerConnection).ValidateAsync()); }
        finally { await fixture.Execute($"ALTER TABLE \"{table}\" OWNER TO postgres; REVOKE {role} FROM saveit_app,saveit_search_worker; DROP ROLE {role}"); }
    }
    [Fact]
    public async Task Revision_limit_rejects_publication_without_overflow_and_replay_keeps_projection_unchanged()
    {
        var t = await Template(); var a = await Asset(); var r = await Create(t, a.Id); var doc = (await r.Content.ReadFromJsonAsync<DocumentDto>())!; await Drain();
        var before = await fixture.Scalar<string>($"SELECT row_to_json(s)::text FROM \"SearchEntries\" s WHERE \"Id\"='{doc.Id}'");
        var eventId = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"TargetId\"='{t.Id}' AND \"Type\"='DocumentTemplate.Created'");
        var worker = new SearchProcessor(fixture.WorkerConnection); Assert.True(await worker.ReplayAsync(tenant, eventId)); await Drain();
        Assert.Equal(before, await fixture.Scalar<string>($"SELECT row_to_json(s)::text FROM \"SearchEntries\" s WHERE \"Id\"='{doc.Id}'"));
        // Owner-only fixture establishes an exhausted version; runtime UPDATE cannot skip versions.
        await fixture.Execute("ALTER TABLE \"DocumentTemplates\" DISABLE TRIGGER document_template_version");
        try { await fixture.Execute($"UPDATE \"DocumentTemplates\" SET \"Version\"=2147483647 WHERE \"Id\"='{t.Id}'; INSERT INTO \"DocumentTemplateRevisions\" SELECT \"TenantId\",\"OrganizationId\",\"Id\",\"Version\",\"Name\",'Limit title','Limit body',\"IsArchived\",'synthetic-limit',\"UpdatedAt\" FROM \"DocumentTemplates\" WHERE \"Id\"='{t.Id}'"); }
        finally { await fixture.Execute("ALTER TABLE \"DocumentTemplates\" ENABLE TRIGGER document_template_version"); }
        Assert.Equal(HttpStatusCode.Conflict, (await Publish(t with { Version = int.MaxValue })).StatusCode);
    }
    private async Task WaitForLocks(int count)
    {
        for (var i = 0; i < 100; i++) { if (await fixture.Scalar<long>("SELECT count(*) FROM pg_stat_activity WHERE usename='saveit_app' AND datname=current_database() AND wait_event_type='Lock'") >= count) return; await Task.Delay(30); }
        Assert.Fail("The expected real application lock wait was not observed.");
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task Publication_or_archive_committing_first_rejects_the_waiting_reviewed_creation(bool archive)
    {
        var t = await Template(); var a = await Asset(); await using var owner = new NpgsqlConnection(fixture.OwnerConnection); await owner.OpenAsync(); await using var tx = await owner.BeginTransactionAsync();
        await using (var update = new NpgsqlCommand($"UPDATE \"DocumentTemplates\" SET \"Version\"=2,\"IsArchived\"={(archive ? "true" : "false")},\"UpdatedAt\"=now() WHERE \"Id\"='{t.Id}'; INSERT INTO \"DocumentTemplateRevisions\" SELECT h.\"TenantId\",h.\"OrganizationId\",h.\"Id\",h.\"Version\",h.\"Name\",'Published title','Published body',h.\"IsArchived\",'synthetic-race',h.\"UpdatedAt\" FROM \"DocumentTemplates\" h WHERE h.\"Id\"='{t.Id}'", owner, tx)) await update.ExecuteNonQueryAsync();
        var create = Create(t, a.Id);
        try { await WaitForLocks(1); await tx.CommitAsync(); Assert.Equal(HttpStatusCode.Conflict, (await create.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode); }
        finally { await tx.DisposeAsync(); await create; }
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateOrigins\" WHERE \"OrganizationId\"='{org}'"));
    }
    [Fact]
    public async Task Creation_holds_source_share_lock_until_document_origin_audit_outbox_commit_before_publisher()
    {
        var t = await Template(); var a = await Asset(); await using var owner = new NpgsqlConnection(fixture.OwnerConnection); await owner.OpenAsync(); await using var tx = await owner.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("SELECT pg_advisory_xact_lock(190020)", owner, tx)) await hold.ExecuteNonQueryAsync();
        await fixture.Execute($"CREATE FUNCTION hold_template_creation() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"OrganizationId\"='{org}' AND NEW.\"Action\"='Document.Created' THEN PERFORM pg_advisory_xact_lock(190020); END IF; RETURN NEW; END $$; CREATE TRIGGER hold_template_creation BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION hold_template_creation()");
        var create = Create(t, a.Id); Task<HttpResponseMessage>? publish = null;
        try { await WaitForLocks(1); publish = Publish(t, true); await WaitForLocks(2); await tx.CommitAsync(); Assert.Equal(HttpStatusCode.Created, (await create.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode); Assert.Equal(HttpStatusCode.OK, (await publish.WaitAsync(TimeSpan.FromSeconds(10))).StatusCode); }
        finally { await tx.DisposeAsync(); await create; if (publish is not null) await publish; await fixture.Execute("DROP TRIGGER hold_template_creation ON \"AuditEvents\"; DROP FUNCTION hold_template_creation()"); }
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateOrigins\" WHERE \"OrganizationId\"='{org}' AND \"TemplateVersion\"=1"));
    }
    [Fact]
    public async Task Additive_migration_preserves_existing_content_history_and_placement_without_inventing_origins()
    {
        await using var isolated = new DatabaseFixture(); await isolated.InitializeAsync();
        var scope = new TenantScope(); await using var db = new SaveItDb(new DbContextOptionsBuilder<SaveItDb>().UseNpgsql(isolated.OwnerConnection).Options, scope);
        var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261031013417_DocumentFolders");
        var asset = Guid.NewGuid(); var doc = Guid.NewGuid(); var folder = Guid.NewGuid();
        await isolated.Execute($"INSERT INTO \"Assets\" (\"Id\",\"TenantId\",\"OrganizationId\",\"Name\",\"Kind\",\"CreatedAt\",\"Version\",\"UpdatedAt\",\"FieldsJson\") VALUES ('{asset}','{tenant}','{DevelopmentSeed.OrgA}','Legacy asset','Server',now(),1,now(),'{{}}'); INSERT INTO \"Documents\" VALUES ('{doc}','{tenant}','{DevelopmentSeed.OrgA}','{asset}','Legacy title','Legacy body',7,now()); INSERT INTO \"DocumentRevisions\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"Version\",\"Title\",\"Content\",\"Actor\",\"CreatedAt\") SELECT \"TenantId\",\"OrganizationId\",\"Id\",7,\"Title\",\"Content\",'legacy',\"UpdatedAt\" FROM \"Documents\" WHERE \"Id\"='{doc}'; INSERT INTO \"DocumentFolders\" VALUES ('{folder}','{tenant}','{DevelopmentSeed.OrgA}',NULL,'Legacy folder',1,now()); INSERT INTO \"DocumentPlacements\" VALUES ('{tenant}','{DevelopmentSeed.OrgA}','{doc}','{folder}',1,now())");
        var before = new List<string>(); foreach (var table in new[] { "Documents", "DocumentRevisions", "DocumentPlacements" }) before.Add(await isolated.Scalar<string>($"SELECT jsonb_agg(to_jsonb(t))::text FROM \"{table}\" t"));
        await migrator.MigrateAsync(); Assert.Equal(0L, await isolated.Scalar<long>("SELECT count(*) FROM \"DocumentTemplateOrigins\""));
        var after = new List<string>(); foreach (var table in new[] { "Documents", "DocumentRevisions", "DocumentPlacements" }) after.Add(await isolated.Scalar<string>($"SELECT jsonb_agg(to_jsonb(t))::text FROM \"{table}\" t")); Assert.Equal(before, after);
        await migrator.MigrateAsync("20261031013417_DocumentFolders"); Assert.False(await isolated.Scalar<bool>("SELECT to_regprocedure('public.validate_document_template_v1()') IS NOT NULL")); await migrator.MigrateAsync();
    }
    public async Task InitializeAsync()
    {
        client = await Login(); var r = await Write(client, "/api/v1/organizations", new { name = "ZZZ template tests " + Guid.NewGuid() }); Assert.Equal(HttpStatusCode.Created, r.StatusCode); org = (await r.Content.ReadFromJsonAsync<OrganizationDto>())!.Id;
    }
    public async Task DisposeAsync()
    {
        await fixture.Execute($"DELETE FROM \"DocumentTemplateOrigins\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"DocumentTemplateRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"DocumentTemplates\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"DocumentPlacements\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"DocumentRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"Documents\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"DocumentFolders\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"SearchEntries\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"AssetRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"Assets\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"SearchDeliveries\" d USING \"OutboxEvents\" e WHERE d.\"EventId\"=e.\"Id\" AND e.\"OrganizationId\"='{org}'; DELETE FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"AuditEvents\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"OrganizationRevisions\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{org}'; DELETE FROM \"Organizations\" WHERE \"Id\"='{org}'");
        client.Dispose(); await factory.DisposeAsync();
    }
}
