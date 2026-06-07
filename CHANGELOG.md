# Changelog

Notable changes per release. Dates are in 2026.

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
