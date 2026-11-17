import type { DiffStep, Span, TraceSummary } from './types'

const n = (v: number | null | undefined) => v ?? 0

// Cache reads over prompt size. Counts that sit beside the input (cache > input) are folded
// in first, same as the collector, so the rate can't pass 100%.
export function cacheHit(input: number | null | undefined, read: number | null | undefined, write: number | null | undefined): number | null {
  if (read == null) return null
  const prompt = read + n(write) > n(input) ? n(input) + read + n(write) : n(input)
  return prompt > 0 ? read / prompt : null
}

export function pct(v: number | null | undefined): string {
  return v == null ? '-' : `${Math.round(v * 100)}%`
}

export function tokenText(
  input: number | null | undefined,
  output: number | null | undefined,
  cached?: number | null,
  reasoning?: number | null,
): string {
  if (input == null && output == null) return 'n/a'
  const c = cached ? ` (${cached.toLocaleString('en-US')} cached)` : ''
  const r = reasoning ? ` (${reasoning.toLocaleString('en-US')} reasoning)` : ''
  return `${n(input).toLocaleString('en-US')} in${c} / ${n(output).toLocaleString('en-US')} out${r}`
}

export function spanCache(s: Span): number | null {
  return cacheHit(s.promptTokens, s.cacheReadTokens, s.cacheWriteTokens)
}

// Rate over the llm spans that reported cache reads, plus total reasoning tokens.
export function traceCache(spans: Span[]): { rate: number | null; read: number; reasoning: number } {
  let read = 0, prompt = 0, reasoning = 0, seen = false
  for (const s of spans) {
    if (s.kind !== 'llm') continue
    reasoning += n(s.reasoningTokens)
    if (s.cacheReadTokens == null) continue
    seen = true
    const w = n(s.cacheWriteTokens), i = n(s.promptTokens)
    read += s.cacheReadTokens
    prompt += s.cacheReadTokens + w > i ? i + s.cacheReadTokens + w : i
  }
  return { rate: seen && prompt > 0 ? read / prompt : null, read, reasoning }
}

export function hasLlmBelow(spans: Span[], span: Span): boolean {
  if (span.kind !== 'agent') return false
  const stack = spans.filter(s => s.parentId === span.id)
  while (stack.length) {
    const s = stack.pop()!
    if (s.kind === 'llm') return true
    stack.push(...spans.filter(c => c.parentId === s.id))
  }
  return false
}

export type Tone = 'good' | 'bad' | ''
type Delta = { text: string; tone: Tone }

const sign = (d: number) => (d > 0 ? '+' : '-')
const rel = (a: number, d: number) => (a > 0 ? ` (${sign(d)}${Math.round(Math.abs(d / a) * 100)}%)` : '')

// lower: a rise is bad (cost, latency, errors). plain: just a number, no color.
export function delta(a: number | null | undefined, b: number | null | undefined, lower: boolean): Delta {
  if (a == null || b == null) return { text: '', tone: '' }
  const d = b - a
  if (d === 0) return { text: '=', tone: '' }
  return { text: `${sign(d)}${Math.abs(d).toLocaleString('en-US', { maximumFractionDigits: 0 })}${rel(a, d)}`, tone: lower ? (d > 0 ? 'bad' : 'good') : '' }
}

export function deltaMoney(a: number | null, b: number | null): Delta {
  if (a == null || b == null) return { text: '', tone: '' }
  const d = b - a
  if (Math.abs(d) < 1e-9) return { text: '=', tone: '' }
  const m = Math.abs(d)
  return { text: `${sign(d)}$${m < 0.01 ? m.toFixed(6) : m.toFixed(4)}${rel(a, d)}`, tone: d > 0 ? 'bad' : 'good' }
}

export function deltaMs(a: number, b: number): Delta {
  const d = b - a
  if (Math.abs(d) < 0.5) return { text: '=', tone: '' }
  const m = Math.abs(d)
  return { text: `${sign(d)}${m < 1000 ? Math.round(m) + ' ms' : (m / 1000).toFixed(2) + ' s'}${rel(a, d)}`, tone: d > 0 ? 'bad' : 'good' }
}

export function deltaPoints(a: number | null, b: number | null): string {
  if (a == null || b == null) return ''
  const d = Math.round((b - a) * 100)
  return d === 0 ? '=' : `${sign(d)}${Math.abs(d)} pts`
}

export type Mark = { name: string; status: 'same' | 'removed' | 'added' }

// Tool path for one side, flagging the calls the other side doesn't have.
export function pathMarks(steps: DiffStep[], side: 'a' | 'b'): Mark[] {
  const out: Mark[] = []
  for (const s of steps) {
    if ((s.kind !== 'tool' && s.kind !== 'mcp') || !s[side]) continue
    out.push({ name: s.name, status: s.status === 'removed' ? 'removed' : s.status === 'added' ? 'added' : 'same' })
  }
  return out
}

export function counts(steps: DiffStep[]): Record<DiffStep['status'], number> {
  const c = { same: 0, changed: 0, removed: 0, added: 0 }
  for (const s of steps) c[s.status]++
  return c
}

// The older of two picked traces is A.
export function pickPair(ids: string[], traces: TraceSummary[]): [string, string] | null {
  if (ids.length !== 2) return null
  const at = (id: string) => traces.find(t => t.id === id)?.startedAt ?? 0
  return at(ids[0]) <= at(ids[1]) ? [ids[0], ids[1]] : [ids[1], ids[0]]
}

// Newest trace with the same name that started before this one.
export function previousTrace(traces: TraceSummary[], t: TraceSummary): TraceSummary | null {
  let best: TraceSummary | null = null
  for (const o of traces) {
    if (o.id === t.id || o.name !== t.name || o.startedAt >= t.startedAt) continue
    if (!best || o.startedAt > best.startedAt) best = o
  }
  return best
}

export function togglePick(ids: string[], id: string): string[] {
  if (ids.includes(id)) return ids.filter(x => x !== id)
  return ids.length >= 2 ? [ids[1], id] : [...ids, id]
}
