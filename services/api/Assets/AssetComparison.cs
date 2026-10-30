using System.Text.Json;

namespace SaveIT.Api.Assets;

public sealed record AssetComparisonSnapshot(int Version, string Name, string Kind, Guid? LocationId,
    Guid? AssetTypeId, int? AssetTypeVersion, string FieldsJson, string Actor, DateTimeOffset CreatedAt, int? RestoredFromVersion);

public static class AssetComparison
{
    public const int PageSize = 20;

    public static IReadOnlyList<AssetComparisonRowDto> Compare(AssetComparisonSnapshot from, AssetComparisonSnapshot to,
        AssetTypeRevisionDto? fromSchema, AssetTypeRevisionDto? toSchema)
    {
        var rows = new List<AssetComparisonRowDto>
        {
            Metadata("name", "Asset name", from.Name, to.Name), Metadata("kind", "Asset type", from.Kind, to.Kind),
            Metadata("location", "Location ID", from.LocationId?.ToString(), to.LocationId?.ToString()),
            Metadata("template", "Template pin", Pin(from), Pin(to))
        };
        var fromDefinitions = Definitions(fromSchema); var toDefinitions = Definitions(toSchema);
        var fromValues = AssetSchemas.Json(from.FieldsJson); var toValues = AssetSchemas.Json(to.FieldsJson);
        foreach (var key in fromDefinitions.Keys.Union(toDefinitions.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var old = Side(fromDefinitions, fromValues, key); var current = Side(toDefinitions, toValues, key);
            var definitionChanged = !SameDefinition(old.Definition, current.Definition);
            var valueChanged = old.Present != current.Present || !SameScalar(old.Value, current.Value);
            var change = old.Definition is null ? "added" : current.Definition is null ? "removed"
                : definitionChanged || valueChanged ? "changed" : "unchanged";
            rows.Add(new("field", key, key, change, valueChanged, definitionChanged, old, current));
        }
        return rows;
    }

    public static AssetComparisonRevisionDto Summary(AssetComparisonSnapshot snapshot, AssetTypeRevisionDto? schema) =>
        new(snapshot.Version, snapshot.Name, snapshot.Actor, snapshot.CreatedAt, snapshot.RestoredFromVersion,
            schema is null ? null : new(schema.AssetTypeId, schema.Version, schema.Name));
    private static string? Pin(AssetComparisonSnapshot x) => x.AssetTypeId is null ? null : $"{x.AssetTypeId}@{x.AssetTypeVersion}";
    private static Dictionary<string, JsonElement> Definitions(AssetTypeRevisionDto? schema) => schema is null ? new(StringComparer.Ordinal)
        : schema.Definition.EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!, f => f, StringComparer.Ordinal);
    private static AssetComparisonSideDto Side(Dictionary<string, JsonElement> definitions, JsonElement values, string key)
    {
        if (!definitions.TryGetValue(key, out var definition)) return new(false, null, null);
        var present = values.TryGetProperty(key, out var value);
        return new(present, present ? value : null, definition);
    }
    private static AssetComparisonRowDto Metadata(string key, string label, string? from, string? to)
    {
        var changed = !string.Equals(from, to, StringComparison.Ordinal);
        return new("metadata", key, label, changed ? "changed" : "unchanged", changed, false,
            new(from is not null, from is null ? null : JsonSerializer.SerializeToElement(from), null),
            new(to is not null, to is null ? null : JsonSerializer.SerializeToElement(to), null));
    }
    // Only persisted v1 scalar values/defaults reach this comparer. Numeric representation
    // differences are immaterial; string values (including line endings) remain exact.
    private static bool SameScalar(JsonElement? a, JsonElement? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Value.ValueKind != b.Value.ValueKind) return false;
        return a.Value.ValueKind switch
        {
            JsonValueKind.Number => a.Value.GetDecimal() == b.Value.GetDecimal(),
            JsonValueKind.String => string.Equals(a.Value.GetString(), b.Value.GetString(), StringComparison.Ordinal),
            JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null => true,
            _ => throw new InvalidOperationException("Asset comparison requires scalar fields.")
        };
    }
    private static JsonElement? Optional(JsonElement field, string key) =>
        field.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;
    private static IEnumerable<string> Options(JsonElement field) => Optional(field, "options") is { } options
        ? options.EnumerateArray().Select(x => x.GetString()!) : [];
    private static bool SameDefinition(JsonElement? a, JsonElement? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return new[] { "key", "label", "type", "required" }.All(key => SameScalar(a.Value.GetProperty(key), b.Value.GetProperty(key)))
            && Options(a.Value).SequenceEqual(Options(b.Value), StringComparer.Ordinal)
            && SameScalar(Optional(a.Value, "defaultValue"), Optional(b.Value, "defaultValue"));
    }
}
