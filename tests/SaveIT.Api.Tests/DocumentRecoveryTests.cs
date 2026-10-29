using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class DocumentRecoveryTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static readonly Guid Org = DevelopmentSeed.OrgA;
    private static readonly Guid Tenant = DevelopmentSeed.TenantA;
    private static string Path(Guid id, Guid? org = null) => $"/api/v1/organizations/{org ?? Org}/documents/{id}";
    private sealed record Csrf(string Token);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var client = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, HttpMethod.Post, "/api/v1/dev/login", new { profile })).StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, HttpMethod method, string path, object body)
    {
        var token = await client.GetFromJsonAsync<Csrf>("/api/v1/csrf");
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token!.Token);
        return await client.SendAsync(request);
    }
    private static async Task<DocumentDto> Create(HttpClient client, string original = "Check the uplink.\nKeep a recovery copy.", string latest = "Check the standby.\nKeep a recovery copy.")
    {
        var response = await Write(client, HttpMethod.Post, $"/api/v1/organizations/{Org}/documents", new {
            assetId = "10000000-0000-0000-0000-000000001000", title = "Original recovery guide", content = original
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var doc = (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
        response = await Write(client, HttpMethod.Put, Path(doc.Id), new { title = "Current recovery guide", content = latest, expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
    }
    private static Task<HttpResponseMessage> Restore(HttpClient client, Guid id, int source = 1, int expected = 2, string reason = "Recover the verified procedure") =>
        Write(client, HttpMethod.Post, Path(id) + "/restore", new { sourceVersion = source, expectedVersion = expected, reason });

    [Fact]
    public async Task Comparison_returns_faithful_changes_and_durable_read_audit_without_content_in_audit()
    {
        using var client = await Login(); var doc = await Create(client);
        var comparison = (await client.GetFromJsonAsync<DocumentComparisonDto>(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2"))!;
        Assert.True(comparison.TitleChanged); Assert.True(comparison.ContentChanged); Assert.False(comparison.Simplified);
        Assert.Equal(2, comparison.CurrentVersion); Assert.Equal(1, comparison.AddedLines); Assert.Equal(1, comparison.RemovedLines);
        Assert.Contains(comparison.Lines, x => x.Kind == "removed" && x.Text == "Check the uplink." && x.OldLine == 1);
        Assert.Contains(comparison.Lines, x => x.Kind == "added" && x.Text == "Check the standby." && x.NewLine == 1);
        Assert.Contains(comparison.Lines, x => x.Kind == "unchanged" && x.OldLine == 2 && x.NewLine == 2);
        Assert.True(await fixture.Scalar<bool>($"SELECT EXISTS(SELECT 1 FROM \"AuditEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Action\"='Document.Compared' AND \"SourceVersion\"=1 AND \"TargetVersion\"=2)"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}'"));
    }

    [Fact]
    public async Task Restore_preserves_history_and_provenance_and_search_rehydrates_new_revision()
    {
        using var client = await Login(); var oldTerm = "recover" + Guid.NewGuid().ToString("N"); var currentTerm = "current" + Guid.NewGuid().ToString("N");
        var doc = await Create(client, oldTerm, currentTerm);
        var processor = new SearchProcessor(fixture.WorkerConnection); await processor.ProcessBatchAsync(Tenant);
        var reason = "Recover the independently checked procedure";
        var response = await Restore(client, doc.Id, reason: reason); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var restored = (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
        Assert.Equal(3, restored.Version); Assert.Equal(oldTerm, restored.Content); Assert.Equal("Original recovery guide", restored.Title);
        Assert.Equal(doc.AssetId, restored.AssetId); Assert.Equal(doc.OrganizationId, restored.OrganizationId);
        using var fresh = await Login();
        Assert.Equal(oldTerm, (await fresh.GetFromJsonAsync<DocumentDto>(Path(doc.Id)))!.Content);
        var history = (await fresh.GetFromJsonAsync<CursorPage<RevisionDto>>(Path(doc.Id) + "/revisions"))!;
        Assert.Equal(new[] { 3, 2, 1 }, history.Items.Select(x => x.Version));
        Assert.Equal(1, history.Items[0].RestoredFromVersion); Assert.Equal(reason, history.Items[0].RestoreReason);
        Assert.Equal(currentTerm, history.Items[1].Content); Assert.Equal(oldTerm, history.Items[2].Content);
        Assert.Null(history.Items[1].RestoredFromVersion);
        Assert.True(await fixture.Scalar<bool>($"SELECT EXISTS(SELECT 1 FROM \"AuditEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Action\"='Document.Restored' AND \"SourceVersion\"=1 AND \"TargetVersion\"=3)"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Type\"='Document.Restored'"));
        Assert.False(await fixture.Scalar<bool>($"SELECT EXISTS(SELECT 1 FROM \"AuditEvents\" a WHERE a.\"TargetId\"='{doc.Id}' AND to_jsonb(a)::text LIKE '%{reason}%')"));
        Assert.Empty((await fresh.GetFromJsonAsync<SearchPage>("/api/v1/search?q=" + currentTerm))!.Items);
        await processor.ProcessBatchAsync(Tenant);
        var hit = Assert.Single((await fresh.GetFromJsonAsync<SearchPage>("/api/v1/search?q=" + oldTerm))!.Items);
        Assert.Equal(3, hit.Version); Assert.Equal(doc.Id, hit.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Restore(client, doc.Id)).StatusCode);
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'"));
    }

    [Fact]
    public async Task Viewer_can_compare_but_restore_is_denied_and_cross_scope_ids_are_hidden()
    {
        using var alice = await Login(); var doc = await Create(alice); using var viewer = await Login("viewer"); using var bob = await Login("bob");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Restore(viewer, doc.Id)).StatusCode);
        foreach (var (client, org) in new[] { (bob, DevelopmentSeed.OrgB), (alice, DevelopmentSeed.OrgA2), (bob, Org) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(doc.Id, org) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Write(client, HttpMethod.Post, Path(doc.Id, org) + "/restore", new { sourceVersion = 1, expectedVersion = 2, reason = "Synthetic restore" })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Path(Guid.NewGuid()) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Path(doc.Id) + "/compare?fromVersion=99&toVersion=2")).StatusCode);
        Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Documents\" WHERE \"Id\"='{doc.Id}'"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_restore_or_edit_has_one_winner_and_no_false_history(bool competeWithEdit)
    {
        using var client = await Login(); var doc = await Create(client);
        var competitor = competeWithEdit
            ? Write(client, HttpMethod.Put, Path(doc.Id), new { title = "Concurrent edit", content = "Independent new procedure", expectedVersion = 2 })
            : Restore(client, doc.Id);
        var results = await Task.WhenAll(Restore(client, doc.Id), competitor);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(3, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Documents\" WHERE \"Id\"='{doc.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}'"));
    }

    [Theory]
    [InlineData("audit")]
    [InlineData("outbox")]
    public async Task Restore_failure_rolls_back_document_revision_audit_and_outbox_together(string boundary)
    {
        using var client = await Login(); var doc = await Create(client);
        var table = boundary == "audit" ? "AuditEvents" : "OutboxEvents";
        var action = boundary == "audit" ? "Action" : "Type";
        await fixture.Execute($"CREATE FUNCTION fail_document_restore() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"{action}\"='Document.Restored' THEN RAISE EXCEPTION 'Synthetic restore failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_document_restore BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_document_restore()");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Restore(client, doc.Id)).StatusCode);
            Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Documents\" WHERE \"Id\"='{doc.Id}'"));
            Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'"));
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Action\"='Document.Restored'"));
            Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}'"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_document_restore ON \"{table}\"; DROP FUNCTION fail_document_restore()"); }
    }

    [Fact]
    public async Task Comparison_audit_failure_withholds_historical_content()
    {
        using var client = await Login(); var canary = "compare" + Guid.NewGuid().ToString("N"); var doc = await Create(client, canary);
        await fixture.Execute("CREATE FUNCTION fail_comparison_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='Document.Compared' THEN RAISE EXCEPTION 'Synthetic comparison audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_comparison_audit BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_comparison_audit()");
        try
        {
            var response = await client.GetAsync(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2");
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); Assert.DoesNotContain(canary, await response.Content.ReadAsStringAsync());
        }
        finally { await fixture.Execute("DROP TRIGGER fail_comparison_audit ON \"AuditEvents\"; DROP FUNCTION fail_comparison_audit()"); }
    }

    [Fact]
    public async Task Revocation_between_comparison_and_restore_is_effective_with_existing_cookie()
    {
        using var client = await Login(); var doc = await Create(client); var member = DevelopmentSeed.Profiles[0].MembershipId;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{Org}'");
            Assert.Equal(HttpStatusCode.NotFound, (await Restore(client, doc.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Restore(client, doc.Id)).StatusCode);
        }
        finally
        {
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'; INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{Tenant}','{member}','{Org}','Editor') ON CONFLICT DO NOTHING");
        }
        Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Documents\" WHERE \"Id\"='{doc.Id}'"));
    }

    [Fact]
    public async Task Large_comparisons_are_bounded_paginated_and_report_simplified_replacements()
    {
        using var client = await Login();
        var doc = await Create(client, string.Join('\n', Enumerable.Range(0, 1200).Select(i => "old " + i)), string.Join('\n', Enumerable.Range(0, 1200).Select(i => "new " + i)));
        var page = (await client.GetFromJsonAsync<DocumentComparisonDto>(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2"))!;
        Assert.True(page.Simplified); Assert.Equal(1200, page.AddedLines); Assert.Equal(1200, page.RemovedLines);
        Assert.Equal(2400, page.TotalRows); Assert.Equal(200, page.Lines.Count); Assert.Equal(200, page.NextOffset);
        var next = (await client.GetFromJsonAsync<DocumentComparisonDto>(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2&offset=200"))!;
        Assert.Equal("old 200", next.Lines[0].Text); Assert.Equal(201, next.Lines[0].OldLine);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2&offset=2400")).StatusCode);
        var same = (await client.GetFromJsonAsync<DocumentComparisonDto>(Path(doc.Id) + "/compare?fromVersion=1&toVersion=1"))!;
        Assert.False(same.TitleChanged); Assert.False(same.ContentChanged); Assert.Equal(0, same.AddedLines); Assert.Equal(0, same.RemovedLines);
    }

    [Fact]
    public async Task Restore_rejects_invalid_inputs_missing_csrf_anonymous_requests_and_no_op_sources()
    {
        using var client = await Login(); var doc = await Create(client);
        foreach (var (source, expected, reason) in new[] { (0, 2, "Valid reason"), (1, 0, "Valid reason"), (2, 2, "Valid reason"),
            (99, 2, "Valid reason"), (1, 2, ""), (1, 2, "  "), (1, 2, "x"), (1, 2, new string('x', 501)), (1, 2, "bad\0reason") })
            Assert.Equal(HttpStatusCode.BadRequest, (await Restore(client, doc.Id, source, expected, reason)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Restore(client, doc.Id, expected: 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(doc.Id) + "/restore", new { sourceVersion = 1, expectedVersion = 2, reason = "Missing CSRF" })).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await Restore(anonymous, doc.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(doc.Id) + "/compare?fromVersion=1&toVersion=2")).StatusCode);
    }

    [Fact]
    public async Task Database_provenance_rejects_partial_metadata_and_references_to_another_documents_history()
    {
        using var client = await Login(); var doc = await Create(client); var other = await Create(client);
        await Write(client, HttpMethod.Put, Path(other.Id), new { title = "Other history", content = "Other version three", expectedVersion = 2 });
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        foreach (var (source, reason, expectedState) in new[] { ("1", "NULL", "23514"), ("NULL", "'Partial provenance'", "23514"), ("3", "'Foreign version reference'", "23503") })
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{Tenant}',true)", connection, transaction); await set.ExecuteNonQueryAsync();
            await using var insert = new NpgsqlCommand($"INSERT INTO \"DocumentRevisions\" (\"TenantId\",\"OrganizationId\",\"DocumentId\",\"Version\",\"Title\",\"Content\",\"Actor\",\"CreatedAt\",\"RestoredFromVersion\",\"RestoreReason\") VALUES ('{Tenant}','{Org}','{doc.Id}',4,'Synthetic','Synthetic','alice',now(),{source},{reason})", connection, transaction);
            Assert.Equal(expectedState, (await Assert.ThrowsAsync<PostgresException>(async () => await insert.ExecuteNonQueryAsync())).SqlState);
        }
    }

    public void Dispose() => factory.Dispose();
}
