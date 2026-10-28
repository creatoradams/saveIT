using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SaveIT.Api.Data;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class VerticalSliceTests(DatabaseFixture fixture)
{
    private static readonly Guid A = DevelopmentSeed.OrgA;
    private static readonly Guid A2 = DevelopmentSeed.OrgA2;
    private static readonly Guid B = DevelopmentSeed.OrgB;
    private static readonly Guid AssetA = Guid.Parse("10000000-0000-0000-0000-000000001000");
    private static readonly Guid AssetB = Guid.Parse("20000000-0000-0000-0000-000000001000");
    private static string Org(Guid id) => $"/api/v1/organizations/{id}";

    private async Task<HttpClient> Login(string profile)
    {
        var client = fixture.Factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        var response = await Mutate(client, HttpMethod.Post, "/api/v1/dev/login", new { profile });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return client;
    }
    private static async Task<HttpResponseMessage> Mutate(HttpClient client, HttpMethod method, string url, object input)
    {
        var csrf = await client.GetFromJsonAsync<Csrf>("/api/v1/csrf");
        var request = new HttpRequestMessage(method, url) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.Token);
        return await client.SendAsync(request);
    }
    private async Task<DocumentDto> CreateDocument(HttpClient client, string content = "Synthetic procedure")
    {
        var response = await Mutate(client, HttpMethod.Post, Org(A) + "/documents", new { assetId = AssetA, title = "Recovery runbook", content });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DocumentDto>())!;
    }
    private sealed record Csrf(string Token);

    [Fact]
    public async Task Full_flow_persists_assets_documents_revisions_audit_and_outbox()
    {
        using var alice = await Login("alice");
        var orgs = await alice.GetFromJsonAsync<OrganizationDto[]>("/api/v1/organizations");
        Assert.Equal(2, orgs!.Length);
        var name = "Edge firewall " + Guid.NewGuid().ToString("N");
        var created = await Mutate(alice, HttpMethod.Post, Org(A) + "/assets", new { name, kind = "Network" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var asset = (await created.Content.ReadFromJsonAsync<AssetDto>())!;
        Assert.Equal(A, asset.OrganizationId);
        var docResponse = await Mutate(alice, HttpMethod.Post, Org(A) + "/documents", new { assetId = asset.Id, title = "Failover", content = "Step one" });
        var doc = (await docResponse.Content.ReadFromJsonAsync<DocumentDto>())!;
        Assert.Equal(1, doc.Version);
        var revised = await Mutate(alice, HttpMethod.Put, Org(A) + $"/documents/{doc.Id}", new { title = "Failover", content = "Step two", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        // A new session exercises durable storage rather than a client-side optimistic state.
        using var freshSession = await Login("alice");
        var persisted = await freshSession.GetFromJsonAsync<DocumentDto>(Org(A) + $"/documents/{doc.Id}");
        Assert.Equal("Step two", persisted!.Content);
        Assert.Equal(2, persisted.Version);
        var history = await freshSession.GetFromJsonAsync<CursorPage<RevisionDto>>(Org(A) + $"/documents/{doc.Id}/revisions");
        Assert.Equal(new[] { 2, 1 }, history!.Items.Select(x => x.Version));
        Assert.Equal("Step one", history.Items[1].Content);
        var audit = await freshSession.GetFromJsonAsync<CursorPage<AuditDto>>(Org(A) + "/audit");
        Assert.Contains(audit!.Items, x => x.TargetId == asset.Id && x.Action == "Asset.Created" && x.Actor == "alice");
        Assert.Contains(audit.Items, x => x.TargetId == doc.Id && x.Action == "Document.Revised" && x.CorrelationId.Length > 0);
        Assert.Contains(audit.Items, x => x.TargetId == doc.Id && x.Action == "Document.Read");
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"SchemaVersion\"=1 AND \"DeliveredAt\" IS NULL"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"Assets\" WHERE \"Id\"='{asset.Id}'"));
    }

    [Fact]
    public async Task Guessed_ids_cross_tenant_and_cross_organization_links_are_hidden()
    {
        using var alice = await Login("alice");
        using var bob = await Login("bob");
        var doc = await CreateDocument(alice);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Org(A) + "/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Org(B) + $"/assets/{AssetA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Org(B) + $"/documents/{doc.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync(Org(B) + $"/documents/{doc.Id}/revisions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Org(A2) + $"/documents/{doc.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Org(Guid.NewGuid()) + "/audit")).StatusCode);
        foreach (var (org, foreignAsset) in new[] { (A, AssetB), (A2, AssetA) })
        {
            var response = await Mutate(alice, HttpMethod.Post, Org(org) + "/documents", new { assetId = foreignAsset, title = "Forbidden", content = "Synthetic" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        var bobAssets = await bob.GetFromJsonAsync<CursorPage<AssetDto>>(Org(B) + "/assets");
        Assert.Contains(bobAssets!.Items, x => x.Name == "Core switch");
        Assert.All(bobAssets.Items, x => Assert.Equal(B, x.OrganizationId));
    }

    [Fact]
    public async Task Read_only_permissions_are_enforced_for_every_write_and_audit_read()
    {
        using var alice = await Login("alice");
        var doc = await CreateDocument(alice);
        using var viewer = await Login("viewer");
        var orgs = await viewer.GetFromJsonAsync<OrganizationDto[]>("/api/v1/organizations");
        Assert.Single(orgs!); Assert.Equal("Viewer", orgs![0].Role);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Org(A) + "/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Org(A) + $"/documents/{doc.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Org(A) + $"/documents/{doc.Id}/revisions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync(Org(A2) + "/assets")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(Org(A) + "/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(viewer, HttpMethod.Post, Org(A) + "/assets", new { name = "Denied", kind = "Network" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(viewer, HttpMethod.Post, Org(A) + "/documents", new { assetId = AssetA, title = "Denied", content = "Synthetic" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(viewer, HttpMethod.Put, Org(A) + $"/documents/{doc.Id}", new { title = "Denied", content = "Synthetic", expectedVersion = 1 })).StatusCode);
    }

    [Fact]
    public async Task Organization_switching_and_untrusted_tenant_input_do_not_change_authority()
    {
        using var alice = await Login("alice");
        alice.DefaultRequestHeaders.Add("X-Tenant-ID", DevelopmentSeed.TenantB.ToString());
        var session = await alice.GetFromJsonAsync<SessionDto>("/api/v1/session");
        Assert.Equal(DevelopmentSeed.TenantA, session!.TenantId);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Org(B) + "/assets")).StatusCode);
        var newAsset = await Mutate(alice, HttpMethod.Post, Org(A2) + "/assets", new { name = "Harbor router", kind = "Network", tenantId = DevelopmentSeed.TenantB });
        var asset = (await newAsset.Content.ReadFromJsonAsync<AssetDto>())!;
        Assert.Equal(A2, asset.OrganizationId);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Org(A) + $"/assets/{asset.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync(Org(A2) + $"/assets/{asset.Id}")).StatusCode);
        Assert.Equal(DevelopmentSeed.TenantA, await fixture.Scalar<Guid>($"SELECT \"TenantId\" FROM \"Assets\" WHERE \"Id\"='{asset.Id}'"));
    }

    [Fact]
    public async Task Stale_and_concurrent_revisions_do_not_overwrite_or_add_false_events()
    {
        using var alice = await Login("alice");
        var doc = await CreateDocument(alice);
        var requests = await Task.WhenAll(Enumerable.Range(1, 2).Select(i => Mutate(alice, HttpMethod.Put, Org(A) + $"/documents/{doc.Id}", new { title = "Race", content = "Writer " + i, expectedVersion = 1 })));
        Assert.Single(requests, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(requests, x => x.StatusCode == HttpStatusCode.Conflict);
        var stale = await Mutate(alice, HttpMethod.Put, Org(A) + $"/documents/{doc.Id}", new { title = "Stale", content = "Do not persist", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentRevisions\" WHERE \"DocumentId\"='{doc.Id}'"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{doc.Id}'"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{doc.Id}' AND \"Action\"='Document.Revised'"));
    }

    [Fact]
    public async Task Revocation_is_effective_on_next_request_with_existing_cookie()
    {
        using var alice = await Login("alice");
        var membership = DevelopmentSeed.Profiles[0].MembershipId;
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{membership}' AND \"OrganizationId\"='{A2}'");
            Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(Org(A2) + "/assets")).StatusCode);
            Assert.DoesNotContain((await alice.GetFromJsonAsync<OrganizationDto[]>("/api/v1/organizations"))!, x => x.Id == A2);
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{membership}'");
            Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync(Org(A) + "/assets")).StatusCode);
        }
        finally
        {
            await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{membership}'; INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{DevelopmentSeed.TenantA}','{membership}','{A2}','Editor') ON CONFLICT DO NOTHING");
        }
    }

    [Fact]
    public async Task Anonymous_forged_session_and_csrf_less_writes_are_rejected()
    {
        using var anonymous = fixture.Factory.CreateClient();
        anonymous.DefaultRequestHeaders.Add("X-User", "alice");
        anonymous.DefaultRequestHeaders.Add("X-Tenant-ID", DevelopmentSeed.TenantA.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/organizations")).StatusCode);
        anonymous.DefaultRequestHeaders.Add("Cookie", "SaveIT.Development=forged");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/dev/login", new { profile = "alice" })).StatusCode);
        using var alice = await Login("alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync(Org(A) + "/assets", new { name = "CSRF", kind = "Network" })).StatusCode);
    }

    [Fact]
    public async Task Invalid_payloads_never_create_records()
    {
        using var alice = await Login("alice");
        foreach (var name in new[] { "", "   ", new string('x', 161) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(alice, HttpMethod.Post, Org(A) + "/assets", new { name, kind = "Network" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(alice, HttpMethod.Post, Org(A) + "/documents", new { assetId = AssetA, title = "Large", content = new string('x', 50001) })).StatusCode);
    }

    [Fact]
    public async Task Rls_protects_all_tables_with_actual_app_role_and_no_context_denies_all()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnection);
        await connection.OpenAsync();
        foreach (var table in new[] { "Tenants", "Memberships", "Organizations", "OrganizationGrants", "Assets", "Documents", "DocumentRevisions", "AuditEvents", "OutboxEvents" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"", connection);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var setTenant = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantA}',true)", connection, transaction);
            await setTenant.ExecuteNonQueryAsync();
            await using var raw = new NpgsqlCommand($"SELECT count(*) FROM \"Assets\" WHERE \"Id\"='{AssetB}'", connection, transaction);
            Assert.Equal(0L, await raw.ExecuteScalarAsync());
            await transaction.CommitAsync();
        }
        await using var reset = new NpgsqlCommand("SELECT count(*) FROM \"Assets\"", connection);
        Assert.Equal(0L, await reset.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Database_rejects_cross_tenant_writes_and_cross_scope_foreign_keys()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnection);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantA}',true)", connection, transaction); await set.ExecuteNonQueryAsync();
            await using var insert = new NpgsqlCommand($"INSERT INTO \"Assets\" (\"Id\",\"TenantId\",\"OrganizationId\",\"Name\",\"Kind\",\"CreatedAt\") VALUES (gen_random_uuid(),'{DevelopmentSeed.TenantB}','{B}','Forbidden','Network',now())", connection, transaction);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await insert.ExecuteNonQueryAsync())).SqlState);
        }
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using var set = new NpgsqlCommand($"SELECT set_config('app.tenant_id','{DevelopmentSeed.TenantA}',true)", connection, transaction); await set.ExecuteNonQueryAsync();
            await using var insert = new NpgsqlCommand($"INSERT INTO \"Documents\" (\"Id\",\"TenantId\",\"OrganizationId\",\"AssetId\",\"Title\",\"Content\",\"Version\",\"UpdatedAt\") VALUES (gen_random_uuid(),'{DevelopmentSeed.TenantA}','{A2}','{AssetA}','Forbidden','Synthetic',1,now())", connection, transaction);
            Assert.Equal("23503", (await Assert.ThrowsAsync<PostgresException>(async () => await insert.ExecuteNonQueryAsync())).SqlState);
        }
    }

    [Fact]
    public async Task App_role_cannot_rewrite_audit_history_or_revisions_or_its_own_grants()
    {
        await using var connection = new NpgsqlConnection(fixture.AppConnection);
        await connection.OpenAsync();
        foreach (var sql in new[] { "DELETE FROM \"AuditEvents\"", "UPDATE \"DocumentRevisions\" SET \"Content\"='Changed'", "UPDATE \"OrganizationGrants\" SET \"Role\"='Editor'", "CREATE TABLE public.elevation(id int)" })
        {
            await using var command = new NpgsqlCommand(sql, connection);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(async () => await command.ExecuteNonQueryAsync())).SqlState);
        }
    }

    [Fact]
    public async Task Outbox_failure_rolls_back_business_change_and_audit_together()
    {
        using var alice = await Login("alice");
        var name = "Rollback " + Guid.NewGuid().ToString("N");
        var auditsBefore = await fixture.Scalar<long>("SELECT count(*) FROM \"AuditEvents\"");
        var outboxBefore = await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\"");
        await fixture.Execute("CREATE FUNCTION fail_test_outbox() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Synthetic outbox failure'; END $$; CREATE TRIGGER fail_outbox BEFORE INSERT ON \"OutboxEvents\" FOR EACH ROW EXECUTE FUNCTION fail_test_outbox()");
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await Mutate(alice, HttpMethod.Post, Org(A) + "/assets", new { name, kind = "Network" })).StatusCode);
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"Assets\" WHERE \"Name\"='{name}'"));
            Assert.Equal(auditsBefore, await fixture.Scalar<long>("SELECT count(*) FROM \"AuditEvents\""));
            Assert.Equal(outboxBefore, await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\""));
        }
        finally { await fixture.Execute("DROP TRIGGER fail_outbox ON \"OutboxEvents\"; DROP FUNCTION fail_test_outbox()"); }
    }

    [Fact]
    public async Task Document_body_and_history_fail_closed_when_read_audit_cannot_persist()
    {
        using var alice = await Login("alice");
        var content = "Synthetic restricted body " + Guid.NewGuid().ToString("N");
        var doc = await CreateDocument(alice, content);
        await fixture.Execute("CREATE FUNCTION fail_test_read_audit() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" IN ('Document.Read','Document.RevisionsRead') THEN RAISE EXCEPTION 'Synthetic read audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_read_audit BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_test_read_audit()");
        try
        {
            foreach (var suffix in new[] { "", "/revisions" })
            {
                var response = await alice.GetAsync(Org(A) + $"/documents/{doc.Id}" + suffix);
                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.DoesNotContain(content, await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{doc.Id}'"));
        }
        finally { await fixture.Execute("DROP TRIGGER fail_read_audit ON \"AuditEvents\"; DROP FUNCTION fail_test_read_audit()"); }
    }

    [Fact]
    public async Task Cursor_pagination_is_bounded_and_scoped()
    {
        using var alice = await Login("alice");
        var orgs = await alice.GetFromJsonAsync<OrganizationDto[]>("/api/v1/organizations");
        Assert.All(orgs!, x => Assert.NotEqual(B, x.Id));
        for (var i = 0; i < 52; i++)
            await fixture.Execute($"INSERT INTO \"Assets\" (\"Id\",\"TenantId\",\"OrganizationId\",\"Name\",\"Kind\",\"CreatedAt\") VALUES (gen_random_uuid(),'{DevelopmentSeed.TenantA}','{A2}','Page asset {i}','Network',now())");
        var first = (await alice.GetFromJsonAsync<CursorPage<AssetDto>>(Org(A2) + "/assets"))!;
        Assert.Equal(50, first.Items.Count); Assert.NotNull(first.NextCursor);
        var second = (await alice.GetFromJsonAsync<CursorPage<AssetDto>>(Org(A2) + "/assets?after=" + first.NextCursor))!;
        Assert.NotEmpty(second.Items); Assert.Null(second.NextCursor);
        Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.All(first.Items.Concat(second.Items), x => Assert.Equal(A2, x.OrganizationId));
    }

    [Fact]
    public void Development_identity_cannot_be_enabled_in_production()
    {
        using var factory = new ApiFactory(fixture.AppConnection, true, "Production");
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Development identity requires", exception.Message);
    }

    [Fact]
    public async Task Development_identity_routes_require_explicit_opt_in()
    {
        await using var factory = new ApiFactory(fixture.AppConnection, false);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/dev/profiles")).StatusCode);
    }

    [Fact]
    public void Api_refuses_privileged_database_role()
    {
        using var factory = new ApiFactory(fixture.OwnerConnection);
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("non-owner", exception.Message);
    }
}
