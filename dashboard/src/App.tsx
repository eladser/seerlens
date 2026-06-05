import { useMemo, useState } from 'react'
import { CostBreakdown } from './components/CostBreakdown'
import { EvalsView } from './components/EvalsView'
import { TraceDetail } from './components/TraceDetail'
import { TraceList } from './components/TraceList'
import { dur, money } from './format'
import { useLive } from './useLive'

export default function App() {
  const { traces, latestId, connected } = useLive()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [view, setView] = useState<'traces' | 'evals'>('traces')

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
            <div className="sidebar-scroll">
              <TraceList
                traces={traces}
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
          <EvalsView />
        </main>
      )}
    </div>
  )
}
