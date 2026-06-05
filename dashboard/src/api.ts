import type { EvalRun, EvalRunDetail, Stats, TraceDetail, TraceSummary } from './types'

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
