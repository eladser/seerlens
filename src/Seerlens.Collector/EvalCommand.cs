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
    static int Gate(EvalRun run, Options opt)
    {
        var pct = run.Score.ToString("P0", CultureInfo.InvariantCulture);
        var failed = false;

        if (opt.Min is { } min)
        {
            if (BelowFloor(run.Score, min))
            {
                Console.WriteLine($"\nFAIL  score {pct} is below the floor of {min:P0}.");
                failed = true;
            }
            else
            {
                Console.WriteLine($"\nPASS  score {pct} meets the floor of {min:P0}.");
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
                    Console.WriteLine($"REGRESSION  score dropped {drop:P0} versus the baseline ({b.Score:P0} -> {pct}), more than the {opt.Tolerance:P0} allowed.");
                    failed = true;
                }
                else
                {
                    Console.WriteLine($"baseline ok  {b.Score:P0} -> {pct} (allowed drop {opt.Tolerance:P0}).");
                }
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

    internal static bool BelowFloor(double score, double min) => score + 1e-9 < min;

    internal static bool Regressed(double baseline, double score, double tolerance) =>
        baseline - score > tolerance + 1e-9;

    static void PrintTable(EvalRun run)
    {
        foreach (var c in run.Cases)
        {
            var mark = c.Score >= 0.999 ? "ok " : c.Score <= 0.001 ? "miss" : "    ";
            Console.WriteLine($"  {c.Score,5:P0}  {mark}  {Trim(c.Input, 64)}");
        }
        Console.WriteLine($"\n  mean  {run.Score:P0}  across {run.Cases.Count} cases");
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
                sb.AppendLine($"""    <failure message="scored {c.Score:P0}, below {caseFloor:P0}">{Xml(c.Answer)}</failure>""");
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
          seerlens eval evals/support.json --baseline .seerlens/support.base --junit results.xml
        """);

    sealed class Options
    {
        public string Set = "";
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
            if (!Scoring.IsKnown(o.Scorer))
                o.Error = $"--scorer must be one of keyword, llm-judge, rubric, consensus, regex, json-schema, embedding, agent, got '{o.Scorer}'";
            return o;
        }

        static string? Next(string[] args, ref int i) => ++i < args.Length ? args[i] : null;

        static double? Num(string? s, ref string? error)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return n;
            error = $"expected a number, got '{s}'";
            return null;
        }
    }

    internal record BaselineFile(string Set, string Scorer, double Score, long SavedAt);

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
                new BaselineFile(run.Set, run.Scorer, run.Score, run.CreatedAt), Json));
        }
    }
}
