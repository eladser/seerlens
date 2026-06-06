import { useEffect, useMemo, useState } from 'react'
import { getEvalRun, getEvals, getSets, runEval, type SetsInfo } from '../api'
import { downloadJson } from '../download'
import { ago } from '../format'
import type { EvalRun, EvalRunDetail } from '../types'
import { SetEditor } from './SetEditor'
import { TrendChart } from './TrendChart'

type Scorer = 'keyword' | 'llm-judge'
type Editing = { name: string | null } | null

export function EvalsView() {
  const [runs, setRuns] = useState<EvalRun[]>([])
  const [info, setInfo] = useState<SetsInfo | null>(null)
  const [set, setSet] = useState<string | null>(null)
  const [scorer, setScorer] = useState<Scorer>('keyword')
  const [running, setRunning] = useState(false)
  const [runId, setRunId] = useState<string | null>(null)
  const [detail, setDetail] = useState<EvalRunDetail | null>(null)
  const [editing, setEditing] = useState<Editing>(null)

  const load = () => getEvals().then(setRuns).catch(() => {})
  const loadSets = () => getSets().then(setInfo).catch(() => {})

  useEffect(() => { load() }, [])
  useEffect(() => { loadSets() }, [])

  // default the selected set once data arrives
  useEffect(() => {
    if (set) return
    if (runs.length) setSet(runs[0].set)
    else if (info?.sets.length) setSet(info.sets[0])
  }, [runs, info, set])

  useEffect(() => {
    if (!runId) return setDetail(null)
    let live = true
    getEvalRun(runId).then(d => live && setDetail(d)).catch(() => {})
    return () => { live = false }
  }, [runId])

  const allSets = useMemo(() => {
    const names = new Set<string>()
    runs.forEach(r => names.add(r.set))
    info?.sets.forEach(s => names.add(s))
    return [...names]
  }, [runs, info])

  const forSet = useMemo(() => runs.filter(r => r.set === set), [runs, set])

  async function run() {
    if (!set || running) return
    setRunning(true)
    try {
      await runEval(set, scorer)
      await load()
      setRunId(null)
    } catch {
      // surfaced by the disabled/idle state; nothing to do here
    } finally {
      setRunning(false)
    }
  }

  const first = forSet[0]
  const latest = forSet[forSet.length - 1]
  const delta = latest && first ? latest.score - first.score : 0

  if (editing !== null) {
    return (
      <div className="evals">
        <SetEditor
          name={editing.name}
          onClose={() => setEditing(null)}
          onSaved={name => {
            setEditing(null)
            loadSets()
            if (name) setSet(name)
          }}
        />
      </div>
    )
  }

  return (
    <div className="evals">
      <div className="run-bar">
        <select value={set ?? ''} onChange={e => { setSet(e.target.value); setRunId(null) }}>
          {allSets.map(s => <option key={s} value={s}>{s}</option>)}
        </select>

        <div className="scorer-toggle">
          {(['keyword', 'llm-judge'] as Scorer[]).map(s => (
            <button key={s} className={scorer === s ? 'on' : ''} onClick={() => setScorer(s)}>{s}</button>
          ))}
        </div>

        <button className="run-btn" onClick={run} disabled={!info?.aiConfigured || !set || running}>
          {running ? 'running…' : 'Run eval'}
        </button>

        <button className="ghost-btn" onClick={() => setEditing({ name: set })} disabled={!set}>Edit set</button>
        <button className="ghost-btn" onClick={() => setEditing({ name: null })}>New set</button>

        {info && (info.aiConfigured
          ? <span className="muted run-note">{info.model}</span>
          : <span className="muted run-note">set <code>SEERLENS_AI_*</code> to run from here</span>)}
      </div>

      {forSet.length === 0 ? (
        <div className="empty">
          <p className="muted">No runs yet for this set. Hit Run eval to score it.</p>
        </div>
      ) : (
        <>
          <div className="eval-head">
            <h2>{set}</h2>
            <span className="num">latest {pct(latest.score)}</span>
            {forSet.length > 1 && (
              <span className={'num ' + (delta < 0 ? 'bad' : 'good')}>
                {delta >= 0 ? '+' : ''}{pct(delta)} over time
              </span>
            )}
            {detail && (
              <button className="ghost-btn export" onClick={() => downloadJson(`eval-${detail.run.id}.json`, detail)}>
                Export run
              </button>
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
        </>
      )}
    </div>
  )
}

const pct = (s: number) => `${Math.round(s * 100)}%`
const scoreClass = (s: number) => (s >= 0.8 ? 'good' : s >= 0.5 ? 'mid' : 'bad')
