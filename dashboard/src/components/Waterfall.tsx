import { useMemo } from 'react'
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
  // Lay the spans out as a tree by parent, depth-first, so an agent run reads as
  // nested steps (the model call, the tool calls under it, retries) instead of a
  // flat list. Depth drives the indent.
  const ordered = useMemo(() => tree(spans), [spans])

  return (
    <div className="waterfall">
      <div className="wf-ruler">
        <div className="wf-track">
          <span className="tick" style={{ left: '0%' }}>0</span>
          <span className="tick mid" style={{ left: '50%' }}>{dur(total / 2)}</span>
          <span className="tick end" style={{ left: '100%' }}>{dur(total)}</span>
        </div>
      </div>

      {ordered.map(({ span: s, depth }) => {
        const left = Math.min(((s.startedAt - traceStart) / total) * 100, 98.5)
        const width = Math.max(1.5, Math.min((s.durationMs / total) * 100, 100 - left))
        return (
          <div
            key={s.id}
            className={'wf-row' + (s.id === selectedId ? ' selected' : '')}
            onClick={() => onSelect(s.id)}
          >
            <div className="wf-label" style={{ paddingLeft: depth * 14 }}>
              <span className={'kind kind-' + s.kind}>{s.kind}</span>
              <span className="wf-name">{s.name}</span>
            </div>
            <div className="wf-track gridded">
              <div
                className={'wf-bar kind-' + s.kind + (s.error ? ' bar-err' : '')}
                style={{ left: `${left}%`, width: `${width}%` }}
              />
            </div>
            <span className="wf-dur num">{dur(s.durationMs)}</span>
          </div>
        )
      })}
    </div>
  )
}

type Node = { span: Span; depth: number }

function tree(spans: Span[]): Node[] {
  const byParent = new Map<string | null, Span[]>()
  const ids = new Set(spans.map(s => s.id))
  for (const s of spans) {
    // treat a span whose parent isn't in this trace as a root
    const key = s.parentId && ids.has(s.parentId) ? s.parentId : null
    const list = byParent.get(key) ?? []
    list.push(s)
    byParent.set(key, list)
  }
  for (const list of byParent.values()) list.sort((a, b) => a.startedAt - b.startedAt)

  const out: Node[] = []
  const walk = (parent: string | null, depth: number) => {
    for (const s of byParent.get(parent) ?? []) {
      out.push({ span: s, depth })
      walk(s.id, depth + 1)
    }
  }
  walk(null, 0)
  return out
}
