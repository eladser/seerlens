using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class TextDiffTests
{
    static string Lines(int n) => string.Join("\n", Enumerable.Range(1, n).Select(i => $"line {i}"));

    [Fact]
    public void Identical_text_is_all_eq()
    {
        var r = TextDiff.Diff("a\nb", "a\nb");
        Assert.All(r.Rows, x => Assert.Equal("eq", x.T));
        Assert.Equal(2, r.Rows.Count);
    }

    [Fact]
    public void Empty_both_sides_has_no_rows_and_null_is_empty()
    {
        Assert.Empty(TextDiff.Diff(null, "").Rows);
    }

    [Fact]
    public void Added_and_removed_lines_are_ins_and_del()
    {
        var r = TextDiff.Diff("a\nb\nc", "a\nc\nd");
        Assert.Contains(r.Rows, x => x.T == "del" && x.A == "b" && x.B is null);
        Assert.Contains(r.Rows, x => x.T == "ins" && x.B == "d" && x.A is null);
    }

    [Fact]
    public void Changed_line_is_mod_with_word_pieces()
    {
        var r = TextDiff.Diff("You are a helpful assistant", "You are a terse assistant");
        var row = Assert.Single(r.Rows);
        Assert.Equal("mod", row.T);
        Assert.Contains(row.AWords!, w => w.T == "del" && w.Text.Contains("helpful"));
        Assert.Contains(row.BWords!, w => w.T == "ins" && w.Text.Contains("terse"));
        Assert.Contains(row.AWords!, w => w.T == "eq");
    }

    [Fact]
    public void Whitespace_changes_are_kept()
    {
        var r = TextDiff.Diff("a b", "a  b");
        Assert.DoesNotContain(r.Rows, x => x.T == "eq");
    }

    [Fact]
    public void Long_equal_runs_collapse_to_three_skip_three()
    {
        var r = TextDiff.Diff(Lines(20), Lines(20) + "\nextra");

        Assert.Equal(["eq", "eq", "eq", "skip", "eq", "eq", "eq", "ins"], r.Rows.Select(x => x.T));
        Assert.Equal(20 - 6, r.Rows.Single(x => x.T == "skip").Skipped);
    }

    [Fact]
    public void Six_equal_lines_are_not_collapsed()
    {
        var r = TextDiff.Diff(Lines(6) + "\nx", Lines(6) + "\ny");
        Assert.DoesNotContain(r.Rows, x => x.T == "skip");
    }

    [Fact]
    public void Text_over_200k_is_too_large_with_both_sizes()
    {
        var big = new string('a', TextDiff.MaxChars + 1);
        var r = TextDiff.Diff(big, "small");
        Assert.True(r.TooLarge);
        Assert.Empty(r.Rows);
        Assert.Equal(big.Length, r.SizeA);
        Assert.Equal(5, r.SizeB);
    }

    [Fact]
    public void Text_at_the_limit_still_diffs()
    {
        var r = TextDiff.Diff(new string('a', TextDiff.MaxChars), new string('a', TextDiff.MaxChars));
        Assert.False(r.TooLarge);
    }
}
