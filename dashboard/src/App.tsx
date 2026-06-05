import { useMemo, useState } from 'react'
import { TraceDetail } from './components/TraceDetail'
import { TraceList } from './components/TraceList'
import { dur, money } from './format'
import { useLive } from './useLive'

export default function App() {
  const { traces, latestId } = useLive()
  const [selectedId, setSelectedId] = useState<string | null>(null)

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
          <span className="muted">DevTools for AI calls</span>
        </div>
        <div className="topbar-stats">
          <span><b>{stats.count}</b> traces</span>
          <span><b>{money(stats.cost)}</b> total</span>
          <span><b>{dur(stats.avg)}</b> avg</span>
        </div>
      </header>

      <main className="layout">
        <aside className="sidebar">
          <TraceList
            traces={traces}
            selectedId={selectedId}
            latestId={latestId}
            onSelect={setSelectedId}
          />
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
