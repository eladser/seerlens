using System.Text.Json;
using Seerlens.Collector;

var builder = WebApplication.CreateBuilder(args);

var dbPath = builder.Configuration["SEERLENS_DB"]
    ?? Environment.GetEnvironmentVariable("SEERLENS_DB")
    ?? "seerlens.db";

builder.Services.AddSingleton(TraceStore.ForFile(dbPath));
builder.Services.AddSingleton<LiveFeed>();

var app = builder.Build();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/ingest", (IngestTrace trace, TraceStore store, LiveFeed live) =>
{
    var summary = store.Add(trace);
    live.Publish(summary);
    return Results.Accepted();
});

app.MapGet("/api/traces", (TraceStore store, int? limit) => store.List(limit ?? 200));

app.MapGet("/api/traces/{id}", (string id, TraceStore store) =>
    store.Get(id) is { } detail ? Results.Ok(detail) : Results.NotFound());

app.MapGet("/api/stats", (TraceStore store) => store.Stats());

app.MapGet("/events", async (HttpContext ctx, LiveFeed live, CancellationToken ct) =>
{
    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";

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
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
