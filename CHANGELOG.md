# Changelog

Notable changes per release. Dates are in 2026.

## 1.5.0 - 09-30

- Trace totals (tokens, cost, duration) now accumulate across a trace's spans on every insert, so a trace split across several OTLP batches keeps the full total instead of the last batch overwriting it.
- OTLP ingest also reads the current GenAI semconv keys: `gen_ai.provider.name`, `gen_ai.input.messages`/`gen_ai.output.messages` (structured message arrays, rendered as readable text), and the `gen_ai.client.inference.operation.details` span event. The older `gen_ai.system`/`gen_ai.prompt`/`gen_ai.completion` keys still work.
- The .NET, Python, and JS SDKs now emit `gen_ai.provider.name` and structured `gen_ai.input.messages`/`gen_ai.output.messages`, and still send the older keys so collectors on 1.4 and earlier keep working.
- `/v1/traces` accepts OTLP/HTTP protobuf (`Content-Type: application/x-protobuf`) as well as JSON, gzip-compressed or not. Payloads that can't be decoded get a 400.
- Scheduled evals persist their last-run date, so a restart around the scheduled time doesn't trigger a second run the same day.
- The budget alert now fires right after ingest when spend crosses the cap, not only when the cost view is opened.
- Golden sets default to `~/.seerlens/evals`, seeded from the bundled sample sets the first time it's empty. `SEERLENS_EVALS_DIR` still overrides it.
- `/api/traces` clamps `limit` to 1000. Failed alert webhook calls are logged instead of dropped.
- The Docker image builds on .NET 10 again and keeps settings and golden sets in the `/data` volume.
- Dependency updates: Microsoft.Extensions.AI.* to 10.10.1, Microsoft.SemanticKernel.Abstractions to 1.80.1, Microsoft.Data.Sqlite to 10.0.12, SQLitePCLRaw.bundle_e_sqlite3 to 3.0.5, JsonSchema.Net to 9.4.0, and minor npm bumps for the dashboard.

## 1.4.0 - 06-23

- Scheduled evals: a golden set can run on its own once a day, set the time from the Settings page (or the `/api/schedules` endpoint). The run is scored and stored like a manual one, and a quality drop fires the existing regression webhook, so regressions surface without anyone remembering to look.
- Consensus scorer: `consensus` runs the LLM judge several times and averages, steadier than one verdict, and warns when the votes disagree past a threshold so a flaky judgement doesn't pass as a confident one.
- Embedding-similarity scorer: `embedding` scores cosine similarity between the answer and a case's new `reference` field. Embeddings default to the chat provider; point them elsewhere with `SEERLENS_EMBED_BASE_URL`/`_KEY`/`_MODEL` when the judge provider has no embeddings.

## 1.3.0 - 06-08

- Semantic Kernel integration: a new `Seerlens.SemanticKernel` package adds a kernel filter that traces every SK function automatically. Prompt calls become `llm` spans (model, tokens, and cost read from the connector); native functions become `tool` spans; all grouped per top-level invocation. Wire it with one line, `builder.AddSeerlens("http://localhost:5005")`, no `SeerlensTrace` calls in your code. The model is resolved from the kernel's chat service when the result metadata doesn't carry it. Multi-targets net8.0/net9.0/net10.0. See `samples/SkSample`.
- DI extension: `services.AddSeerlens(url)` on `IServiceCollection` configures the collector once at app startup. Added to `Seerlens.Sdk`.

## 1.2.0 - 06-07

- Rubric judging: the `rubric` scorer grades each criterion in a case's rubric on its own and averages them, instead of one holistic verdict.
- Two more scorers: `regex` (fraction of patterns matched, offline) and `json-schema` (the answer must parse as JSON and validate against a schema, for structured output). Available from the Evals tab and the CLI (`--scorer`).
- Moved to .NET 10. The `Seerlens` dotnet tool now requires the .NET 10 runtime (or use a self-contained binary). The `Seerlens.Sdk` package multi-targets net8.0/net9.0/net10.0, so apps on .NET 8, 9, or 10 can all use it. Older releases (1.1.0 and earlier) stay available for anyone on .NET 9.

## 1.1.0 - 06-07

- Agent evals: a third scorer, `agent`, that gives the model a case's tools, lets it call them (canned results from the golden set), and scores the call sequence against what you expected. Runs from the Evals tab or the CLI (`--scorer agent`), so you can gate CI on tool behavior.
- Refreshed model pricing and provider detection to the current lineup: OpenAI GPT-5.x and o-series, Anthropic Claude 4 family, Google Gemini 2.5, xAI Grok 4, DeepSeek, and Llama on Groq. Anything missing is one line in a `SEERLENS_PRICING_FILE`.

## 1.0.0 - 06-06

First stable release. Went from a local trace and eval viewer to a tool you can gate builds on.

- Evals in CI: `seerlens eval <set>` with a score floor, regression-vs-baseline gating, JUnit and JSON output, a `--quiet` mode, and a copy-paste GitHub Action.
- Model and prompt comparison, quality next to cost and latency.
- Author golden sets in the dashboard, and promote a real trace into a test case.
- Cost you can act on: a monthly budget with an over-budget alert, spend by model and by day, and a pricing override file.
- Agent and MCP runs shown as a step tree, with recorded-run tool-sequence scoring.
- Webhook alerts (Slack-compatible) on regression and over-budget, and JSON export of any trace or run.
- The .NET SDK now emits OTLP like the Python and JS ones.

## 0.2.0 - 06-05

- Spend by provider and model on the Traces landing view.
- Security pass: no API keys in the Docker layer, loopback-only Docker example, fixes for two malformed-input crashes with regression tests.
- Rewrote the evals docs and added a roadmap.

## 0.1.2 - 06-05

- Python and JavaScript SDKs, both over OTLP.
- Run evals from the dashboard against any OpenAI-compatible provider, scored by keyword or an LLM judge.
- Fixed the Docker image failing to start; bumped CI actions; fixed NuGet README images.

## 0.1.1 - 06-05

- OpenTelemetry (OTLP) ingest at `/v1/traces`, so any instrumented app works with no SDK.
- Eval engine: score a golden set and watch quality over time as a trend.
- Streaming responses recorded as their own span; automated NuGet publishing on release.

## 0.1.0 - 06-05

- First release. .NET SDK (`.UseSeerlens()`), the collector (SQLite, live feed, serves the dashboard), and the dashboard (trace list, waterfall, cost/tokens/latency, prompts and completions).
- Shipped as a dotnet tool and self-contained binaries, with CI and a release pipeline.
