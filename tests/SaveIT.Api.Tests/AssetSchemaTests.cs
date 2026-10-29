using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using SaveIT.Api.Assets;
using SaveIT.Api.Data;
using SaveIT.Search;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class AssetSchemaTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static readonly Guid Org = DevelopmentSeed.OrgA;
    private static readonly Guid TA = DevelopmentSeed.TenantA;
    private static string Root(string resource, Guid? org = null) => $"/api/v1/organizations/{org ?? Org}/{resource}";
    private const string Definition = """[{"key":"serial","label":"Serial","type":"text","required":true,"defaultValue":"synthetic-default"},{"key":"watts","label":"Watts","type":"number","required":false},{"key":"managed","label":"Managed","type":"boolean","required":true},{"key":"expiry","label":"Expiry","type":"date","required":false},{"key":"tier","label":"Tier","type":"choice","required":false,"options":["Core","Edge"]}]""";
    private static JsonElement Json(string s) => AssetSchemas.Json(s);
    private sealed record Csrf(string Token);
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
    private static AssetTypeRequest Input(int version = 0, bool archived = false, string definition = Definition) => new("Synthetic equipment", Json(definition), version, archived);
    private static async Task<AssetTypeDto> Type(HttpClient client, Guid? org = null)
    {
        var r = await Write(client, Root("asset-types", org), Input()); Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetTypeDto>())!;
    }
    private static async Task<AssetDto> Asset(HttpClient client, AssetTypeDto type, string values = """{"serial":"private-marker","managed":false,"watts":123.125,"expiry":"2028-02-29","tier":"Core"}""")
    {
        var r = await Write(client, Root("assets"), new CreateAssetRequest("Synthetic equipment", "Network", type.Id, type.Version, Json(values)));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetDto>())!;
    }

    [Fact]
    public async Task Typed_asset_preserves_pinned_definition_values_history_and_exact_restore()
    {
        using var client = await Login(); var type = await Type(client); var asset = await Asset(client, type); var path = Root("assets") + "/" + asset.Id;
        var evolved = Definition.Replace("\"required\":false", "\"required\":true");
        Assert.Equal(HttpStatusCode.OK, (await Write(client, Root("asset-types") + "/" + type.Id, Input(1, definition: evolved), true)).StatusCode);
        var read = (await client.GetFromJsonAsync<AssetFieldsDto>(path + "/fields"))!;
        Assert.Equal(1, read.Schema!.Version); Assert.False(read.Values.GetProperty("managed").GetBoolean()); Assert.Equal(123.125m, read.Values.GetProperty("watts").GetDecimal());
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path, new ReviseAssetRequest("Renamed", "Switch", null, 1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, path + "/fields", new ReviseAssetFieldsRequest(2, type.Id, 2, Json("""{"serial":"upgrade","managed":true}""")), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path + "/fields", new ReviseAssetFieldsRequest(2, type.Id, 1, Json("""{"serial":"revised","managed":true}""")), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(client, path + "/fields", new ReviseAssetFieldsRequest(2, type.Id, 1, read.Values), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path + "/restore", new RestoreDocumentRequest(1, 3, "Restore exact typed snapshot"))).StatusCode);
        read = (await client.GetFromJsonAsync<AssetFieldsDto>(path + "/fields"))!;
        Assert.Equal(4, read.Asset.Version); Assert.Equal(1, read.Schema!.Version); Assert.Equal("private-marker", read.Values.GetProperty("serial").GetString());
        var history = (await client.GetFromJsonAsync<CursorPage<AssetRevisionDto>>(path + "/revisions"))!;
        Assert.Equal(new[] { 4, 3, 2, 1 }, history.Items.Select(x => x.Version)); Assert.Equal(1, history.Items[0].RestoredFromVersion);
        Assert.Equal("private-marker", history.Items[2].Fields.GetProperty("serial").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Write(client, path + "/fields", new ReviseAssetFieldsRequest(4, type.Id, 2, read.Values), true)).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<AssetFieldsDto>(path + "/fields"))!.Schema!.Version);
        using var fresh = await Login(); Assert.Equal(5, (await fresh.GetFromJsonAsync<AssetFieldsDto>(path + "/fields"))!.Asset.Version);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("[{\"key\":\"bad-key\",\"label\":\"X\",\"type\":\"text\",\"required\":true}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"html\",\"required\":true}]")]
    [InlineData("[{\"key\":\"x\",\"label\":null,\"type\":\"text\",\"required\":true}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"text\"}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"text\",\"required\":true,\"regex\":\".*\"}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"choice\",\"required\":false,\"options\":[\"A\",\"A\"]}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"boolean\",\"required\":true,\"defaultValue\":\"true\"}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"date\",\"required\":true,\"defaultValue\":\"2027-02-29\"}]")]
    [InlineData("[{\"key\":\"x\",\"label\":\"X\",\"type\":\"number\",\"required\":true,\"defaultValue\":1e9999}]")]
    [InlineData("[{\"key\":\"x\",\"key\":\"y\",\"label\":\"X\",\"type\":\"text\",\"required\":true}]")]
    public async Task Invalid_definition_is_rejected_before_any_business_write(string definition)
    {
        using var client = await Login(); var count = await fixture.Scalar<long>("SELECT count(*) FROM \"AssetTypes\"");
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root("asset-types"), Input(definition: definition))).StatusCode);
        Assert.Equal(count, await fixture.Scalar<long>("SELECT count(*) FROM \"AssetTypes\""));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"serial\":null,\"managed\":false}")]
    [InlineData("{\"serial\":\" \",\"managed\":false}")]
    [InlineData("{\"serial\":\"S\",\"managed\":\"false\"}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"watts\":1000000001}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"watts\":0.0001}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"expiry\":\"2027-02-29\"}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"expiry\":\"0000-01-01\"}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"expiry\":\"10/29/2026\"}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"tier\":\"Other\"}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"unknown\":true}")]
    [InlineData("{\"serial\":{},\"managed\":false}")]
    [InlineData("{\"serial\":\"S\",\"managed\":false,\"managed\":true}")]
    public async Task Invalid_values_are_rejected_and_defaults_never_bypass_required_fields(string values)
    {
        using var client = await Login(); var type = await Type(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root("assets"), new CreateAssetRequest("Invalid", "Network", type.Id, 1, Json(values)))).StatusCode);
    }

    [Theory]
    [InlineData("1e-1000000")]
    [InlineData("1e-29")]
    [InlineData("0.000000000000000000000000000001")]
    [InlineData("0e99999999")]
    [InlineData("-79228162514264337593543950335")]
    public async Task Extreme_numeric_representations_return_generic_validation_errors(string number)
    {
        using var client = await Login(); var type = await Type(client);
        var response = await Write(client, Root("assets"), new CreateAssetRequest("Invalid", "Network", type.Id, 1, Json("{\"serial\":\"S\",\"managed\":false,\"watts\":"+number+"}")));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_unicode_is_rejected_and_valid_surrogate_pairs_persist()
    {
        using var client = await Login(); var type = await Type(client);
        var csrf = (await client.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        var raw = "{\"name\":\"Invalid\",\"kind\":\"Network\",\"assetTypeId\":\""+type.Id+"\",\"assetTypeVersion\":1,\"fields\":{\"serial\":\"\\ud800\",\"managed\":false}}";
        using var request = new HttpRequestMessage(HttpMethod.Post, Root("assets")) { Content = new StringContent(raw, Encoding.UTF8,"application/json") }; request.Headers.Add("X-CSRF-TOKEN",csrf.Token);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.SendAsync(request)).StatusCode);
        var asset = await Asset(client,type,"{\"serial\":\"\\ud83d\\ude80\",\"managed\":false}");
        Assert.Equal("🚀",(await client.GetFromJsonAsync<AssetFieldsDto>(Root("assets")+"/"+asset.Id+"/fields"))!.Values.GetProperty("serial").GetString());
    }

    [Fact]
    public async Task Bounded_schema_and_values_reject_duplicate_keys_controls_oversized_text_and_partial_refs()
    {
        using var client = await Login(); var type = await Type(client);
        var field = """{"key":"x","label":"X","type":"text","required":false}""";
        var large = JsonSerializer.Serialize(Enumerable.Range(0,20).Select(i => new { key = "x"+i, label = "X", type = "choice", required = false, options = Enumerable.Range(0,20).Select(n => new string('x',78)+n) }));
        foreach (var def in new[] { "["+field+","+field+"]", "["+string.Join(",", Enumerable.Range(0,21).Select(i => field.Replace("\"x\"", "\"x"+i+"\"")))+"]", Definition.Replace("Serial",new string('x',81)), Definition.Replace("Serial","bad\\u0001label"), large })
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root("asset-types"), Input(definition: def))).StatusCode);
        foreach (var value in new[] { new string('x',501), "bad\0value", "bad\u0001value" })
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root("assets"), new CreateAssetRequest("Invalid", "Network", type.Id,1,Json(JsonSerializer.Serialize(new { serial = value, managed = false }))))).StatusCode);
        foreach (var input in new[] { new CreateAssetRequest("X","Network",type.Id), new CreateAssetRequest("X","Network",null,1), new CreateAssetRequest("X","Network",Fields:Json("{\"serial\":\"X\"}")), new CreateAssetRequest("X","Network",type.Id,999,Json("{}")) })
            Assert.Equal(HttpStatusCode.BadRequest, (await Write(client, Root("assets"), input)).StatusCode);
    }

    [Fact]
    public async Task Current_grants_viewer_csrf_and_cross_scope_checks_cover_definitions_fields_and_history()
    {
        using var alice = await Login(); using var viewer = await Login("viewer"); using var bob = await Login("bob");
        var type = await Type(alice); var foreign = await Type(alice, DevelopmentSeed.OrgA2); var asset = await Asset(alice,type);
        var path = Root("asset-types")+"/"+type.Id; var fields = Root("assets")+"/"+asset.Id+"/fields";
        foreach (var suffix in new[] { "", "/revisions", "/revisions/1" })
        { Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync(path+suffix)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(path+suffix)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await alice.GetAsync(Root("asset-types",DevelopmentSeed.OrgA2)+"/"+type.Id+suffix)).StatusCode); }
        Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync(fields)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(viewer,Root("asset-types"),Input())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(viewer,path,Input(1),true)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(viewer,fields,new ReviseAssetFieldsRequest(1,type.Id,1,Json("{}")),true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Write(alice,fields,new ReviseAssetFieldsRequest(1,foreign.Id,1,Json("{}")),true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.PutAsJsonAsync(path,Input(1))).StatusCode);
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{DevelopmentSeed.Profiles[0].MembershipId}' AND \"OrganizationId\"='{Org}'");
            Assert.Equal(HttpStatusCode.NotFound,(await alice.GetAsync(fields)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await Write(alice,path,Input(1),true)).StatusCode);
        }
        finally { await fixture.Execute($"INSERT INTO \"OrganizationGrants\" VALUES ('{TA}','{DevelopmentSeed.Profiles[0].MembershipId}','{Org}','Editor')"); }
    }

    [Fact]
    public async Task Archive_preserves_pins_but_requires_reactivation_for_new_or_historical_rebinding()
    {
        using var client = await Login(); var type = await Type(client); var asset = await Asset(client,type); var typePath=Root("asset-types")+"/"+type.Id; var path=Root("assets")+"/"+asset.Id;
        Assert.Equal(HttpStatusCode.OK,(await Write(client,typePath,Input(1,true),true)).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<CursorPage<AssetTypeDto>>(Root("asset-types")))!.Items,x=>x.Id==type.Id);
        Assert.Contains((await client.GetFromJsonAsync<CursorPage<AssetTypeDto>>(Root("asset-types")+"?includeArchived=true"))!.Items,x=>x.Id==type.Id);
        Assert.Equal(HttpStatusCode.BadRequest,(await Write(client,Root("assets"),new CreateAssetRequest("No","Network",type.Id,1,Json("{\"serial\":\"S\",\"managed\":true}")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(client,path+"/fields",new ReviseAssetFieldsRequest(1,type.Id,1,Json("{\"serial\":\"Changed\",\"managed\":true}")),true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(client,path+"/restore",new RestoreDocumentRequest(1,2,"Retain same archived pin"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(client,path+"/fields",new ReviseAssetFieldsRequest(3,null,null,Json("{}")),true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Write(client,path+"/restore",new RestoreDocumentRequest(1,4,"Try archived rebinding"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(client,typePath,Input(2),true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(client,path+"/restore",new RestoreDocumentRequest(1,4,"Reactivate historical pin"))).StatusCode);
    }

    [Theory]
    [InlineData("AuditEvents")]
    [InlineData("OutboxEvents")]
    [InlineData("SearchDeliveries")]
    public async Task Failures_roll_back_type_publish_field_edits_and_typed_restore(string boundary)
    {
        using var client = await Login(); var type=await Type(client); var asset=await Asset(client,type); var path=Root("assets")+"/"+asset.Id;
        Assert.Equal(HttpStatusCode.OK,(await Write(client,path+"/fields",new ReviseAssetFieldsRequest(1,type.Id,1,Json("{\"serial\":\"Changed\",\"managed\":true}")),true)).StatusCode);
        var condition=boundary=="SearchDeliveries" ? "true" : $"NEW.\"{(boundary=="AuditEvents"?"Action":"Type")}\" IN ('AssetType.Created','AssetType.Revised','Asset.Revised','Asset.Restored')";
        await fixture.Execute($"CREATE FUNCTION fail_schema() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF {condition} THEN RAISE EXCEPTION 'Synthetic failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_schema BEFORE INSERT ON \"{boundary}\" FOR EACH ROW EXECUTE FUNCTION fail_schema()");
        try
        {
            var count=await fixture.Scalar<long>("SELECT count(*) FROM \"AssetTypes\"");
            Assert.Equal(HttpStatusCode.InternalServerError,(await Write(client,Root("asset-types"),Input())).StatusCode);
            Assert.Equal(count,await fixture.Scalar<long>("SELECT count(*) FROM \"AssetTypes\""));
            Assert.Equal(HttpStatusCode.InternalServerError,(await Write(client,Root("asset-types")+"/"+type.Id,Input(1),true)).StatusCode);
            Assert.Equal(1,await fixture.Scalar<int>($"SELECT \"Version\" FROM \"AssetTypes\" WHERE \"Id\"='{type.Id}'"));
            Assert.Equal(1L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetTypeRevisions\" WHERE \"AssetTypeId\"='{type.Id}'"));
            Assert.Equal(HttpStatusCode.InternalServerError,(await Write(client,path+"/fields",new ReviseAssetFieldsRequest(2,type.Id,1,Json("{\"serial\":\"No\",\"managed\":true}")),true)).StatusCode);
            Assert.Equal(HttpStatusCode.InternalServerError,(await Write(client,path+"/restore",new RestoreDocumentRequest(1,2,"Must roll back exact fields"))).StatusCode);
            Assert.Equal(2,await fixture.Scalar<int>($"SELECT \"Version\" FROM \"Assets\" WHERE \"Id\"='{asset.Id}'"));
            Assert.Equal(2L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{asset.Id}'"));
            Assert.Equal("Changed",await fixture.Scalar<string>($"SELECT \"FieldsJson\"->>'serial' FROM \"Assets\" WHERE \"Id\"='{asset.Id}'"));
        }
        finally { await fixture.Execute($"DROP TRIGGER fail_schema ON \"{boundary}\"; DROP FUNCTION fail_schema()"); }
    }

    [Fact]
    public async Task Audited_content_reads_fail_closed_and_worker_cannot_read_or_index_fields_defaults_or_schemas()
    {
        using var client=await Login(); var type=await Type(client); var asset=await Asset(client,type); var path=Root("assets")+"/"+asset.Id;
        var processor=new SearchProcessor(fixture.WorkerConnection); for(var i=0;i<10 && (await processor.ProcessBatchAsync(TA)).Completed>0;i++) { }
        Assert.Equal(1L,await fixture.Scalar<long>($"SELECT count(*) FROM \"SearchDeliveries\" d JOIN \"OutboxEvents\" e ON e.\"TenantId\"=d.\"TenantId\" AND e.\"Id\"=d.\"EventId\" WHERE e.\"TargetId\"='{type.Id}' AND d.\"CompletedAt\" IS NOT NULL"));
        foreach(var table in new[]{"SearchEntries","AuditEvents","OutboxEvents"}) Assert.False(await fixture.Scalar<bool>($"SELECT EXISTS (SELECT 1 FROM \"{table}\" t WHERE to_jsonb(t)::text LIKE '%private-marker%' OR to_jsonb(t)::text LIKE '%synthetic-default%')"));
        await using(var worker=new NpgsqlConnection(fixture.WorkerConnection))
        { await worker.OpenAsync(); foreach(var sql in new[]{"SELECT \"FieldsJson\" FROM \"Assets\"","SELECT * FROM \"AssetTypes\"","SELECT \"DefinitionJson\" FROM \"AssetTypeRevisions\""}) { await using var cmd=new NpgsqlCommand(sql,worker); Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(async()=>await cmd.ExecuteScalarAsync())).SqlState); } }
        await fixture.Execute("CREATE FUNCTION fail_fields_read() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\" IN ('Asset.FieldsRead','AssetType.RevisionRead','AssetType.RevisionsRead','AssetTypes.Read','AssetType.Read') THEN RAISE EXCEPTION 'Synthetic audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_fields_read BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_fields_read()");
        try { foreach(var url in new[]{path+"/fields",Root("asset-types"),Root("asset-types")+"/"+type.Id,Root("asset-types")+"/"+type.Id+"/revisions/1",Root("asset-types")+"/"+type.Id+"/revisions"}) { var r=await client.GetAsync(url); Assert.Equal(HttpStatusCode.InternalServerError,r.StatusCode); var body=await r.Content.ReadAsStringAsync(); Assert.DoesNotContain("private-marker",body); Assert.DoesNotContain("synthetic-default",body); } }
        finally { await fixture.Execute("DROP TRIGGER fail_fields_read ON \"AuditEvents\"; DROP FUNCTION fail_fields_read()"); }
    }

    [Fact]
    public async Task Concurrent_publish_and_field_edits_each_have_one_winner_with_atomic_revision_count()
    {
        using var client=await Login(); var type=await Type(client); var asset=await Asset(client,type);
        var published=await Task.WhenAll(Write(client,Root("asset-types")+"/"+type.Id,Input(1),true),Write(client,Root("asset-types")+"/"+type.Id,Input(1),true));
        Assert.Single(published,x=>x.StatusCode==HttpStatusCode.OK); Assert.Single(published,x=>x.StatusCode==HttpStatusCode.Conflict);
        var request=new ReviseAssetFieldsRequest(1,type.Id,1,Json("{\"serial\":\"Winner\",\"managed\":true}"));
        var edited=await Task.WhenAll(Write(client,Root("assets")+"/"+asset.Id+"/fields",request,true),Write(client,Root("assets")+"/"+asset.Id+"/fields",request,true));
        Assert.Single(edited,x=>x.StatusCode==HttpStatusCode.OK); Assert.Single(edited,x=>x.StatusCode==HttpStatusCode.Conflict);
        Assert.Equal(2L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{asset.Id}'"));
        Assert.Equal(2L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetTypeRevisions\" WHERE \"AssetTypeId\"='{type.Id}'"));
    }

    [Fact]
    public async Task Database_enforces_rls_scoped_references_append_only_revisions_and_pure_validation()
    {
        using var client=await Login(); var type=await Type(client); var foreign=await Type(client,DevelopmentSeed.OrgA2); var asset=await Asset(client,type);
        foreach(var table in new[]{"AssetTypes","AssetTypeRevisions"}) Assert.True(await fixture.Scalar<bool>($"SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='\"{table}\"'::regclass"));
        await using var app=new NpgsqlConnection(fixture.AppConnection); await app.OpenAsync();
        foreach(var table in new[]{"AssetTypes","AssetTypeRevisions"}) { await using var c=new NpgsqlCommand($"SELECT count(*) FROM \"{table}\"",app); Assert.Equal(0L,await c.ExecuteScalarAsync()); }
        foreach(var sql in new[]{"UPDATE \"AssetTypes\" SET \"TenantId\"=gen_random_uuid()","UPDATE \"AssetTypeRevisions\" SET \"Name\"='No'","DELETE FROM \"AssetTypes\"","DELETE FROM \"AssetTypeRevisions\""})
        { await using var c=new NpgsqlCommand(sql,app); Assert.Equal("42501",(await Assert.ThrowsAsync<PostgresException>(async()=>await c.ExecuteNonQueryAsync())).SqlState); }
        foreach(var assignment in new[]{"\"FieldsJson\"='{}'::jsonb",$"\"AssetTypeId\"='{foreign.Id}'","\"AssetTypeVersion\"=NULL","\"FieldsJson\"='{\"serial\":\"S\",\"managed\":true,\"unknown\":5}'::jsonb"})
        {
            await using var tx=await app.BeginTransactionAsync(); await using(var set=new NpgsqlCommand($"SELECT set_config('app.tenant_id','{TA}',true)",app,tx)) await set.ExecuteNonQueryAsync();
            await using var c=new NpgsqlCommand($"UPDATE \"Assets\" SET {assignment} WHERE \"Id\"='{asset.Id}'",app,tx);
            Assert.Contains((await Assert.ThrowsAsync<PostgresException>(async()=>await c.ExecuteNonQueryAsync())).SqlState,new[]{"23503","23514"}); await tx.RollbackAsync();
        }
        Assert.False(await fixture.Scalar<bool>("SELECT public.valid_asset_definition_v1(NULL) OR public.valid_asset_fields_v1('[]'::jsonb,NULL)"));
        Assert.False(await fixture.Scalar<bool>("SELECT public.valid_asset_definition_v1('[{\"key\":\"x\",\"label\":\"X\",\"type\":\"text\",\"required\":null}]'::jsonb)"));
        foreach(var sql in new[] {
            $"INSERT INTO \"AssetRevisions\" (\"TenantId\",\"OrganizationId\",\"AssetId\",\"Version\",\"Name\",\"Kind\",\"Actor\",\"CreatedAt\",\"AssetTypeId\",\"AssetTypeVersion\",\"FieldsJson\") SELECT \"TenantId\",\"OrganizationId\",\"Id\",999,\"Name\",\"Kind\",'synthetic-sql',CURRENT_TIMESTAMP,\"AssetTypeId\",\"AssetTypeVersion\",'{{}}'::jsonb FROM \"Assets\" WHERE \"Id\"='{asset.Id}'",
            $"INSERT INTO \"AssetTypeRevisions\" (\"TenantId\",\"OrganizationId\",\"AssetTypeId\",\"Version\",\"Name\",\"DefinitionJson\",\"Actor\",\"CreatedAt\") VALUES ('{TA}','{Org}','{type.Id}',999,'Invalid SQL definition','[]'::jsonb,'synthetic-sql',CURRENT_TIMESTAMP)" })
        {
            await using var tx=await app.BeginTransactionAsync();
            await using(var set=new NpgsqlCommand($"SELECT set_config('app.tenant_id','{TA}',true)",app,tx)) await set.ExecuteNonQueryAsync();
            await using var c=new NpgsqlCommand(sql,app,tx);
            Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(async()=>await c.ExecuteNonQueryAsync())).SqlState);await tx.RollbackAsync();
        }
    }

    [Theory]
    [InlineData("GRANT SELECT (\"FieldsJson\") ON \"Assets\" TO saveit_search_worker", "REVOKE SELECT (\"FieldsJson\") ON \"Assets\" FROM saveit_search_worker")]
    [InlineData("GRANT SELECT ON \"AssetTypeRevisions\" TO saveit_search_worker", "REVOKE SELECT ON \"AssetTypeRevisions\" FROM saveit_search_worker")]
    [InlineData("GRANT UPDATE (\"Name\") ON \"AssetTypes\" TO saveit_search_worker", "REVOKE UPDATE (\"Name\") ON \"AssetTypes\" FROM saveit_search_worker")]
    [InlineData("GRANT SELECT (\"FieldsJson\") ON \"AssetRevisions\" TO saveit_search_worker", "REVOKE SELECT (\"FieldsJson\") ON \"AssetRevisions\" FROM saveit_search_worker")]
    [InlineData("GRANT SELECT (\"FieldsJson\") ON \"AssetRevisions\" TO PUBLIC", "REVOKE SELECT (\"FieldsJson\") ON \"AssetRevisions\" FROM PUBLIC")]
    [InlineData("CREATE ROLE saveit_test_history_reader NOLOGIN; GRANT SELECT (\"FieldsJson\") ON \"AssetRevisions\" TO saveit_test_history_reader; GRANT saveit_test_history_reader TO saveit_search_worker", "REVOKE saveit_test_history_reader FROM saveit_search_worker; REVOKE ALL PRIVILEGES (\"FieldsJson\") ON \"AssetRevisions\" FROM saveit_test_history_reader; DROP ROLE saveit_test_history_reader")]
    public async Task Worker_guard_fails_for_custom_content_access_or_new_core_writes(string grant,string revoke)
    {
        await fixture.Execute(grant);
        try { await using var connection=new NpgsqlConnection(fixture.WorkerConnection); await connection.OpenAsync(); await Assert.ThrowsAsync<InvalidOperationException>(()=>DatabaseRoleGuard.ValidateSearchWorkerAsync(connection)); }
        finally { await fixture.Execute(revoke); }
    }

    [Fact]
    public async Task Template_and_definition_history_pages_are_bounded_complete_and_metadata_only()
    {
        using var client=await Login();var type=await Type(client);
        // Owner-provisioned synthetic volume; the application still reads through real RLS/audit.
        await fixture.Execute($"""
            WITH heads AS (INSERT INTO "AssetTypes" ("Id","TenantId","OrganizationId","Name","Version","IsArchived","UpdatedAt")
              SELECT gen_random_uuid(),'{TA}','{Org}','Paged synthetic template',1,false,CURRENT_TIMESTAMP FROM generate_series(1,64) RETURNING *)
            INSERT INTO "AssetTypeRevisions" ("TenantId","OrganizationId","AssetTypeId","Version","Name","DefinitionJson","Actor","CreatedAt")
              SELECT "TenantId","OrganizationId","Id",1,"Name",'{Definition}'::jsonb,'synthetic-volume',"UpdatedAt" FROM heads;
            INSERT INTO "AssetTypeRevisions" ("TenantId","OrganizationId","AssetTypeId","Version","Name","DefinitionJson","Actor","CreatedAt")
              SELECT '{TA}','{Org}','{type.Id}',v,'Synthetic equipment','{Definition}'::jsonb,'synthetic-volume',CURRENT_TIMESTAMP FROM generate_series(2,54) v;
            UPDATE "AssetTypes" SET "Version"=54 WHERE "Id"='{type.Id}';
            """);
        var ids=new HashSet<Guid>();string? cursor=null;var pages=0;
        do { var response=await client.GetAsync(Root("asset-types")+(cursor==null?"":"?after="+cursor));var text=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("synthetic-default",text);var page=(await response.Content.ReadFromJsonAsync<CursorPage<AssetTypeDto>>())!;Assert.InRange(page.Items.Count,1,50);foreach(var row in page.Items)Assert.True(ids.Add(row.Id));cursor=page.NextCursor;pages++;Assert.True(pages<20); } while(cursor!=null);
        Assert.True(ids.Count>=65);Assert.True(pages>=2);
        var first=(await client.GetFromJsonAsync<CursorPage<AssetTypeRevisionSummaryDto>>(Root("asset-types")+"/"+type.Id+"/revisions"))!;
        Assert.Equal(50,first.Items.Count);Assert.Equal(54,first.Items[0].Version);Assert.Equal("5",first.NextCursor);
        var last=(await client.GetFromJsonAsync<CursorPage<AssetTypeRevisionSummaryDto>>(Root("asset-types")+"/"+type.Id+"/revisions?before="+first.NextCursor))!;
        Assert.Equal(new[]{4,3,2,1},last.Items.Select(x=>x.Version));Assert.Null(last.NextCursor);
    }
    public void Dispose() => factory.Dispose();
}
