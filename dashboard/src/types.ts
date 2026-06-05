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
