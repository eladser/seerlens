# Reference

Everything configurable in one place: packages, environment variables, the `seerlens eval` command, how cost is counted, and the HTTP API.

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
| `SEERLENS_RETENTION_DAYS` | none | Keep this many days of traces. Older traces and their spans are dropped at startup and once a day. Unset or `0` keeps everything. Eval runs and settings aren't touched. The file doesn't shrink afterwards (no VACUUM); stop the collector and run `sqlite3 <db> vacuum` to get the space back. Month-to-date spend is summed from stored traces, so a value under 31 can undercount the monthly budget. |
| `SEERLENS_EVALS_DIR` | `~/.seerlens/evals` | Folder the golden sets are loaded from and saved to. Seeded from the bundled sample sets the first time it's empty. |
| `SEERLENS_SETTINGS` | `seerlens-settings.json` | Where the budget and alert settings are stored. |
| `SEERLENS_PRICING_FILE` | none | JSON of `{ "model": { "in": 1.0, "out": 2.0 } }` (USD per 1M tokens) to override or extend the built-in prices. `"cachedIn"` and `"cacheWrite"` price cache reads and cache writes; without them those tokens cost `in`. For a model priced by prompt size, add `"above": 100000, "inAbove": 0.5, "outAbove": 2.5`: a call with a bigger prompt than `above` is priced at `inAbove`/`outAbove` for the whole call, one at or under it at `in`/`out`. An entry replaces the built-in row for that model as a whole. See [How cost is counted](#how-cost-is-counted). |
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
| `--max-cost-increase <fraction>` | Fail if the run costs more than the baseline's cost x (1 + fraction), so `0.25` allows 25% more. Needs `--baseline`. Skipped with a note when the baseline has no cost (saved before 1.6) or the run has no priced calls. |
| `--save-baseline <path>` | Write this run's score and cost as the baseline at `<path>`. The cost (`costUsd`) is the target model's calls only, judge and scorer calls aren't in it. |
| `--scorer <name>` | `keyword` (default), `llm-judge`, `rubric`, `consensus`, `regex`, `json-schema`, `embedding`, or `agent`. |
| `--model <name>` | Override `SEERLENS_AI_MODEL` for this run. |
| `--json <path>` | Write the full run as JSON. |
| `--junit <path>` | Write JUnit XML for CI test reporters. |
| `--report <url>` | Also send the run to a running dashboard's trend. |
| `--quiet` | Print only the verdict, not the per-case table. |

Exit codes: `0` pass, `1` below the floor, a regression, or over the cost limit, `2` usage or config error.

When both the baseline and the run have a cost, a `cost` line is printed with or without the flag, for example `COST  $0.0003 vs $0.0002 baseline (+34%), more than the 25% increase allowed.` A baseline file from an older version still reads; it just has no cost.

## Golden set format

A set is `{ "name": "...", "cases": [ ... ] }`. Each case:

| Field | Used by | Meaning |
|---|---|---|
| `id` | eval runs | A stable case id. Runs store it, and "What changed" uses it (with the input) to find the last run where the case passed. Cases added from the dashboard get `case-N`. |
| `input` | all | The question or task. |
| `keywords` | keyword scorer | Terms a good answer must contain. |
| `criteria` | llm-judge scorer | Plain-English rubric the judge grades against. |
| `rubric` | rubric scorer | A list of criteria, each scored 0..1 by the judge and then averaged. |
| `patterns` | regex scorer | Regex patterns a good answer must match. Score is the fraction matched. |
| `schema` | json-schema scorer | A JSON Schema (as a string) the answer, parsed as JSON, must validate against. |
| `reference` | embedding scorer | A gold answer; the score is cosine similarity between it and the answer. |
| `tools` | agent scorer | Tools the model may call: `{ name, description, result }`. `result` is the canned value returned when called. |
| `expectedTools` | agent scorer | The tool names you expect, in order. The run is scored on the in-order match. |

## How cost is counted

Every LLM span is priced once, when it's ingested, from its model's row in the price table. Stored costs aren't recomputed when the table changes. Missing counts are 0.

- **Inclusive or exclusive.** The [GenAI semantic conventions](https://github.com/open-telemetry/semantic-conventions-genai/blob/main/docs/registry/attributes/gen-ai.md#gen-ai-usage-input-tokens) say `input_tokens` should include cache reads and writes. Anthropic's raw usage leaves them out. When cache reads plus cache writes are more than the input count, the span is taken as exclusive and the prompt size is input + reads + writes. Otherwise the prompt size is the input count and the uncached part is input - reads - writes. So an exporter that reports exclusive input with a cached part smaller than the new input is under-priced: the cached tokens get subtracted from input that never included them.
- **Tier.** For a row with `above`, a prompt bigger than `above` prices the whole call at `inAbove`/`outAbove`. The prompt size includes cached tokens, which is also how Anthropic counts Haiku 5.5's threshold.
- **Cache prices.** Cache reads cost `cachedIn` and cache writes cost `cacheWrite`, both scaled by the tier ratio (`inAbove / in`) above the threshold. A row without one bills those tokens at the input price for the tier, which is what every call cost before 1.6. Built-in cache-write prices are the short-TTL ones (Anthropic's 5-minute write, OpenAI's 30-minute write where listed).
- **Reasoning.** Reasoning tokens are part of the output count per the conventions, so they're billed as output. When the reasoning count is bigger than the output count, the exporter left it out and it's added. A separately reported reasoning count smaller than the output is taken as already inside it, and under-priced.
- **Agent spans.** A span whose `gen_ai.operation.name` starts with `invoke_` or is `create_agent` is stored as kind `agent`. It's priced like any other, but a trace's token and cost totals leave it out when it has an LLM span below it, since its usage is the total of those calls. An agent span with nothing below it (a remote agent, say) still counts. This applies to traces ingested by 1.6; traces stored by 1.5 or earlier keep the double-counted totals they were stored with.

The **cache hit rate** is cache reads over the prompt size, with the prompt size worked out as above, so it can't go over 100%. It's summed over LLM spans that report a cache read count, and it's blank when there are none. `/api/cost` returns it for the month and per model; the diff returns it per run.

A span with no cache or reasoning data costs exactly what it did in 1.5.

### Token attributes

Ingest takes the first key present for each value, newest spelling first. JSON and protobuf payloads read the same keys.

| Value | Keys, in order |
|---|---|
| input | `gen_ai.usage.input_tokens`, `gen_ai.usage.prompt_tokens` |
| output | `gen_ai.usage.output_tokens`, `gen_ai.usage.completion_tokens` |
| cache read | `gen_ai.usage.cache_read.input_tokens`, `gen_ai.usage.cache_read_input_tokens`, `ai.usage.inputTokenDetails.cacheReadTokens`, `ai.usage.cachedInputTokens` |
| cache write | `gen_ai.usage.cache_write.input_tokens`, `gen_ai.usage.cache_creation.input_tokens`, `gen_ai.usage.cache_creation_input_tokens`, `ai.usage.inputTokenDetails.cacheWriteTokens` |
| reasoning | `gen_ai.usage.reasoning.output_tokens`, `gen_ai.usage.reasoning_tokens`, `llm.usage.reasoning_tokens`, `ai.usage.outputTokenDetails.reasoningTokens`, `ai.usage.reasoningTokens` |

The dotted `gen_ai.usage.cache_read.input_tokens` form is the current semantic conventions. `cache_write` replaced `cache_creation` there in August 2026 with no alias, and older core semconv versions, the OpenTelemetry Python GenAI utilities and the Vercel AI SDK's `@ai-sdk/otel` still send `cache_creation`. The underscore and `llm.` forms come from OpenLLMetry, and the `ai.usage.*` keys from the Vercel AI SDK's older telemetry. The Seerlens .NET SDK and Semantic Kernel filter send the current keys.

Whether a count arrives depends on the instrumentation. Two checked against Seerlens 1.6: `@ai-sdk/otel` sends cache reads, and `opentelemetry-instrumentation-openai-v2` 2.4b0 sends input and output counts for chat completions but no cache or reasoning counts. The OpenTelemetry Vertex AI instrumentation reports no thinking tokens.

Spans also keep `gen_ai.operation.name` and their request parameters: every `gen_ai.request.*` key except the model, with the prefix dropped, plus `gen_ai.response.finish_reasons`. The run diff compares those.

## HTTP API

The collector listens on `SEERLENS_URL`. Traces are normalized into one model whether they arrive as OTLP or the legacy JSON contract.

**Ingest**
- `POST /v1/traces` - OpenTelemetry OTLP/HTTP. Accepts JSON or protobuf (`Content-Type: application/x-protobuf`), whichever your exporter sends. What every SDK posts.
- `POST /ingest` - legacy simple JSON. Kept for compatibility.

**Traces**
- `GET /api/traces?limit=` - list summaries.
- `GET /api/traces/{id}` - one trace with its spans. Each span has `cacheReadTokens`, `cacheWriteTokens`, `reasoningTokens`, `operation` and `params` (a JSON object as text), and `kind` is `llm`, `tool`, `mcp`, `agent` or `other`.
- `DELETE /api/traces/{id}` - delete one. `DELETE /api/traces` - clear all.
- `GET /events` - server-sent events, new traces as they arrive.

**Run diff**

A run ref is a trace id, or `eval:{runId}:{caseIdx}` for one case of a stored eval run (`caseIdx` is the case's position in that run, from 0).

- `GET /api/diff?a=&b=` - line up two runs. Returns `a` and `b` (trace summaries), `totalsA`/`totalsB` (duration, cost, input, output, cache read, cache write and reasoning tokens, cache hit rate, steps, tool calls, errors, models), `toolsA`/`toolsB` (the tool path), `truncated`, and `steps[]`. Each step is `{ status, kind, name, a, b, changes, params }`: `status` is `same`, `changed`, `removed` or `added`, `changes` lists any of `model`, `params`, `finish`, `input`, `output`, `error`, and `a`/`b` carry the step's model, offset, duration, tokens, cost and error. Only the first changed step has its `input` and `output` text diffs inline. Over 2,000 steps on a side, the first 2,000 are lined up and `truncated` is true. 400 when a ref is missing, malformed, or `a == b`; 404 with `missing: "a"` or `"b"` when a ref doesn't resolve.
- `GET /api/diff/step?a=&b=&sa=&sb=` - the word-level `input` and `output` diffs for one pair of steps (`sa`, `sb` are span ids from the diff). With only `sa` or only `sb`, for a removed or added step, it returns that step's raw text. A text over 200,000 characters comes back as `tooLarge` with both sizes.

Steps are a run's `llm`, `tool` and `mcp` spans, in start order. Calls that start within 10 ms of the first call of a group, under the same parent, are sorted by kind, tool name and input so parallel fan-out lines up. Steps match on kind and lowercased tool name, never the model. Token, cost and latency differences show as deltas and don't make a step `changed`.

The dashboard serves the same diff at `/diff?a=&b=`.

**Evals**
- `GET /api/sets` - set names plus provider status. `GET /api/sets/{name}` - full set.
- `PUT /api/sets/{name}` - create or replace. `DELETE /api/sets/{name}` - delete. `POST /api/sets/{name}/cases` - append a case.
- `POST /eval/run` - run a set through the provider and store it. `POST /eval/runs` - store a run produced elsewhere; each case can carry `caseId` and `steps`.
- `GET /api/evals?set=` - runs for the trend. `GET /api/evals/{id}` - one run with its cases. Each case has `caseId`, `steps` (each model call with model, timing, input, output, cached and reasoning tokens, and each tool call with its arguments) and `baseline`: for a case under 0.8, `{ runId, caseIdx, score, createdAt }` of the last earlier run of the same set and scorer where it scored 0.8 or more, else null. The 0.8 is the case's own score, fixed, and separate from `seerlens eval --min`. Runs stored before 1.6 have no steps and diff on the answer alone.
- `POST /eval/compare` - run a set across several models. Each case is priced on its own. 400 for an unknown scorer, `agent`, or `embedding` with no embeddings provider. `POST /eval/tools` - score a trace's tool calls against an expected sequence.

**Cost and settings**
- `GET /api/stats` - trace count, total cost, average latency.
- `GET /api/cost` - month-to-date and all-time spend, by model, last 14 days, budget state. `spend.cacheHitRate` and `byModel[].cacheHitRate` are null when no span reported cache reads.
- `PUT /api/budget` - set the monthly budget.
- `GET /api/alerts`, `PUT /api/alerts` - webhook URL and regression threshold.
- `GET /api/schedules`, `PUT /api/schedules` - daily eval schedules, each `{ set, scorer, dailyAt }` (`dailyAt` is a `HH:mm:ss` local time).
- `GET /api/config` - provider status, evals dir, pricing override, budget, alerts, schedules.
