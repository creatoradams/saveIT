using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class SearchTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private readonly SearchProcessor processor = new(fixture.WorkerConnection);
    private static readonly Guid A = DevelopmentSeed.OrgA;
    private static readonly Guid A2 = DevelopmentSeed.OrgA2;
    private static readonly Guid B = DevelopmentSeed.OrgB;
    private static readonly Guid TA = DevelopmentSeed.TenantA;
    private static readonly Guid TB = DevelopmentSeed.TenantB;
    private static string Key() => "search" + Guid.NewGuid().ToString("N");
    private static string Org(Guid org) => $"/api/v1/organizations/{org}";
    private static string Query(string q, Guid? org = null, string? type = null, string? after = null) =>
        "/api/v1/search?q=" + Uri.EscapeDataString(q) + (org.HasValue ? "&organizationId=" + org : "")
        + (type is null ? "" : "&type=" + type) + (after is null ? "" : "&after=" + Uri.EscapeDataString(after));
    private sealed record Csrf(string Token);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var client = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, HttpMethod.Post, "/api/v1/dev/login", new { profile })).StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string url, object body)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/v1/csrf");
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }
    private static async Task<AssetDto> Asset(HttpClient client, Guid org, string name)
    {
        var response = await Write(client, HttpMethod.Post, Org(org) + "/assets", new { name, kind = "Network" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
    }
    private static async Task<DocumentDto> Doc(HttpClient client, Guid org, Guid assetId, string title, string content)
    {
        var response = await Write(client, HttpMethod.Post, Org(org) + "/documents", new { assetId, title, content });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
    }
    private static async Task<SearchPage> Find(HttpClient client, string query, Guid? org = null, string? type = null, string? after = null)
    {
        var response = await client.GetAsync(Query(query, org, type, after));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SearchPage>())!;
    }

    [Fact]
    public async Task Global_and_scoped_search_filter_current_tenant_organization_and_record_type()
    {
        using var alice = await Login(); using var bob = await Login("bob"); using var viewer = await Login("viewer");
        var key = Key();
        var a = await Asset(alice, A, key); var a2 = await Asset(alice, A2, key); var b = await Asset(bob, B, key);
        var doc = await Doc(alice, A, a.Id, "Procedure", key + " authorized procedure");
        await Doc(bob, B, b.Id, "Procedure", key + " foreign procedure");
        await processor.ProcessBatchAsync(TA); await processor.ProcessBatchAsync(TB);
        var alicePage = await Find(alice, key);
        Assert.Equal(3, alicePage.Items.Count); Assert.DoesNotContain(alicePage.Items, x => x.OrganizationId == B);
        Assert.Contains(alicePage.Items, x => x.Id == a2.Id);
        var viewerPage = await Find(viewer, key);
        Assert.Equal(2, viewerPage.Items.Count); Assert.All(viewerPage.Items, x => Assert.Equal(A, x.OrganizationId));
        var docs = await Find(alice, key, A, "document");
        Assert.Equal(doc.Id, Assert.Single(docs.Items).Id); Assert.Contains("authorized procedure", docs.Items[0].Preview);
        var bobPage = await Find(bob, key);
        Assert.All(bobPage.Items, x => Assert.Equal(B, x.OrganizationId));
        Assert.DoesNotContain("foreign procedure", await (await alice.GetAsync(Query(key))).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Query(key, A))).StatusCode);
        Assert.True(await fixture.Scalar<bool>($"SELECT EXISTS(SELECT 1 FROM \"AuditEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Action\"='Search.DocumentRead')"));
    }

    [Fact]
    public async Task Stale_index_cannot_reveal_removed_content_or_matches_after_a_revision()
    {
        using var alice = await Login(); var oldKey = Key(); var newKey = Key();
        var asset = await Asset(alice, A, "Router"); var doc = await Doc(alice, A, asset.Id, "Procedure", oldKey);
        await processor.ProcessBatchAsync(TA);
        Assert.Single((await Find(alice, oldKey)).Items);
        Assert.Equal(HttpStatusCode.OK, (await Write(alice, HttpMethod.Put, Org(A) + $"/documents/{doc.Id}", new { title = "New procedure", content = newKey, expectedVersion = 1 })).StatusCode);
        Assert.Empty((await Find(alice, oldKey)).Items); Assert.Empty((await Find(alice, newKey)).Items);
        await processor.ProcessBatchAsync(TA);
        var current = Assert.Single((await Find(alice, newKey)).Items);
        Assert.Equal(2, current.Version); Assert.Equal(newKey, current.Preview);
        Assert.Empty((await Find(alice, oldKey)).Items);
    }

    [Fact]
    public async Task Revocation_hides_results_and_pagination_with_an_unchanged_projection_and_cookie()
    {
        using var alice = await Login(); var key = Key(); var asset = await Asset(alice, A2, key);
        await processor.ProcessBatchAsync(TA); Assert.Single((await Find(alice, key)).Items);
        var member = DevelopmentSeed.Profiles[0].MembershipId;
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{A2}'");
            var hidden = await Find(alice, key); Assert.Empty(hidden.Items); Assert.Null(hidden.NextCursor);
            Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Query(key, A2))).StatusCode);
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{asset.Id}'"));
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync(Query(key))).StatusCode);
        }
        finally
        {
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'; INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{TA}','{member}','{A2}','Editor') ON CONFLICT DO NOTHING");
        }
    }

    [Fact]
    public async Task Cursor_pages_are_bounded_query_bound_and_do_not_count_inaccessible_rows()
    {
        var key = Key();
        foreach (var (tenant, org, count) in new[] { (TA, A2, 31), (TB, B, 40) })
            for (var i = 0; i < count; i++)
                await fixture.Execute($"INSERT INTO \"Assets\" (\"Id\",\"TenantId\",\"OrganizationId\",\"Name\",\"Kind\",\"CreatedAt\") VALUES (gen_random_uuid(),'{tenant}','{org}','{key}','Network',now())");
        await processor.RebuildAsync(TA); await processor.RebuildAsync(TB);
        using var alice = await Login(); using var viewer = await Login("viewer"); using var bob = await Login("bob");
        var first = await Find(alice, key); Assert.Equal(25, first.Items.Count); Assert.NotNull(first.NextCursor);
        var second = await Find(alice, key, after: first.NextCursor); Assert.Equal(6, second.Items.Count); Assert.Null(second.NextCursor);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        var denied = await Find(viewer, key); Assert.Empty(denied.Items); Assert.Null(denied.NextCursor);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.GetAsync(Query(key, after: first.NextCursor))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(Query("different query", after: first.NextCursor))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(Query(key, A, after: first.NextCursor))).StatusCode);
    }

    [Fact]
    public async Task Search_outage_returns_unavailable_without_cached_or_unaudited_previews()
    {
        using var alice = await Login(); var key = Key(); await Asset(alice, A, key);
        await processor.ProcessBatchAsync(TA);
        await fixture.Execute("ALTER TABLE \"SearchEntries\" RENAME TO \"SearchEntries_unavailable\"");
        try
        {
            var response = await alice.GetAsync(Query(key));
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain(key, await response.Content.ReadAsStringAsync());
        }
        finally { await fixture.Execute("ALTER TABLE \"SearchEntries_unavailable\" RENAME TO \"SearchEntries\""); }
        Assert.Single((await Find(alice, key)).Items);
    }

    [Fact]
    public async Task Search_audit_failure_withholds_all_previews_and_cursor()
    {
        using var alice = await Login(); var key = Key(); await Asset(alice, A, key);
        await processor.ProcessBatchAsync(TA);
        await fixture.Execute("CREATE FUNCTION fail_search_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" LIKE 'Search.%' THEN RAISE EXCEPTION 'Synthetic audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_search_audit BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_search_audit()");
        try
        {
            var response = await alice.GetAsync(Query(key)); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.DoesNotContain(key, await response.Content.ReadAsStringAsync());
        }
        finally { await fixture.Execute("DROP TRIGGER fail_search_audit ON \"AuditEvents\"; DROP FUNCTION fail_search_audit()"); }
    }

    [Fact]
    public async Task Duplicate_reordered_and_concurrent_delivery_rehydrates_latest_state_once_per_event()
    {
        using var alice = await Login(); var asset = await Asset(alice, A, "Router"); var key = Key();
        var doc = await Doc(alice, A, asset.Id, "Original", "Old contents");
        await Write(alice, HttpMethod.Put, Org(A) + $"/documents/{doc.Id}", new { title = "Current", content = key, expectedVersion = 1 });
        await Task.WhenAll(processor.ProcessBatchAsync(TA), new SearchProcessor(fixture.WorkerConnection).ProcessBatchAsync(TA));
        var oldEvent = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Type\"='Document.Created'");
        Assert.True(await processor.ReplayAsync(TA, oldEvent)); await processor.ProcessBatchAsync(TA);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{doc.Id}' AND \"Version\"=2 AND \"Body\"='{key}'"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchDeliveries\" d JOIN \"OutboxEvents\" e ON e.\"Id\"=d.\"EventId\" WHERE e.\"TargetId\"='{doc.Id}' AND d.\"CompletedAt\" IS NOT NULL"));
        Assert.Single((await Find(alice, key)).Items);
    }

    [Fact]
    public async Task Projection_failure_retries_backs_off_dead_letters_and_can_be_replayed()
    {
        await processor.ProcessBatchAsync(TA);
        using var alice = await Login(); var asset = await Asset(alice, A, Key());
        var eventId = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"TargetId\"='{asset.Id}'");
        await fixture.Execute("CREATE FUNCTION fail_search_projection() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic projection failure'; END $$; CREATE TRIGGER fail_search_projection BEFORE INSERT ON \"SearchEntries\" FOR EACH ROW EXECUTE FUNCTION fail_search_projection()");
        try
        {
            var first = await processor.ProcessBatchAsync(TA); Assert.Equal(1, first.Retried);
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{asset.Id}'"));
            Assert.Equal(0, (await processor.ProcessBatchAsync(TA)).Retried);
            Assert.True(await fixture.Scalar<bool>($"SELECT \"NextAttemptAt\">now() AND \"CompletedAt\" IS NULL FROM \"SearchDeliveries\" WHERE \"EventId\"='{eventId}'"));
            for (var i = 0; i < 4; i++)
            {
                await fixture.Execute($"UPDATE \"SearchDeliveries\" SET \"NextAttemptAt\"=now()-interval '1 second' WHERE \"EventId\"='{eventId}'");
                await processor.ProcessBatchAsync(TA);
            }
            Assert.True(await fixture.Scalar<bool>($"SELECT \"Attempts\"=5 AND \"DeadLetteredAt\" IS NOT NULL AND \"ErrorCode\"='projection_database_failure' FROM \"SearchDeliveries\" WHERE \"EventId\"='{eventId}'"));
        }
        finally { await fixture.Execute("DROP TRIGGER fail_search_projection ON \"SearchEntries\"; DROP FUNCTION fail_search_projection()"); }
        Assert.True(await processor.ReplayAsync(TA, eventId)); Assert.Equal(1, (await processor.ProcessBatchAsync(TA)).Completed);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{asset.Id}'"));
    }

    [Fact]
    public async Task Failed_receipt_commit_rolls_back_projection_and_is_recoverable()
    {
        await processor.ProcessBatchAsync(TA); using var alice = await Login(); var asset = await Asset(alice, A, Key());
        await fixture.Execute("CREATE FUNCTION fail_search_receipt() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"CompletedAt\" IS NOT NULL THEN RAISE EXCEPTION 'Synthetic receipt failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_search_receipt BEFORE INSERT OR UPDATE ON \"SearchDeliveries\" FOR EACH ROW EXECUTE FUNCTION fail_search_receipt()");
        try
        {
            Assert.Equal(1, (await processor.ProcessBatchAsync(TA)).Retried);
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{asset.Id}'"));
        }
        finally { await fixture.Execute("DROP TRIGGER fail_search_receipt ON \"SearchDeliveries\"; DROP FUNCTION fail_search_receipt()"); }
        await fixture.Execute("UPDATE \"SearchDeliveries\" SET \"NextAttemptAt\"=now()-interval '1 second' WHERE \"CompletedAt\" IS NULL AND \"DeadLetteredAt\" IS NULL");
        Assert.True((await processor.ProcessBatchAsync(TA)).Completed >= 1);
    }

    [Fact]
    public async Task Unknown_schema_and_mismatched_scope_are_quarantined_without_changing_core_records()
    {
        await processor.ProcessBatchAsync(TA); using var alice = await Login(); var asset = await Asset(alice, A, Key());
        await processor.ProcessBatchAsync(TA);
        foreach (var (schema, org) in new[] { (99, A), (1, A2) })
            await fixture.Execute($"INSERT INTO \"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES (gen_random_uuid(),'{TA}','{org}','{asset.Id}','Asset.Created',{schema},now())");
        Assert.Equal(2, (await processor.ProcessBatchAsync(TA)).DeadLettered);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{asset.Id}' AND \"OrganizationId\"='{A}'"));
        Assert.Equal(A, await fixture.Scalar<Guid>($"SELECT \"OrganizationId\" FROM \"Assets\" WHERE \"Id\"='{asset.Id}'"));
    }

    [Fact]
    public async Task Rebuild_and_missing_source_recovery_are_tenant_scoped()
    {
        using var alice = await Login(); var key = Key(); var asset = await Asset(alice, A, "Router");
        var doc = await Doc(alice, A, asset.Id, "Procedure", key);
        await processor.ProcessBatchAsync(TA); var beforeB = await processor.RebuildAsync(TB);
        await fixture.Execute($"DELETE FROM \"SearchEntries\" WHERE \"TenantId\"='{TA}'");
        Assert.Empty((await Find(alice, key)).Items); Assert.True(await processor.RebuildAsync(TA) > 0);
        Assert.Single((await Find(alice, key)).Items);
        Assert.Equal((long)beforeB, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"TenantId\"='{TB}'"));
        await fixture.Execute($"DELETE FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'; DELETE FROM \"Documents\" WHERE \"Id\"='{doc.Id}'");
        Assert.Empty((await Find(alice, key)).Items);
        var eventId = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}' LIMIT 1");
        await processor.ReplayAsync(TA, eventId); await processor.ProcessBatchAsync(TA);
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{doc.Id}'"));
    }

    [Fact]
    public async Task Worker_and_application_roles_have_separate_least_privilege_and_forced_rls()
    {
        await using var worker = new NpgsqlConnection(fixture.WorkerConnection); await worker.OpenAsync();
        foreach (var table in new[] { "Assets", "Documents", "OutboxEvents", "SearchEntries", "SearchDeliveries", "CoreSearchSources" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", worker);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        foreach (var sql in new[] { "UPDATE \"Assets\" SET \"Name\"='Forbidden'", "UPDATE \"OutboxEvents\" SET \"DeliveredAt\"=now()", "SELECT * FROM \"Memberships\"", "INSERT INTO \"AuditEvents\" DEFAULT VALUES" })
        {
            await using var command = new NpgsqlCommand(sql, worker);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState);
        }
        await using (var transaction = await worker.BeginTransactionAsync())
        {
            await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{TA}',true)", worker, transaction);
            await set.ExecuteNonQueryAsync();
            await using var foreignRead = new NpgsqlCommand($"SELECT count(*) FROM \"CoreSearchSources\" WHERE \"TenantId\"='{TB}'", worker, transaction);
            Assert.Equal(0L, await foreignRead.ExecuteScalarAsync());
            await using var foreignWrite = new NpgsqlCommand($"INSERT INTO \"SearchEntries\" (\"TenantId\",\"RecordType\",\"Id\",\"OrganizationId\",\"AssetId\",\"Title\",\"Body\",\"Version\",\"UpdatedAt\",\"IndexedAt\") VALUES ('{TB}','asset',gen_random_uuid(),'{B}','20000000-0000-0000-0000-000000001000','Forbidden','Synthetic',1,now(),now())", worker, transaction);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await foreignWrite.ExecuteNonQueryAsync())).SqlState);
        }
        await using var app = new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync();
        await using var forbidden = new NpgsqlCommand("DELETE FROM \"SearchEntries\"", app);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await forbidden.ExecuteNonQueryAsync())).SqlState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchProcessor(fixture.OwnerConnection).ValidateAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SearchProcessor(fixture.AppConnection).ValidateAsync());
    }

    [Fact]
    public async Task Search_rejects_anonymous_invalid_queries_and_mismatched_cursors()
    {
        using var anonymous = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Query("Router"))).StatusCode);
        using var alice = await Login();
        foreach (var q in new[] { "", "x", "bad\0query", new string('x', 121), string.Join(' ', Enumerable.Repeat("word", 13)) })
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(Query(q))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(Query("Router", type: "vault"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.GetAsync(Query("Router", after: "forged-cursor"))).StatusCode);
        Assert.Empty((await Find(alice, "') ; DROP TABLE nonexistent; --")).Items);
    }

    public void Dispose() => factory.Dispose();
}
