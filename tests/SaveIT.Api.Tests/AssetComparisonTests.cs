using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaveIT.Api.Assets;
using SaveIT.Api.Data;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class AssetComparisonTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static readonly Guid Org = DevelopmentSeed.OrgA;
    private static string Root(string resource, Guid? org = null) => $"/api/v1/organizations/{org ?? Org}/{resource}";
    private static JsonElement Json(string s) => AssetSchemas.Json(s);
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
    private static async Task<AssetDto> Asset(HttpClient c, AssetTypeDto? type = null, string fields = "{}", Guid? org = null)
    {
        var r = await Write(c, Root("assets", org), new CreateAssetRequest("Historical synthetic asset", "Network", type?.Id, type?.Version, Json(fields)));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetDto>())!;
    }
    private static async Task<AssetTypeDto> Type(HttpClient c, string definition, string name = "Historical template")
    {
        var r = await Write(c, Root("asset-types"), new AssetTypeRequest(name, Json(definition)));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetTypeDto>())!;
    }
    private static string Path(AssetDto a) => Root("assets") + "/" + a.Id;
    private static string Compare(AssetDto a, int from = 1, int to = 2, string extra = "") => Path(a) + $"/compare?fromVersion={from}&toVersion={to}{extra}";
    private const string BasicDefinition = """[{"key":"serial","label":"Historical serial label","type":"text","required":true,"defaultValue":"private-historical-default"}]""";
    private static async Task<AssetDto> Revised(HttpClient c, AssetDto a)
    {
        var r = await Write(c, Path(a), new ReviseAssetRequest("Revised synthetic asset", "Server", null, a.Version), true);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<AssetDto>())!;
    }

    [Fact]
    public async Task Compare_is_read_only_with_numeric_audit_metadata_reverse_and_same_revision_support()
    {
        using var c = await Login(); var a = await Asset(c);
        var location = await Write(c, Root("locations"), new DirectoryRequest("Synthetic site", "", "", "", ""));
        Assert.Equal(HttpStatusCode.Created, location.StatusCode); var loc = (await location.Content.ReadFromJsonAsync<DirectoryDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(a), new ReviseAssetRequest("Revised synthetic asset", "Server", loc.Id, 1), true)).StatusCode);
        var outbox = await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\"");
        var comparison = (await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a)))!;
        Assert.Equal(2, comparison.CurrentVersion); Assert.Equal(4, comparison.ChangedRows); Assert.Equal(4, comparison.TotalRows); Assert.True(comparison.ChangesOnly);
        Assert.Equal(new[] { "name", "kind", "location", "locationName" }, comparison.Rows.Select(x => x.Key));
        var placement = comparison.Rows.Single(x => x.Key == "location"); Assert.False(placement.From.Present); Assert.Null(placement.From.Value); Assert.Equal(loc.Id.ToString(), placement.To.Value!.Value.GetString());
        Assert.Equal("Historical synthetic asset", comparison.From.Name); Assert.Equal("Revised synthetic asset", comparison.To.Name);
        Assert.Equal(1, comparison.From.Version); Assert.Equal(2, comparison.To.Version); Assert.Null(comparison.NextOffset);
        Assert.Equal(1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{a.Id}' AND \"Action\"='Asset.Compared' AND \"SourceVersion\"=1 AND \"TargetVersion\"=2"));
        Assert.Equal(outbox, await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\""));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{a.Id}'"));
        var reversed = (await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a, 2, 1)))!;
        Assert.Equal(4, reversed.ChangedRows); Assert.True(reversed.Rows.Single(x => x.Key == "location").From.Present); Assert.False(reversed.Rows.Single(x => x.Key == "location").To.Present);
        var same = (await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a, 2, 2)))!;
        Assert.Empty(same.Rows); Assert.Equal(0, same.TotalRows); Assert.Equal(0, same.ChangedRows); Assert.Null(same.NextOffset);
        same = (await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a, 2, 2, "&changesOnly=false")))!;
        Assert.Equal(5, same.Rows.Count); Assert.All(same.Rows, row => Assert.Equal("unchanged", row.Change));
    }

    [Fact]
    public async Task Pinned_schema_definitions_values_presence_and_numeric_semantics_remain_historical()
    {
        const string before = """[{"key":"serial","label":"Old label","type":"text","required":true,"defaultValue":"old-default"},{"key":"watts","label":"Watts","type":"number","required":false},{"key":"voltage","label":"Voltage","type":"number","required":false,"options":null,"defaultValue":1.0},{"key":"managed","label":"Managed","type":"boolean","required":true,"defaultValue":false},{"key":"notes","label":"Notes","type":"text","required":false,"defaultValue":null},{"key":"retired","label":"Retired","type":"text","required":false},{"key":"gone","label":"Gone","type":"text","required":false},{"key":"tier","label":"Tier","type":"choice","required":false,"options":["Core","Edge"]},{"key":"optional_flag","label":"Optional flag","type":"boolean","required":false}]""";
        const string after = """[{"key":"serial","label":"New label","type":"text","required":true,"defaultValue":"new-default"},{"key":"watts","label":"Watts","type":"text","required":false},{"key":"voltage","label":"Voltage","type":"number","required":false,"defaultValue":1},{"key":"managed","label":"Managed","type":"boolean","required":true,"defaultValue":false},{"key":"notes","label":"Notes","type":"text","required":false},{"key":"new_date","label":"New date","type":"date","required":false},{"key":"tier","label":"Tier","type":"choice","required":false,"options":["Edge","Core"]},{"key":"optional_flag","label":"Optional flag","type":"boolean","required":false}]""";
        using var c = await Login(); var type = await Type(c, before);
        var a = await Asset(c, type, """{"serial":"same","watts":0,"voltage":1.000,"managed":false,"notes":"","gone":"obsolete","tier":"Core","optional_flag":false}""");
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Root("asset-types") + "/" + type.Id, new AssetTypeRequest("Second template", Json(after), 1), true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(a) + "/fields", new ReviseAssetFieldsRequest(1, type.Id, 2, Json("""{"serial":"same","watts":"0","voltage":1,"managed":false,"notes":"","tier":"Core"}""")), true)).StatusCode);
        // A later archived head must not supply historical labels, defaults or authority.
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Root("asset-types") + "/" + type.Id, new AssetTypeRequest("Future template", Json(after.Replace("New label", "future-label").Replace("new-default", "future-default")), 2, true), true)).StatusCode);
        var result = await c.GetAsync(Compare(a, extra: "&changesOnly=false")); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var text = await result.Content.ReadAsStringAsync(); Assert.DoesNotContain("future-label", text); Assert.DoesNotContain("future-default", text);
        var comparison = (await result.Content.ReadFromJsonAsync<AssetComparisonDto>())!;
        Assert.Equal("Historical template", comparison.From.Template!.Name); Assert.Equal("Second template", comparison.To.Template!.Name);
        Assert.Equal(1, comparison.From.Template.Version); Assert.Equal(2, comparison.To.Template.Version);
        AssetComparisonRowDto Row(string key) => comparison.Rows.Single(x => x.Section == "field" && x.Key == key);
        var serial = Row("serial"); Assert.True(serial.DefinitionChanged); Assert.False(serial.ValueChanged); Assert.Equal("Old label", serial.From.Definition!.Value.GetProperty("label").GetString()); Assert.Equal("new-default", serial.To.Definition!.Value.GetProperty("defaultValue").GetString()); Assert.Equal("same", serial.To.Value!.Value.GetString());
        Assert.True(Row("watts").DefinitionChanged); Assert.True(Row("watts").ValueChanged); Assert.Equal(0, Row("watts").From.Value!.Value.GetInt32()); Assert.Equal("0", Row("watts").To.Value!.Value.GetString());
        Assert.Equal("unchanged", Row("voltage").Change); Assert.Equal("unchanged", Row("managed").Change); Assert.False(Row("managed").From.Value!.Value.GetBoolean());
        Assert.Equal("unchanged", Row("notes").Change); Assert.True(Row("notes").From.Present); Assert.Equal("", Row("notes").From.Value!.Value.GetString());
        Assert.Equal("removed", Row("retired").Change); Assert.False(Row("retired").From.Present); Assert.NotNull(Row("retired").From.Definition); Assert.Null(Row("retired").To.Definition); Assert.False(Row("retired").ValueChanged);
        Assert.Equal("removed", Row("gone").Change); Assert.True(Row("gone").ValueChanged);
        Assert.Equal("added", Row("new_date").Change); Assert.False(Row("new_date").To.Present); Assert.NotNull(Row("new_date").To.Definition); Assert.False(Row("new_date").ValueChanged);
        Assert.True(Row("tier").DefinitionChanged); Assert.False(Row("tier").ValueChanged);
        Assert.True(Row("optional_flag").ValueChanged); Assert.False(Row("optional_flag").DefinitionChanged); Assert.False(Row("optional_flag").To.Present);
        Assert.Equal(8, comparison.ChangedRows);
        Assert.False(await fixture.Scalar<bool>($"SELECT EXISTS (SELECT 1 FROM \"AuditEvents\" e WHERE e.\"TargetId\"='{a.Id}' AND to_jsonb(e)::text LIKE '%old-default%')"));
    }

    [Fact]
    public async Task Maximal_disjoint_schemas_have_stable_bounded_filtered_pages_and_every_page_is_audited()
    {
        static string Definition(string prefix) => JsonSerializer.Serialize(Enumerable.Range(0,20).Select(i => new { key=prefix+i.ToString("D2"),label="Field "+i,type="text",required=false,defaultValue="private-schema-default" }));
        static string Values(string prefix) => JsonSerializer.Serialize(Enumerable.Range(0,20).ToDictionary(i=>prefix+i.ToString("D2"),_=>new string('x',500)));
        using var c = await Login(); var type = await Type(c, Definition("old")); var a = await Asset(c,type,Values("old"));
        Assert.Equal(HttpStatusCode.OK,(await Write(c,Root("asset-types")+"/"+type.Id,new AssetTypeRequest("New shape",Json(Definition("new")),1),true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(c,Path(a)+"/fields",new ReviseAssetFieldsRequest(1,type.Id,2,Json(Values("new"))),true)).StatusCode);
        var keys = new List<string>(); int? offset=0;var pages=0;
        while(offset!=null)
        {
            var p=(await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a,extra:"&offset="+offset)))!;
            Assert.Equal(41,p.TotalRows);Assert.Equal(41,p.ChangedRows);Assert.InRange(p.Rows.Count,1,20);Assert.Equal(offset,p.Offset);
            keys.AddRange(p.Rows.Select(r=>r.Section+":"+r.Key));offset=p.NextOffset;Assert.True(++pages<=3);
        }
        Assert.Equal(3,pages);Assert.Equal(41,keys.Distinct().Count());Assert.Equal("metadata:template",keys[0]);
        Assert.Equal(Enumerable.Range(0,20).Select(i=>"field:new"+i.ToString("D2")).Concat(Enumerable.Range(0,20).Select(i=>"field:old"+i.ToString("D2"))),keys.Skip(1));
        Assert.Equal(3L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{a.Id}' AND \"Action\"='Asset.Compared' AND \"SourceVersion\"=1 AND \"TargetVersion\"=2"));
        var full=(await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a,extra:"&changesOnly=false")))!;Assert.Equal(45,full.TotalRows);Assert.Equal(20,full.Rows.Count);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.GetAsync(Compare(a,extra:"&offset=41"))).StatusCode);
        Assert.Equal(0L,await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\" WHERE \"Type\"='Asset.Compared'"));
        using var viewer=await Login("viewer");
        Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync(Compare(a))).StatusCode);
        var member=DevelopmentSeed.Profiles.Single(x=>x.Id=="viewer").MembershipId;
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{Org}'");
            var denied=await viewer.GetAsync(Compare(a,extra:"&offset=20"));Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            var body=await denied.Content.ReadAsStringAsync();Assert.DoesNotContain("\"rows\"",body);Assert.DoesNotContain("nextOffset",body);Assert.DoesNotContain("private-schema-default",body);
        }
        finally {await fixture.Execute($"INSERT INTO \"OrganizationGrants\" VALUES ('{DevelopmentSeed.TenantA}','{member}','{Org}','Viewer')");}
    }

    [Theory]
    [InlineData(0,1,-1)]
    [InlineData(1,0,0)]
    [InlineData(-1,1,0)]
    [InlineData(1,1,-1)]
    [InlineData(1,1,1)]
    [InlineData(1,1,int.MaxValue)]
    public async Task Invalid_versions_or_offsets_do_not_audit_or_return_comparison_content(int from,int to,int offset)
    {
        using var c=await Login();var a=await Asset(c);var r=await c.GetAsync(Compare(a,from,to,"&offset="+offset));Assert.Equal(HttpStatusCode.BadRequest,r.StatusCode);
        Assert.DoesNotContain(a.Name,await r.Content.ReadAsStringAsync());Assert.Equal(0L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"Action\"='Asset.Compared' AND \"TargetId\"='{a.Id}'"));
    }

    [Fact]
    public async Task Viewer_can_compare_but_guessed_scopes_and_live_revoked_authority_withhold_all_content()
    {
        using var c=await Login();using var viewer=await Login("viewer");using var bob=await Login("bob");
        var type=await Type(c,BasicDefinition);var a=await Asset(c,type,"{\"serial\":\"private-comparison-value\"}");await Revised(c,a);
        Assert.Equal(HttpStatusCode.OK,(await viewer.GetAsync(Compare(a))).StatusCode);
        using var anon=factory.CreateClient();Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync(Compare(a))).StatusCode);
        foreach(var url in new[]{Compare(a,1,999),Root("assets",DevelopmentSeed.OrgA2)+$"/{a.Id}/compare?fromVersion=1&toVersion=2",Root("assets")+$"/{Guid.NewGuid()}/compare?fromVersion=1&toVersion=2"}) Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(Compare(a))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Write(viewer,Path(a)+"/restore",new RestoreDocumentRequest(1,2,"No authority from comparison"))).StatusCode);
        var member=DevelopmentSeed.Profiles.Single(x=>x.Id=="viewer").MembershipId;
        try
        {
            await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{Org}'");
            var r=await viewer.GetAsync(Compare(a,extra:"&offset=0&changesOnly=false"));Assert.Equal(HttpStatusCode.NotFound,r.StatusCode);Assert.DoesNotContain("private-comparison-value",await r.Content.ReadAsStringAsync());
        }
        finally {await fixture.Execute($"INSERT INTO \"OrganizationGrants\" VALUES ('{DevelopmentSeed.TenantA}','{member}','{Org}','Viewer')");}
        try {await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");Assert.Equal(HttpStatusCode.Unauthorized,(await viewer.GetAsync(Compare(a))).StatusCode);}
        finally {await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'");}
    }

    [Fact]
    public async Task Missing_pinned_schema_source_failure_and_failed_audit_never_return_rows_or_default_content()
    {
        using var c=await Login();var type=await Type(c,BasicDefinition);var a=await Asset(c,type,"{\"serial\":\"private-comparison-value\"}");await Revised(c,a);
        async Task Withheld(HttpStatusCode status)
        {
            var response=await c.GetAsync(Compare(a,extra:"&changesOnly=false"));Assert.Equal(status,response.StatusCode);var body=await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("private-comparison-value",body);Assert.DoesNotContain("private-historical-default",body);Assert.DoesNotContain("Historical serial label",body);Assert.DoesNotContain("\"rows\"",body);Assert.DoesNotContain("nextOffset",body);
        }
        await fixture.Execute("CREATE POLICY comparison_hide_schema ON \"AssetTypeRevisions\" AS RESTRICTIVE FOR SELECT TO saveit_app USING (false)");
        try {await Withheld(HttpStatusCode.ServiceUnavailable);} finally {await fixture.Execute("DROP POLICY comparison_hide_schema ON \"AssetTypeRevisions\"");}
        await fixture.Execute("REVOKE SELECT ON \"AssetRevisions\" FROM saveit_app");
        try {await Withheld(HttpStatusCode.InternalServerError);} finally {await fixture.Execute("GRANT SELECT ON \"AssetRevisions\" TO saveit_app");}
        await fixture.Execute("CREATE FUNCTION fail_asset_comparison() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='Asset.Compared' THEN RAISE EXCEPTION 'Synthetic comparison audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_asset_comparison BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_asset_comparison()");
        try {await Withheld(HttpStatusCode.InternalServerError);Assert.Equal(0L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"Action\"='Asset.Compared' AND \"TargetId\"='{a.Id}'"));}
        finally {await fixture.Execute("DROP TRIGGER fail_asset_comparison ON \"AuditEvents\"; DROP FUNCTION fail_asset_comparison()");}
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync(Compare(a))).StatusCode);
    }

    [Fact]
    public async Task Comparison_never_substitutes_for_restore_concurrency_and_restored_content_compares_equal()
    {
        using var c=await Login();var type=await Type(c,BasicDefinition);var a=await Asset(c,type,"{\"serial\":\"exact-original\"}");var changed=await Revised(c,a);
        var comparison=(await c.GetFromJsonAsync<AssetComparisonDto>(Compare(a)))!;Assert.Equal(2,comparison.CurrentVersion);
        var latest=await Revised(c,changed);
        Assert.Equal(HttpStatusCode.Conflict,(await Write(c,Path(a)+"/restore",new RestoreDocumentRequest(1,comparison.CurrentVersion,"Stale comparison cannot authorize restore"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Write(c,Path(a)+"/restore",new RestoreDocumentRequest(1,latest.Version,"Confidential restore rationale"))).StatusCode);
        var response=await c.GetAsync(Compare(a,1,4));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.DoesNotContain("Confidential restore rationale",await response.Content.ReadAsStringAsync());
        comparison=(await response.Content.ReadFromJsonAsync<AssetComparisonDto>())!;Assert.Equal(0,comparison.ChangedRows);Assert.Empty(comparison.Rows);Assert.Equal(1,comparison.To.RestoredFromVersion);Assert.Equal(4,comparison.CurrentVersion);
        Assert.Equal(4L,await fixture.Scalar<long>($"SELECT count(*) FROM \"AssetRevisions\" WHERE \"AssetId\"='{a.Id}'"));
    }
    public void Dispose() => factory.Dispose();
}
