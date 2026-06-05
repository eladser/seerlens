using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Seerlens.Collector;

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

app.MapPost("/eval/runs", (EvalRunIn run, EvalStore evals) =>
{
    evals.Add(run);
    return Results.Accepted();
});

app.MapGet("/api/evals", (EvalStore evals, string? set) => evals.List(set));

app.MapGet("/api/evals/{id}", (string id, EvalStore evals) =>
    evals.Get(id) is { } detail ? Results.Ok(detail) : Results.NotFound());

app.MapGet("/api/stats", (TraceStore store) => store.Stats());

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

public partial class Program { }
