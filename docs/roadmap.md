# Roadmap

Seerlens does the core job today: diff two runs of your AI calls step by step, see what each call really costs with cached and reasoning tokens counted, and fail CI when quality or cost regresses. Around that it shows every call (cost, speed, prompts, failures), runs evals from the dashboard, the CLI or a schedule, compares models, watches spend against a budget, and traces agent and MCP runs.

A note on direction, because it shapes everything below. Live trace viewing is turning into a commodity. The dashboards that ship with frameworks now show a GenAI call's prompt, response, tool calls and tokens for free, in your stack, during a debug session. Seerlens is not going to win by being one more live span viewer, and it won't try. Those tools are read-only mirrors of telemetry: they show you what happened in the moment and then forget it. They don't judge whether the answer was any good, they don't turn tokens into a budget you can act on, and they can't fail your build when quality drops.

That last part is the whole opening. Seerlens moves up the stack, from watching calls to **comparing them, costing them, and catching regressions before they ship, with history that outlives a single run.** Tracing is the on-ramp. The diff, cost and the CI gate are the product.

None of that is tied to a language. Seerlens reads plain OpenTelemetry GenAI spans, so the README leads with a Python or JavaScript app and no Seerlens package. .NET stays the most polished path: in .NET the parts exist on their own (Microsoft.Extensions.AI.Evaluation scores answers, the Aspire dashboard shows spans) but nothing ties scoring, history, cost in dollars and a CI gate together in one local tool, so the .NET SDK and the Semantic Kernel filter go deepest.

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

## Shipped in 1.4

- **Scheduled evals.** A golden set runs on its own once a day, at a time you set from the dashboard. The run is scored and stored like a manual one, and a drop fires the regression webhook, so regressions surface without anyone remembering to look.
- **Consensus judging.** The `consensus` scorer runs the judge several times and averages, then warns when the votes disagree, a more trustworthy verdict than one shot.
- **Embedding-similarity scorer.** The `embedding` scorer scores cosine distance to a case's reference answer. Embeddings default to the chat provider, with `SEERLENS_EMBED_*` overrides for when the judge provider has none.

## Shipped in 1.5

- **Works with stock OpenTelemetry exporters.** `/v1/traces` takes OTLP/HTTP protobuf and gzip, the defaults most exporters ship with, and reads the current GenAI semantic conventions (`gen_ai.provider.name`, structured input/output messages).
- **Correct totals for split traces.** Cost and tokens add up across every batch a trace arrives in.
- **Budget alerts fire on ingest**, and scheduled evals survive restarts without running twice.

## Shipped in 1.6

- **Run diff.** Pick two traces, or a failing eval case and the last run where it passed, and see them lined up step by step: totals with deltas, the tool path with added and dropped calls, what changed per call (model, parameters, finish reason, input, output, error), and a word-level diff of any prompt. From the trace list, "Diff with previous" on a trace, "What changed" on an eval case, a `/diff` link, or `GET /api/diff`.
- **Cost that counts cached and reasoning tokens.** Ingest reads cache read, cache write and reasoning counts under every spelling in use, the price table has cache prices for Anthropic, OpenAI and Gemini, and cached tokens are priced once whether the provider counts them inside the input or beside it. A cache hit rate shows per trace, per model and for the month. An `invoke_agent` span no longer doubles a trace's totals.
- **A cost gate in CI.** `seerlens eval --max-cost-increase 0.25` fails the build when a run costs more than 25% over the saved baseline, next to the existing score floor and regression check.
- **Eval runs keep their steps.** Each case stores its model and tool calls with tokens and timing, which is what the eval diff reads.
- **Works with the default OpenTelemetry JS exporter.** OTLP/JSON with numeric `intValue` and timestamps is accepted.
- **Opt-in retention.** Set `SEERLENS_RETENTION_DAYS` and the collector drops traces older than that many days, at startup and then once a day. It's off unless set, and eval runs and settings are never pruned.
- **Tool results from standard OpenTelemetry exporters.** OTLP ingest reads `gen_ai.tool.call.result`, so tool spans from exporters that follow the GenAI semantic conventions show what the tool returned, and structured arguments and results render as JSON.
- **Current model prices.** Built-in prices for Claude Fable, Mythos, Opus and Sonnet 5.x, more of the GPT-5 family plus o3-pro, the GPT-6 and GPT-5.6 models, and Gemini 3.1 Pro, 3 Flash, 3.5 Flash-Lite and 3.1 Flash-Lite. Anything missing is still one line in a `SEERLENS_PRICING_FILE`.

## What's next

### Deeper in .NET, continued

The SK filter and DI extension shipped in 1.3, and the SDK sends cached and reasoning counts since 1.6. Next in the same direction: clean interop next to the Aspire dashboard, so Seerlens sits alongside the tooling .NET devs already run.

### Scale, when you actually need it

Retention shipped in 1.6. Still to come: sampling so a busy app doesn't store every call, and an optional non-SQLite backend for the rare case you outgrow one machine. Local-first stays the default; this is opt-in.

## Later

- A hosted team version with auth, for a shared view of shared traffic. Stays optional; the tool stays local-first.
- A fourth SDK, Java/Spring AI for the enterprise crowd or Go for infra, but only once the .NET depth above is real.

---

The throughline: tracing gets you in the door, and the diff, the cost and the CI gate are why you keep it running.
