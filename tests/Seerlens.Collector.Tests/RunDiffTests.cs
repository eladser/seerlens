using System.Diagnostics;
using Seerlens.Collector;

namespace Seerlens.Collector.Tests;

public class RunDiffTests
{
    static SpanRow Sp(string id, string kind, string name, long at, string? model = null, string? input = null,
        string? output = null, string? parent = null, string? op = null, string? pars = null, string? error = null,
        long? tin = null, long? tout = null, double? cost = null) =>
        new(id, parent, name, kind, at, 10, model, tin, tout, cost, input, output, error, Operation: op, Params: pars);

    static TraceDetail Tr(string id, params SpanRow[] spans) =>
        new(new TraceSummary(id, id, spans.Length > 0 ? spans.Min(s => s.StartedAt) : 0, 100, null, null, "ok", null, null, null), spans);

    static string[] Shape(DiffResponse r) => r.Steps.Select(s => $"{s.Status}:{s.Name}").ToArray();

    [Fact]
    public void Identical_runs_are_all_same_and_nothing_is_inlined()
    {
        SpanRow[] mk() => [Sp("a", "llm", "chat", 0, "m", "q", "r"), Sp("b", "tool", "search", 100, input: "x"), Sp("c", "llm", "chat", 200, "m", "q2", "r2")];
        var d = RunDiff.Compare(Tr("a", mk()), Tr("b", mk()));

        Assert.All(d.Steps, s => Assert.Equal("same", s.Status));
        Assert.All(d.Steps, s => Assert.Null(s.Input));
        Assert.Equal(["search"], d.ToolsA);
        Assert.False(d.Truncated);
    }

    [Fact]
    public void Model_swap_still_lines_up()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("a1", "llm", "chat", 0, "gpt-4o", "q", "r")),
            Tr("b", Sp("b1", "llm", "chat", 0, "GPT-4o-mini", "q", "r")));

        var s = Assert.Single(d.Steps);
        Assert.Equal("changed", s.Status);
        Assert.Equal(["model"], s.Changes);
    }

    [Fact]
    public void Model_compare_ignores_case()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("a1", "llm", "chat", 0, "GPT-4o")),
            Tr("b", Sp("b1", "llm", "chat", 0, "gpt-4o")));
        Assert.Equal("same", d.Steps[0].Status);
    }

    [Fact]
    public void Removed_tool_prints_removed_row_and_keeps_the_rest_aligned()
    {
        var a = Tr("a", Sp("1", "llm", "chat", 0), Sp("2", "tool", "search", 10), Sp("3", "llm", "chat", 20),
            Sp("4", "tool", "read", 30), Sp("5", "llm", "chat", 40));
        var b = Tr("b", Sp("1", "llm", "chat", 0), Sp("2", "tool", "search", 10), Sp("3", "llm", "chat", 20), Sp("4", "llm", "chat", 40));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(["same:chat", "same:search", "same:chat", "removed:read", "same:chat"], Shape(d));
        Assert.Null(d.Steps[3].B);
        Assert.Equal("4", d.Steps[3].A!.SpanId);
    }

    [Fact]
    public void Added_tool_is_added()
    {
        var a = Tr("a", Sp("1", "llm", "chat", 0), Sp("2", "llm", "chat", 20));
        var b = Tr("b", Sp("1", "llm", "chat", 0), Sp("2", "tool", "lookup", 10), Sp("3", "llm", "chat", 20));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(["same:chat", "added:lookup", "same:chat"], Shape(d));
        Assert.Equal(["lookup"], d.ToolsB);
        Assert.Empty(d.ToolsA);
    }

    [Fact]
    public void Reordered_tools_become_removed_and_added()
    {
        var a = Tr("a", Sp("1", "tool", "alpha", 0), Sp("2", "tool", "beta", 100));
        var b = Tr("b", Sp("1", "tool", "beta", 0), Sp("2", "tool", "alpha", 100));

        var d = RunDiff.Compare(a, b);

        // one pair matches, the other tool shows as removed then added
        Assert.Equal(["removed:alpha", "same:beta", "added:alpha"], Shape(d));
    }

    [Fact]
    public void Repeated_tools_match_earliest_first()
    {
        var a = Tr("a", Sp("1", "tool", "t", 0, input: "x"), Sp("2", "tool", "t", 100, input: "x"), Sp("3", "tool", "t", 200, input: "x"));
        var b = Tr("b", Sp("1", "tool", "t", 0, input: "x"), Sp("2", "tool", "t", 100, input: "x"));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(["same:t", "same:t", "removed:t"], Shape(d));
        Assert.Equal("3", d.Steps[2].A!.SpanId);
    }

    [Fact]
    public void Tie_break_prefers_the_match_with_equal_input()
    {
        var a = Tr("a", Sp("1", "tool", "fetch", 0, input: "url=A"), Sp("2", "tool", "fetch", 100, input: "url=B"));
        var b = Tr("b", Sp("1", "tool", "fetch", 0, input: "url=B"));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(["removed:fetch", "same:fetch"], Shape(d));
        Assert.Equal("2", d.Steps[1].A!.SpanId);
    }

    [Fact]
    public void Match_count_beats_equal_input_count()
    {
        // matching x-x and y-y pairs can't both happen, so only one match and three rows
        var a = Tr("a", Sp("1", "tool", "x", 0, input: "p"), Sp("2", "tool", "y", 100, input: "q"));
        var b = Tr("b", Sp("1", "tool", "y", 0, input: "q"), Sp("2", "tool", "x", 100, input: "p"));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(1, d.Steps.Count(s => s.Status == "same"));
        Assert.Equal(3, d.Steps.Count);
    }

    [Fact]
    public void More_matches_win_over_more_equal_inputs()
    {
        // both fetches can match (2 matches, 0 equal inputs) vs one match with an equal input
        var a = Tr("a", Sp("1", "tool", "fetch", 0, input: "A"), Sp("2", "tool", "fetch", 100, input: "B"));
        var b = Tr("b", Sp("1", "tool", "fetch", 0, input: "X"), Sp("2", "tool", "fetch", 100, input: "Y"));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(["changed:fetch", "changed:fetch"], d.Steps.Select(s => $"{s.Status}:{s.Name}"));
    }

    [Fact]
    public void Alignment_is_deterministic()
    {
        var rng = new Random(7);
        string[] names = ["a", "b", "c"];
        SpanRow[] gen(int n) => Enumerable.Range(0, n)
            .Select(i => Sp($"s{i}", "tool", names[rng.Next(3)], i * 100, input: rng.Next(2).ToString())).ToArray();
        var a = Tr("a", gen(40));
        var b = Tr("b", gen(40));

        var one = RunDiff.Compare(a, b);
        var two = RunDiff.Compare(a, b);

        Assert.Equal(
            one.Steps.Select(s => (s.Status, s.A?.SpanId, s.B?.SpanId)),
            two.Steps.Select(s => (s.Status, s.A?.SpanId, s.B?.SpanId)));
    }

    [Fact]
    public void Parallel_calls_sort_by_key_so_launch_order_does_not_matter()
    {
        var a = Tr("a", Sp("1", "tool", "weather", 0, input: "x"), Sp("2", "tool", "calendar", 3, input: "y"));
        var b = Tr("b", Sp("1", "tool", "calendar", 0, input: "y"), Sp("2", "tool", "weather", 4, input: "x"));

        var d = RunDiff.Compare(a, b);

        Assert.All(d.Steps, s => Assert.Equal("same", s.Status));
        Assert.Equal(["calendar", "weather"], d.ToolsA);
    }

    [Fact]
    public void Groups_are_anchored_so_chained_starts_do_not_merge()
    {
        // 16 is more than 10 ms from the anchor at 0, even though it is 8 after the second step
        var a = Tr("a", Sp("1", "tool", "c", 0), Sp("2", "tool", "b", 8), Sp("3", "tool", "a", 16));

        Assert.Equal(["b", "c", "a"], RunDiff.Order(a).Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Different_parents_do_not_group()
    {
        var a = Tr("a", Sp("1", "tool", "z", 0, parent: "p1"), Sp("2", "tool", "a", 2, parent: "p2"));

        Assert.Equal(["z", "a"], RunDiff.Order(a).Select(s => s.Name).ToArray());
    }

    [Fact]
    public void Group_ties_on_key_sort_by_input_text()
    {
        var a = Tr("a", Sp("1", "tool", "t", 0, input: "b"), Sp("2", "tool", "t", 1, input: "a"));

        Assert.Equal(["2", "1"], RunDiff.Order(a).Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Agent_and_other_spans_are_not_steps_but_nested_children_are()
    {
        var a = Tr("a",
            Sp("root", "agent", "invoke_agent", 0),
            Sp("c1", "llm", "chat", 5, parent: "root"),
            Sp("o", "other", "retrieval", 6, parent: "root"),
            Sp("sub", "agent", "invoke_agent", 10, parent: "root"),
            Sp("c2", "llm", "chat", 15, parent: "sub"),
            Sp("t", "tool", "search", 20, parent: "sub"));
        var b = Tr("b", Sp("c1", "llm", "chat", 0), Sp("c2", "llm", "chat", 100), Sp("t", "tool", "search", 200));

        var d = RunDiff.Compare(a, b);

        Assert.Equal(3, d.TotalsA.Steps);
        Assert.Equal(["same:chat", "same:chat", "same:search"], Shape(d));
    }

    [Fact]
    public void Empty_runs_diff_to_nothing()
    {
        var d = RunDiff.Compare(Tr("a"), Tr("b"));
        Assert.Empty(d.Steps);
        Assert.Null(d.TotalsA.CacheHitRate);
        Assert.Null(d.TotalsA.CostUsd);
    }

    [Fact]
    public void One_empty_side_is_all_added()
    {
        var d = RunDiff.Compare(Tr("a"), Tr("b", Sp("1", "llm", "chat", 0), Sp("2", "tool", "t", 10)));
        Assert.All(d.Steps, s => Assert.Equal("added", s.Status));
    }

    [Fact]
    public void Embeddings_and_chat_have_different_keys()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "embed", 0, op: "embeddings")),
            Tr("b", Sp("1", "llm", "chat", 0)));
        Assert.Equal(["removed:embed", "added:chat"], Shape(d));
    }

    [Fact]
    public void Tool_names_match_case_insensitively()
    {
        var d = RunDiff.Compare(Tr("a", Sp("1", "tool", "Search", 0)), Tr("b", Sp("1", "tool", "search", 0)));
        Assert.Equal("same", d.Steps[0].Status);
    }

    [Fact]
    public void Param_key_set_change_and_value_change_both_count()
    {
        var added = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "c", 0, pars: """{"temperature":0.7}""")),
            Tr("b", Sp("1", "llm", "c", 0, pars: """{"temperature":0.7,"top_p":1}""")));
        Assert.Equal(["params"], added.Steps[0].Changes);
        Assert.Equal(new ParamChange("top_p", null, "1"), Assert.Single(added.Steps[0].Params));

        var value = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "c", 0, pars: """{"temperature":0.7}""")),
            Tr("b", Sp("1", "llm", "c", 0, pars: """{"temperature":0.2}""")));
        Assert.Equal(["params"], value.Steps[0].Changes);
    }

    [Fact]
    public void Param_values_compare_as_canonical_json()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "c", 0, pars: """{"temperature":0.70,"stop":{"b":1,"a":[1,2]}}""")),
            Tr("b", Sp("1", "llm", "c", 0, pars: """{"stop":{"a":[1,2],"b":1.0},"temperature":0.7}""")));
        Assert.Equal("same", d.Steps[0].Status);
    }

    [Fact]
    public void Finish_reasons_are_reported_separately_from_params()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "c", 0, pars: """{"temperature":0.7,"finish_reasons":["stop"]}""")),
            Tr("b", Sp("1", "llm", "c", 0, pars: """{"temperature":0.7,"finish_reasons":["length"]}""")));

        Assert.Equal(["finish"], d.Steps[0].Changes);
        Assert.Equal("changed", d.Steps[0].Status);
    }

    [Fact]
    public void Error_presence_and_text_change()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("1", "tool", "t", 0)),
            Tr("b", Sp("1", "tool", "t", 0, error: "boom")));
        Assert.Equal(["error"], d.Steps[0].Changes);
        Assert.Equal(1, d.TotalsB.Errors);
    }

    [Fact]
    public void Only_the_first_changed_row_carries_text_diffs()
    {
        SpanRow[] mk(string suffix) =>
        [
            Sp("1", "llm", "c", 0, "m", "same prompt", "same"),
            Sp("2", "llm", "c", 100, "m", "prompt one" + suffix, "out" + suffix),
            Sp("3", "llm", "c", 200, "m", "prompt two" + suffix, "out2" + suffix),
        ];
        var d = RunDiff.Compare(Tr("a", mk("A")), Tr("b", mk("B")));

        Assert.Equal(["same", "changed", "changed"], d.Steps.Select(s => s.Status));
        Assert.Null(d.Steps[0].Input);
        Assert.NotNull(d.Steps[1].Input);
        Assert.NotNull(d.Steps[1].Output);
        Assert.Null(d.Steps[2].Input);
        Assert.Null(d.Steps[2].Output);
    }

    [Fact]
    public void Tokens_cost_and_latency_never_set_changed()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "c", 0, "m", "q", "r", tin: 10, tout: 5, cost: 0.1)),
            Tr("b", Sp("1", "llm", "c", 0, "m", "q", "r", tin: 900, tout: 500, cost: 9)));
        Assert.Equal("same", d.Steps[0].Status);
    }

    [Fact]
    public void Totals_and_cache_hit_rate()
    {
        var a = Tr("a",
            new SpanRow("1", "ag", "c", "llm", 0, 10, "m1", 1000, 100, 0.5, null, null, null, CacheReadTokens: 400, ReasoningTokens: 20),
            // exclusive counts: cr + cw > input, so the denominator is in + cr + cw
            new SpanRow("2", "ag", "c", "llm", 10, 10, "m2", 100, 50, 0.25, null, null, null, CacheReadTokens: 300, CacheWriteTokens: 100),
            new SpanRow("3", null, "t", "tool", 20, 10, null, null, null, null, null, null, "x"),
            new SpanRow("ag", null, "invoke_agent", "agent", 0, 50, null, 5000, 5000, 9.0, null, null, null));

        var t = RunDiff.Compare(a, Tr("b")).TotalsA;

        Assert.Equal(1100, t.InputTokens);
        Assert.Equal(150, t.OutputTokens);
        Assert.Equal(700, t.CacheReadTokens);
        Assert.Equal(100, t.CacheWriteTokens);
        Assert.Equal(20, t.ReasoningTokens);
        Assert.Equal(0.75, t.CostUsd!.Value, 6);
        Assert.Equal(700.0 / (1000 + 500), t.CacheHitRate!.Value, 6);
        Assert.Equal(3, t.Steps);
        Assert.Equal(1, t.ToolCalls);
        Assert.Equal(1, t.Errors);
        Assert.Equal(["m1", "m2"], t.Models);
        Assert.Equal(100, t.DurationMs);
    }

    [Fact]
    public void Over_the_cap_the_first_2000_steps_are_aligned_and_truncated_is_set()
    {
        var spans = Enumerable.Range(0, RunDiff.MaxSteps + 5).Select(i => Sp($"s{i}", "tool", "t", i * 100L)).ToArray();

        var d = RunDiff.Compare(Tr("a", spans), Tr("b", spans));

        Assert.True(d.Truncated);
        Assert.Equal(RunDiff.MaxSteps, d.Steps.Count);
        Assert.Equal(RunDiff.MaxSteps + 5, d.TotalsA.Steps);
    }

    [Fact]
    public void Large_traces_diff_in_reasonable_time()
    {
        var rng = new Random(1);
        string[] names = ["a", "b", "c", "d"];
        SpanRow[] gen() => Enumerable.Range(0, RunDiff.MaxSteps)
            .Select(i => Sp($"s{i}", "tool", names[rng.Next(4)], i * 100L, input: new string('x', 200) + rng.Next(3))).ToArray();
        var a = Tr("a", gen());
        var b = Tr("b", gen());

        var sw = Stopwatch.StartNew();
        var d = RunDiff.Compare(a, b);
        sw.Stop();

        Assert.NotEmpty(d.Steps);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"took {sw.Elapsed}");
    }

    [Fact]
    public void Step_offsets_are_relative_to_each_run_start()
    {
        var d = RunDiff.Compare(
            Tr("a", Sp("1", "llm", "c", 1000), Sp("2", "tool", "t", 1250)),
            Tr("b", Sp("1", "llm", "c", 5000), Sp("2", "tool", "t", 5300)));

        Assert.Equal(250, d.Steps[1].A!.OffsetMs);
        Assert.Equal(300, d.Steps[1].B!.OffsetMs);
    }
}
