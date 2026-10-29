using SaveIT.Api.Documentation;
using Xunit;

namespace SaveIT.Api.Tests;

public sealed class LineComparisonTests
{
    [Theory]
    [InlineData("", "new")]
    [InlineData("old", "")]
    [InlineData("a\na\nb\n", "a\nb\na\n")]
    [InlineData("one\r\ntwo\r\n", "one\ntwo\n")]
    [InlineData("start\nold\nend", "start\nnew\nend")]
    public void Diff_reconstructs_both_documents_and_preserves_line_numbers(string oldText, string newText)
    {
        var result = LineComparison.Compare(oldText, newText);
        var oldLines = result.Lines.Where(x => x.Kind != "added").ToList();
        var newLines = result.Lines.Where(x => x.Kind != "removed").ToList();
        Assert.Equal(LineComparison.Normalize(oldText), string.Join('\n', oldLines.Select(x => x.Text)));
        Assert.Equal(LineComparison.Normalize(newText), string.Join('\n', newLines.Select(x => x.Text)));
        Assert.Equal(Enumerable.Range(1, oldLines.Count), oldLines.Select(x => x.OldLine!.Value));
        Assert.Equal(Enumerable.Range(1, newLines.Count), newLines.Select(x => x.NewLine!.Value));
    }

    [Fact]
    public void Worst_case_document_size_uses_bounded_faithful_block_diff()
    {
        var oldText = new string('\n', 49_999); var newText = string.Join('\n', Enumerable.Repeat("x", 25_000));
        var result = LineComparison.Compare(oldText, newText);
        Assert.True(result.Simplified);
        Assert.Equal(oldText, string.Join('\n', result.Lines.Where(x => x.Kind != "added").Select(x => x.Text)));
        Assert.Equal(newText, string.Join('\n', result.Lines.Where(x => x.Kind != "removed").Select(x => x.Text)));
    }
}
