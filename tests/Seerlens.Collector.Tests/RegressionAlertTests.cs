using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class RegressionAlertTests : IDisposable
{
    readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-reg-{Guid.NewGuid():N}.db");
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"seerlens-reg-{Guid.NewGuid():N}");
    readonly string _settings = Path.Combine(Path.GetTempPath(), $"seerlens-reg-{Guid.NewGuid():N}.json");

    sealed class ReplyClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken ct = default) => throw new NotSupportedException();
        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public async Task Only_a_drop_against_the_same_scorer_sends_the_webhook()
    {
        using var hook = Hook.Start();
        var settings = new SettingsStore(_settings);
        settings.SetAlerts(new Alerts(hook.Url, 0.05));
        var sets = new GoldenSets(_dir);
        sets.Save(new GoldenSet("caps", [new GoldenCase("c1", "capital of France?", Keywords: ["0.4"], Criteria: "mentions a city")]));
        var evals = EvalStore.ForFile(_db);
        var alerter = new Alerter(settings, NullLogger<Alerter>.Instance);

        Task Run(string scorer, string reply) => EvalService.RunAndStore(
            "caps", scorer, sets, new AiProvider(new ReplyClient(reply), "test"), evals, settings, alerter);

        await Run("keyword", "0.4");     // 1.0
        await Run("llm-judge", "0.4");   // 0.4, a different scorer: nothing to compare against
        await Task.Delay(400);
        Assert.Equal(0, hook.Count);

        await Run("llm-judge", "0.1");   // 0.4 -> 0.1 on the same scorer
        await hook.WaitFor(1);
        Assert.Equal(1, hook.Count);
    }

    sealed class Hook : IDisposable
    {
        readonly HttpListener _l = new();
        int _count;
        public int Count => Volatile.Read(ref _count);
        public string Url { get; private set; } = "";

        public static Hook Start()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();

            var h = new Hook { Url = $"http://localhost:{port}/" };
            h._l.Prefixes.Add(h.Url);
            h._l.Start();
            _ = Task.Run(async () =>
            {
                while (h._l.IsListening)
                {
                    try
                    {
                        var ctx = await h._l.GetContextAsync();
                        Interlocked.Increment(ref h._count);
                        ctx.Response.StatusCode = 200;
                        ctx.Response.Close();
                    }
                    catch { break; }
                }
            });
            return h;
        }

        public async Task WaitFor(int n)
        {
            for (var i = 0; i < 40 && Count < n; i++) await Task.Delay(50);
        }

        public void Dispose() => _l.Close();
    }

    public void Dispose()
    {
        foreach (var f in new[] { _db, _settings })
            try { File.Delete(f); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
