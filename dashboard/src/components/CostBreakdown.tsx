import { useMemo } from 'react'
import { dur, money } from '../format'
import type { TraceSummary } from '../types'

type Row = { name: string; cost: number; calls: number; tokens: number }

export function CostBreakdown({ traces }: { traces: TraceSummary[] }) {
  const { byProvider, byModel, total } = useMemo(() => groupSpend(traces), [traces])

  if (traces.length === 0) {
    return <div className="empty">Send some traces and the spend breakdown shows up here.</div>
  }

  const avg = traces.reduce((s, t) => s + t.durationMs, 0) / traces.length

  return (
    <div className="spend">
      <div className="spend-head">
        <Stat label="spent" value={money(total)} />
        <Stat label="calls" value={String(traces.length)} />
        <Stat label="avg latency" value={dur(avg)} />
      </div>

      <Section title="By provider" rows={byProvider} max={maxCost(byProvider)} />
      <Section title="By model" rows={byModel} max={maxCost(byModel)} />
    </div>
  )
}

function Section({ title, rows, max }: { title: string; rows: Row[]; max: number }) {
  if (rows.length === 0) return null
  return (
    <div className="spend-section">
      <h3>{title}</h3>
      {rows.map(r => (
        <div key={r.name} className="spend-row">
          <span className="spend-name">{r.name}</span>
          <div className="spend-bar-track">
            <div className="spend-bar" style={{ width: `${max ? (r.cost / max) * 100 : 0}%` }} />
          </div>
          <span className="spend-cost num">{money(r.cost)}</span>
          <span className="spend-meta muted">{r.calls} · {tokenLabel(r.tokens)}</span>
        </div>
      ))}
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className="stat">
      <span className="stat-label muted">{label}</span>
      <span className="stat-value">{value}</span>
    </div>
  )
}

function groupSpend(traces: TraceSummary[]) {
  const prov = new Map<string, Row>()
  const model = new Map<string, Row>()
  let total = 0

  for (const t of traces) {
    const cost = t.costUsd ?? 0
    const tokens = (t.promptTokens ?? 0) + (t.completionTokens ?? 0)
    total += cost
    add(prov, t.provider ?? 'unknown', cost, tokens)
    add(model, t.model ?? 'unknown', cost, tokens)
  }

  const sort = (m: Map<string, Row>) => [...m.values()].sort((a, b) => b.cost - a.cost)
  return { byProvider: sort(prov), byModel: sort(model), total }
}

function add(m: Map<string, Row>, name: string, cost: number, tokens: number) {
  const r = m.get(name) ?? { name, cost: 0, calls: 0, tokens: 0 }
  r.cost += cost
  r.calls += 1
  r.tokens += tokens
  m.set(name, r)
}

const maxCost = (rows: Row[]) => rows.reduce((m, r) => Math.max(m, r.cost), 0)
const tokenLabel = (n: number) => (n >= 1000 ? `${(n / 1000).toFixed(1)}k tok` : `${n} tok`)
