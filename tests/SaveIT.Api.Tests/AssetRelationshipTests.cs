using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class AssetRelationshipTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static readonly Guid Org = DevelopmentSeed.OrgA;
    private static readonly Guid Tenant = DevelopmentSeed.TenantA;
    private static string Path(Guid asset, Guid? org = null) => $"/api/v1/organizations/{org ?? Org}/assets/{asset}/relationships";
    private sealed record Csrf(string Token);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var client = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, "/api/v1/dev/login", new { profile })).StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, object body)
    {
        var token = (await client.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token.Token);
        return await client.SendAsync(request);
    }
    private static async Task<AssetDto> Asset(HttpClient client, Guid? org = null, string name = "Synthetic relationship endpoint")
    {
        var response = await Write(client, $"/api/v1/organizations/{org ?? Org}/assets", new { name, kind = "Network" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetDto>())!;
    }
    private static Task<HttpResponseMessage> Create(HttpClient client, Guid source, Guid target, string kind = "DependsOn", Guid? org = null) =>
        Write(client, Path(source, org), new { targetAssetId = target, kind });
    private static Task<HttpResponseMessage> Remove(HttpClient client, Guid asset, Guid edge, int expected = 1, Guid? org = null) =>
        Write(client, Path(asset, org) + $"/{edge}/remove", new { expectedVersion = expected });
    private static async Task<AssetRelationshipDto> Link(HttpClient client, Guid source, Guid target, string kind = "DependsOn")
    {
        var response = await Create(client, source, target, kind); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AssetRelationshipDto>())!;
    }
    private static async Task<CursorPage<AssetRelationshipDto>> List(HttpClient client, Guid asset, string suffix = "") =>
        (await client.GetFromJsonAsync<CursorPage<AssetRelationshipDto>>(Path(asset) + suffix))!;

    [Fact]
    public async Task Persistent_directed_links_are_visible_from_both_endpoints_and_changes_are_audited()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client);
        var edge = await Link(client, a.Id, b.Id);
        Assert.Equal(1, edge.Version); Assert.Equal("alice", edge.CreatedBy);
        using var fresh = await Login();
        var forward = Assert.Single((await List(fresh, a.Id)).Items);
        Assert.Equal(forward, Assert.Single((await List(fresh, b.Id)).Items));
        Assert.Equal(edge with { CreatedAt = forward.CreatedAt }, forward);
        Assert.InRange((edge.CreatedAt - forward.CreatedAt).Ticks, 0, 9); // PostgreSQL stores microseconds.
        await Link(client, b.Id, a.Id, "Contains");
        Assert.Equal(2, (await List(fresh, a.Id)).Items.Count);
        Assert.True(await fixture.Scalar<bool>($"SELECT EXISTS(SELECT 1 FROM \"AuditEvents\" WHERE \"TargetId\"='{edge.Id}' AND \"Action\"='AssetRelationship.Created')"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{edge.Id}' AND \"Type\"='AssetRelationship.Created' AND \"SchemaVersion\"=1"));
    }

    [Fact]
    public async Task Viewer_can_read_but_not_create_or_remove_and_both_tenant_and_organization_boundaries_hold()
    {
        using var client = await Login(); using var viewer = await Login("viewer"); using var bob = await Login("bob");
        var a = await Asset(client); var b = await Asset(client); var harbor = await Asset(client, DevelopmentSeed.OrgA2);
        var edge = await Link(client, a.Id, b.Id);
        Assert.Single((await List(viewer, a.Id)).Items);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(viewer, a.Id, b.Id, "Contains")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Remove(viewer, a.Id, edge.Id)).StatusCode);
        foreach (var foreign in new[] { harbor.Id, Guid.Parse("20000000-0000-0000-0000-000000001000"), Guid.NewGuid() })
            Assert.Equal(HttpStatusCode.NotFound, (await Create(client, a.Id, foreign)).StatusCode);
        foreach (var (actor, org) in new[] { (client, DevelopmentSeed.OrgA2), (bob, DevelopmentSeed.OrgB), (bob, Org) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync(Path(a.Id, org))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Create(actor, a.Id, b.Id, org: org)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Remove(actor, a.Id, edge.Id, org: org)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await Remove(client, harbor.Id, edge.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Remove(client, a.Id, Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Concurrent_duplicate_creates_have_one_winner_and_only_one_audit_and_event()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client);
        var results = await Task.WhenAll(Create(client, a.Id, b.Id), Create(client, a.Id, b.Id));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var edge = Assert.Single((await List(client, a.Id)).Items);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{edge.Id}'"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{edge.Id}'"));
    }

    [Fact]
    public async Task Concurrent_removals_preserve_endpoints_and_provenance_and_allow_a_new_link()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client); var edge = await Link(client, a.Id, b.Id);
        var results = await Task.WhenAll(Remove(client, a.Id, edge.Id), Remove(client, b.Id, edge.Id));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Empty((await List(client, a.Id)).Items); Assert.Empty((await List(client, b.Id)).Items);
        Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"AssetRelationships\" WHERE \"Id\"='{edge.Id}' AND \"CreatedBy\"='alice' AND \"RemovedBy\"='alice' AND \"RemovedAt\" IS NOT NULL"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"Assets\" WHERE \"Id\" IN ('{a.Id}','{b.Id}')"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{edge.Id}'"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{edge.Id}'"));
        var replacement = await Link(client, a.Id, b.Id); Assert.NotEqual(edge.Id, replacement.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await Remove(client, a.Id, replacement.Id, expected: 2)).StatusCode);
    }

    [Theory]
    [InlineData(false, "audit")]
    [InlineData(false, "outbox")]
    [InlineData(true, "audit")]
    [InlineData(true, "outbox")]
    public async Task Failed_audit_or_outbox_rolls_back_creation_or_removal(bool removing, string boundary)
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client);
        var edge = removing ? await Link(client, a.Id, b.Id) : null;
        var action = removing ? "AssetRelationship.Removed" : "AssetRelationship.Created";
        var table = boundary == "audit" ? "AuditEvents" : "OutboxEvents"; var column = boundary == "audit" ? "Action" : "Type";
        await fixture.Execute($"CREATE FUNCTION fail_relationship() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"{column}\"='{action}' THEN RAISE EXCEPTION 'Synthetic link failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_relationship BEFORE INSERT ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION fail_relationship()");
        try
        {
            var result = removing ? await Remove(client, a.Id, edge!.Id) : await Create(client, a.Id, b.Id);
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            var links = (await List(client, a.Id)).Items;
            if (removing) { Assert.Equal(edge!.Id, Assert.Single(links).Id); Assert.Equal(1, links[0].Version); }
            else Assert.Empty(links);
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"OrganizationId\"='{Org}' AND \"Action\"='{action}' AND \"TargetId\" IN (SELECT \"Id\" FROM \"AssetRelationships\" WHERE \"SourceAssetId\"='{a.Id}')"));
            Assert.Equal(removing ? 1L : 0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\" IN (SELECT \"Id\" FROM \"AssetRelationships\" WHERE \"SourceAssetId\"='{a.Id}')"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_relationship ON \"{table}\"; DROP FUNCTION fail_relationship()"); }
    }

    [Fact]
    public async Task Revoked_organization_and_membership_take_effect_on_the_existing_cookie()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client); var edge = await Link(client, a.Id, b.Id);
        var member = DevelopmentSeed.Profiles[0].MembershipId;
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{Org}'");
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Path(a.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Create(client, b.Id, a.Id)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Remove(client, a.Id, edge.Id)).StatusCode);
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await Remove(client, a.Id, edge.Id)).StatusCode);
        }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'; INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{Tenant}','{member}','{Org}','Editor') ON CONFLICT DO NOTHING"); }
        Assert.Single((await List(client, a.Id)).Items);
    }

    [Fact]
    public async Task Database_rls_both_endpoint_foreign_keys_checks_and_column_grants_reject_raw_bypasses()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client); var harbor = await Asset(client, DevelopmentSeed.OrgA2);
        var edge = await Link(client, a.Id, b.Id);
        await using var connection = new NpgsqlConnection(fixture.AppConnection); await connection.OpenAsync();
        await using (var count = new NpgsqlCommand("SELECT count(*) FROM \"AssetRelationships\"", connection)) Assert.Equal(0L, await count.ExecuteScalarAsync());
        Assert.True(await fixture.Scalar<bool>("SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='\"AssetRelationships\"'::regclass"));
        foreach (var (sql, state) in new[] {
            ($"UPDATE \"AssetRelationships\" SET \"SourceAssetId\"='{b.Id}' WHERE \"Id\"='{edge.Id}'", "42501"),
            ($"DELETE FROM \"AssetRelationships\" WHERE \"Id\"='{edge.Id}'", "42501"),
            ($"UPDATE \"AssetRelationships\" SET \"Version\"=2 WHERE \"Id\"='{edge.Id}'", "23514"),
            ($"INSERT INTO \"AssetRelationships\" (\"Id\",\"TenantId\",\"OrganizationId\",\"SourceAssetId\",\"TargetAssetId\",\"Kind\",\"Version\",\"CreatedBy\",\"CreatedAt\") VALUES (gen_random_uuid(),'{Tenant}','{Org}','{a.Id}','{harbor.Id}','Contains',1,'alice',now())", "23503"),
            ($"INSERT INTO \"AssetRelationships\" (\"Id\",\"TenantId\",\"OrganizationId\",\"SourceAssetId\",\"TargetAssetId\",\"Kind\",\"Version\",\"CreatedBy\",\"CreatedAt\") VALUES (gen_random_uuid(),'{Tenant}','{Org}','{harbor.Id}','{b.Id}','Contains',1,'alice',now())", "23503"),
            ($"INSERT INTO \"AssetRelationships\" (\"Id\",\"TenantId\",\"OrganizationId\",\"SourceAssetId\",\"TargetAssetId\",\"Kind\",\"Version\",\"CreatedBy\",\"CreatedAt\") VALUES (gen_random_uuid(),'{Tenant}','{Org}','{a.Id}','20000000-0000-0000-0000-000000001000','Contains',1,'alice',now())", "23503"),
            ($"INSERT INTO \"AssetRelationships\" (\"Id\",\"TenantId\",\"OrganizationId\",\"SourceAssetId\",\"TargetAssetId\",\"Kind\",\"Version\",\"CreatedBy\",\"CreatedAt\") VALUES (gen_random_uuid(),'{DevelopmentSeed.TenantB}','{DevelopmentSeed.OrgB}','20000000-0000-0000-0000-000000001000','{b.Id}','Contains',1,'alice',now())", "42501"),
            ($"INSERT INTO \"AssetRelationships\" (\"Id\",\"TenantId\",\"OrganizationId\",\"SourceAssetId\",\"TargetAssetId\",\"Kind\",\"Version\",\"CreatedBy\",\"CreatedAt\") VALUES (gen_random_uuid(),'{Tenant}','{Org}','{a.Id}','{a.Id}','Contains',1,'alice',now())", "23514") })
        {
            await using var transaction = await connection.BeginTransactionAsync();
            await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{Tenant}',true)", connection, transaction); await set.ExecuteNonQueryAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            Assert.Equal(state, (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState);
        }
        await using var worker = new NpgsqlConnection(fixture.WorkerConnection); await worker.OpenAsync();
        await using var read = new NpgsqlCommand("SELECT * FROM \"AssetRelationships\"", worker);
        Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await read.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task Pages_are_bounded_disjoint_and_currently_authorized_and_read_audit_failure_withholds_labels()
    {
        using var client = await Login(); var source = await Asset(client);
        await fixture.Execute($"WITH targets AS (INSERT INTO \"Assets\" (\"Id\",\"TenantId\",\"OrganizationId\",\"Name\",\"Kind\",\"CreatedAt\") SELECT gen_random_uuid(),'{Tenant}','{Org}','Link label canary','Synthetic',now() FROM generate_series(1,51) RETURNING \"Id\") INSERT INTO \"AssetRelationships\" (\"Id\",\"TenantId\",\"OrganizationId\",\"SourceAssetId\",\"TargetAssetId\",\"Kind\",\"Version\",\"CreatedBy\",\"CreatedAt\") SELECT gen_random_uuid(),'{Tenant}','{Org}','{source.Id}',\"Id\",'DependsOn',1,'alice',now() FROM targets");
        var first = await List(client, source.Id); Assert.Equal(50, first.Items.Count); Assert.NotNull(first.NextCursor);
        var second = await List(client, source.Id, "?after=" + first.NextCursor); Assert.Single(second.Items); Assert.Null(second.NextCursor);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        await fixture.Execute("CREATE FUNCTION fail_relationship_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='Asset.RelationshipsRead' THEN RAISE EXCEPTION 'Synthetic read failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_relationship_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_relationship_read()");
        try { var response = await client.GetAsync(Path(source.Id)); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); Assert.DoesNotContain("Link label canary", await response.Content.ReadAsStringAsync()); }
        finally { await fixture.Execute("DROP TRIGGER fail_relationship_read ON \"AuditEvents\"; DROP FUNCTION fail_relationship_read()"); }
    }

    [Fact]
    public async Task Invalid_requests_csrf_and_anonymous_access_are_rejected()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client); var edge = await Link(client, a.Id, b.Id);
        foreach (var kind in new[] { "", "dependson", "Unknown", "Contains\0" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Create(client, a.Id, b.Id, kind)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(client, a.Id, a.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Create(client, a.Id, Guid.Empty)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Remove(client, a.Id, edge.Id, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Path(a.Id) + "?after=bad")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path(a.Id), new { targetAssetId = b.Id, kind = "Contains" })).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Path(a.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(anonymous, a.Id, b.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Remove(anonymous, a.Id, edge.Id)).StatusCode);
    }

    [Fact]
    public async Task Search_acknowledges_only_known_relationship_schemas_without_projecting_links_and_replays_safely()
    {
        using var client = await Login(); var a = await Asset(client); var b = await Asset(client); var edge = await Link(client, a.Id, b.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await Remove(client, a.Id, edge.Id)).StatusCode);
        var processor = new SearchProcessor(fixture.WorkerConnection);
        // Other test fixtures may leave more than a batch; drain bounded work in this isolated test database.
        for (var i = 0; i < 5; i++) await processor.ProcessBatchAsync(Tenant);
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchDeliveries\" d JOIN \"OutboxEvents\" e ON d.\"EventId\"=e.\"Id\" WHERE e.\"TargetId\"='{edge.Id}' AND d.\"CompletedAt\" IS NOT NULL AND d.\"DeadLetteredAt\" IS NULL"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchEntries\" WHERE \"Id\"='{edge.Id}'"));
        var eventId = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"TargetId\"='{edge.Id}' AND \"Type\"='AssetRelationship.Created'");
        Assert.True(await processor.ReplayAsync(Tenant, eventId)); await processor.ProcessBatchAsync(Tenant);
        Assert.True(await fixture.Scalar<bool>($"SELECT \"CompletedAt\" IS NOT NULL FROM \"SearchDeliveries\" WHERE \"EventId\"='{eventId}'"));
        Assert.True(await fixture.Scalar<bool>($"SELECT bool_and(\"DeliveredAt\" IS NULL) FROM \"OutboxEvents\" WHERE \"TargetId\"='{edge.Id}'"));
        var unsupported = Guid.NewGuid();
        await fixture.Execute($"INSERT INTO \"OutboxEvents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"TargetId\",\"Type\",\"SchemaVersion\",\"CreatedAt\") VALUES ('{unsupported}','{Tenant}','{Org}','{edge.Id}','AssetRelationship.Created',2,now())");
        await processor.ProcessBatchAsync(Tenant);
        Assert.True(await fixture.Scalar<bool>($"SELECT \"DeadLetteredAt\" IS NOT NULL FROM \"SearchDeliveries\" WHERE \"EventId\"='{unsupported}' AND \"ErrorCode\"='unsupported_event'"));
        // Column-level core write grants must also make the worker startup guard fail.
        await fixture.Execute("GRANT UPDATE (\"RemovedAt\") ON \"AssetRelationships\" TO saveit_search_worker");
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ValidateAsync()); }
        finally { await fixture.Execute("REVOKE UPDATE (\"RemovedAt\") ON \"AssetRelationships\" FROM saveit_search_worker"); }
    }

    public void Dispose() => factory.Dispose();
}
