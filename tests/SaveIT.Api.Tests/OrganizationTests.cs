using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class OrganizationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private readonly HashSet<Guid> created = [];
    private static readonly Guid TA = DevelopmentSeed.TenantA, TB = DevelopmentSeed.TenantB;
    private static readonly Guid Alice = DevelopmentSeed.Profiles[0].MembershipId, Viewer = DevelopmentSeed.Profiles[1].MembershipId;
    private sealed record Csrf(string Token);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var client = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(client, "/api/v1/dev/login", new { profile })).StatusCode); return client;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient client, string path, object input, bool put = false)
    {
        var csrf = (await client.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token); return await client.SendAsync(request);
    }
    private async Task<OrganizationDto> Create(HttpClient client, string? name = null)
    {
        var response = await Write(client, "/api/v1/organizations", new { name = name ?? "ZZZ Synthetic branch " + Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var row = (await response.Content.ReadFromJsonAsync<OrganizationDto>())!; created.Add(row.Id); return row;
    }

    [Fact]
    public async Task Create_grants_only_its_creator_and_rename_persists_with_atomic_metadata_events()
    {
        using var alice = await Login(); var row = await Create(alice);
        Assert.True((await alice.GetFromJsonAsync<SessionDto>("/api/v1/session"))!.CanManageOrganizations);
        Assert.Equal("Editor", row.Role); Assert.Equal(1, row.Version);
        Assert.Equal(Alice, await fixture.Scalar<Guid>($"SELECT \"MembershipId\" FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{row.Id}'"));
        var renamed = await Write(alice, $"/api/v1/organizations/{row.Id}", new { name = "ZZZ Renamed branch", expectedVersion = 1 }, true);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode); Assert.Equal(2, (await renamed.Content.ReadFromJsonAsync<OrganizationDto>())!.Version);
        using var fresh = await Login(); Assert.Equal("ZZZ Renamed branch", (await fresh.GetFromJsonAsync<OrganizationDto>($"/api/v1/organizations/{row.Id}"))!.Name);
        using var viewer = await Login("viewer"); using var bob = await Login("bob");
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/v1/organizations/{row.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/v1/organizations/{row.Id}")).StatusCode);
        var processor = new SearchProcessor(fixture.WorkerConnection);
        for (var i = 0; i < 10 && (await processor.ProcessBatchAsync(TA)).Completed > 0; i++) { }
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" e JOIN \"SearchDeliveries\" d ON d.\"TenantId\"=e.\"TenantId\" AND d.\"EventId\"=e.\"Id\" WHERE e.\"OrganizationId\"='{row.Id}' AND d.\"CompletedAt\" IS NOT NULL"));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"OrganizationId\"='{row.Id}' AND \"Action\" IN ('Organization.Created','Organization.Revised')"));
    }

    [Fact]
    public async Task Administration_requires_live_capability_and_existing_editor_grant_with_current_cookie()
    {
        using var alice = await Login(); using var viewer = await Login("viewer"); var row = await Create(alice); var path = $"/api/v1/organizations/{row.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, "/api/v1/organizations", new { name = "No authority" })).StatusCode);
        try
        {
            await fixture.Execute($"UPDATE \"Memberships\" SET \"CanManageOrganizations\"=false WHERE \"Id\"='{Alice}'");
            Assert.False((await alice.GetFromJsonAsync<SessionDto>("/api/v1/session"))!.CanManageOrganizations);
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(alice, path, new { name = "No authority", expectedVersion = 1 }, true)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(alice, "/api/v1/organizations", new { name = "No authority" })).StatusCode);
            await fixture.Execute($"UPDATE \"Memberships\" SET \"CanManageOrganizations\"=true WHERE \"Id\"='{Alice}'; UPDATE \"OrganizationGrants\" SET \"Role\"='Viewer' WHERE \"OrganizationId\"='{row.Id}'");
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(alice, path, new { name = "No editor grant", expectedVersion = 1 }, true)).StatusCode);
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{row.Id}'");
            Assert.Equal(HttpStatusCode.NotFound, (await alice.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await Write(alice, path, new { name = "No grant", expectedVersion = 1 }, true)).StatusCode);
        }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"CanManageOrganizations\"=true WHERE \"Id\"='{Alice}'"); }
        Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/v1/organizations", new { name = "Missing CSRF" })).StatusCode);
        foreach (var name in new[] { " ", new string('x',161), "bad\0name" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(alice, "/api/v1/organizations", new { name })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_renames_have_one_winner_and_preserve_expected_version()
    {
        using var alice = await Login(); var row = await Create(alice); var path = $"/api/v1/organizations/{row.Id}";
        var results = await Task.WhenAll(Write(alice, path, new { name = "ZZZ First", expectedVersion = 1 }, true), Write(alice, path, new { name = "ZZZ Second", expectedVersion = 1 }, true));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(2, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Organizations\" WHERE \"Id\"='{row.Id}'"));
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{row.Id}' AND \"Type\"='Organization.Revised'"));
    }

    [Theory]
    [InlineData(false,"AuditEvents")]
    [InlineData(false,"OutboxEvents")]
    [InlineData(false,"SearchDeliveries")]
    [InlineData(true,"AuditEvents")]
    [InlineData(true,"OutboxEvents")]
    [InlineData(true,"SearchDeliveries")]
    public async Task Failed_administration_rolls_back_record_creator_grant_audit_and_outbox(bool rename, string boundary)
    {
        using var alice = await Login(); var row = rename ? await Create(alice) : null;
        var name = "ZZZ rollback " + Guid.NewGuid().ToString("N"); var action = rename ? "Organization.Revised" : "Organization.Created";
        var condition = boundary == "SearchDeliveries" ? "true" : $"NEW.\"{(boundary == "AuditEvents" ? "Action" : "Type")}\"='{action}'";
        await fixture.Execute($"CREATE FUNCTION fail_organization() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION 'Synthetic administration failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_organization BEFORE INSERT ON \"{boundary}\" FOR EACH ROW EXECUTE FUNCTION fail_organization()");
        try
        {
            var response = await Write(alice, "/api/v1/organizations" + (rename ? "/"+row!.Id : ""), new { name, expectedVersion = 1 }, rename);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"Organizations\" WHERE \"Name\"='{name}'"));
            if (row is not null)
            {
                Assert.Equal(1, await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Organizations\" WHERE \"Id\"='{row.Id}'"));
                Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{row.Id}' AND \"Type\"='{action}'"));
                Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"OrganizationId\"='{row.Id}' AND \"Action\"='{action}'"));
            }
            Assert.Equal(0L, await fixture.Scalar<long>("SELECT count(*) FROM \"OrganizationGrants\" g LEFT JOIN \"Organizations\" o ON o.\"Id\"=g.\"OrganizationId\" AND o.\"TenantId\"=g.\"TenantId\" WHERE o.\"Id\" IS NULL"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_organization ON \"{boundary}\"; DROP FUNCTION fail_organization()"); }
    }

    [Fact]
    public async Task Pages_exceed_old_cap_handle_duplicate_names_and_recheck_grants_with_bound_cursors()
    {
        using var alice = await Login(); using var viewer = await Login("viewer"); using var bob = await Login("bob");
        var key = "ZZZ pages " + Guid.NewGuid().ToString("N");
        for (var i=0;i<117;i++)
        {
            var id=Guid.NewGuid(); created.Add(id);
            await fixture.Execute($"INSERT INTO \"Organizations\" (\"Id\",\"TenantId\",\"Name\") VALUES ('{id}','{TA}','{key}'); INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{TA}','{Alice}','{id}','Editor')");
        }
        var page = (await alice.GetFromJsonAsync<CursorPage<OrganizationDto>>("/api/v1/organizations"))!;
        Assert.Equal(50,page.Items.Count); Assert.NotNull(page.NextCursor);
        foreach(var client in new[]{viewer,bob}) Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/v1/organizations?after="+Uri.EscapeDataString(page.NextCursor!))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.GetAsync("/api/v1/organizations?after=forged")).StatusCode);
        var removed=created.Except(page.Items.Select(x=>x.Id)).First();
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{removed}'");
        var all=page.Items.ToList();
        while(page.NextCursor is not null)
        {
            page=(await alice.GetFromJsonAsync<CursorPage<OrganizationDto>>("/api/v1/organizations?after="+Uri.EscapeDataString(page.NextCursor)))!;
            Assert.InRange(page.Items.Count,1,50); Assert.Empty(all.Select(x=>x.Id).Intersect(page.Items.Select(x=>x.Id))); all.AddRange(page.Items);
        }
        Assert.Equal(116,all.Count(x=>x.Name==key)); Assert.DoesNotContain(all,x=>x.Id==removed); Assert.All(all,x=>Assert.NotEqual(DevelopmentSeed.OrgB,x.Id));
    }

    [Fact]
    public async Task Organization_read_audit_failure_withholds_fields_and_cursor()
    {
        using var alice=await Login(); var row=await Create(alice);
        await fixture.Execute("CREATE FUNCTION fail_organization_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" IN ('Organization.Read','Organization.ListRead') THEN RAISE EXCEPTION 'Synthetic read failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_organization_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_organization_read()");
        try
        {
            foreach(var path in new[]{"/api/v1/organizations",$"/api/v1/organizations/{row.Id}"})
            { var response=await alice.GetAsync(path); Assert.Equal(HttpStatusCode.InternalServerError,response.StatusCode); Assert.DoesNotContain(row.Name,await response.Content.ReadAsStringAsync()); Assert.DoesNotContain("nextCursor",await response.Content.ReadAsStringAsync()); }
        }
        finally { await fixture.Execute("DROP TRIGGER fail_organization_read ON \"AuditEvents\"; DROP FUNCTION fail_organization_read()"); }
    }

    [Fact]
    public async Task Raw_api_cannot_edit_authority_and_checked_functions_reject_spoofed_scope_or_existing_organization()
    {
        await using var app=new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync();
        foreach(var sql in new[]{"UPDATE \"Organizations\" SET \"Name\"='Forbidden'","INSERT INTO \"Organizations\" DEFAULT VALUES","UPDATE \"Memberships\" SET \"CanManageOrganizations\"=true","INSERT INTO \"OrganizationGrants\" DEFAULT VALUES"})
        { await using var command=new NpgsqlCommand(sql,app); Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(async()=>await command.ExecuteNonQueryAsync())).SqlState); }
        foreach(var call in new[]{ $"create_organization('{Guid.NewGuid()}','Synthetic','{Alice}')", $"rename_organization('{DevelopmentSeed.OrgA}','Synthetic','{Alice}',1)" })
        { await using var command=new NpgsqlCommand("SELECT public."+call,app); Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(async()=>await command.ExecuteNonQueryAsync())).SqlState); }
        foreach(var (tenant,contextMember,argumentMember,org,expected) in new[]{(TA,Viewer,Viewer,Guid.NewGuid(),"42501"),(TB,Alice,Alice,Guid.NewGuid(),"42501"),(TA,Alice,Viewer,Guid.NewGuid(),"42501"),(TA,Alice,Alice,DevelopmentSeed.OrgA,"23505")})
        {
            await using var transaction=await app.BeginTransactionAsync();
            await using(var context=new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true),set_config('app.membership_id','{contextMember}',true)",app,transaction)) await context.ExecuteNonQueryAsync();
            await using var command=new NpgsqlCommand($"SELECT public.create_organization('{org}','Synthetic','{argumentMember}')",app,transaction);
            Assert.Equal(expected,(await Assert.ThrowsAsync<PostgresException>(async()=>await command.ExecuteNonQueryAsync())).SqlState);
        }
        Assert.False(await fixture.Scalar<bool>("SELECT has_function_privilege('saveit_search_worker','public.create_organization(uuid,text,uuid)','EXECUTE')"));
        foreach(var (tenant,member) in new[]{(TA,Viewer),(TB,Alice)})
        {
            await using var transaction=await app.BeginTransactionAsync();
            await using(var context=new NpgsqlCommand($"SELECT set_config('app.tenant_id','{tenant}',true),set_config('app.membership_id','{member}',true)",app,transaction)) await context.ExecuteNonQueryAsync();
            await using var rename=new NpgsqlCommand($"SELECT public.rename_organization('{DevelopmentSeed.OrgA}','Forbidden','{member}',1)",app,transaction);
            Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(async()=>await rename.ExecuteNonQueryAsync())).SqlState);
        }
    }

    [Theory]
    [InlineData("create_organization(uuid,text,uuid)",false)]
    [InlineData("rename_organization(uuid,text,uuid,integer)",false)]
    [InlineData("create_organization(uuid,text,uuid)",true)]
    [InlineData("rename_organization(uuid,text,uuid,integer)",true)]
    [InlineData("create_organization(uuid,text,uuid)",null)]
    [InlineData("rename_organization(uuid,text,uuid,integer)",null)]
    public async Task Worker_guard_rejects_direct_public_or_noinherit_administration_execute(string signature,bool? indirect)
    {
        var role=indirect==true ? "saveit_org_"+Guid.NewGuid().ToString("N") : indirect==false ? "saveit_search_worker" : "PUBLIC";
        if(indirect==true) await fixture.Execute($"CREATE ROLE {role} NOLOGIN NOINHERIT; GRANT {role} TO saveit_search_worker WITH INHERIT FALSE");
        await fixture.Execute($"GRANT EXECUTE ON FUNCTION public.{signature} TO {role}");
        var processor=new SearchProcessor(fixture.WorkerConnection);
        try { await Assert.ThrowsAsync<InvalidOperationException>(()=>processor.ValidateAsync()); }
        finally
        {
            await fixture.Execute($"REVOKE EXECUTE ON FUNCTION public.{signature} FROM {role}");
            if(indirect==true) await fixture.Execute($"REVOKE {role} FROM saveit_search_worker; DROP ROLE {role}");
        }
        await processor.ValidateAsync();
    }

    public Task InitializeAsync()=>Task.CompletedTask;
    public async Task DisposeAsync()
    {
        await factory.DisposeAsync();
        foreach(var id in created)
            await fixture.Execute($"DELETE FROM \"SearchDeliveries\" WHERE \"EventId\" IN (SELECT \"Id\" FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{id}'); DELETE FROM \"OutboxEvents\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"AuditEvents\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"OrganizationGrants\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"OrganizationRevisions\" WHERE \"OrganizationId\"='{id}'; DELETE FROM \"Organizations\" WHERE \"Id\"='{id}'");
    }
}
