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
  cacheReadTokens?: number | null
  cacheWriteTokens?: number | null
  reasoningTokens?: number | null
  operation?: string | null
  params?: string | null
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

export type EvalBaseline = { runId: string; caseIdx: number; score: number; createdAt: number }

export type EvalCase = {
  input: string
  answer: string
  score: number
  caseId?: string | null
  baseline?: EvalBaseline | null
}

export type EvalRunDetail = { run: EvalRun; cases: EvalCase[] }

export type GoldenCase = {
  id: string
  input: string
  keywords: string[] | null
  criteria: string | null
  tools?: unknown[] | null
  expectedTools?: string[] | null
  rubric?: string[] | null
  patterns?: string[] | null
  schema?: string | null
  reference?: string | null
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

export type ModelSpend = { model: string; costUsd: number; calls: number; tokens: number; cacheHitRate?: number | null }
export type DaySpend = { date: string; costUsd: number }
export type Spend = {
  monthToDateUsd: number
  cacheHitRate?: number | null
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

export type ToolScoreResult = {
  score: number
  orderOk: boolean
  missing: string[]
  expected: string[]
  actual: string[]
}

export type Alerts = { webhookUrl: string | null; regressionDrop: number }

// dailyAt is a "HH:mm:ss" time of day, in the collector's local time
export type Schedule = { set: string; scorer: string; dailyAt: string }

export type Config = {
  providerConfigured: boolean
  model: string
  endpoint: string | null
  evalsDir: string
  setCount: number
  pricingOverride: boolean
  budget: Budget
  alerts: Alerts
  schedules: Schedule[]
}

export type RunTotals = {
  durationMs: number
  costUsd: number | null
  inputTokens: number
  outputTokens: number
  cacheReadTokens: number
  cacheWriteTokens: number
  reasoningTokens: number
  cacheHitRate: number | null
  steps: number
  toolCalls: number
  errors: number
  models: string[]
}

export type StepView = {
  spanId: string
  model: string | null
  operation: string | null
  offsetMs: number
  durationMs: number
  promptTokens: number | null
  completionTokens: number | null
  cacheReadTokens: number | null
  cacheWriteTokens: number | null
  reasoningTokens: number | null
  costUsd: number | null
  error: string | null
}

export type WordPiece = { t: 'eq' | 'del' | 'ins'; text: string }

export type TextRow = {
  t: 'eq' | 'del' | 'ins' | 'mod' | 'skip'
  aLine?: number | null
  bLine?: number | null
  a?: string | null
  b?: string | null
  aWords?: WordPiece[] | null
  bWords?: WordPiece[] | null
  skipped?: number | null
}

export type TextDiffResult = { rows: TextRow[]; tooLarge: boolean; sizeA?: number | null; sizeB?: number | null }

export type ParamChange = { key: string; a: string | null; b: string | null }

export type DiffStep = {
  status: 'same' | 'changed' | 'removed' | 'added'
  kind: string
  name: string
  a: StepView | null
  b: StepView | null
  changes: string[]
  params: ParamChange[]
  input?: TextDiffResult | null
  output?: TextDiffResult | null
}

export type DiffResponse = {
  a: TraceSummary
  b: TraceSummary
  totalsA: RunTotals
  totalsB: RunTotals
  toolsA: string[]
  toolsB: string[]
  truncated: boolean
  steps: DiffStep[]
}

export type StepDiff = { input: TextDiffResult; output: TextDiffResult }
export type StepRaw = { input?: string | null; output?: string | null }
