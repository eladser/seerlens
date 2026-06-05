import { ago, dur, money } from '../format'
import type { TraceSummary } from '../types'

type Props = {
  traces: TraceSummary[]
  selectedId: string | null
  latestId: string | null
  onSelect: (id: string) => void
}

export function TraceList({ traces, selectedId, latestId, onSelect }: Props) {
  if (traces.length === 0) {
    return (
      <div className="empty list-empty">
        <p>No traces yet.</p>
        <p className="muted">Run an app wired with the Seerlens SDK and calls show up here.</p>
      </div>
    )
  }

  return (
    <ul className="trace-list">
      {traces.map(t => (
        <li
          key={t.id}
          className={
            'trace-row' +
            (t.id === selectedId ? ' selected' : '') +
            (t.id === latestId ? ' flash' : '')
          }
          onClick={() => onSelect(t.id)}
        >
          <div className="row-top">
            <span className={'dot ' + (t.status === 'ok' ? 'ok' : 'err')} />
            <span className="row-name">{t.name}</span>
            <span className="row-time">{ago(t.startedAt)}</span>
          </div>
          <div className="row-bottom muted">
            {t.model && <span className="badge">{t.model}</span>}
            <span>{dur(t.durationMs)}</span>
            <span>{money(t.costUsd)}</span>
          </div>
        </li>
      ))}
    </ul>
  )
}
