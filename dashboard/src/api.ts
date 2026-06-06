import type {
  Alerts, Budget, CompareResult, Config, CostReport, EvalRun, EvalRunDetail, GoldenCase, GoldenSet,
  Stats, ToolScoreResult, TraceDetail, TraceSummary,
} from './types'

export async function getTraces(limit = 200): Promise<TraceSummary[]> {
  const r = await fetch(`/api/traces?limit=${limit}`)
  if (!r.ok) throw new Error(`traces: ${r.status}`)
  return r.json()
}

export async function getTrace(id: string): Promise<TraceDetail> {
  const r = await fetch(`/api/traces/${id}`)
  if (!r.ok) throw new Error(`trace ${id}: ${r.status}`)
  return r.json()
}

export async function clearTraces(): Promise<void> {
  const r = await fetch('/api/traces', { method: 'DELETE' })
  if (!r.ok && r.status !== 204) throw new Error(`clear: ${r.status}`)
}

export async function getStats(): Promise<Stats> {
  const r = await fetch('/api/stats')
  if (!r.ok) throw new Error(`stats: ${r.status}`)
  return r.json()
}

export async function getEvals(set?: string): Promise<EvalRun[]> {
  const q = set ? `?set=${encodeURIComponent(set)}` : ''
  const r = await fetch(`/api/evals${q}`)
  if (!r.ok) throw new Error(`evals: ${r.status}`)
  return r.json()
}

export async function getEvalRun(id: string): Promise<EvalRunDetail> {
  const r = await fetch(`/api/evals/${id}`)
  if (!r.ok) throw new Error(`eval ${id}: ${r.status}`)
  return r.json()
}

export type SetsInfo = { sets: string[]; aiConfigured: boolean; model: string }

export async function getSets(): Promise<SetsInfo> {
  const r = await fetch('/api/sets')
  if (!r.ok) throw new Error(`sets: ${r.status}`)
  return r.json()
}

export async function runEval(set: string, scorer: string): Promise<EvalRun> {
  const r = await fetch('/eval/run', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ set, scorer }),
  })
  if (!r.ok) throw new Error(`run failed: ${r.status}`)
  return r.json()
}

export async function getConfig(): Promise<Config> {
  const r = await fetch('/api/config')
  if (!r.ok) throw new Error(`config: ${r.status}`)
  return r.json()
}

export async function setAlerts(alerts: Alerts): Promise<Alerts> {
  const r = await fetch('/api/alerts', {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(alerts),
  })
  if (!r.ok) throw new Error(`alerts: ${r.status}`)
  return r.json()
}

export async function scoreTools(traceId: string, expected: string[]): Promise<ToolScoreResult> {
  const r = await fetch('/eval/tools', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ traceId, expected }),
  })
  if (!r.ok) throw new Error(`tool score: ${r.status}`)
  return r.json()
}

export async function getCost(): Promise<CostReport> {
  const r = await fetch('/api/cost')
  if (!r.ok) throw new Error(`cost: ${r.status}`)
  return r.json()
}

export async function setBudget(budget: Budget): Promise<Budget> {
  const r = await fetch('/api/budget', {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(budget),
  })
  if (!r.ok) throw new Error(`budget: ${r.status}`)
  return r.json()
}

export async function getSet(name: string): Promise<GoldenSet> {
  const r = await fetch(`/api/sets/${encodeURIComponent(name)}`)
  if (!r.ok) throw new Error(`set ${name}: ${r.status}`)
  return r.json()
}

export async function saveSet(name: string, cases: GoldenCase[]): Promise<GoldenSet> {
  const r = await fetch(`/api/sets/${encodeURIComponent(name)}`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name, cases }),
  })
  if (!r.ok) throw new Error(`save ${name}: ${r.status}`)
  return r.json()
}

export async function deleteSet(name: string): Promise<void> {
  const r = await fetch(`/api/sets/${encodeURIComponent(name)}`, { method: 'DELETE' })
  if (!r.ok && r.status !== 404) throw new Error(`delete ${name}: ${r.status}`)
}

export async function addCase(name: string, c: GoldenCase): Promise<GoldenSet> {
  const r = await fetch(`/api/sets/${encodeURIComponent(name)}/cases`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(c),
  })
  if (!r.ok) throw new Error(`add case: ${r.status}`)
  return r.json()
}

export async function compareModels(
  set: string,
  models: string[],
  scorer: string,
  promptPrefix?: string,
): Promise<CompareResult> {
  const r = await fetch('/eval/compare', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ set, models, scorer, promptPrefix: promptPrefix || null }),
  })
  if (!r.ok) throw new Error(`compare failed: ${r.status}`)
  return r.json()
}
