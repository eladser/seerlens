using System.Globalization;

namespace Seerlens.Collector;

// A run ref is a trace id or "eval:{runId}:{caseIdx}".
public static class RunRefs
{
    const string EvalPrefix = "eval:";

    public static bool TryParseEval(string r, out string runId, out int idx)
    {
        runId = "";
        idx = 0;
        if (!r.StartsWith(EvalPrefix, StringComparison.Ordinal)) return false;
        var cut = r.LastIndexOf(':');
        if (cut <= EvalPrefix.Length) return false;
        runId = r[EvalPrefix.Length..cut];
        return runId.Length > 0 && ParseIdx(r.AsSpan(cut + 1), out idx);
    }

    // digits only, and no leading zeros, so "03" and "3" can't be two spellings of one case
    static bool ParseIdx(ReadOnlySpan<char> s, out int idx)
    {
        idx = 0;
        if (s.Length > 1 && s[0] == '0') return false;
        return int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out idx);
    }

    // "eval:" refs that don't parse are malformed; any other string is a trace id.
    public static bool IsMalformed(string r) =>
        r.StartsWith(EvalPrefix, StringComparison.Ordinal) && !TryParseEval(r, out _, out _);

    public static TraceDetail? Resolve(string r, TraceStore traces, EvalStore evals)
    {
        if (r.StartsWith(EvalPrefix, StringComparison.Ordinal))
        {
            if (!TryParseEval(r, out var runId, out var idx)) return null;
            return evals.Get(runId) is { } run ? EvalCaseTrace.Build(run, idx) : null;
        }
        return traces.Get(r);
    }
}
