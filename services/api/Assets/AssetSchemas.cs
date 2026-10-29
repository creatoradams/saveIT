using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SaveIT.Api.Core;
using SaveIT.Api.Data;

namespace SaveIT.Api.Assets;

public static class AssetSchemas
{
    public static JsonElement Json(string value) => JsonSerializer.Deserialize<JsonElement>(value);
    public static AssetTypeDto Dto(AssetType x) => new(x.Id, x.OrganizationId, x.Name, x.Version, x.IsArchived, x.UpdatedAt);
    public static AssetTypeRevisionDto Dto(AssetTypeRevision x) => new(x.AssetTypeId, x.Version, x.Name, Json(x.DefinitionJson), x.Actor, x.CreatedAt);
    public static IResult Invalid() => Results.Problem(statusCode: 400, title: "Use a valid template and field values. Definitions and values are limited to 16 KiB and 20 fields; check required fields, types and limits.");

    // Check the original JSON before jsonb normalization can erase duplicate properties.
    public static bool SafeJson(JsonElement value)
    {
        try
        {
            if (value.ValueKind == JsonValueKind.Undefined || Encoding.UTF8.GetByteCount(value.GetRawText()) > 16384) return false;
            return Walk(value);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or JsonException) { return false; }
    }
    private static bool Walk(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() == value.EnumerateObject().Count()
            && value.EnumerateObject().All(x => !x.Name.Contains('\0') && Walk(x.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(Walk),
        JsonValueKind.String => value.GetString() is { } s && !s.Contains('\0') && s.All(c => !char.IsControl(c) || c is '\r' or '\n' or '\t'),
        JsonValueKind.Number => SafeNumber(value),
        JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
        _ => false
    };
    private static bool SafeNumber(JsonElement value)
    {
        var raw = value.GetRawText();
        var parts = raw.Split(['e', 'E']);
        if (raw.Length > 64 || parts.Length > 1 && (!int.TryParse(parts[1], out var exponent) || exponent is < -28 or > 28)) return false;
        return value.TryGetDecimal(out var number) && number is >= -1_000_000_000 and <= 1_000_000_000
            && decimal.Round(number, 3) == number && (number != 0 || !parts[0].Any(c => c is >= '1' and <= '9'));
    }
    public static async Task<bool> ValidDefinition(SaveItDb db, JsonElement definition, CancellationToken ct) =>
        SafeJson(definition) && await db.Database.SqlQuery<bool>($"SELECT public.valid_asset_definition_v1({definition.GetRawText()}::jsonb) AS \"Value\"").SingleAsync(ct);

    public static async Task<bool> ValidBinding(SaveItDb db, Guid org, Guid? type, int? version, JsonElement fields, bool newBinding, CancellationToken ct)
    {
        if (fields.ValueKind != JsonValueKind.Object || !SafeJson(fields)) return false;
        if (type is null || version is null) return type is null && version is null && !fields.EnumerateObject().Any();
        if (version < 1 || !await db.AssetTypes.AnyAsync(x => x.Id == type && x.OrganizationId == org && (!newBinding || !x.IsArchived), ct)) return false;
        var definition = await db.AssetTypeRevisions.Where(x => x.AssetTypeId == type && x.OrganizationId == org && x.Version == version).Select(x => x.DefinitionJson).SingleOrDefaultAsync(ct);
        return definition != null && await db.Database.SqlQuery<bool>($"SELECT public.valid_asset_fields_v1({definition}::jsonb, {fields.GetRawText()}::jsonb) AS \"Value\"").SingleAsync(ct);
    }
    public static AssetTypeRevision Snapshot(AssetType type, string definition, TenantScope scope) => new()
    {
        TenantId = scope.TenantId, OrganizationId = type.OrganizationId, AssetTypeId = type.Id, Version = type.Version,
        Name = type.Name, DefinitionJson = definition, Actor = scope.Subject, CreatedAt = type.UpdatedAt
    };
}
