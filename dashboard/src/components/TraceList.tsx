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
        <p className="empty-title">Waiting for traces</p>
        <p className="muted">
          Point an app at this collector with the Seerlens SDK and its AI calls land here, live.
        </p>
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
          <span className={'mono-mark prov-' + (t.provider ?? 'none')}>
            {mark(t.provider, t.model)}
          </span>
          <div className="row-body">
            <div className="row-top">
              <span className="row-name">{t.name}</span>
              <span className="row-time">{ago(t.startedAt)}</span>
            </div>
            <div className="row-meta">
              <span className={'dot ' + (t.status === 'ok' ? 'ok' : 'err')} />
              <span className="num">{dur(t.durationMs)}</span>
              <span className="num">{money(t.costUsd)}</span>
            </div>
          </div>
        </li>
      ))}
    </ul>
  )
}

function mark(provider: string | null, model: string | null): string {
  const src = provider ?? model ?? '?'
  return src[0]!.toUpperCase()
}
