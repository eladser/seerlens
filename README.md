<p align="center">
  <img src="https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/banner.png" alt="Seerlens, DevTools for AI calls" width="760" />
</p>

<p align="center">
  <a href="https://www.nuget.org/packages/Seerlens"><img src="https://img.shields.io/nuget/v/Seerlens?label=tool" alt="tool on nuget" /></a>
  <a href="https://www.nuget.org/packages/Seerlens.Sdk"><img src="https://img.shields.io/nuget/v/Seerlens.Sdk?label=sdk" alt="sdk on nuget" /></a>
  <a href="https://github.com/eladser/seerlens/actions/workflows/ci.yml"><img src="https://github.com/eladser/seerlens/actions/workflows/ci.yml/badge.svg" alt="ci" /></a>
</p>

DevTools for AI calls. One line of setup and a local dashboard shows every LLM call your app makes: the prompt, what it cost, how many tokens, how long it took, and which tools it called. Runs on your machine. No signup.

Think of it as the browser Network tab, pointed at your AI calls.

![Seerlens demo](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/demo.gif)

## The problem

When you build something on top of an LLM you mostly fly blind. You send a prompt, you get an answer, and the interesting parts are invisible: the exact text that went to the model after your code stitched it together, the dollar cost of that one call, whether the agent called a tool and how long it took, and whether a prompt or model swap quietly made things worse.

The tools that answer this (Langfuse, Arize Phoenix, Helicone) are platforms you deploy. Seerlens is the opposite: a single command you run locally while you build.

## What you get

- **Live trace feed.** Calls show up the moment they happen.
- **A timeline per trace.** LLM calls and tool calls laid out on a real time ruler, so you can see what ran when and what was slow.
- **Cost, tokens, latency** per call and per trace, priced across the common OpenAI, Anthropic, and Google models.
- **The actual prompt and completion**, not a summary.
- **Failures, captured.** A call that throws is recorded with its error, so you can see what broke.
- **Eval trends.** Score a golden set against your prompts and watch the number over time, so a model swap that drops quality shows up as a line heading down, not a surprise in production.

![Failed call](https://raw.githubusercontent.com/eladser/seerlens/main/docs/img/error-trace.png)

## Quick start

Install the collector and run it:

```bash
dotnet tool install -g Seerlens
seerlens
```

That serves the dashboard at http://localhost:5005.

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
    using (SeerlensTrace.Tool("lookupOrder"))
        order = await orders.Find(id);
    await client.GetResponseAsync(followup);
}
```

The SDK ships traces on a background queue. If the collector is down or busy, traces are dropped and your app keeps running. Instrumentation never blocks or throws into your code.

### Other ways to run it

- **Docker:** `docker build -t seerlens . && docker run -p 5005:5005 seerlens`
- **No .NET installed?** Grab a self-contained build (`seerlens-win-x64.zip`, `linux-x64`, `osx-arm64`) from the [releases](https://github.com/eladser/seerlens/releases) and run the `seerlens` binary inside.
- **SDK on NuGet:** `dotnet add package Seerlens.Sdk`.

### From other languages

The collector speaks OTLP, so any OpenTelemetry-instrumented app shows up at `http://localhost:5005/v1/traces` with no Seerlens SDK. There are also small SDKs for [Python](sdk/python) and [JavaScript](sdk/js):

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

Score a golden set against your prompts from the **Evals** tab. Drop a golden set JSON in `evals/` next to the collector, point it at any OpenAI-compatible provider, and hit Run:

```bash
# env vars, or a gitignored .env.local next to the collector
SEERLENS_AI_BASE_URL=https://api.groq.com/openai/v1   # or OpenAI, Gemini, anything compatible
SEERLENS_AI_KEY=...
SEERLENS_AI_MODEL=llama-3.3-70b-versatile
```

Both scorers run each question through the provider to get an answer. **keyword** then checks the answer for the expected terms (no extra calls); **llm-judge** asks the model to grade the answer against each case's criteria. Either way the run lands on the trend, so a model swap that drops quality shows up as a line heading down.

## How it works

The collector takes traces, stores them in a local SQLite file, and pushes new ones to the dashboard over server-sent events. It accepts both a small JSON contract (what the .NET SDK posts) and raw OpenTelemetry traces at `/v1/traces`, normalizing GenAI spans from either into one model. The dashboard is a small React app the collector serves itself.

```
your app ──► Seerlens SDK ──► collector ──► SQLite
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
dotnet test
```

Covers the store and pricing, the ingest endpoint, and the SDK's safety contract (it records on success, rethrows real errors, and a broken collector can't break the host app).

## Status and what's next

Tracing with SDKs for .NET, Python, and JavaScript, OTLP ingest for everything else, and eval trends scored by keyword or an LLM judge, run straight from the dashboard. Ideas on the list:

- **Golden sets from the UI.** Upload and edit golden sets in the dashboard instead of dropping JSON files in `evals/`.
- **A hosted option.** Keep the local-first tool, add a deployable team version for shared dashboards.

## Made by

Elad Sertshuk, a full-stack engineer who builds developer tools.

- GitHub: [@eladser](https://github.com/eladser)
- LinkedIn: [elad-sertshuk](https://www.linkedin.com/in/elad-sertshuk)
- Site: [eladser.dev](https://eladser.dev)

If Seerlens saved you some time, you can [buy me a coffee](https://ko-fi.com/eladser).

## License

MIT
