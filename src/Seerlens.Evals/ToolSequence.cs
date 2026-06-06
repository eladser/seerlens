namespace Seerlens.Evals;

// Scores how well an agent's actual tool calls match the tools you expected, in
// order. This is the run-level eval question: did the agent reach for the right
// tools, in the right sequence? Score is the longest in-order match over the
// expected count, so a missing or out-of-order tool pulls it down.
public static class ToolSequence
{
    public static ToolScore Score(IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        if (expected.Count == 0)
            return new ToolScore(1, [], true);

        var inOrder = Lcs(expected, actual);
        var missing = expected
            .Where(e => !actual.Any(a => a.Equals(e, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var orderOk = inOrder == expected.Count;
        return new ToolScore((double)inOrder / expected.Count, missing, orderOk);
    }

    // Length of the longest common subsequence, case-insensitive.
    static int Lcs(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var dp = new int[a.Count + 1, b.Count + 1];
        for (var i = 1; i <= a.Count; i++)
            for (var j = 1; j <= b.Count; j++)
                dp[i, j] = a[i - 1].Equals(b[j - 1], StringComparison.OrdinalIgnoreCase)
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Count, b.Count];
    }
}

public record ToolScore(double Score, string[] Missing, bool OrderOk);
