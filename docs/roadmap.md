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

## Shipped in 1.1

- **Score agents by running them.** Beyond scoring a recorded trace, the eval now produces the run: a case declares its tools, the model gets them and is left to call them, and the run is scored on whether it called the expected tools in order. Tools return canned results from the golden set, so a run is repeatable and never touches a real system. Runs from the dashboard or the CLI (`--scorer agent`), so you can gate CI on tool behavior, not just answer text.
- **Current model pricing.** The price table and provider detection now cover the 2026 lineup: OpenAI (GPT-5.x, o-series), Anthropic (Claude 4 family), Google (Gemini 2.5), xAI (Grok 4), DeepSeek, and Llama on Groq. Anything missing is one line in a `SEERLENS_PRICING_FILE`.

## Shipped in 1.2

- **Rubric judging.** The judge scores each criterion in a case's rubric on its own and averages them, a more defensible number than one holistic verdict.
- **More scorers.** A `regex` scorer (fraction of patterns matched, offline) and a `json-schema` scorer (the answer must parse as JSON and validate against a schema, for structured output), alongside keyword, llm-judge and agent.
- **.NET 10.** Moved the whole stack to .NET 10. The published dotnet tool now needs the .NET 10 runtime.

## Shipped in 1.3

- **Semantic Kernel, traced automatically.** A new `Seerlens.SemanticKernel` package adds a kernel filter that turns every SK function into a span, prompt calls priced with tokens and cost, native functions as tool calls, grouped per invocation. One `builder.AddSeerlens(url)` call, no `SeerlensTrace` in your code.
- **DI wiring.** An `AddSeerlens` extension on `IServiceCollection` so an ASP.NET app configures the collector once at startup.

## What's next

### Judging, continued

The rest of trustworthy judging: a consensus mode that runs more than one judge and flags disagreement, and an embedding-similarity scorer (cosine distance to a reference answer), which needs an embeddings provider since the eval provider may not offer one.

### Scheduled evals

Run a set on its own, nightly against a sample of real inputs, and fire the existing webhook on a drop. Regressions should surface without anyone remembering to look.

### Deeper in .NET, continued

The SK filter and DI extension shipped in 1.3. Next in the same direction: clean interop next to the Aspire dashboard, so Seerlens sits alongside the tooling .NET devs already run. This is where the .NET-first identity compounds rather than spreads thin.

### Scale, when you actually need it

Sampling and a retention policy so the local store doesn't grow forever, and an optional non-SQLite backend for the rare case you outgrow one machine. Local-first stays the default; this is opt-in.

## Later

- A hosted team version with auth, for a shared view of shared traffic. Stays optional; the tool stays local-first.
- A fourth SDK, Java/Spring AI for the enterprise crowd or Go for infra, but only once the .NET depth above is real.

---

The throughline is unchanged: tracing gets you in the door, evals and cost keep you there, and in .NET there's no one else doing them.
