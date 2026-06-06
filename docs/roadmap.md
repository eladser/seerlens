# Roadmap

Seerlens does the core job today: see your AI calls (cost, speed, prompts, failures), run quality evals from the dashboard or CI, compare models, watch spend against a budget, and trace agent and MCP runs.

A note on direction, because it shapes everything below. Live trace viewing is turning into a commodity. The dashboards that ship with frameworks now show a GenAI call's prompt, response, tool calls and tokens for free, in your stack, during a debug session. Seerlens is not going to win by being one more live span viewer, and it won't try. Those tools are read-only mirrors of telemetry: they show you what happened in the moment and then forget it. They don't judge whether the answer was any good, they don't turn tokens into a budget you can act on, and they can't fail your build when quality drops.

That last part is the whole opening. Seerlens moves up the stack, from watching calls to **judging them, costing them, and catching regressions before they ship, with history that outlives a single run.** Tracing is the on-ramp. Evals and cost are the product.

And there's a place to aim all of it. In the wider market this layer is crowded, but in .NET it's empty: the .NET AI stack traces calls and stops there, no quality scoring, no cost in dollars. So Seerlens leads **.NET-first**, evals and cost native to the ecosystem that has no other option, while OTLP ingest keeps the door open to any language. That's the identity the rest of this roadmap serves.

## Shipped in 1.0

The 0.3 through 0.7 plan all landed:

- **Evals in CI.** `seerlens eval` with a score floor and regression-vs-baseline gating, JUnit output, and a copy-paste GitHub Action.
- **Model and prompt comparison**, quality next to cost and latency.
- **Author golden sets in the dashboard**, and promote a real trace into a test case.
- **Cost you can act on**, a monthly budget with an over-budget alert, by-model and by-day breakdowns, a pricing override file.
- **Agent and MCP runs** as a step tree, with recorded-run tool-sequence scoring.
- **Webhook alerts** on regression and over-budget, **JSON export**, and all three SDKs (.NET, Python, JS) unified on OTLP.

## What's next

### 1.1: Score agents by running them

The recorded-run scorer answers "did this trace use the right tools, in order?" The missing half is producing the run. Give an eval a set of tools, run a tool-capable agent on each golden case, and score the tool calls it actually makes, plus the cost and latency of the whole run. That closes the agent-eval loop end to end, and nobody offers it in .NET. This is the genuine next build and the highest hiring signal left.

### 1.2: Judging you can trust

Gating a build on a number is only as good as the number. Rubric-based judging that scores each criterion instead of one blunt verdict, an optional second judge for consensus, and more scorers: JSON-schema for structured output, regex, and embedding similarity. A judge has to be defensible before you fail a build on it.

### 1.3: Scheduled evals

Run a set on its own, nightly against a sample of real inputs, and fire the existing webhook on a drop. Regressions should surface without anyone remembering to look.

### Deeper in .NET

Lean further into the one ecosystem with no native option: a DI / ASP.NET setup extension for zero-config wiring, a Semantic Kernel filter that traces SK agents with no extra code, and clean interop next to the Aspire dashboard. This is where the .NET-first identity compounds rather than spreads thin.

### Scale, when you actually need it

Sampling and a retention policy so the local store doesn't grow forever, and an optional non-SQLite backend for the rare case you outgrow one machine. Local-first stays the default; this is opt-in.

## Later

- A hosted team version with auth, for a shared view of shared traffic. Stays optional; the tool stays local-first.
- A fourth SDK, Java/Spring AI for the enterprise crowd or Go for infra, but only once the .NET depth above is real.

---

The throughline is unchanged: tracing gets you in the door, evals and cost keep you there, and in .NET there's no one else doing them.
