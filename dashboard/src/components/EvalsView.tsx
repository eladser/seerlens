import { useEffect, useMemo, useState } from 'react'
import { getEvalRun, getEvals } from '../api'
import { ago } from '../format'
import type { EvalRun, EvalRunDetail } from '../types'
import { TrendChart } from './TrendChart'

export function EvalsView() {
  const [runs, setRuns] = useState<EvalRun[]>([])
  const [set, setSet] = useState<string | null>(null)
  const [runId, setRunId] = useState<string | null>(null)
  const [detail, setDetail] = useState<EvalRunDetail | null>(null)

  useEffect(() => {
    getEvals()
      .then(rs => {
        setRuns(rs)
        if (rs.length) setSet(rs[0].set)
      })
      .catch(() => {})
  }, [])

  const sets = useMemo(() => [...new Set(runs.map(r => r.set))], [runs])
  const forSet = useMemo(() => runs.filter(r => r.set === set), [runs, set])

  useEffect(() => {
    if (!runId) return setDetail(null)
    let live = true
    getEvalRun(runId).then(d => live && setDetail(d)).catch(() => {})
    return () => { live = false }
  }, [runId])

  if (runs.length === 0) {
    return (
      <div className="empty">
        <p className="empty-title">No eval runs yet</p>
        <p className="muted">Score a golden set against your prompts and the trend over time shows up here.</p>
      </div>
    )
  }

  const first = forSet[0]
  const latest = forSet[forSet.length - 1]
  const delta = latest && first ? latest.score - first.score : 0

  return (
    <div className="evals">
      {sets.length > 1 && (
        <div className="set-tabs">
          {sets.map(s => (
            <button
              key={s}
              className={'set-tab' + (s === set ? ' on' : '')}
              onClick={() => { setSet(s); setRunId(null) }}
            >
              {s}
            </button>
          ))}
        </div>
      )}

      <div className="eval-head">
        <h2>{set}</h2>
        <span className="muted">{forSet.length} runs</span>
        {latest && <span className="num">latest {pct(latest.score)}</span>}
        {forSet.length > 1 && (
          <span className={'num ' + (delta < 0 ? 'bad' : 'good')}>
            {delta >= 0 ? '+' : ''}{pct(delta)} over time
          </span>
        )}
      </div>

      <TrendChart runs={forSet} selectedId={runId} onSelect={setRunId} />

      <table className="runs">
        <tbody>
          {[...forSet].reverse().map(r => (
            <tr key={r.id} className={r.id === runId ? 'selected' : ''} onClick={() => setRunId(r.id)}>
              <td className={'num ' + scoreClass(r.score)}>{pct(r.score)}</td>
              <td>{r.target}</td>
              <td className="muted">{r.scorer}</td>
              <td className="muted">{r.caseCount} cases</td>
              <td className="muted">{ago(r.createdAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>

      {detail && (
        <div className="eval-cases">
          {detail.cases.map((c, i) => (
            <div key={i} className="eval-case">
              <div className="case-top">
                <span className={'case-score ' + scoreClass(c.score)}>{pct(c.score)}</span>
                <span className="case-input">{c.input}</span>
              </div>
              <pre>{c.answer}</pre>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

const pct = (s: number) => `${Math.round(s * 100)}%`
const scoreClass = (s: number) => (s >= 0.8 ? 'good' : s >= 0.5 ? 'mid' : 'bad')
