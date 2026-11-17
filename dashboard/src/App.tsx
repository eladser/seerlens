import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { clearTraces } from './api'
import { CompareView } from './components/CompareView'
import { CostBreakdown } from './components/CostBreakdown'
import { CostView } from './components/CostView'
import { DiffView } from './components/DiffView'
import { EvalsView } from './components/EvalsView'
import { SettingsView } from './components/SettingsView'
import { TraceDetail } from './components/TraceDetail'
import { TraceList } from './components/TraceList'
import { pickPair, togglePick } from './diff'
import { dur, money } from './format'
import { parseRoute, pushDiff } from './route'
import { useLive } from './useLive'

type View = 'traces' | 'evals' | 'compare' | 'cost' | 'settings' | 'diff'

const startRoute = () => parseRoute(location.pathname, location.search)

export default function App() {
  const { traces, latestId, connected, clear } = useLive()
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [view, setView] = useState<View>(() => (startRoute() ? 'diff' : 'traces'))
  const [refs, setRefs] = useState(() => startRoute() ?? { a: '', b: '' })
  const [picking, setPicking] = useState(false)
  const [picked, setPicked] = useState<string[]>([])
  const back = useRef<View>('traces')
  const inApp = useRef(false)

  const [q, setQ] = useState('')
  const [errorsOnly, setErrorsOnly] = useState(false)

  useEffect(() => {
    const onPop = () => {
      const r = startRoute()
      if (r) {
        setRefs(r)
        setView('diff')
      } else {
        setView(v => (v === 'diff' ? back.current : v))
      }
    }
    window.addEventListener('popstate', onPop)
    return () => window.removeEventListener('popstate', onPop)
  }, [])

  const openDiff = useCallback((a: string, b: string) => {
    if (view !== 'diff') back.current = view
    inApp.current = true
    pushDiff(a, b)
    setRefs({ a, b })
    setView('diff')
    setPicking(false)
    setPicked([])
  }, [view])

  const swap = () => {
    pushDiff(refs.b, refs.a, true)
    setRefs({ a: refs.b, b: refs.a })
  }

  const closeDiff = () => {
    if (inApp.current) {
      inApp.current = false
      history.back()
    } else {
      history.pushState(null, '', '/')
      setView(back.current)
    }
  }

  const goto = (v: View) => {
    if (view === 'diff') {
      inApp.current = false
      history.pushState(null, '', '/')
    }
    setView(v)
  }

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
          <button className={view === 'traces' ? 'on' : ''} onClick={() => goto('traces')}>Traces</button>
          <button className={view === 'evals' ? 'on' : ''} onClick={() => goto('evals')}>Evals</button>
          <button className={view === 'compare' ? 'on' : ''} onClick={() => goto('compare')}>Compare</button>
          <button className={view === 'cost' ? 'on' : ''} onClick={() => goto('cost')}>Cost</button>
          <button className={view === 'settings' ? 'on' : ''} onClick={() => goto('settings')}>Settings</button>
          {view === 'diff' && <button className="on">Diff</button>}
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
              <button
                className={'filter-toggle' + (picking ? ' on' : '')}
                onClick={() => { setPicking(v => !v); setPicked([]) }}
                title="pick two traces to diff"
              >
                diff
              </button>
              <button
                className="filter-toggle"
                title="delete all traces"
                onClick={async () => {
                  if (!confirm('Delete all traces?')) return
                  await clearTraces().catch(() => {})
                  clear()
                  setSelectedId(null)
                }}
              >
                clear
              </button>
            </div>
            {picking && (
              <div className="pick-bar">
                <span className="muted">
                  {picked.length === 2 ? 'older run is A' : `pick ${2 - picked.length} more`}
                </span>
                <button
                  className="run-btn"
                  disabled={picked.length !== 2}
                  onClick={() => {
                    const p = pickPair(picked, traces)
                    if (p) openDiff(p[0], p[1])
                  }}
                >
                  Diff 2 runs
                </button>
              </div>
            )}
            <div className="sidebar-scroll">
              <TraceList
                traces={shown}
                selectedId={selectedId}
                latestId={latestId}
                onSelect={setSelectedId}
                picking={picking}
                picked={picked}
                onPick={id => setPicked(p => togglePick(p, id))}
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
                <TraceDetail traceId={selectedId} traces={traces} onDiff={openDiff} />
              </>
            ) : (
              <CostBreakdown traces={traces} />
            )}
          </section>
        </main>
      ) : (
        <main className="content eval-main">
          {view === 'evals' && <EvalsView onDiff={openDiff} />}
          {view === 'compare' && <CompareView />}
          {view === 'cost' && <CostView />}
          {view === 'settings' && <SettingsView />}
          {view === 'diff' && <DiffView a={refs.a} b={refs.b} onSwap={swap} onClose={closeDiff} />}
        </main>
      )}
    </div>
  )
}
