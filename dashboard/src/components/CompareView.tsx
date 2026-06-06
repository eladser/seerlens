import { useEffect, useMemo, useState } from 'react'
import { compareModels, getSets, type SetsInfo } from '../api'
import { dur, money } from '../format'
import type { CompareResult } from '../types'

type Scorer = 'keyword' | 'llm-judge'

// Run one golden set across a few models and an optional system prompt, then put
// quality, cost and latency side by side. The whole point: see the tradeoff before
// you switch models to save money.
export function CompareView() {
  const [info, setInfo] = useState<SetsInfo | null>(null)
  const [set, setSet] = useState('')
  const [models, setModels] = useState('')
  const [prompt, setPrompt] = useState('')
  const [scorer, setScorer] = useState<Scorer>('keyword')
  const [busy, setBusy] = useState(false)
  const [result, setResult] = useState<CompareResult | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    getSets().then(i => {
      setInfo(i)
      if (i.sets.length) setSet(i.sets[0])
      if (i.model) setModels(i.model)
    }).catch(() => {})
  }, [])

  const list = useMemo(
    () => models.split(',').map(m => m.trim()).filter(Boolean),
    [models],
  )

  async function run() {
    if (!set || list.length === 0 || busy) return
    setBusy(true)
    setErr(null)
    try {
      setResult(await compareModels(set, list, scorer, prompt))
    } catch {
      setErr('Compare failed. Check that the models exist on your provider.')
    } finally {
      setBusy(false)
    }
  }

  const best = useMemo(() => {
    const rows = result?.rows ?? []
    const priced = rows.filter(r => r.costUsd != null)
    return {
      score: Math.max(...rows.map(r => r.score), 0),
      cost: priced.length ? Math.min(...priced.map(r => r.costUsd!)) : null,
      latency: rows.length ? Math.min(...rows.map(r => r.avgLatencyMs)) : 0,
    }
  }, [result])

  return (
    <div className="compare">
      <div className="run-bar">
        <select value={set} onChange={e => setSet(e.target.value)}>
          {info?.sets.map(s => <option key={s} value={s}>{s}</option>)}
        </select>

        <input
          className="models-input"
          value={models}
          onChange={e => setModels(e.target.value)}
          placeholder="gpt-4o, gpt-4o-mini"
          spellCheck={false}
        />

        <div className="scorer-toggle">
          {(['keyword', 'llm-judge'] as Scorer[]).map(s => (
            <button key={s} className={scorer === s ? 'on' : ''} onClick={() => setScorer(s)}>{s}</button>
          ))}
        </div>

        <button className="run-btn" onClick={run} disabled={!info?.aiConfigured || !set || list.length === 0 || busy}>
          {busy ? 'running…' : 'Compare'}
        </button>

        {info && !info.aiConfigured && (
          <span className="muted run-note">set <code>SEERLENS_AI_*</code> to run from here</span>
        )}
      </div>

      <textarea
        className="prompt-input"
        value={prompt}
        onChange={e => setPrompt(e.target.value)}
        placeholder="Optional system prompt to put in front of every question (A/B a prompt across models)"
        rows={2}
      />

      {err && <p className="muted bad">{err}</p>}

      {result && result.rows.length > 0 ? (
        <table className="compare-table">
          <thead>
            <tr><th>model</th><th>score</th><th>cost / run</th><th>avg latency</th><th>tokens</th></tr>
          </thead>
          <tbody>
            {result.rows.map(r => (
              <tr key={r.model}>
                <td className="mono">{r.model}</td>
                <td className={'num ' + (r.score === best.score ? 'good' : '')}>{pct(r.score)}</td>
                <td className={'num ' + (r.costUsd != null && r.costUsd === best.cost ? 'good' : '')}>
                  {money(r.costUsd)}
                </td>
                <td className={'num ' + (r.avgLatencyMs === best.latency ? 'good' : '')}>{dur(r.avgLatencyMs)}</td>
                <td className="num muted">{r.tokens.toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className="empty">
          <p className="muted">Pick a set, list a couple of models, and Compare. Green marks the best in each column.</p>
        </div>
      )}
    </div>
  )
}

const pct = (s: number) => `${Math.round(s * 100)}%`
