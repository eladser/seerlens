using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Seerlens.Evals;

namespace Seerlens.Collector.Tests;

public class SchedulerTests : IDisposable
{
    readonly string _db = Path.Combine(Path.GetTempPath(), $"seerlens-sched-{Guid.NewGuid():N}.db");
    readonly string _dir = Path.Combine(Path.GetTempPath(), $"seerlens-sched-{Guid.NewGuid():N}");
    readonly string _settings = Path.Combine(Path.GetTempPath(), $"seerlens-sched-{Guid.NewGuid():N}.json");

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

    // a fixed clock so the tests don't depend on the wall time
    static readonly DateTime Noon = new(2026, 6, 9, 12, 0, 0, DateTimeKind.Local);

    EvalScheduler Build(TimeOnly at)
    {
        var sets = new GoldenSets(_dir);
        sets.Save(new GoldenSet("caps", [new GoldenCase("c1", "capital of France?", Keywords: ["Paris"])]));
        var evals = EvalStore.ForFile(_db);
        var ai = new AiProvider(new ReplyClient("The capital is Paris."), "test");
        var settings = new SettingsStore(_settings);
        settings.SetSchedules([new Schedule("caps", "keyword", at)]);
        return new EvalScheduler(settings, sets, ai, evals, new Alerter(settings), NullLogger<EvalScheduler>.Instance);
    }

    [Fact]
    public async Task Runs_a_due_schedule_once_per_day()
    {
        var s = Build(new TimeOnly(9, 0));   // due by noon
        await s.Tick(CancellationToken.None, Noon);
        await s.Tick(CancellationToken.None, Noon);   // same day: must not run again

        var runs = EvalStore.ForFile(_db).List("caps");
        Assert.Single(runs);
        Assert.Equal(1.0, runs[0].Score);   // answer contains the keyword
    }

    [Fact]
    public async Task Skips_a_schedule_whose_time_has_not_come()
    {
        var s = Build(new TimeOnly(18, 0));   // not due until evening
        await s.Tick(CancellationToken.None, Noon);
        Assert.Empty(EvalStore.ForFile(_db).List("caps"));
    }

    [Fact]
    public async Task A_failing_schedule_does_not_throw_out_of_the_tick()
    {
        // a schedule naming a set that doesn't exist: the run throws, Tick must swallow it
        var evals = EvalStore.ForFile(_db);
        var ai = new AiProvider(new ReplyClient("x"), "test");
        var settings = new SettingsStore(_settings);
        settings.SetSchedules([new Schedule("missing", "keyword", new TimeOnly(9, 0))]);
        var sched = new EvalScheduler(settings, new GoldenSets(_dir), ai, evals,
            new Alerter(settings), NullLogger<EvalScheduler>.Instance);

        await sched.Tick(CancellationToken.None, Noon);   // must not throw
        Assert.Empty(evals.List("missing"));
    }

    public void Dispose()
    {
        try { File.Delete(_db); } catch { }
        try { File.Delete(_settings); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }
}
