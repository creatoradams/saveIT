using System.Net;
using System.Net.Http.Json;
using SaveIT.Api.Data;
using Xunit;

namespace SaveIT.Api.Tests;

[Collection("database")]
public sealed class DirectoryComparisonTests(DatabaseFixture fixture) : IDisposable
{
    private readonly ApiFactory factory = new(fixture.AppConnection);
    private static string Root(string resource, Guid? org = null) => $"/api/v1/organizations/{org ?? DevelopmentSeed.OrgA}/{resource}";
    private static string Table(string resource) => resource == "contacts" ? "ContactRevisions" : "LocationRevisions";
    private static string Action(string resource) => resource == "contacts" ? "Contact.Compared" : "Location.Compared";
    private sealed record Csrf(string Token);
    private async Task<HttpClient> Login(string profile = "alice")
    {
        var c = factory.CreateClient(new() { HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.NoContent, (await Write(c, "/api/v1/dev/login", new { profile })).StatusCode); return c;
    }
    private static async Task<HttpResponseMessage> Write(HttpClient c, string path, object input, bool put = false)
    {
        var csrf = (await c.GetFromJsonAsync<Csrf>("/api/v1/csrf"))!;
        using var r = new HttpRequestMessage(put ? HttpMethod.Put : HttpMethod.Post, path) { Content = JsonContent.Create(input) };
        r.Headers.Add("X-CSRF-TOKEN", csrf.Token); return await c.SendAsync(r);
    }
    private static DirectoryRequest Input(string resource, int version = 0) => new("Private <img src=x> 😀", "Synthetic address / department",
        resource == "contacts" ? "old@example.invalid" : "", resource == "contacts" ? "+1 555 0100" : "", "Exact\r\nnotes 😀\n", false, version);
    private static async Task<DirectoryDto> Create(HttpClient c, string resource)
    {
        var r = await Write(c, Root(resource), Input(resource)); Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<DirectoryDto>())!;
    }
    private static string Path(string resource, DirectoryDto row) => Root(resource) + "/" + row.Id;
    private static string Compare(string resource, DirectoryDto row, int from = 1, int to = 2, string extra = "") => Path(resource, row) + $"/compare?fromVersion={from}&toVersion={to}{extra}";
    private static async Task<DirectoryDto> Revise(HttpClient c, string resource, DirectoryDto row, DirectoryRequest? input = null)
    {
        var r = await Write(c, Path(resource, row), input ?? Input(resource, row.Version) with { Name = "Revised private name", Details = "", Email = resource == "contacts" ? "new@example.invalid" : "", Phone = "", Notes = "Exact\nnotes 😀\n", IsArchived = true }, true);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); return (await r.Content.ReadFromJsonAsync<DirectoryDto>())!;
    }
    private static void Withheld(string text)
    {
        foreach (var value in new[] { "Private", "Revised", "Exact", "old@example.invalid", "rows", "changedRows", "totalRows", "nextOffset", "restoredFromVersion" }) Assert.DoesNotContain(value, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Exact_fields_empty_text_line_endings_lifecycle_reverse_same_and_restored_equality_are_audited(string resource)
    {
        using var c = await Login(); var original = await Create(c, resource); var current = await Revise(c, resource, original);
        var events = await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\"");
        var first = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare(resource, original)))!;
        Assert.Equal(2, first.CurrentVersion); Assert.Equal(resource == "contacts" ? 6 : 4, first.ChangedRows); Assert.True(first.ChangesOnly);
        Assert.Equal(1, first.From.Version); Assert.False(first.From.IsArchived); Assert.Equal(2, first.To.Version); Assert.True(first.To.IsArchived);
        Assert.InRange(first.Rows.Count, 1, 4);
        var rows = first.Rows.ToList();
        if (first.NextOffset is { } next) { Assert.Equal(4, next); rows.AddRange((await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare(resource, original, extra: "&offset=" + next)))!.Rows); }
        Assert.Equal(Input(resource).Name, rows.Single(x => x.Key == "name").From.GetString());
        Assert.Equal("", rows.Single(x => x.Key == "details").To.GetString());
        var notes = rows.Single(x => x.Key == "notes"); Assert.True(notes.LineEndingsOnly); Assert.Equal("Exact\r\nnotes 😀\n", notes.From.GetString()); Assert.Equal("Exact\nnotes 😀\n", notes.To.GetString());
        var lifecycle = rows.Single(x => x.Key == "isArchived"); Assert.False(lifecycle.From.GetBoolean()); Assert.True(lifecycle.To.GetBoolean()); Assert.False(lifecycle.LineEndingsOnly);
        if (resource == "contacts") { Assert.Equal("old@example.invalid", rows.Single(x => x.Key == "email").From.GetString()); Assert.Equal("", rows.Single(x => x.Key == "phone").To.GetString()); }
        var reverse = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare(resource, original, 2, 1)))!;
        Assert.Equal(first.ChangedRows, reverse.ChangedRows); Assert.Equal(first.Rows[0].From.GetString(), reverse.Rows[0].To.GetString());
        var same = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare(resource, original, 2, 2)))!;
        Assert.Empty(same.Rows); Assert.Equal(0, same.TotalRows); Assert.Equal(0, same.ChangedRows); Assert.Null(same.NextOffset);
        Assert.Equal(events, await fixture.Scalar<long>("SELECT count(*) FROM \"OutboxEvents\""));
        Assert.Equal(2L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{original.Id}'"));
        Assert.Equal(resource == "contacts" ? 2L : 1L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{original.Id}' AND \"Action\"='{Action(resource)}' AND \"SourceVersion\"=1 AND \"TargetVersion\"=2"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" e WHERE e.\"TargetId\"='{original.Id}' AND to_jsonb(e)::text LIKE '%old@example.invalid%'"));
        Assert.Equal(HttpStatusCode.OK, (await Write(c, Path(resource, original) + "/restore", new RestoreDocumentRequest(1, 2, "Private restore reason never compared"))).StatusCode);
        await Revise(c, resource, original with { Version = 3 }, Input(resource, 3) with { Name = "Current head must not replace historical content" });
        var restored = await c.GetAsync(Compare(resource, original, 1, 3, "&changesOnly=false")); Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
        var text = await restored.Content.ReadAsStringAsync(); Assert.DoesNotContain("Private restore reason", text); Assert.DoesNotContain("restoreReason", text); Assert.DoesNotContain("Current head must", text);
        var equal = (await restored.Content.ReadFromJsonAsync<DirectoryComparisonDto>())!;
        Assert.Equal(4, equal.CurrentVersion); Assert.Equal(1, equal.To.RestoredFromVersion); Assert.Equal(0, equal.ChangedRows); Assert.All(equal.Rows, x => Assert.False(x.Changed));
    }

    [Fact]
    public async Task Contact_pages_filter_before_offsets_and_recheck_audit_and_current_grants()
    {
        using var c = await Login(); var row = await Create(c, "contacts"); await Revise(c, "contacts", row);
        var first = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare("contacts", row)))!;
        Assert.Equal(new[] { "name", "details", "email", "phone" }, first.Rows.Select(x => x.Key)); Assert.Equal(6, first.TotalRows); Assert.Equal(4, first.NextOffset);
        var tail = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare("contacts", row, extra: "&offset=4")))!;
        Assert.Equal(new[] { "notes", "isArchived" }, tail.Rows.Select(x => x.Key)); Assert.Equal(4, tail.Offset); Assert.Null(tail.NextOffset);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Compare("contacts", row, extra: "&offset=6"))).StatusCode);
        var changed = await Revise(c, "contacts", row with { Version = 2 }, Input("contacts", 2) with { Notes = "Different only notes" });
        var filtered = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare("contacts", row, 1, 3)))!;
        Assert.Equal("notes", Assert.Single(filtered.Rows).Key); Assert.Equal(1, filtered.TotalRows); Assert.Null(filtered.NextOffset);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Compare("contacts", row, 1, 3, "&offset=4"))).StatusCode);
        var full = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare("contacts", row, 1, 3, "&changesOnly=false&offset=4")))!;
        Assert.Equal(6, full.TotalRows); Assert.Equal(1, full.ChangedRows); Assert.Equal(new[] { "notes", "isArchived" }, full.Rows.Select(x => x.Key));
        using var viewer = await Login("viewer"); await viewer.GetAsync(Compare("contacts", row));
        var member = DevelopmentSeed.Profiles.Single(x => x.Id == "viewer").MembershipId;
        await fixture.Execute($"UPDATE \"OrganizationGrants\" SET \"Role\"='Editor' WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'");
        // Remove the grant between real page requests; no metadata/count/cursor can survive it.
        await fixture.Execute($"DELETE FROM \"OrganizationGrants\" WHERE \"MembershipId\"='{member}' AND \"OrganizationId\"='{DevelopmentSeed.OrgA}'");
        try { var denied = await viewer.GetAsync(Compare("contacts", row, extra: "&offset=4")); Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Withheld(await denied.Content.ReadAsStringAsync()); }
        finally { await fixture.Execute($"INSERT INTO \"OrganizationGrants\" (\"TenantId\",\"MembershipId\",\"OrganizationId\",\"Role\") VALUES ('{DevelopmentSeed.TenantA}','{member}','{DevelopmentSeed.OrgA}','Viewer')"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Current_viewer_tenant_organization_resource_and_membership_boundaries_apply(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); await Revise(c, resource, row);
        using var viewer = await Login("viewer"); using var bob = await Login("bob"); using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(Compare(resource, row))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(viewer, Path(resource, row) + "/restore", new RestoreDocumentRequest(1, 2, "No comparison authority"))).StatusCode);
        bob.DefaultRequestHeaders.Add("X-Tenant-Id", DevelopmentSeed.TenantA.ToString());
        foreach (var denied in new[] { await bob.GetAsync(Compare(resource, row)), await c.GetAsync(Root(resource, DevelopmentSeed.OrgA2) + $"/{row.Id}/compare?fromVersion=1&toVersion=2"), await c.GetAsync(Root(resource == "contacts" ? "locations" : "contacts") + $"/{row.Id}/compare?fromVersion=1&toVersion=2") })
        { Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode); Withheld(await denied.Content.ReadAsStringAsync()); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Compare(resource, row))).StatusCode);
        var member = DevelopmentSeed.Profiles.Single(x => x.Id == "viewer").MembershipId;
        await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=false WHERE \"Id\"='{member}'");
        try { var denied = await viewer.GetAsync(Compare(resource, row)); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Withheld(await denied.Content.ReadAsStringAsync()); }
        finally { await fixture.Execute($"UPDATE \"Memberships\" SET \"Active\"=true WHERE \"Id\"='{member}'"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Invalid_offsets_versions_binding_and_missing_history_have_no_success_audit_or_fallback(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); await Revise(c, resource, row);
        foreach (var query in new[] { "", "?fromVersion=1", "?fromVersion=0&toVersion=2", "?fromVersion=-1&toVersion=2", "?fromVersion=2147483648&toVersion=2", "?fromVersion=1&toVersion=2&offset=-1", "?fromVersion=1&toVersion=2&offset=99", "?fromVersion=1&toVersion=2&offset=bad", "?fromVersion=1&toVersion=2&changesOnly=bad" })
        { var r = await c.GetAsync(Path(resource, row) + "/compare" + query); Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(Compare(resource, row, 1, int.MaxValue))).StatusCode);
        await fixture.Execute($"DELETE FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}' AND \"Version\"=1");
        var missing = await c.GetAsync(Compare(resource, row)); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode); Withheld(await missing.Content.ReadAsStringAsync());
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"AuditEvents\" WHERE \"TargetId\"='{row.Id}' AND \"Action\"='{Action(resource)}'"));
    }

    [Theory]
    [InlineData("locations", "source")]
    [InlineData("contacts", "source")]
    [InlineData("locations", "current")]
    [InlineData("contacts", "current")]
    [InlineData("locations", "audit")]
    [InlineData("contacts", "audit")]
    public async Task Actual_source_current_and_audit_failures_withhold_all_historical_output(string resource, string boundary)
    {
        using var c = await Login(); var row = await Create(c, resource); await Revise(c, resource, row);
        var table = boundary == "current" ? resource == "contacts" ? "Contacts" : "Locations" : Table(resource);
        if (boundary == "audit") await fixture.Execute($"CREATE FUNCTION fail_directory_comparison() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.\"Action\"='{Action(resource)}' THEN RAISE EXCEPTION 'Synthetic compare audit failure'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_directory_comparison BEFORE INSERT ON \"AuditEvents\" FOR EACH ROW EXECUTE FUNCTION fail_directory_comparison()");
        else await fixture.Execute($"REVOKE SELECT ON \"{table}\" FROM saveit_app");
        try { var r = await c.GetAsync(Compare(resource, row)); Assert.Equal(HttpStatusCode.InternalServerError, r.StatusCode); Withheld(await r.Content.ReadAsStringAsync()); }
        finally
        {
            if (boundary == "audit") await fixture.Execute("DROP TRIGGER fail_directory_comparison ON \"AuditEvents\"; DROP FUNCTION fail_directory_comparison()");
            else await fixture.Execute($"GRANT SELECT ON \"{table}\" TO saveit_app");
        }
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Compare(resource, row))).StatusCode);
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Projection_requires_only_current_version_and_never_reads_restore_reason(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); await Revise(c, resource, row);
        var current = resource == "contacts" ? "Contacts" : "Locations"; var history = Table(resource);
        var columns = "\"TenantId\",\"OrganizationId\",\"RecordId\",\"Version\",\"Name\",\"Details\",\"Notes\",\"IsArchived\",\"Actor\",\"CreatedAt\",\"RestoredFromVersion\"" + (resource == "contacts" ? ",\"Email\",\"Phone\"" : "");
        await fixture.Execute($"REVOKE SELECT ON \"{current}\",\"{history}\" FROM saveit_app; GRANT SELECT (\"TenantId\",\"OrganizationId\",\"Id\",\"Version\") ON \"{current}\" TO saveit_app; GRANT SELECT ({columns}) ON \"{history}\" TO saveit_app");
        try { Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Compare(resource, row))).StatusCode); }
        finally { await fixture.Execute($"REVOKE SELECT (\"TenantId\",\"OrganizationId\",\"Id\",\"Version\") ON \"{current}\" FROM saveit_app; REVOKE SELECT ({columns}) ON \"{history}\" FROM saveit_app; GRANT SELECT ON \"{current}\",\"{history}\" TO saveit_app"); }
    }

    [Theory]
    [InlineData("locations")]
    [InlineData("contacts")]
    public async Task Comparison_does_not_bypass_later_restore_version_conflicts(string resource)
    {
        using var c = await Login(); var row = await Create(c, resource); var current = await Revise(c, resource, row);
        var observed = (await c.GetFromJsonAsync<DirectoryComparisonDto>(Compare(resource, row)))!;
        await Revise(c, resource, current, Input(resource, 2));
        var denied = await Write(c, Path(resource, row) + "/restore", new RestoreDocumentRequest(1, observed.CurrentVersion, "Comparison grants no write authority"));
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal(3L, await fixture.Scalar<long>($"SELECT count(*) FROM \"{Table(resource)}\" WHERE \"RecordId\"='{row.Id}'"));
        Assert.Equal(0L, await fixture.Scalar<long>($"SELECT count(*) FROM \"OutboxEvents\" WHERE \"TargetId\"='{row.Id}' AND \"Type\" LIKE '%.Restored'"));
    }

    public void Dispose() => factory.Dispose();
}
