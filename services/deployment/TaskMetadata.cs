using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SaveIT.Deployment;

internal static class TaskMetadata
{
    internal static void Match(string value, string pattern)
    {
        if (!Regex.IsMatch(value, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw new InvalidOperationException();
    }

    internal static Dictionary<string, JsonElement> Object(JsonElement value, params string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException();
        var result = value.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        if (result.Count != fields.Length || fields.Any(x => !result.ContainsKey(x))) throw new InvalidOperationException();
        return result;
    }

    internal static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidOperationException();
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    internal static void RemoveDefault(JsonObject value, string name, JsonNode expected)
    {
        if (!value.TryGetPropertyValue(name, out var actual)) return;
        if (!JsonNode.DeepEquals(actual, expected)) throw new InvalidOperationException();
        value.Remove(name);
    }

    internal static JsonObject NamedValues(JsonArray values, string valueField = "value")
    {
        var result = new JsonObject();
        foreach (var value in values)
        {
            var pair = value!.AsObject();
            if (pair.Count != 2 || pair["name"] is null || pair[valueField] is null) throw new InvalidOperationException();
            result.Add(pair["name"]!.GetValue<string>(), pair[valueField]!.GetValue<string>());
        }
        return result;
    }

    // No caller-provided default templates. Unknown semantics survive and fail equality.
    internal static JsonObject Normalize(JsonElement observed)
    {
        if (observed.ValueKind != JsonValueKind.Object || observed.EnumerateObject().Any(x => x.Name is not ("taskDefinition" or "tags"))) throw new InvalidOperationException();
        var task = JsonNode.Parse(observed.GetProperty("taskDefinition").GetRawText())!.AsObject();
        foreach (var metadata in new[] { "registeredAt", "registeredBy", "requiresAttributes", "compatibilities" }) task.Remove(metadata);
        RemoveDefault(task, "enableFaultInjection", JsonValue.Create(false)!);
        RemoveDefault(task, "placementConstraints", new JsonArray());
        RemoveDefault(task, "inferenceAccelerators", new JsonArray());
        RemoveDefault(task, "ephemeralStorage", new JsonObject { ["sizeInGiB"] = 20 });
        var containers = task["containerDefinitions"]!.AsArray();
        if (containers.Count != 1) throw new InvalidOperationException();
        var container = containers[0]!.AsObject();
        RemoveDefault(container, "cpu", JsonValue.Create(0)!);
        foreach (var field in new[] { "environmentFiles", "systemControls", "volumesFrom", "ulimits" }) RemoveDefault(container, field, new JsonArray());
        RemoveDefault(container["linuxParameters"]!["capabilities"]!.AsObject(), "add", new JsonArray());
        RemoveDefault(container["logConfiguration"]!.AsObject(), "secretOptions", new JsonArray());
        foreach (var volume in task["volumes"]!.AsArray())
        {
            RemoveDefault(volume!.AsObject(), "host", new JsonObject());
            RemoveDefault(volume.AsObject(), "configuredAtLaunch", JsonValue.Create(false)!);
        }
        container["environment"] = NamedValues(container["environment"]!.AsArray());
        return task;
    }
}
