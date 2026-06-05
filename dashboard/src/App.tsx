import { useEffect, useMemo, useState } from 'react'
import { TraceDetail } from './components/TraceDetail'
import { TraceList } from './components/TraceList'
import { dur, money } from './format'
import { useLive } from './useLive'

export default function App() {
  const { traces, latestId, connected } = useLive()
  const [selectedId, setSelectedId] = useState<string | null>(null)

  // show the newest trace on first load instead of an empty pane
  useEffect(() => {
    if (!selectedId && traces.length) setSelectedId(traces[0].id)
  }, [traces, selectedId])

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
          <span className="logo">Seerlens</span>
          <span className={'live' + (connected ? ' on' : '')}>
            <span className="live-dot" />
            {connected ? 'live' : 'offline'}
          </span>
        </div>
        <div className="topbar-stats">
          <span><b>{stats.count}</b> traces</span>
          <span><b>{money(stats.cost)}</b> spent</span>
          <span><b>{dur(stats.avg)}</b> avg latency</span>
        </div>
      </header>

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
          {selectedId
            ? <TraceDetail traceId={selectedId} />
            : <div className="empty">Pick a trace to see what the model did.</div>}
        </section>
      </main>
    </div>
  )
}
