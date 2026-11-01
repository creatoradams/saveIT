using System.Net;
using System.Net.Http.Json;
using SaveIT.Api.Data;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class DocumentTemplateComparisonTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static string Root(Guid? org = null) => $"/api/v1/organizations/{org ?? DevelopmentSeed.OrgA}/document-templates";
    private static string Path(DocumentTemplateDto t) => Root() + $"/{t.Id}";
    private static string Compare(DocumentTemplateDto t, int from = 1, int to = 2, string extra = "") =>
        Path(t) + $"/compare?fromVersion={from}&toVersion={to}{extra}";
    private sealed record Csrf(string Token);
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object body, bool put = false)
    {
        var csrf = (await c.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        using var request = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.Token);
        return await c.SendAsync(request);
    }
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var c = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile })).StatusCode);
        return c;
    }
    private static async Task<DocumentTemplateDto> Create(HttpClient c, string content = "Private original body\r\nExact 😀\n")
    {
        var r = await Write(c, Root(), new DocumentTemplateRequest("Private original <img src=x>", "Private original title", content));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<DocumentTemplateDto>())!;
    }
    private static async Task<DocumentTemplateDto> Revise(HttpClient c, DocumentTemplateDto t,
        string content = "Private changed body\nExact 😀\n", bool archived = true)
    {
        var r = await Write(c, Path(t), new DocumentTemplateRequest("Private revised", "Private revised title", content, t.Version, archived), true);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<DocumentTemplateDto>())!;
    }
    private static void Withheld(string text)
    {
        foreach (var field in new[] { "Private", "\"from\"", "\"to\"", "\"lines\"", "\"totalRows\"", "\"nextOffset\"", "\"currentVersion\"" })
            Assert.DoesNotContain(field, text);
    }

    [Fact]
    public async Task Exact_immutable_metadata_lifecycle_content_same_and_reverse_comparisons_only_add_numeric_read_audits()
    {
        using var c = await Login(); var t = await Create(c); var second = await Revise(c, t);
        await Revise(c, second, "Private current content must not replace history", false);
        var before = await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{t.Id}'");
        var d = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t)))!;
        Assert.Equal(3, d.CurrentVersion); Assert.Equal(t.Name, d.From.Name); Assert.Equal("Private original title", d.From.Title);
        Assert.False(d.From.IsArchived); Assert.True(d.To.IsArchived);
        Assert.True(d.NameChanged && d.TitleChanged && d.LifecycleChanged && d.ContentChanged); Assert.False(d.LineEndingsOnly);
        Assert.True(d.ChangesOnly); Assert.Equal(2, d.TotalRows); Assert.Equal(1, d.AddedLines); Assert.Equal(1, d.RemovedLines);
        Assert.Equal(new[] { "removed", "added" }, d.Lines.Select(x => x.Kind));
        Assert.Equal("Private original body", d.Lines[0].Text); Assert.Equal("Private changed body", d.Lines[1].Text);
        var reverse = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t, 2, 1)))!;
        Assert.Equal(d.To, reverse.From); Assert.Equal(d.From, reverse.To);
        Assert.Equal("Private changed body", reverse.Lines[0].Text);
        var same = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t, 1, 1)))!;
        Assert.Empty(same.Lines); Assert.Equal(0, same.TotalRows); Assert.Null(same.NextOffset);
        Assert.False(same.NameChanged || same.TitleChanged || same.LifecycleChanged || same.ContentChanged);
        var all = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t, 1, 1, "&changesOnly=false")))!;
        Assert.Equal(new[] { "Private original body", "Exact 😀", "" }, all.Lines.Select(x => x.Text));
        Assert.All(all.Lines, x => Assert.Equal("unchanged", x.Kind));
        Assert.Equal(before, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{t.Id}'"));
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateRevisions\" WHERE \"TemplateId\"='{t.Id}'"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateOrigins\" WHERE \"TemplateId\"='{t.Id}'"));
        Assert.Equal(4L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{t.Id}' AND \"Action\"='DocumentTemplate.Compared' AND \"SourceVersion\" IS NOT NULL AND \"TargetVersion\" IS NOT NULL"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" e WHERE e.\"TargetId\"='{t.Id}' AND to_jsonb(e)::text LIKE '%Private%'"));
    }

    [Theory]
    [InlineData("Line\r\nExact\r\n", "Line\nExact\n")]
    [InlineData("Line\rExact\r", "Line\nExact\n")]
    public async Task Line_endings_remain_exact_in_storage_and_are_explained_without_false_line_edits(string from, string to)
    {
        using var c = await Login(); var t = await Create(c, from); await Revise(c, t, to);
        var d = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t)))!;
        Assert.True(d.ContentChanged && d.LineEndingsOnly); Assert.Empty(d.Lines); Assert.Equal(0, d.AddedLines + d.RemovedLines);
        Assert.Equal(from, await fixture.Scalar<string>($"SELECT \"Content\" FROM \"DocumentTemplateRevisions\" WHERE \"TemplateId\"='{t.Id}' AND \"Version\"=1"));
        Assert.Equal(to, await fixture.Scalar<string>($"SELECT \"Content\" FROM \"DocumentTemplateRevisions\" WHERE \"TemplateId\"='{t.Id}' AND \"Version\"=2"));
    }

    [Fact]
    public async Task Large_replacements_filter_before_bounded_pages_and_reconstruct_every_changed_line_with_audit_per_page()
    {
        using var c = await Login(); var old = Enumerable.Range(1, 650).Select(i => $"old {i}").ToArray(); var next = Enumerable.Range(1, 650).Select(i => $"new {i}").ToArray();
        var t = await Create(c, "prefix\n" + string.Join('\n', old) + "\nsuffix");
        await Revise(c, t, "prefix\n" + string.Join('\n', next) + "\nsuffix");
        var rows = new List<DiffLineDto>(); int? offset = 0; var pages = 0;
        while (offset is not null)
        {
            var d = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t, extra: $"&offset={offset}")))!;
            Assert.True(d.Simplified); Assert.Equal(1300, d.TotalRows); Assert.InRange(d.Lines.Count, 1, 200);
            Assert.Equal(offset.Value, d.Offset); Assert.DoesNotContain(d.Lines, x => x.Kind == "unchanged");
            rows.AddRange(d.Lines); offset = d.NextOffset; pages++;
        }
        Assert.Equal(old.Concat(next), rows.Select(x => x.Text)); Assert.Equal(7, pages);
        Assert.Equal(2, rows[0].OldLine); Assert.Equal(651, rows[649].OldLine); Assert.Equal(2, rows[650].NewLine);
        var all = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t, extra: "&changesOnly=false")))!;
        Assert.Equal(1302, all.TotalRows); Assert.Equal("prefix", all.Lines[0].Text); Assert.Equal(200, all.NextOffset);
        Assert.Equal(8L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{t.Id}' AND \"Action\"='DocumentTemplate.Compared'"));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Compare(t, extra: "&offset=1300"))).StatusCode);
    }

    [Fact]
    public async Task Live_viewer_membership_tenant_and_organization_authority_are_rechecked_on_every_page()
    {
        using var c = await Login(); var t = await Create(c, string.Join('\n', Enumerable.Range(1, 230))); await Revise(c, t, "different");
        using var viewer = await Login("viewer"); using var bob = await Login("bob"); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Compare(t))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Path(t), new DocumentTemplateRequest("No", "No", "No", 2), true)).StatusCode);
        bob.DefaultRequestHeaders.Add("X-Tenant-Id", DevelopmentSeed.TenantA.ToString());
        foreach (var response in new[] { await bob.GetAsync(Compare(t)), await c.GetAsync(Root(DevelopmentSeed.OrgA2) + $"/{t.Id}/compare?fromVersion=1&toVersion=2"), await c.GetAsync(Root() + $"/{Guid.NewGuid()}/compare?fromVersion=1&toVersion=2") })
        { Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); Withheld(await response.Content.ReadAsStringAsync()); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Compare(t))).StatusCode);
        var member = DevelopmentSeed.Profiles.Single(x => x.Id == "viewer").MembershipId;
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'");
        try { var denied = await viewer.GetAsync(Compare(t, extra: "&offset=200")); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Withheld(await denied.Content.ReadAsStringAsync()); }
        finally { await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{DevelopmentSeed.TenantA}','{member}','{DevelopmentSeed.OrgA}','Viewer')"); }
        await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
        try { var denied = await viewer.GetAsync(Compare(t)); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Withheld(await denied.Content.ReadAsStringAsync()); }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'"); }
    }

    [Fact]
    public async Task Invalid_binding_offsets_versions_and_missing_history_have_no_success_audit_or_head_fallback()
    {
        using var c = await Login(); var t = await Create(c); await Revise(c, t);
        foreach (var query in new[] { "", "?fromVersion=1", "?fromVersion=0&toVersion=2", "?fromVersion=-1&toVersion=2", "?fromVersion=2147483648&toVersion=2", "?fromVersion=1&toVersion=2&offset=-1", "?fromVersion=1&toVersion=2&offset=2", "?fromVersion=1&toVersion=2&offset=bad", "?fromVersion=1&toVersion=2&changesOnly=bad" })
        { var r = await c.GetAsync(Path(t) + "/compare" + query); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(Compare(t, 1, int.MaxValue))).StatusCode);
        await fixture.Execute($"DELETE FROM \"DocumentTemplateRevisions\" WHERE \"TemplateId\"='{t.Id}' AND \"Version\"=1");
        var missing = await c.GetAsync(Compare(t)); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); Withheld(await missing.Content.ReadAsStringAsync());
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{t.Id}' AND \"Action\"='DocumentTemplate.Compared'"));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("current")]
    [InlineData("audit")]
    [InlineData("commit")]
    public async Task Actual_source_current_audit_and_deferred_commit_failures_withhold_all_output(string boundary)
    {
        using var c = await Login(); var t = await Create(c); await Revise(c, t);
        var table = boundary == "current" ? "DocumentTemplates" : "DocumentTemplateRevisions";
        if (boundary is "audit" or "commit")
            await fixture.Execute("CREATE FUNCTION fail_template_compare() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='DocumentTemplate.Compared' THEN RAISE EXCEPTION 'Synthetic comparison failure'; END IF; RETURN NEW; END $$; " +
                (boundary == "audit" ? "CREATE TRIGGER fail_template_compare BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_template_compare()" : "CREATE CONSTRAINT TRIGGER fail_template_compare AFTER INSERT ON \"AuditEvents\" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION fail_template_compare()"));
        else await fixture.Execute($"REVOKE SELECT ON \"{table}\" FROM saveit_app");
        try { var r = await c.GetAsync(Compare(t)); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        finally
        {
            if (boundary is "audit" or "commit") await fixture.Execute("DROP TRIGGER fail_template_compare ON \"AuditEvents\"; DROP FUNCTION fail_template_compare()");
            else await fixture.Execute($"GRANT SELECT ON \"{table}\" TO saveit_app");
        }
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{t.Id}' AND \"Action\"='DocumentTemplate.Compared'"));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Compare(t))).StatusCode);
    }

    [Fact]
    public async Task Current_head_projection_requires_only_scope_keys_and_version()
    {
        using var c = await Login(); var t = await Create(c); await Revise(c, t);
        const string columns = "\"TenantId\",\"OrganizationId\",\"Id\",\"Version\"";
        await fixture.Execute($"REVOKE SELECT ON \"DocumentTemplates\" FROM saveit_app; GRANT SELECT ({columns}) ON \"DocumentTemplates\" TO saveit_app");
        try { Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Compare(t))).StatusCode); }
        finally { await fixture.Execute($"REVOKE SELECT ({columns}) ON \"DocumentTemplates\" FROM saveit_app; GRANT SELECT ON \"DocumentTemplates\" TO saveit_app"); }
    }

    [Fact]
    public async Task Comparison_cannot_bypass_later_publication_or_document_creation_conflicts()
    {
        using var c = await Login(); var t = await Create(c); var second = await Revise(c, t, archived: false);
        var observed = (await c.GetFromJsonAsync<DocumentTemplateComparisonDto>(Compare(t)))!;
        await Revise(c, second, archived: false);
        Assert.Equal(HttpStatusCode.Conflict, (await Write(c, Path(t), new DocumentTemplateRequest("No", "No", "No", observed.CurrentVersion), true)).StatusCode);
        var asset = await fixture.Scalar<Guid>($"SELECT \"Id\" FROM \"Assets\" WHERE \"OrganizationId\"='{DevelopmentSeed.OrgA}' LIMIT 1");
        Assert.Equal(HttpStatusCode.Conflict, (await Write(c, Path(t) + "/documents", new CreateTemplateDocumentRequest(asset, observed.CurrentVersion, "No", "No"))).StatusCode);
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateRevisions\" WHERE \"TemplateId\"='{t.Id}'"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"DocumentTemplateOrigins\" WHERE \"TemplateId\"='{t.Id}'"));
    }

    public void Dispose() => factory.Dispose();
}
