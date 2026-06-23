using Seerlens.Evals;

namespace Seerlens.Collector;

// Runs a golden set, stores the run, and fires the regression webhook on a drop.
// Shared by the /eval/run endpoint and the scheduler so both behave the same.
static class EvalService
{
    public static async Task<EvalRunSummary> RunAndStore(
        string setName, string? scorer, GoldenSets sets, AiProvider ai,
        EvalStore evals, SettingsStore settings, Alerter alerter)
    {
        var set = sets.Get(setName) ?? throw new InvalidOperationException($"unknown set: {setName}");
        var prev = evals.List(setName).LastOrDefault();   // most recent run before this one

        // "agent" runs the model with the case's tools and scores the tool sequence;
        // the others score a single answer.
        var run = scorer == "agent"
            ? await new AgentRunner(ai.Client!).Run(set, ai.Model)
            : await new EvalRunner(ai.Client!, Scoring.For(scorer, ai.Client!, ai.Embedder)).Run(set, ai.Model);

        var summary = evals.Add(ToEvalRunIn(run));
        if (prev is not null && prev.Score - run.Score > settings.GetAlerts().RegressionDrop)
            _ = alerter.EvalRegressed(setName, prev.Score, run.Score);
        return summary;
    }

    static EvalRunIn ToEvalRunIn(EvalRun r) =>
        new(r.Id, r.Set, r.Target, r.Scorer, r.CreatedAt, r.Score,
            r.Cases.Select(c => new EvalCaseIn(c.Input, c.Answer, c.Score)).ToList());
}
