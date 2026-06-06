# Roadmap

Seerlens does the core job today: see your AI calls (cost, speed, prompts, failures) and run quality evals from the dashboard.

A note on direction, because it shapes everything below. Live trace viewing is turning into a commodity. The dashboards that ship with frameworks now show a GenAI call's prompt, response, tool calls and tokens for free, in your stack, during a debug session. Seerlens is not going to win by being one more live span viewer, and it won't try. Those tools are read-only mirrors of telemetry: they show you what happened in the moment and then forget it. They don't judge whether the answer was any good, they don't turn tokens into a budget you can act on, and they can't fail your build when quality drops.

That last part is the whole opening. Seerlens moves up the stack, from watching calls to **judging them, costing them, and catching regressions before they ship, with history that outlives a single run.** Tracing is the on-ramp. Evals and cost are the product.

And there's a place to aim all of it. In the wider market this layer is crowded, but in .NET it's empty: the .NET AI stack traces calls and stops there, no quality scoring, no cost in dollars. So Seerlens leads **.NET-first**, evals and cost native to the ecosystem that has no other option, while OTLP ingest keeps the door open to any language. That's the identity the rest of this roadmap serves. Depth over breadth, and each release ships on its own.

## v0.3: Evals in CI

**Goal:** gate your builds on answer quality, the way you already gate on unit tests.

- `seerlens eval <set> --min 0.8`, a command that runs a golden set against a provider, prints the scores, and exits non-zero if the score falls below the threshold.
- **Catch regressions, not just absolute floors.** Every run gets stored, so `--baseline` can fail the build when the score drops more than a set amount versus the last passing commit. A PR that quietly costs you 8% answer quality becomes a red check instead of a production surprise.
- A GitHub Actions example in the repo that uses it as a required check, plus a PR comment showing the per-case score diff.
- Machine-readable output (JSON or JUnit) so CI can show per-case results.

**Why it matters:** a live dashboard can show you a call, it can't block a merge. The CI gate is the one workflow a trace viewer structurally can't own, and "I fail the build when answer quality regresses" is the clearest signal that you've run AI for real.

## v0.4: Model and prompt comparison

**Goal:** answer "should I switch models, or change this prompt?" with quality and cost side by side.

- Run one golden set against several models or providers in a single pass.
- A comparison view: per model, the eval score, the dollar cost, and the latency, lined up. For example `gpt-4o: 95%, $0.04, 1.2s` next to `gpt-4o-mini: 78%, $0.003, 0.4s`.
- The same for prompts: change the system prompt, run the set, and see quality and cost move together before you commit to the change.

**Why it matters:** a viewer shows you one call. This compares many and tells you which one to ship. It's the cost-versus-quality decision the spend view and the eval trend only hint at, made explicit, which is what separates a senior AI engineer from someone who just reaches for the biggest model.

## v0.5: Author evals where you work

**Goal:** build and manage golden sets without leaving the tool, and seed them from real usage.

- Create and edit golden sets in the dashboard, no hand-edited JSON.
- **Promote a real trace into an eval case.** Saw a bad answer in your traces? One click turns that prompt into a golden-set question. That closes the loop from "spotted in prod" to "covered by a test," which a read-only viewer can't do because it has nowhere to put the test.
- Search and filter traces (by model, status, cost, or text), which you need once traces pile up.

**Why it matters:** the golden set is the actual work of evals, and today it's a JSON file. Making it first-class, and seeding it from captured traffic, is the workflow real teams want.

## v0.6: Cost you can act on

**Goal:** turn the spend view from a number you glance at into a budget that warns you.

- A maintained pricing table, so dollar cost stays right across providers and models without hand-math.
- Cost broken down where it's useful: per eval run, per conversation, per model, not only a running total.
- **Budgets and alerts.** Set a ceiling and get flagged when a provider or model spikes, or when a run blows past it.
- Spend over time, annotated with model swaps and deploys, so a jump on the chart has a cause you can point at.

**Why it matters:** counting tokens is table stakes, and the built-in viewers already do it. Counting dollars, with a line you don't want to cross and a heads-up when you approach it, is the part teams actually act on and the part a token-counter leaves on the floor. Persistence is what makes this possible at all, which is the same reason the eval trend works and a live viewer's in-memory log doesn't.

## v0.7 and beyond: agents and MCP

**Goal:** follow where the failures are actually moving. A single LLM call is the easy case. The hard, current case is agents: multi-step runs that call tools, hand off context, and fail by returning a confident, well-formed, wrong answer after one bad tool call.

- **Step-level agent traces.** Show a whole run as a tree, every model call, tool call and retry, not a flat list of spans, so you can see where it went sideways.
- **MCP tool-call visibility.** Model Context Protocol is becoming the standard way agents reach tools, and those calls are scattered across server logs today. Surface them as first-class spans: which tool, what arguments, what came back, how long, did it error.
- **Evals that score a run, not just an answer.** Did the agent pick the right tool, in the right order, and stop when it should have? That's the eval question agents actually need, and nobody answers it in .NET.

**Why it matters:** this is the layer the whole field is moving to, and almost every entrant is Python or Go. None of it is native to .NET. It's also the part I work with day to day (MCP servers, agent orchestration, fallback chains), so it's the most honest place for this project to go deep.

## Later (backlog, not scheduled)

- **Trustworthy LLM-judge scoring.** Rubric and criteria based judging, optionally more than one judge, so an eval score is repeatable and defensible instead of one model's mood on the day. The judge is only worth gating on if you trust it.
- **Scheduled evals with alerting.** Run a set on its own (nightly, against a sample of real inputs) and notify on a drop, so a regression surfaces without anyone remembering to look. A live viewer needs you watching; this watches for you.
- More scorers: JSON-schema for structured output, regex, embedding similarity.
- Switch the .NET SDK to OTLP for uniformity with the other SDKs.
- Latency percentiles (p50/p95).
- A hosted team version with auth for shared dashboards. Stays optional; the tool stays local-first.

---

The throughline: tracing gets you in the door, evals and cost keep you there. They're the parts a free, in-stack, live viewer won't grow, and they're the highest hiring signal too. Every release demos on its own.
