import type { EvalRun } from '../types'

type Props = {
  runs: EvalRun[] // chronological
  selectedId: string | null
  onSelect: (id: string) => void
}

const W = 620
const H = 190
const PAD = { left: 38, right: 16, top: 16, bottom: 26 }

export function TrendChart({ runs, selectedId, onSelect }: Props) {
  const plotW = W - PAD.left - PAD.right
  const plotH = H - PAD.top - PAD.bottom

  const x = (i: number) =>
    PAD.left + (runs.length <= 1 ? plotW / 2 : (i / (runs.length - 1)) * plotW)
  const y = (score: number) => PAD.top + (1 - score) * plotH

  const points = runs.map((r, i) => ({ run: r, cx: x(i), cy: y(r.score), i }))
  const line = points.map(p => `${p.cx},${p.cy}`).join(' ')

  return (
    <svg className="trend" viewBox={`0 0 ${W} ${H}`} role="img" aria-label="score over time">
      {[0, 0.5, 1].map(g => (
        <g key={g}>
          <line className="grid-line" x1={PAD.left} x2={W - PAD.right} y1={y(g)} y2={y(g)} />
          <text className="grid-label" x={PAD.left - 8} y={y(g) + 3} textAnchor="end">
            {g * 100}
          </text>
        </g>
      ))}

      <polyline className="trend-line" points={line} fill="none" />

      {points.map(p => {
        const prev = points[p.i - 1]
        const dropped = prev && p.run.score < prev.run.score - 0.05
        const selected = p.run.id === selectedId
        return (
          <circle
            key={p.run.id}
            cx={p.cx}
            cy={p.cy}
            r={selected ? 6 : 4}
            className={'trend-dot' + (dropped ? ' drop' : '') + (selected ? ' selected' : '')}
            onClick={() => onSelect(p.run.id)}
          />
        )
      })}
    </svg>
  )
}
