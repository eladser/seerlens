import { dur } from '../format'
import type { Span } from '../types'

type Props = {
  spans: Span[]
  traceStart: number
  traceDuration: number
  selectedId: string | null
  onSelect: (id: string) => void
}

export function Waterfall({ spans, traceStart, traceDuration, selectedId, onSelect }: Props) {
  const total = traceDuration || 1

  return (
    <div className="waterfall">
      <div className="wf-ruler">
        <div className="wf-track">
          <span className="tick" style={{ left: '0%' }}>0</span>
          <span className="tick mid" style={{ left: '50%' }}>{dur(total / 2)}</span>
          <span className="tick end" style={{ left: '100%' }}>{dur(total)}</span>
        </div>
      </div>

      {spans.map(s => {
        const left = ((s.startedAt - traceStart) / total) * 100
        const width = Math.max(1.5, (s.durationMs / total) * 100)
        return (
          <div
            key={s.id}
            className={'wf-row' + (s.id === selectedId ? ' selected' : '')}
            onClick={() => onSelect(s.id)}
          >
            <div className="wf-label">
              <span className={'kind kind-' + s.kind}>{s.kind}</span>
              <span className="wf-name">{s.name}</span>
            </div>
            <div className="wf-track gridded">
              <div
                className={'wf-bar kind-' + s.kind + (s.error ? ' bar-err' : '')}
                style={{ left: `${Math.min(left, 98.5)}%`, width: `${width}%` }}
              />
            </div>
            <span className="wf-dur num">{dur(s.durationMs)}</span>
          </div>
        )
      })}
    </div>
  )
}
