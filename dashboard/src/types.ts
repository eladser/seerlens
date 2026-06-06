export type TraceSummary = {
  id: string
  name: string
  startedAt: number
  durationMs: number
  provider: string | null
  model: string | null
  status: string
  promptTokens: number | null
  completionTokens: number | null
  costUsd: number | null
}

export type Span = {
  id: string
  parentId: string | null
  name: string
  kind: string
  startedAt: number
  durationMs: number
  model: string | null
  promptTokens: number | null
  completionTokens: number | null
  costUsd: number | null
  promptText: string | null
  completionText: string | null
  error: string | null
}

export type TraceDetail = {
  trace: TraceSummary
  spans: Span[]
}

export type Stats = {
  traces: number
  totalCostUsd: number
  avgDurationMs: number
}

export type EvalRun = {
  id: string
  set: string
  target: string
  scorer: string
  createdAt: number
  score: number
  caseCount: number
}

export type EvalCase = { input: string; answer: string; score: number }

export type EvalRunDetail = { run: EvalRun; cases: EvalCase[] }

export type GoldenCase = {
  id: string
  input: string
  keywords: string[] | null
  criteria: string | null
}

export type GoldenSet = { name: string; cases: GoldenCase[] }

export type CompareRow = {
  model: string
  score: number
  costUsd: number | null
  avgLatencyMs: number
  tokens: number
}

export type CompareResult = {
  set: string
  scorer: string
  promptPrefix: string | null
  rows: CompareRow[]
}

export type ModelSpend = { model: string; costUsd: number; calls: number; tokens: number }
export type DaySpend = { date: string; costUsd: number }
export type Spend = {
  monthToDateUsd: number
  totalUsd: number
  byModel: ModelSpend[]
  daily: DaySpend[]
}
export type Budget = { monthlyUsd: number | null; alertPerCallUsd: number | null }
export type CostReport = {
  spend: Spend
  budget: Budget
  overBudget: boolean
  budgetUsedFraction: number | null
}

export type Config = {
  providerConfigured: boolean
  model: string
  endpoint: string | null
  evalsDir: string
  setCount: number
  pricingOverride: boolean
  budget: Budget
}
