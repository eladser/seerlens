import { useEffect, useState } from 'react'
import { getCost } from '../api'
import { money } from '../format'
import type { CostReport } from '../types'

// Cost you can act on: month-to-date against the budget set in Settings, an alert
// when you cross it, plus where the money goes by model and by day.
export function CostView() {
  const [report, setReport] = useState<CostReport | null>(null)

  useEffect(() => { getCost().then(setReport).catch(() => {}) }, [])

  if (!report) return <div className="empty">Loading spend…</div>

  const { spend, budget, overBudget, budgetUsedFraction } = report
  const frac = budgetUsedFraction ?? 0
  const near = !overBudget && frac >= 0.8
  const maxModel = Math.max(...spend.byModel.map(m => m.costUsd), 0)
  const maxDay = Math.max(...spend.daily.map(d => d.costUsd), 0)

  return (
    <div className="cost">
      <div className="spend-head">
        <Stat label="this month" value={money(spend.monthToDateUsd)} />
        <Stat label="all time" value={money(spend.totalUsd)} />
        {budget.monthlyUsd != null && <Stat label="budget" value={money(budget.monthlyUsd)} />}
      </div>

      {overBudget && (
        <div className="alert over">Over budget. {money(spend.monthToDateUsd)} spent against a {money(budget.monthlyUsd)} cap.</div>
      )}
      {near && (
        <div className="alert near">{Math.round(frac * 100)}% of this month's budget used.</div>
      )}

      {budget.monthlyUsd != null && (
        <div className="budget-track">
          <div
            className={'budget-bar' + (overBudget ? ' over' : near ? ' near' : '')}
            style={{ width: `${Math.min(frac * 100, 100)}%` }}
          />
        </div>
      )}

      {budget.monthlyUsd == null && (
        <p className="muted hint">Set a monthly budget in Settings to track spend against a cap.</p>
      )}

      <div className="spend-section">
        <h3>By model</h3>
        {spend.byModel.length === 0 && <p className="muted">No spend yet.</p>}
        {spend.byModel.map(m => (
          <div key={m.model} className="spend-row">
            <span className="spend-name mono">{m.model}</span>
            <div className="spend-bar-track">
              <div className="spend-bar" style={{ width: `${maxModel ? (m.costUsd / maxModel) * 100 : 0}%` }} />
            </div>
            <span className="spend-cost num">{money(m.costUsd)}</span>
            <span className="spend-meta muted">{m.calls} calls</span>
          </div>
        ))}
      </div>

      {spend.daily.length > 0 && (
        <div className="spend-section">
          <h3>Last 14 days</h3>
          <div className="day-bars">
            {spend.daily.map(d => (
              <div key={d.date} className="day-col" title={`${d.date}: ${money(d.costUsd)}`}>
                <div className="day-bar" style={{ height: `${maxDay ? Math.max((d.costUsd / maxDay) * 100, 2) : 2}%` }} />
                <span className="day-label">{d.date.slice(5)}</span>
              </div>
            ))}
          </div>
        </div>
      )}
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
