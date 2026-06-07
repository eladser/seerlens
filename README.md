![Seerlens, DevTools for AI calls](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/banner.png)

[![tool on nuget](https://img.shields.io/nuget/v/Seerlens?label=tool)](https://www.nuget.org/packages/Seerlens)
[![sdk on nuget](https://img.shields.io/nuget/v/Seerlens.Sdk?label=sdk)](https://www.nuget.org/packages/Seerlens.Sdk)
[![pypi](https://img.shields.io/pypi/v/seerlens?label=pypi)](https://pypi.org/project/seerlens/)
[![npm](https://img.shields.io/npm/v/seerlens?label=npm)](https://www.npmjs.com/package/seerlens)
[![ci](https://github.com/eladser/seerlens/actions/workflows/ci.yml/badge.svg)](https://github.com/eladser/seerlens/actions/workflows/ci.yml)

DevTools for AI calls. One line of setup and a local dashboard shows every LLM call your app makes: the prompt, what it cost, how many tokens, how long it took, and which tools it called. Runs on your machine. No signup.

Think of it as the browser Network tab, pointed at your AI calls.

Built .NET-first. It adds the two things the .NET AI stack doesn't give you, answer-quality evals and cost in dollars, and because it speaks OpenTelemetry, calls from Python, JavaScript or any other language land in the same dashboard.

![Seerlens demo](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/demo.gif)

## The problem

When you build something on top of an LLM you mostly fly blind. You send a prompt, you get an answer, and the interesting parts are invisible: the exact text that went to the model after your code stitched it together, the dollar cost of that one call, whether the agent called a tool and how long it took, and whether a prompt or model swap quietly made things worse.

The tools that answer this (Langfuse, Arize Phoenix, Helicone) are platforms you deploy. Seerlens is the opposite: a single command you run locally while you build.

## What you get

- **Live trace feed.** Calls show up the moment they happen.
- **A timeline per trace.** LLM calls and tool calls laid out on a real time ruler, so you can see what ran when and what was slow.
- **Cost, tokens, latency** per call and per trace, plus a spend breakdown by provider and model, priced across the current OpenAI, Anthropic, Google, xAI, and DeepSeek lineups (and overridable for anything else).
- **The actual prompt and completion**, not a summary.
- **Failures, captured.** A call that throws is recorded with its error, so you can see what broke.
- **Eval trends.** Score a golden set against your prompts and watch the number over time, so a model swap that drops quality shows up as a line heading down, not a surprise in production.
- **Model comparison.** Run one golden set across several models (and an optional system prompt) and see quality, cost, and latency side by side.
- **Cost control.** Set a monthly budget and get warned when you cross it, with spend broken down by model and by day.
- **Agent and MCP runs.** A run shows as a step tree, and MCP tool calls show their arguments and result. Score whether an agent used the right tools two ways: against a recorded trace, or by actually running the model with a set of tools and grading the calls it makes.
- **Alerts and export.** Point a webhook at regressions and over-budget spend, and export any trace or eval run as JSON.

![Failed call](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/error-trace.png)

## Quick start

Install the collector and run it:

```bash
dotnet tool install -g Seerlens
seerlens
```

That serves the dashboard at http://localhost:5005.

The tool needs the **.NET 10 runtime**. No .NET installed? Grab a self-contained `seerlens-*.zip` from the [releases](https://github.com/eladser/seerlens/releases), it bundles everything. The `Seerlens.Sdk` package is separate and works on **.NET 8, 9, and 10**, so wrapping your app doesn't force you onto .NET 10.

Then point your app at it. In .NET, wrap the `IChatClient` you already use:

```csharp
using Seerlens.Sdk;

SeerlensTrace.Configure("http://localhost:5005");

IChatClient client = baseClient.UseSeerlens();
```

That's it. Every call through `client` shows up in the dashboard. To group a multi-step interaction (a couple of model calls with a tool lookup between them) into a single trace:

```csharp
using (SeerlensTrace.Begin("answer support ticket"))
{
    await client.GetResponseAsync(messages);

    using (var t = SeerlensTrace.Tool("lookupOrder", $"{{\"id\":\"{id}\"}}"))
        t.Complete((order = await orders.Find(id)).Status);

    // an MCP tool call is the same idea with .Mcp(...)
    await client.GetResponseAsync(followup);
}
```

The tool and MCP calls show up as steps in the trace, with their arguments and result, and you can score whether an agent used the tools you expected from the trace view.

The SDK ships traces on a background queue. If the collector is down or busy, traces are dropped and your app keeps running. Instrumentation never blocks or throws into your code.

### Other ways to run it

- **Docker:** `docker build -t seerlens . && docker run -p 127.0.0.1:5005:5005 seerlens`
- **No .NET installed?** Grab a self-contained build (`seerlens-win-x64.zip`, `linux-x64`, `osx-arm64`) from the [releases](https://github.com/eladser/seerlens/releases) and run the `seerlens` binary inside.
- **SDK on NuGet:** `dotnet add package Seerlens.Sdk`.

The collector has no auth, by design: it binds `localhost` and the Docker example publishes only to `127.0.0.1`. It's a local dev tool. If you put it on a shared host or a network, gate it yourself, the captured prompts and your provider key are worth protecting.

### From other languages

The collector speaks OTLP, so any OpenTelemetry-instrumented app shows up at `http://localhost:5005/v1/traces` with no Seerlens SDK. There are also small SDKs on [PyPI](https://pypi.org/project/seerlens/) and [npm](https://www.npmjs.com/package/seerlens):

```bash
pip install seerlens     # Python
npm install seerlens      # JavaScript / Node
```

```python
import seerlens
seerlens.configure("http://localhost:5005")

with seerlens.trace("answer ticket", model="gpt-4o") as span:
    reply = my_llm(prompt)
    span.complete(prompt=prompt, completion=reply, input_tokens=40, output_tokens=12)
```

```js
import * as seerlens from 'seerlens'
seerlens.configure('http://localhost:5005')

const span = seerlens.trace('answer ticket', { model: 'gpt-4o' })
const reply = await myLlm(prompt)
span.complete({ prompt, completion: reply, inputTokens: 40, outputTokens: 12 })
```

## Running evals

An eval is a quality test for your AI's answers, the part you can't catch with normal tests. AI doesn't crash when it gets worse, it just quietly gives worse answers. So you write a small set of questions where you know what a good answer looks like (a "golden set"), Seerlens runs them through a model and scores the answers, and a drop after a prompt tweak or a model swap shows up as the trend heading down.

**You write the golden set**, because only you know what a good answer is for your app. Drop a JSON file in `evals/` next to the collector:

```json
{
  "name": "support",
  "cases": [
    { "input": "What is your refund policy?", "keywords": ["30", "days"], "criteria": "states the refund window in days" },
    { "input": "Where is my order #5521?", "keywords": ["shipped"], "criteria": "says it shipped and gives an arrival day" }
  ]
}
```

- `input` is a real question your app handles.
- `keywords` are terms a good answer must contain (used by the offline scorer).
- `criteria` is a plain-English rubric the LLM judge grades against.

To run it, point the collector at any OpenAI-compatible provider:

```bash
# copy .env.local.example to .env.local next to the collector, or set env vars
SEERLENS_AI_BASE_URL=https://api.groq.com/openai/v1   # or OpenAI, Gemini, anything compatible
SEERLENS_AI_KEY=...
SEERLENS_AI_MODEL=llama-3.3-70b-versatile
```

Then pick the set in the **Evals** tab and hit Run. The run lands on the trend. There are a few scorers, so you can match the check to what you actually care about:

- **keyword** checks the answer for the expected terms. Offline, no extra calls.
- **llm-judge** asks the model for one 0..1 grade against the case's `criteria`.
- **rubric** asks the judge to score each criterion in the case's `rubric` separately, then averages, a more defensible number than one holistic verdict.
- **regex** scores the fraction of the case's `patterns` the answer matches. Offline.
- **json-schema** is 1 if the answer parses as JSON and validates against the case's `schema`, for structured-output checks. Offline.
- **agent** runs the model with the case's tools and scores the call sequence (see below).

![Run an eval from the dashboard](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/eval-run.png)

### Scoring agents

There's a third scorer, **agent**, for when the thing you care about is whether the model reaches for the right tools. A case declares the tools it may call and the sequence you expect:

```json
{
  "input": "Find our refund policy and tell me the window in days.",
  "tools": [
    { "name": "search_docs", "description": "Search the docs", "result": "3 matches: refunds.md, terms.md" },
    { "name": "read_file", "description": "Read a doc", "result": "Refunds within 30 days." }
  ],
  "expectedTools": ["search_docs", "read_file"]
}
```

The agent scorer gives the model those tools and lets it call them (the `result` is returned, so nothing real is touched), then scores the calls it actually made against `expectedTools`, in order. So "right answer, wrong tool path" shows up as a lower score. Run it from the Evals tab or the CLI with `--scorer agent`.

### In CI

The same scoring runs from the command line, so you can gate a build on it:

```bash
seerlens eval support --min 0.8
# or catch a regression against a saved baseline
seerlens eval support --baseline .seerlens/support.base --junit results.xml
```

It exits non-zero when the mean score is below `--min`, or when it dropped too far below the baseline, which is what turns the eval engine into an actual guardrail. See [docs/ci-eval-gate.yml](docs/ci-eval-gate.yml) for a GitHub Actions example.

## A look around

An agent run as a step tree, with the MCP tool calls and their arguments and result:

![Agent run with MCP tool calls](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/trace-waterfall.png)

Answer quality over time. Here the score falls off a cliff after a model swap:

![Eval trend](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/eval-trend.png)

Compare models on the same golden set, quality next to cost and latency:

![Model comparison](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/compare.png)

Spend against a monthly budget, broken down by model, with an alert when you cross it:

![Cost and budget](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/cost-budget.png)

## How it works

The collector takes traces, stores them in a local SQLite file, and pushes new ones to the dashboard over server-sent events. Every SDK (.NET, Python, JavaScript) emits OpenTelemetry GenAI spans to `/v1/traces`, and the collector normalizes them into one model, so any other OTLP exporter works too. A simpler legacy JSON endpoint is still accepted for compatibility. The dashboard is a small React app the collector serves itself.

```
your app ──► Seerlens SDK (or any OTLP exporter) ──► collector ──► SQLite
                                                          │
                                                          └──► live feed (SSE) ──► dashboard
```

| Piece | What it is |
|-------|-----------|
| `Seerlens.Sdk` | .NET SDK. An `IChatClient` wrapper plus a small API for grouping traces. |
| `Seerlens.Evals` | Golden sets, scorers (keyword or LLM-as-judge), and a runner that scores your prompts and reports the run. |
| `Seerlens.Collector` | ASP.NET Core app. Trace and eval ingest, SQLite store, live feed, and it serves the dashboard. Packaged as the `seerlens` tool. |
| `dashboard` | React + TypeScript UI. Trace timeline, cost and token rollups, and the eval trend. |

## Run it from source

```bash
# build the dashboard into the collector
cd dashboard && npm install && npm run build && cd ..

# run the collector
dotnet run --project src/Seerlens.Collector

# in another shell, send some sample traces
dotnet run --project samples/ChatSample
```

The sample uses a fake model client, so it runs without any API keys.

## Tests

```bash
dotnet test                           # collector + .NET SDK
cd sdk/python && python -m unittest    # python SDK
cd sdk/js && node --test               # js SDK
```

The .NET tests cover the store and pricing, OTLP span mapping, the ingest, eval, compare, cost, and agent-tool-scoring endpoints, golden-set CRUD, settings, and the SDK's safety contract (it records on success, rethrows real errors, builds valid OTLP, and a broken collector can't break the host app). The Python and JS tests check the OTLP payload each SDK builds. All three suites run in CI on every push.

## Known limitations

What this doesn't do, since the tradeoffs were deliberate:

- **One machine, one person.** No auth, no shared dashboard, history lives in a local SQLite file. That's the point for a dev-loop tool, but if your team wants a common view of production traffic, this isn't it. A hosted version is noted below and stays optional.
- **SQLite isn't built for a production firehose.** Fine for the dev loop and thousands of traces. Point a high-volume production stream at it and you'd want a columnar store instead. There's no sampling or retention policy yet, so the file grows until you clear it.
- **Cost depends on a pricing table.** Tokens become dollars from a per-model price list, so a brand-new model or a price change needs the table updated or the cost reads as zero. You can point `SEERLENS_PRICING_FILE` at a JSON to override prices, but token counts are always right while the dollar figure is only as fresh as the table.
- **LLM-judge scoring costs money and isn't perfectly repeatable.** The judge is itself a model call, so it adds latency and spend, and two runs can disagree at the margin. The keyword scorer is deterministic but blunt. Pick the one that fits what you're checking.
- **"Any language" is verified for three.** .NET, Python and JavaScript are tested end to end and all emit OTLP GenAI spans. Anything else emitting the same should work, I just haven't proven each one.
- **Agent scoring uses declared tools with canned results.** You can run the model with a case's tools and score the calls it makes, but those tools return the fixed results you put in the golden set. Seerlens doesn't execute your real tools or connect to a live MCP server, and the run needs a provider that supports tool calling.

## Status and what's next

Stable, and installable as a dotnet tool, from NuGet, PyPI, or npm, or via Docker. Everything above is what ships today.

What's next, with the full plan in the [roadmap](docs/roadmap.md):

- **Judging you can trust.** Rubric-based scoring and more scorer types (JSON-schema, regex, embedding similarity), so the number you gate a build on holds up.
- **Scheduled evals.** Run a set nightly against a sample and fire the webhook on a drop.
- **Deeper in .NET.** A DI / ASP.NET setup extension and a Semantic Kernel filter that traces agents with no extra code.

## Docs

- [Reference](docs/reference.md): every environment variable, the `seerlens eval` flags, and the HTTP API.
- [Roadmap](docs/roadmap.md): what shipped in 1.0 and what's next.
- [Design notes](docs/design.md): why it's built the way it is.
- [Changelog](CHANGELOG.md).

## Made by

Elad Sertshuk, a full-stack engineer who builds developer tools.

- GitHub: [@eladser](https://github.com/eladser)
- LinkedIn: [elad-sertshuk](https://www.linkedin.com/in/elad-sertshuk)
- Site: [eladser.dev](https://eladser.dev)

If Seerlens saved you some time, you can [buy me a coffee](https://ko-fi.com/eladser).

## License

MIT
