# Reference

Everything configurable in one place: packages, environment variables, the `seerlens eval` command, and the HTTP API.

## Packages

| Package | Target frameworks | What it's for |
|---|---|---|
| `seerlens` (tool) | net10.0 | The collector and dashboard. `dotnet tool install -g seerlens`, or a self-contained zip. |
| `Seerlens.Sdk` | net8.0 / net9.0 / net10.0 | Wrap an `IChatClient` with `.UseSeerlens()`, group traces with `SeerlensTrace.Begin`, configure via `services.AddSeerlens(url)`. |
| `Seerlens.SemanticKernel` | net8.0 / net9.0 / net10.0 | A Semantic Kernel filter. `builder.AddSeerlens(url)` on the kernel builder traces every SK function with no other code. |

Python and JavaScript SDKs ship to PyPI (`seerlens`) and npm (`seerlens`); see their READMEs under `sdk/`.

## Environment variables

| Variable | Default | What it does |
|---|---|---|
| `SEERLENS_URL` | `http://localhost:5005` | Address the collector binds to. |
| `SEERLENS_DB` | `seerlens.db` | SQLite file for traces and eval runs. |
| `SEERLENS_EVALS_DIR` | `./evals` next to the binary | Folder the golden sets are loaded from and saved to. |
| `SEERLENS_SETTINGS` | `seerlens-settings.json` | Where the budget and alert settings are stored. |
| `SEERLENS_PRICING_FILE` | none | JSON of `{ "model": { "in": 1.0, "out": 2.0 } }` (USD per 1M tokens) to override or extend the built-in prices. |
| `SEERLENS_AI_BASE_URL` | none | OpenAI-compatible endpoint used to run evals and comparisons (Groq, Gemini, OpenAI, ...). |
| `SEERLENS_AI_KEY` | none | API key for that provider. Stays in the environment, never in the UI. |
| `SEERLENS_AI_MODEL` | `gpt-4o-mini` | Default model for eval/compare runs. |
| `SEERLENS_EMBED_MODEL` | `text-embedding-3-small` | Model the `embedding` scorer uses. |
| `SEERLENS_EMBED_BASE_URL` | the chat `SEERLENS_AI_BASE_URL` | Endpoint for embeddings, set this when your judge provider has none. |
| `SEERLENS_EMBED_KEY` | the chat `SEERLENS_AI_KEY` | API key for the embeddings endpoint. |

A `.env.local` next to the collector is loaded automatically, so you don't have to export these by hand. Keep it out of source control.

## `seerlens eval`

```
seerlens eval <set> [options]
```

`<set>` is a path to a `.json`, a name under `./evals`, or a bundled set. Provider comes from the `SEERLENS_AI_*` variables.

| Option | What it does |
|---|---|
| `--min <0..1>` | Fail (exit 1) if the mean score falls below this floor. |
| `--baseline <path>` | Fail if the score dropped too far below a saved baseline. |
| `--tolerance <0..1>` | Allowed drop versus the baseline (default 0.05). |
| `--save-baseline <path>` | Write this run's score as the baseline at `<path>`. |
| `--scorer <name>` | `keyword` (default), `llm-judge`, `rubric`, `consensus`, `regex`, `json-schema`, `embedding`, or `agent`. |
| `--model <name>` | Override `SEERLENS_AI_MODEL` for this run. |
| `--json <path>` | Write the full run as JSON. |
| `--junit <path>` | Write JUnit XML for CI test reporters. |
| `--report <url>` | Also send the run to a running dashboard's trend. |
| `--quiet` | Print only the verdict, not the per-case table. |

Exit codes: `0` pass, `1` below the floor or a regression, `2` usage or config error.

## Golden set format

A set is `{ "name": "...", "cases": [ ... ] }`. Each case:

| Field | Used by | Meaning |
|---|---|---|
| `input` | all | The question or task. |
| `keywords` | keyword scorer | Terms a good answer must contain. |
| `criteria` | llm-judge scorer | Plain-English rubric the judge grades against. |
| `rubric` | rubric scorer | A list of criteria, each scored 0..1 by the judge and then averaged. |
| `patterns` | regex scorer | Regex patterns a good answer must match. Score is the fraction matched. |
| `schema` | json-schema scorer | A JSON Schema (as a string) the answer, parsed as JSON, must validate against. |
| `reference` | embedding scorer | A gold answer; the score is cosine similarity between it and the answer. |
| `tools` | agent scorer | Tools the model may call: `{ name, description, result }`. `result` is the canned value returned when called. |
| `expectedTools` | agent scorer | The tool names you expect, in order. The run is scored on the in-order match. |

## HTTP API

The collector listens on `SEERLENS_URL`. Traces are normalized into one model whether they arrive as OTLP or the legacy JSON contract.

**Ingest**
- `POST /v1/traces` - OpenTelemetry OTLP/HTTP JSON. What every SDK posts.
- `POST /ingest` - legacy simple JSON. Kept for compatibility.

**Traces**
- `GET /api/traces?limit=` - list summaries.
- `GET /api/traces/{id}` - one trace with its spans.
- `DELETE /api/traces/{id}` - delete one. `DELETE /api/traces` - clear all.
- `GET /events` - server-sent events, new traces as they arrive.

**Evals**
- `GET /api/sets` - set names plus provider status. `GET /api/sets/{name}` - full set.
- `PUT /api/sets/{name}` - create or replace. `DELETE /api/sets/{name}` - delete. `POST /api/sets/{name}/cases` - append a case.
- `POST /eval/run` - run a set through the provider and store it. `POST /eval/runs` - store a run produced elsewhere.
- `GET /api/evals?set=` - runs for the trend. `GET /api/evals/{id}` - one run with its cases.
- `POST /eval/compare` - run a set across several models. `POST /eval/tools` - score a trace's tool calls against an expected sequence.

**Cost and settings**
- `GET /api/stats` - trace count, total cost, average latency.
- `GET /api/cost` - month-to-date and all-time spend, by model, last 14 days, budget state.
- `PUT /api/budget` - set the monthly budget.
- `GET /api/alerts`, `PUT /api/alerts` - webhook URL and regression threshold.
- `GET /api/schedules`, `PUT /api/schedules` - daily eval schedules, each `{ set, scorer, dailyAt }` (`dailyAt` is a `HH:mm:ss` local time).
- `GET /api/config` - provider status, evals dir, pricing override, budget, alerts, schedules.
