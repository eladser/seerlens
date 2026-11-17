# Seerlens: Design Spec

*DevTools for AI calls.*
Date: 2026-06-04 · Owner: Elad Sertshuk

**Status (1.6):** On .NET 10. Takes OTLP GenAI traces from any exporter (JSON or protobuf, gzip or not), with SDKs for .NET, Python and JavaScript, a `Seerlens.SemanticKernel` filter that traces SK functions with no code changes, and an `AddSeerlens` DI extension. Two runs (traces or eval cases) diff step by step: totals, tool path, and what changed per model or tool call, down to a word-level prompt diff. Cost counts cached and reasoning tokens, shows a cache hit rate, and counts agent spans once. Evals run from the dashboard and the CLI, or on a daily schedule, scored by keyword, LLM judge, rubric (per-criterion), consensus (multi-judge), regex, JSON-schema, embedding similarity, or agent tool-call sequence. The CLI gates CI on a score floor, a drop against a baseline, and a cost increase against that baseline, and a failing case links to the last run where it passed. Also model and prompt comparison, a budget with alerts, agent and MCP step trees, JSON export, a webhook for regressions and over-budget spend, and opt-in retention (`SEERLENS_RETENTION_DAYS`). What's next is in the [roadmap](roadmap.md): Aspire interop and opt-in sampling.

## Pitch

**Problem:** When you build an app that uses AI, you can't see what it's doing, what prompt actually got sent, what it cost, how slow it was, or whether it's getting worse over time.

**Solution:** One line of setup, and a local dashboard shows every AI call live, prompt, completion, cost, tokens, latency, tool calls, and tracks answer quality so you catch silent regressions. Works in any language. Runs on your machine. No signup.

DevTools for AI calls. Same idea as the browser Network tab, pointed at LLM calls.

## Who it's for

A developer building an AI feature, in the middle of the dev loop, who needs to see what their AI is doing *right now*. Not an ops team monitoring production, that's Langfuse's job. This is the local, zero-setup, "what did my agent just do" tool.

## Positioning

The honest landscape first, because it shapes the whole pitch. Language-agnostic LLM observability is crowded and well funded, and much of it is already local-first and open source. Promptfoo (MIT, runs locally, CI-integrated, 300k+ developers, now part of OpenAI) and Comet's Opik (open source, evals in CI) own the local eval story. Arize Phoenix is local-first and OTel-based. Langfuse and Helicone are the deploy-it platforms. Competing with that group on features, in their languages, is not a fight worth picking.

So the lead is narrow and works the same in any language: **diff your AI calls locally, see what they really cost, fail CI when quality or cost regresses.** A run diff lines up two runs of the same flow step by step, so a prompt edit or a model swap shows as the exact calls that changed. Cost counts cached and reasoning tokens instead of pricing every token at the input rate. And the CLI fails a build on a quality drop or a cost increase against a saved baseline. The input is plain OpenTelemetry GenAI spans, so a Python or JavaScript app needs no Seerlens package at all.

**.NET is the most polished path, not the headline.** The .NET AI stack (Microsoft.Extensions.AI, Semantic Kernel, Aspire) gives you tracing, and the Aspire dashboard even renders GenAI spans, but nothing in it judges answer quality or turns tokens into dollars. The .NET SDK goes furthest: one line on any `IChatClient`, cached and reasoning counts read from MEAI usage, Semantic Kernel traced with no code, DI wiring. That depth stays, it just isn't the first thing a new user has to care about.

For a portfolio the same framing is the honest one: the value here isn't commercial novelty, there's an OpenAI-owned competitor in the broad market. It's that the author can build a standards-based trace pipeline, a run diff, an eval engine and a clean local-first dev tool, end to end.

## Core design decision

Everything is built on **OpenTelemetry GenAI semantic conventions** (`gen_ai.*` spans). OTel is cross-language, so the dashboard, storage, and eval engine are language-agnostic by default, they receive standard telemetry and don't care what produced it. The only language-specific code is the optional convenience SDK. Any app already emitting OTel GenAI spans works with zero SDK.

## Components

| Component | Type | Job | Key dependencies |
|---|---|---|---|
| `seerlens` | CLI / single binary | One command brings up collector + dashboard. The whole tool. | none |
| Collector | HTTP service (ASP.NET Core) | OTLP/HTTP ingest endpoint. Validates + stores spans. Live-pushes to dashboard. | OpenTelemetry |
| Store | Embedded DB (SQLite) | Traces, spans, eval runs. Zero-config, file-based. | SQLite |
| Dashboard | Web app (React/TS + Vite) | Trace list, trace waterfall detail, token/cost rollups, eval trend charts. | none |
| Eval engine | Service (in collector) | Runs a golden set against a provider, scores answers, stores runs, computes regression over time. | Provider APIs, scorer lib |
| Starter SDKs | Thin libraries | "One line to add it." Wrap the host's LLM client, emit OTel GenAI spans. | OTel SDK per language |

### Store choice
SQLite, not Postgres. A local dev tool should have **zero infrastructure**, no container, no connection string. SQLite ships in-process, the DB is a file, and it matches the "one command, no setup" promise. (The earlier draft said Postgres; changed to fit the local-first decision.)

### Starter SDKs (priority order)
1. **.NET / C#**, the deepest integration. Wraps `IChatClient` (Microsoft.Extensions.AI) as a delegating handler. The README leads with plain OpenTelemetry instead, since that works from any language.
2. **Python**, wraps the OpenAI/Anthropic clients or hooks existing OpenLLMetry instrumentation.
3. **JavaScript / TypeScript**, wraps the provider SDKs / Vercel AI SDK.

The SDK is a convenience, not a requirement. The hard contract is "emit OTel GenAI spans to this endpoint," which any language can already do.

### SDK safety contract
Instrumentation must **never break or slow the host app**. Export is fire-and-forget on a bounded buffer; if the collector is down or the buffer is full, spans are dropped and the host call proceeds untouched. The SDK swallows and logs its own errors. This is a hard requirement, not a nice-to-have.

## Data flow

```
your app → SDK emits gen_ai span (OTLP) → Collector ingests → Store
                                                    ↓
                                          live push (SSE/WebSocket)
                                                    ↓
                                          Dashboard: trace lights up
```

Eval path:
```
Dashboard "run eval" → Eval engine runs golden set through a provider
       → scores each answer (keyword match, or an LLM judge against a rubric)
       → stores run → Dashboard plots score-over-time per prompt/model
```

The eval engine owns the provider calls itself, so eval is language-agnostic, it doesn't run through the user's app.

## The two demos that sell it

1. **Live trace.** Split screen: a chat app answering on the left, Seerlens on the right lighting up with `820ms · $0.004 · 1,240 tokens · 3 tool calls`, expanding into a nested waterfall. Click a row, read the exact prompt and response.
2. **Regression catch.** "Switched GPT-4o → a cheaper model to save money; my golden-set score dropped from 100% to 39%," shown as a trend line. The screenshot that says "I've run this in production."

## Scope and phases

De-risked so each phase is shippable on its own.

- **Phase 0, skeleton.** CLI that boots collector + empty dashboard. OTLP ingest endpoint. SQLite store. Minimal trace list.
- **Phase 1, spine + demo (shippable alone).** Trace waterfall detail, token/cost/latency rollups, live feed, the **.NET starter SDK**. A strong portfolio piece even if it stops here.
- **Phase 2, depth.** Eval/regression engine + score-over-time charts. Multi-provider cost view (Groq/Mistral/Gemini/OpenAI/Claude). The senior-signal half.
- **Phase 3, reach.** Python + JS/TS starter SDKs. Cross-language demo.
- **Phase 4, stretch (optional).** An MCP agent over real infrastructure (AWS/K8s/Terraform, read-only) as a flagship demo app to point Seerlens at. Parked unless time allows.

## Out of scope for v1 (YAGNI)

Multi-tenant auth, hosted/cloud version, team collaboration, sampling config, ClickHouse/columnar store, RBAC. These are platform concerns; Seerlens is a local dev tool. (Webhook alerts shipped in 1.0; a hosted version stays a roadmap maybe.)

## Senior-signal artifacts (bake in from day one)

These come straight from the hiring research and are what make the repo read as staff-level:

- An actual `/evals` folder with a real golden set and a runnable regression.
- A README that leads with problem → solution, has an architecture diagram, a short ADR / "why I chose X" section, screenshots/gif of the dashboard, and a **"known limitations + what's next"** section.
- The SDK safety contract documented explicitly (can't break the host app).
- Multi-provider cost view, maps to the "cost optimization / model routing" hiring signal.
- Distribution as a one-command install (`npm` / `pip` / `dotnet tool` / single binary + Docker one-liner). The distribution is the feature.
- Clean, iterative commit history with real messages, not one "initial commit."
- Built to the OpenTelemetry standard, not a proprietary format. State this prominently.

## Testing

- Unit tests on the scorers and on span parsing/normalization.
- One integration test on the AI path: instrument a fake client, make a call, assert the span was recorded and rendered. Testing the non-deterministic part is itself a senior signal.
- The `/evals` folder doubles as a living test of answer quality.

## Risks and limitations

- **Crowded space.** The language-agnostic side is saturated, including local-first open-source incumbents (Promptfoo, Opik, Phoenix). Seerlens doesn't try to out-feature them. It leads with the three things in the pitch, keeps .NET as its deepest integration, and leans on build quality and the standards-based design as the portfolio signal, not commercial novelty.
- **Eval demos as numbers.** Mitigated by rendering eval as score-over-time trends inside the trace UI, not a static table.
- **Scope creep across languages.** Mitigated by phasing, .NET SDK first, others only after the core ships.
- **OTel GenAI conventions are still evolving.** Mitigated by normalizing spans at ingest into an internal model, so a convention change is a one-place fix.

## Success criteria

- `seerlens` boots and shows a live trace from a real app in under a minute, from a clean machine.
- A regression is visible as a trend after switching models on a golden set.
- Works from at least two languages by Phase 3.
- README + repo pass the "30-second senior skim": problem clear, demo gif, evals folder, architecture doc, clean commits.
