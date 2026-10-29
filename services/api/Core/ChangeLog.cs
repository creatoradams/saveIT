using SaveIT.Api.Data;

namespace SaveIT.Api.Core;

public static class ChangeLog
{
    public static void Add(SaveItDb db, TenantScope scope, HttpContext context, Guid org, Guid target, string action, bool publish = true, int? source = null, int? version = null)
    {
        var now = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new() { TenantId = scope.TenantId, OrganizationId = org, TargetId = target, Actor = scope.Subject,
            Action = action, CreatedAt = now, CorrelationId = context.TraceIdentifier, SourceVersion = source, TargetVersion = version });
        if (publish) db.OutboxEvents.Add(new() { Id = Guid.NewGuid(), TenantId = scope.TenantId, OrganizationId = org,
            TargetId = target, Type = action, SchemaVersion = 1, CreatedAt = now });
    }
    public static IResult Conflict() => Results.Problem(statusCode: 409, title: "This record changed. Reload it before saving again.");
    public static bool Text(string? value, int max, bool required = false) => value is not null && value.Length <= max
        && (!required || !string.IsNullOrWhiteSpace(value)) && !value.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'));
}
