using System.Globalization;
using System.Text;
using System.Text.Json;
using Seerlens.Evals;

namespace Seerlens.Collector;

// `seerlens eval <set> [options]` runs a golden set through the configured
// provider, prints the scores, and exits non-zero when quality is too low. This
// is the piece that lets you gate a build on answer quality the way you gate on
// unit tests. No web server, no dashboard; just run, score, exit.
static class EvalCommand
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        var opt = Options.Parse(args);
        if (opt.Error is { } err)
        {
            Console.Error.WriteLine($"seerlens eval: {err}");
            return 2;
        }

        if (Resolve(opt.Set) is not { } path)
        {
            Console.Error.WriteLine($"seerlens eval: can't find golden set '{opt.Set}'. Looked for a file, ./evals/{opt.Set}.json, and the bundled sets.");
            return 2;
        }

        var ai = new AiProvider(EnvConfig(opt.Model));
        if (!ai.Configured)
        {
            Console.Error.WriteLine("seerlens eval: no provider configured. Set SEERLENS_AI_BASE_URL, SEERLENS_AI_KEY and SEERLENS_AI_MODEL (a .env.local works too).");
            return 2;
        }

        GoldenSet set;
        try { set = GoldenSet.Load(path); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"seerlens eval: {e.Message}");
            return 2;
        }

        if (!opt.Quiet)
            Console.WriteLine($"Running '{set.Name}' ({set.Cases.Count} cases) through {ai.Model}, scoring by {opt.Scorer}...\n");

        // "agent" gives the model the case's tools and scores the calls it makes;
        // the others score a single answer.
        var run = opt.Scorer == "agent"
            ? await new AgentRunner(ai.Client!).Run(set, ai.Model)
            : await new EvalRunner(ai.Client!, Scoring.For(opt.Scorer, ai.Client!, ai.Embedder)).Run(set, ai.Model);
        if (!opt.Quiet)
            PrintTable(run);

        if (opt.ReportTo is { } url)
            await TryReport(url, run);

        if (opt.JsonOut is { } jpath)
            File.WriteAllText(jpath, JsonSerializer.Serialize(run, Json));
        if (opt.JUnitOut is { } xpath)
            File.WriteAllText(xpath, JUnit(run, opt.Min ?? 0.5));

        return Gate(run, opt);
    }

    // Compare against the floor and the baseline, print the verdict, return the exit code.
    internal static int Gate(EvalRun run, Options opt)
    {
        var pct = Pct(run.Score);
        var failed = false;

        if (opt.Min is { } min)
        {
            if (BelowFloor(run.Score, min))
            {
                Console.WriteLine($"\nFAIL  score {pct} is below the floor of {Pct(min)}.");
                failed = true;
            }
            else
            {
                Console.WriteLine($"\nPASS  score {pct} meets the floor of {Pct(min)}.");
            }
        }

        if (opt.Baseline is { } bpath)
        {
            var prev = Baseline.Read(bpath);
            if (prev is { } b)
            {
                var drop = b.Score - run.Score;
                if (Regressed(b.Score, run.Score, opt.Tolerance))
                {
                    Console.WriteLine($"REGRESSION  score dropped {Pct(drop)} versus the baseline ({Pct(b.Score)} -> {pct}), more than the {Pct(opt.Tolerance)} allowed.");
                    failed = true;
                }
                else
                {
                    Console.WriteLine($"baseline ok  {Pct(b.Score)} -> {pct} (allowed drop {Pct(opt.Tolerance)}).");
                }
                CheckCost(b, run, opt, ref failed);
            }
            else
            {
                Console.WriteLine($"baseline  none found at {bpath}, nothing to compare against.");
            }
        }

        if (opt.SaveBaseline is { } spath)
        {
            Baseline.Write(spath, run);
            Console.WriteLine($"baseline  saved {pct} to {spath}.");
        }

        if (opt.Min is null && opt.Baseline is null)
            Console.WriteLine($"\nscore {pct}  (no --min or --baseline, so nothing to fail on)");

        return failed ? 1 : 0;
    }

    // one format everywhere; "P0" varies by culture ("100 %" vs "100%")
    internal static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    internal static bool BelowFloor(double score, double min) => score + 1e-9 < min;

    internal static bool Regressed(double baseline, double score, double tolerance) =>
        baseline - score > tolerance + 1e-9;

    // Target-model spend for the run: the sum of its priced llm steps, null when none priced.
    internal static double? RunCost(EvalRun run)
    {
        double? total = null;
        foreach (var s in run.Cases.SelectMany(c => c.Steps ?? []))
        {
            if (s.Kind != "llm") continue;
            var usage = new TokenUsage(s.InputTokens, s.OutputTokens, s.CacheReadTokens, null, s.ReasoningTokens);
            if (Pricing.CostFor(s.Model ?? run.Target, usage) is { } c)
                total = (total ?? 0) + c;
        }
        return total;
    }

    internal static bool CostExceeded(double baseline, double cost, double increase) =>
        cost > baseline * (1 + increase) + 1e-12;

    static void CheckCost(BaselineFile b, EvalRun run, Options opt, ref bool failed)
    {
        var cost = RunCost(run);
        if (b.CostUsd is null)
        {
            if (opt.MaxCostIncrease is not null)
                Console.WriteLine("cost  the baseline has no cost (saved before cost tracking, or without priced steps), so the cost gate is skipped. Re-save it with --save-baseline.");
            return;
        }
        if (cost is null)
        {
            if (opt.MaxCostIncrease is not null)
                Console.WriteLine("cost  this run has no priced steps (unknown model or no usage reported), so the cost gate is skipped.");
            return;
        }

        var delta = b.CostUsd.Value > 0 ? $" ({cost.Value / b.CostUsd.Value - 1:+0%;-0%;0%})" : "";
        var line = $"${cost.Value.ToString("0.0000", CultureInfo.InvariantCulture)} vs ${b.CostUsd.Value.ToString("0.0000", CultureInfo.InvariantCulture)} baseline{delta}";
        if (opt.MaxCostIncrease is { } max && CostExceeded(b.CostUsd.Value, cost.Value, max))
        {
            Console.WriteLine($"COST  {line}, more than the {Pct(max)} increase allowed.");
            failed = true;
        }
        else
        {
            Console.WriteLine($"cost  {line}" + (opt.MaxCostIncrease is { } m ? $" (allowed increase {Pct(m)})." : "."));
        }
    }

    static void PrintTable(EvalRun run)
    {
        foreach (var c in run.Cases)
        {
            var mark = c.Score >= 0.999 ? "ok " : c.Score <= 0.001 ? "miss" : "    ";
            Console.WriteLine($"  {Pct(c.Score),5}  {mark}  {Trim(c.Input, 64)}");
        }
        Console.WriteLine($"\n  mean  {Pct(run.Score)}  across {run.Cases.Count} cases");
    }

    static async Task TryReport(string url, EvalRun run)
    {
        try
        {
            await new EvalReporter(url).Report(run);
            Console.WriteLine($"reported to {url}");
        }
        catch (Exception e)
        {
            // reporting to a running dashboard is a nicety, never fail the gate over it
            Console.Error.WriteLine($"note: couldn't report to {url} ({e.Message})");
        }
    }

    // A set is a path to a .json, a name under ./evals, or one of the bundled sets.
    static string? Resolve(string set)
    {
        if (File.Exists(set)) return set;

        var local = Path.Combine(Directory.GetCurrentDirectory(), "evals", $"{set}.json");
        if (File.Exists(local)) return local;

        var bundled = Path.Combine(AppContext.BaseDirectory, "evals", $"{set}.json");
        return File.Exists(bundled) ? bundled : null;
    }

    static IConfiguration EnvConfig(string? modelOverride)
    {
        var b = new ConfigurationBuilder().AddEnvironmentVariables();
        if (modelOverride is not null)
            b.AddInMemoryCollection(new Dictionary<string, string?> { ["SEERLENS_AI_MODEL"] = modelOverride });
        return b.Build();
    }

    internal static string JUnit(EvalRun run, double caseFloor)
    {
        var failures = run.Cases.Count(c => c.Score + 1e-9 < caseFloor);
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="utf-8"?>""");
        sb.AppendLine($"""<testsuite name="seerlens.{Xml(run.Set)}" tests="{run.Cases.Count}" failures="{failures}">""");
        foreach (var c in run.Cases)
        {
            sb.AppendLine($"""  <testcase name="{Xml(Trim(c.Input, 80))}" classname="{Xml(run.Set)}">""");
            if (c.Score + 1e-9 < caseFloor)
                sb.AppendLine($"""    <failure message="scored {Pct(c.Score)}, below {Pct(caseFloor)}">{Xml(c.Answer)}</failure>""");
            sb.AppendLine("  </testcase>");
        }
        sb.AppendLine("</testsuite>");
        return sb.ToString();
    }

    static string Trim(string s, int n)
    {
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= n ? s : s[..(n - 1)] + "…";
    }

    static string Xml(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    static void PrintUsage() => Console.WriteLine(
        """
        seerlens eval <set> [options]

          Run a golden set through your configured provider and score the answers.
          <set> is a path to a .json, a name under ./evals, or a bundled set.

        Options
          --min <0..1>          fail if the mean score falls below this floor
          --baseline <path>     fail if the score dropped too far below a saved baseline
          --tolerance <0..1>    allowed drop versus the baseline (default 0.05)
          --max-cost-increase <f>  fail if run cost is over the baseline cost x (1 + f); needs --baseline
          --save-baseline <p>   write this run's score as the baseline at <p>
          --scorer <name>       keyword (default), llm-judge, rubric, consensus, regex, json-schema, embedding, or agent
          --model <name>        override SEERLENS_AI_MODEL for this run
          --json <path>         write the full run as JSON
          --junit <path>        write JUnit XML for CI test reporters
          --report <url>        also send the run to a running dashboard's trend
          --quiet               print only the verdict, not the per-case table

        Provider comes from SEERLENS_AI_BASE_URL / SEERLENS_AI_KEY / SEERLENS_AI_MODEL.

        Examples
          seerlens eval support --min 0.8
          seerlens eval evals/support.json --baseline .seerlens/support.base --max-cost-increase 0.25 --junit results.xml
        """);

    internal sealed class Options
    {
        public string Set = "";
        public double? MaxCostIncrease;
        public double? Min;
        public string? Baseline;
        public double Tolerance = 0.05;
        public string? SaveBaseline;
        public string Scorer = "keyword";
        public string? Model;
        public string? JsonOut;
        public string? JUnitOut;
        public string? ReportTo;
        public bool Quiet;
        public string? Error;

        public static Options Parse(string[] args)
        {
            var o = new Options { Set = args[0] };
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--min": o.Min = Num(Next(args, ref i), ref o.Error); break;
                    case "--baseline": o.Baseline = Next(args, ref i); break;
                    case "--max-cost-increase": o.MaxCostIncrease = Num(Next(args, ref i), ref o.Error); break;
                    case "--tolerance": o.Tolerance = Num(Next(args, ref i), ref o.Error) ?? o.Tolerance; break;
                    case "--save-baseline": o.SaveBaseline = Next(args, ref i); break;
                    case "--scorer": o.Scorer = Next(args, ref i) ?? o.Scorer; break;
                    case "--model": o.Model = Next(args, ref i); break;
                    case "--json": o.JsonOut = Next(args, ref i); break;
                    case "--junit": o.JUnitOut = Next(args, ref i); break;
                    case "--report": o.ReportTo = Next(args, ref i); break;
                    case "--quiet": o.Quiet = true; break;
                    default: o.Error = $"unknown option '{args[i]}'"; break;
                }
                if (o.Error is not null) break;
            }
            if (o.Error is null && o.MaxCostIncrease is { } mc)
            {
                if (mc < 0) o.Error = "--max-cost-increase must be 0 or more (0.25 allows 25% more)";
                else if (o.Baseline is null) o.Error = "--max-cost-increase needs --baseline to compare against";
            }
            if (o.Error is null && !Scoring.IsKnown(o.Scorer))
                o.Error = $"--scorer must be one of keyword, llm-judge, rubric, consensus, regex, json-schema, embedding, agent, got '{o.Scorer}'";
            return o;
        }

        static string? Next(string[] args, ref int i) => ++i < args.Length ? args[i] : null;

        static double? Num(string? s, ref string? error)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n)) return n;
            error = $"expected a number, got '{s}'";
            return null;
        }
    }

    internal record BaselineFile(string Set, string Scorer, double Score, long SavedAt, double? CostUsd = null);

    internal static class Baseline
    {
        public static BaselineFile? Read(string path)
        {
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<BaselineFile>(File.ReadAllText(path), Json); }
            catch { return null; }
        }

        public static void Write(string path, EvalRun run)
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } dir)
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(
                new BaselineFile(run.Set, run.Scorer, run.Score, run.CreatedAt, RunCost(run)), Json));
        }
    }
}
