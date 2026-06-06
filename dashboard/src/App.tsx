import { useMemo, useState } from 'react'
import { CompareView } from './components/CompareView'
import { CostBreakdown } from './components/CostBreakdown'
import { CostView } from './components/CostView'
import { EvalsView } from './components/EvalsView'
import { SettingsView } from './components/SettingsView'
import { TraceDetail } from './components/TraceDetail'
import { TraceList } from './components/TraceList'
import { dur, money } from './format'
import { useLive } from './useLive'

type View = 'traces' | 'evals' | 'compare' | 'cost' | 'settings'

export default function App() {
  const { traces, latestId, connected } = useLive()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [view, setView] = useState<View>('traces')

  const [q, setQ] = useState('')
  const [errorsOnly, setErrorsOnly] = useState(false)

  const shown = useMemo(() => {
    const needle = q.trim().toLowerCase()
    return traces.filter(t => {
      if (errorsOnly && t.status === 'ok') return false
      if (!needle) return true
      return (
        t.name.toLowerCase().includes(needle) ||
        (t.model ?? '').toLowerCase().includes(needle) ||
        (t.provider ?? '').toLowerCase().includes(needle)
      )
    })
  }, [traces, q, errorsOnly])

  const stats = useMemo(() => {
    const cost = traces.reduce((sum, t) => sum + (t.costUsd ?? 0), 0)
    const avg = traces.length
      ? traces.reduce((sum, t) => sum + t.durationMs, 0) / traces.length
      : 0
    return { count: traces.length, cost, avg }
  }, [traces])

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <img className="logo-mark" src="/favicon.png" alt="" width="22" height="22" />
          <span className="logo">Seerlens</span>
          <span className={'live' + (connected ? ' on' : '')}>
            <span className="live-dot" />
            {connected ? 'live' : 'offline'}
          </span>
        </div>
        <nav className="nav">
          <button className={view === 'traces' ? 'on' : ''} onClick={() => setView('traces')}>Traces</button>
          <button className={view === 'evals' ? 'on' : ''} onClick={() => setView('evals')}>Evals</button>
          <button className={view === 'compare' ? 'on' : ''} onClick={() => setView('compare')}>Compare</button>
          <button className={view === 'cost' ? 'on' : ''} onClick={() => setView('cost')}>Cost</button>
          <button className={view === 'settings' ? 'on' : ''} onClick={() => setView('settings')}>Settings</button>
        </nav>
        {view === 'traces' && (
          <div className="topbar-stats">
            <span><b>{stats.count}</b> traces</span>
            <span><b>{money(stats.cost)}</b> spent</span>
            <span><b>{dur(stats.avg)}</b> avg latency</span>
          </div>
        )}
      </header>

      {view === 'traces' ? (
        <main className="layout">
          <aside className="sidebar">
            <div className="trace-filter">
              <input
                value={q}
                onChange={e => setQ(e.target.value)}
                placeholder="filter by name, model, provider"
                spellCheck={false}
              />
              <button
                className={'filter-toggle' + (errorsOnly ? ' on' : '')}
                onClick={() => setErrorsOnly(v => !v)}
                title="show only failed calls"
              >
                errors
              </button>
            </div>
            <div className="sidebar-scroll">
              <TraceList
                traces={shown}
                selectedId={selectedId}
                latestId={latestId}
                onSelect={setSelectedId}
              />
            </div>
            <footer className="sidebar-footer">
              <a href="https://github.com/eladser" target="_blank" rel="noreferrer">eladser</a>
              <a href="https://ko-fi.com/eladser" target="_blank" rel="noreferrer">ko-fi</a>
            </footer>
          </aside>
          <section className="content">
            {selectedId ? (
              <>
                <button className="back-link" onClick={() => setSelectedId(null)}>← spend overview</button>
                <TraceDetail traceId={selectedId} />
              </>
            ) : (
              <CostBreakdown traces={traces} />
            )}
          </section>
        </main>
      ) : (
        <main className="content eval-main">
          {view === 'evals' && <EvalsView />}
          {view === 'compare' && <CompareView />}
          {view === 'cost' && <CostView />}
          {view === 'settings' && <SettingsView />}
        </main>
      )}
    </div>
  )
}
