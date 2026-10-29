namespace SaveIT.Api.Documentation;

public sealed record LineComparisonResult(IReadOnlyList<DiffLineDto> Lines, bool Simplified, int Added, int Removed);

// Bounded LCS for small edits; large replacements use faithful prefix/suffix blocks.
// Document inputs are already bounded to 50,000 characters. No quadratic large-input allocation.
public static class LineComparison
{
    private const long MaximumCells = 250_000;
    public static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static LineComparisonResult Compare(string oldText, string newText)
    {
        var oldLines = Split(oldText); var newLines = Split(newText);
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;
        var oldCount = oldLines.Length - prefix - suffix;
        var newCount = newLines.Length - prefix - suffix;
        var simplified = oldCount > 0 && newCount > 0 && (long)(oldCount + 1) * (newCount + 1) > MaximumCells;
        var result = new List<DiffLineDto>(oldLines.Length + newLines.Length);
        for (var i = 0; i < prefix; i++) result.Add(new("unchanged", i + 1, i + 1, oldLines[i]));
        var oldIndex = 0; var newIndex = 0;
        if (!simplified && oldCount > 0 && newCount > 0)
        {
            var lengths = new int[oldCount + 1, newCount + 1];
            for (var i = oldCount - 1; i >= 0; i--)
                for (var j = newCount - 1; j >= 0; j--)
                    lengths[i, j] = oldLines[prefix + i] == newLines[prefix + j]
                        ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            while (oldIndex < oldCount && newIndex < newCount)
            {
                if (oldLines[prefix + oldIndex] == newLines[prefix + newIndex])
                {
                    result.Add(new("unchanged", prefix + oldIndex + 1, prefix + newIndex + 1, oldLines[prefix + oldIndex])); oldIndex++; newIndex++;
                }
                else if (lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1])
                {
                    result.Add(new("removed", prefix + oldIndex + 1, null, oldLines[prefix + oldIndex])); oldIndex++;
                }
                else { result.Add(new("added", null, prefix + newIndex + 1, newLines[prefix + newIndex])); newIndex++; }
            }
        }
        while (oldIndex < oldCount) { result.Add(new("removed", prefix + oldIndex + 1, null, oldLines[prefix + oldIndex])); oldIndex++; }
        while (newIndex < newCount) { result.Add(new("added", null, prefix + newIndex + 1, newLines[prefix + newIndex])); newIndex++; }
        for (var i = suffix; i > 0; i--)
            result.Add(new("unchanged", oldLines.Length - i + 1, newLines.Length - i + 1, oldLines[oldLines.Length - i]));
        return new(result, simplified, result.Count(x => x.Kind == "added"), result.Count(x => x.Kind == "removed"));
    }
    private static string[] Split(string text) => text.Length == 0 ? [] : Normalize(text).Split('\n');
}
