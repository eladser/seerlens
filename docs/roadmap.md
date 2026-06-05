# Roadmap

Seerlens does the core job today: see your AI calls (cost, speed, prompts, failures), and run quality evals from the dashboard. The next few releases lean into **evals**, because that's the part that separates "called an API once" from "ran AI in production." Depth over breadth.

Each release is small and ships on its own.

## v0.3: Evals in CI

**Goal:** gate your builds on answer quality, the way you already gate on unit tests.

- `seerlens eval <set> --min 0.8`, a command that runs a golden set against a provider, prints the scores, and exits non-zero if the score falls below the threshold.
- A GitHub Actions example in the repo that uses it as a required check.
- Machine-readable output (JSON or JUnit) so CI can show per-case results.

**Why it matters:** "I fail the build when answer quality regresses" is the clearest signal that you've run AI for real. It turns the eval engine from a dashboard view into an actual guardrail.

## v0.4: Model comparison

**Goal:** answer "should I switch models?" with quality and cost side by side.

- Run one golden set against several models or providers in a single pass.
- A comparison view: per model, the eval score, the dollar cost, and the latency, lined up. For example `gpt-4o: 95%, $0.04, 1.2s` next to `gpt-4o-mini: 78%, $0.003, 0.4s`.

**Why it matters:** this is the decision the spend view and the eval trend both hint at, made explicit. It shows cost-versus-quality thinking, which is what separates a senior AI engineer from someone who just reaches for the biggest model.

## v0.5: Author evals where you work

**Goal:** build and manage golden sets without leaving the tool, and seed them from real usage.

- Create and edit golden sets in the dashboard, no hand-edited JSON.
- **Promote a real trace into an eval case.** Saw a bad answer in your traces? One click turns that prompt into a golden-set question. That closes the loop from "spotted in prod" to "covered by a test."
- Search and filter traces (by model, status, cost, or text), which you need once traces pile up.

**Why it matters:** the golden set is the actual work of evals, and today it's a JSON file. Making it first-class, and seeding it from real captured traces, is the workflow real teams want.

## v0.6: One more language

**Goal:** prove "any language" with a third SDK people actually run, and get every SDK onto its package manager.

- A **Go SDK**, tested end to end like the Python and JS ones. Go is the lingua franca of cloud and AI infrastructure (the OpenTelemetry collector itself is written in Go), so it broadens the audience and shows systems range. Java is the alternative if the goal is the enterprise and Spring AI crowd instead.
- Publish the SDKs to their registries: Python to PyPI, JS to npm, the Go module (the .NET packages are already on NuGet), so `pip install` / `npm install` / `go get` just work.

**Why it matters:** three tested SDKs across .NET, a scripting language, and a systems language, plus one-command installs everywhere, turns the cross-language story from claimed into concrete.

## Later (backlog, not scheduled)

- More scorers: JSON-schema for structured output, regex, embedding similarity.
- Switch the .NET SDK to OTLP for uniformity with the other SDKs.
- Latency percentiles (p50/p95) and a spend-over-time chart.
- A hosted team version with auth for shared dashboards. Stays optional; the tool stays local-first.

---

The throughline: evals stay the spine, since that's the differentiator and the highest hiring signal, and every release demos on its own.
