using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Seerlens.Collector;
using Seerlens.Evals;

LoadDotEnv(); // pick up SEERLENS_AI_* from .env.local for local dev

// `seerlens eval <set>` runs a golden set and exits, no server. Everything else
// boots the collector + dashboard.
if (args is ["eval", .. var rest])
    return await EvalCommand.Run(rest);

// Anchor the content root to the binary, not the caller's working directory,
// so the bundled dashboard is found wherever the tool is launched from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var dbPath = builder.Configuration["SEERLENS_DB"] ?? "seerlens.db";

builder.WebHost.UseUrls(
    Environment.GetEnvironmentVariable("SEERLENS_URL") ?? "http://localhost:5005");

builder.Services.AddSingleton(TraceStore.ForFile(dbPath));
builder.Services.AddSingleton(EvalStore.ForFile(dbPath));
builder.Services.AddSingleton(new AiProvider(builder.Configuration));
builder.Services.AddSingleton(new SettingsStore(
    builder.Configuration["SEERLENS_SETTINGS"] ?? "seerlens-settings.json"));
builder.Services.AddSingleton<Alerter>();
builder.Services.AddSingleton(new GoldenSets(
    builder.Configuration["SEERLENS_EVALS_DIR"] ?? Path.Combine(AppContext.BaseDirectory, "evals")));
builder.Services.AddSingleton<LiveFeed>();

var app = builder.Build();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// The dashboard is built into ./ui next to the binary. Serve it from there so it
// works the same whether run from source or installed as a global tool.
var uiPath = Path.Combine(AppContext.BaseDirectory, "ui");
var hasUi = Directory.Exists(uiPath);
if (hasUi)
{
    var ui = new PhysicalFileProvider(uiPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = ui });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = ui });
}

app.MapPost("/ingest", (IngestTrace trace, TraceStore store, LiveFeed live) =>
{
    var summary = store.Add(trace);
    live.Publish(summary);
    return Results.Accepted();
});

// Standard OpenTelemetry trace ingest, so any OTel-instrumented app can send
// GenAI spans here with no Seerlens SDK.
app.MapPost("/v1/traces", (OtlpRequest req, TraceStore store, LiveFeed live) =>
{
    foreach (var trace in Otlp.ToTraces(req))
        live.Publish(store.Add(trace));
    return Results.Ok(new { });
});

app.MapGet("/api/traces", (TraceStore store, int? limit) => store.List(limit ?? 200));

app.MapGet("/api/traces/{id}", (string id, TraceStore store) =>
    store.Get(id) is { } detail ? Results.Ok(detail) : Results.NotFound());

app.MapDelete("/api/traces", (TraceStore store) => { store.Clear(); return Results.NoContent(); });

app.MapDelete("/api/traces/{id}", (string id, TraceStore store) => { store.Delete(id); return Results.NoContent(); });

app.MapPost("/eval/runs", (EvalRunIn run, EvalStore evals) =>
{
    evals.Add(run);
    return Results.Accepted();
});

app.MapGet("/api/evals", (EvalStore evals, string? set) => evals.List(set));

app.MapGet("/api/evals/{id}", (string id, EvalStore evals) =>
    evals.Get(id) is { } detail ? Results.Ok(detail) : Results.NotFound());

app.MapGet("/api/sets", (GoldenSets sets, AiProvider ai) =>
    new { sets = sets.Names, aiConfigured = ai.Configured, model = ai.Model });

// Full set content, for editing in the dashboard.
app.MapGet("/api/sets/{name}", (string name, GoldenSets sets) =>
    sets.Get(name) is { } set ? Results.Ok(set) : Results.NotFound());

// Create or replace a set. The body is a golden set ({ name, cases }).
app.MapPut("/api/sets/{name}", (string name, Seerlens.Evals.GoldenSet body, GoldenSets sets) =>
{
    if (body.Cases is null)
        return Results.BadRequest(new { error = "a set needs a cases array" });
    var set = body with { Name = name };
    sets.Save(set);
    return Results.Ok(set);
});

app.MapDelete("/api/sets/{name}", (string name, GoldenSets sets) =>
    sets.Delete(name) ? Results.NoContent() : Results.NotFound());

// Append one case to a set, used by "add this trace to an eval set".
app.MapPost("/api/sets/{name}/cases", (string name, Seerlens.Evals.GoldenCase body, GoldenSets sets) =>
{
    var existing = sets.Get(name);
    var cases = existing?.Cases.ToList() ?? [];
    var id = string.IsNullOrWhiteSpace(body.Id) ? $"case-{cases.Count + 1}" : body.Id;
    cases.Add(body with { Id = id });
    var set = new Seerlens.Evals.GoldenSet(name, cases);
    sets.Save(set);
    return Results.Ok(set);
});

// Run a golden set through the configured provider and score it. The target
// produces the answers; the scorer is keyword (offline) or an LLM judge.
app.MapPost("/eval/run", async (RunRequest req, GoldenSets sets, AiProvider ai, EvalStore evals,
    SettingsStore settings, Alerter alerter) =>
{
    if (!ai.Configured)
        return Results.BadRequest(new { error = "no provider configured; set SEERLENS_AI_BASE_URL/KEY/MODEL" });
    if (sets.Get(req.Set) is not { } set)
        return Results.NotFound(new { error = $"unknown set: {req.Set}" });

    var prev = evals.List(req.Set).LastOrDefault();   // most recent run before this one

    // "agent" runs the model with the case's tools and scores the tool sequence;
    // the others score a single answer.
    Seerlens.Evals.EvalRun run;
    if (req.Scorer == "agent")
    {
        run = await new AgentRunner(ai.Client!).Run(set, ai.Model);
    }
    else
    {
        IScorer scorer = req.Scorer == "llm-judge" ? new LlmJudgeScorer(ai.Client!) : new KeywordScorer();
        run = await new EvalRunner(ai.Client!, scorer).Run(set, ai.Model);
    }
    var summary = evals.Add(ToEvalRunIn(run));

    if (prev is not null && prev.Score - run.Score > settings.GetAlerts().RegressionDrop)
        _ = alerter.EvalRegressed(req.Set, prev.Score, run.Score);

    return Results.Ok(summary);
});

// Run a set across several models (and an optional system prompt) and return
// quality, cost and latency per model, so "should I switch?" has an answer.
app.MapPost("/eval/compare", async (CompareRequest req, GoldenSets sets, AiProvider ai) =>
{
    if (!ai.Configured)
        return Results.BadRequest(new { error = "no provider configured; set SEERLENS_AI_BASE_URL/KEY/MODEL" });
    if (sets.Get(req.Set) is not { } set)
        return Results.NotFound(new { error = $"unknown set: {req.Set}" });

    // cap the fan-out so one request can't kick off a hundred paid model runs
    var models = req.Models is { Count: > 0 } ? req.Models.Take(8).ToList() : [ai.Model];
    var result = await new Comparison(ai).Run(set, models, req.Scorer ?? "keyword", req.PromptPrefix);
    return Results.Ok(result);
});

// Run-level eval: score a recorded agent trace's actual tool calls against the
// tools you expected, in order. Answers "did the agent use the right tools?"
app.MapPost("/eval/tools", (ToolScoreRequest req, TraceStore store) =>
{
    if (store.Get(req.TraceId) is not { } detail)
        return Results.NotFound(new { error = $"unknown trace: {req.TraceId}" });

    var actual = detail.Spans
        .Where(s => s.Kind is "tool" or "mcp")
        .OrderBy(s => s.StartedAt)
        .Select(s => s.Name)
        .ToList();

    var expected = req.Expected ?? [];
    var result = Seerlens.Evals.ToolSequence.Score(expected, actual);
    return Results.Ok(new { score = result.Score, orderOk = result.OrderOk, missing = result.Missing, expected, actual });
});

app.MapGet("/api/stats", (TraceStore store) => store.Stats());

// Cost view: spend rollups plus the budget and whether it's been crossed.
app.MapGet("/api/cost", (TraceStore store, SettingsStore settings, Alerter alerter) =>
{
    var now = DateTimeOffset.UtcNow;
    var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    var since = now.AddDays(-14).ToUnixTimeMilliseconds();

    var spend = store.SpendReport(monthStart, since);
    var budget = settings.GetBudget();
    var frac = budget.MonthlyUsd is { } m and > 0 ? spend.MonthToDateUsd / m : (double?)null;
    var over = frac >= 1.0;

    if (over && budget.MonthlyUsd is { } cap)
        _ = alerter.MaybeOverBudget(spend.MonthToDateUsd, cap, $"{now.Year}-{now.Month:D2}");

    return new CostReport(spend, budget, over, frac);
});

app.MapPut("/api/budget", (Budget budget, SettingsStore settings) =>
{
    // drop nonsense (negative, NaN) rather than store it
    var monthly = budget.MonthlyUsd is { } m && double.IsFinite(m) && m > 0 ? m : (double?)null;
    var clean = new Budget(monthly, budget.AlertPerCallUsd);
    settings.SetBudget(clean);
    return Results.Ok(clean);
});

app.MapGet("/api/alerts", (SettingsStore settings) => settings.GetAlerts());

app.MapPut("/api/alerts", (Alerts alerts, SettingsStore settings) =>
{
    // only accept an http(s) webhook; anything else is treated as "no webhook"
    var url = Uri.TryCreate(alerts.WebhookUrl, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"
        ? alerts.WebhookUrl
        : null;
    var drop = double.IsFinite(alerts.RegressionDrop) ? Math.Clamp(alerts.RegressionDrop, 0, 1) : 0.05;
    var clean = new Alerts(url, drop);
    settings.SetAlerts(clean);
    return Results.Ok(clean);
});

// A read-only look at how the collector is configured, for the Settings page.
app.MapGet("/api/config", (AiProvider ai, GoldenSets sets, SettingsStore settings) => new
{
    providerConfigured = ai.Configured,
    model = ai.Model,
    endpoint = ai.Endpoint,
    evalsDir = sets.Dir,
    setCount = sets.Names.Count,
    pricingOverride = Pricing.HasOverride,
    budget = settings.GetBudget(),
    alerts = settings.GetAlerts(),
});

app.MapGet("/events", async (HttpContext ctx, LiveFeed live, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

    // flush a comment up front so the browser's EventSource fires `open` right away
    await ctx.Response.WriteAsync(": connected\n\n", ct);
    await ctx.Response.Body.FlushAsync(ct);

    var (id, reader) = live.Subscribe();
    try
    {
        await foreach (var trace in reader.ReadAllAsync(ct))
        {
            await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(trace, json)}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) { } // client disconnected
    finally { live.Unsubscribe(id); }
});

// Let the SPA handle its own routes.
if (hasUi)
    app.MapFallbackToFile("index.html", new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(uiPath),
    });

app.Run();
return 0;

static void LoadDotEnv()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), ".env.local");
    if (!File.Exists(path)) return;

    foreach (var line in File.ReadAllLines(path))
    {
        var t = line.Trim();
        if (t.Length == 0 || t.StartsWith('#')) continue;
        var eq = t.IndexOf('=');
        if (eq <= 0) continue;
        var key = t[..eq].Trim();
        if (Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, t[(eq + 1)..].Trim());
    }
}

static EvalRunIn ToEvalRunIn(Seerlens.Evals.EvalRun r) =>
    new(r.Id, r.Set, r.Target, r.Scorer, r.CreatedAt, r.Score,
        r.Cases.Select(c => new EvalCaseIn(c.Input, c.Answer, c.Score)).ToList());

record RunRequest(string Set, string Scorer);
record CompareRequest(string Set, List<string>? Models, string? Scorer, string? PromptPrefix);
record ToolScoreRequest(string TraceId, List<string>? Expected);

public partial class Program { }
