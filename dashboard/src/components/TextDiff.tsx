import type { TextDiffResult, TextRow, WordPiece } from '../types'

function Words({ pieces, text }: { pieces?: WordPiece[] | null; text?: string | null }) {
  if (!pieces) return <>{text}</>
  return (
    <>
      {pieces.map((p, i) => p.t === 'eq'
        ? <span key={i}>{p.text}</span>
        : <mark key={i} className={'w-' + p.t}>{p.text}</mark>)}
    </>
  )
}

function Row({ r }: { r: TextRow }) {
  if (r.t === 'skip') {
    return <div className="td-skip">{r.skipped} unchanged line{r.skipped === 1 ? '' : 's'}</div>
  }
  const left = r.t !== 'ins'
  const right = r.t !== 'del'
  return (
    <div className={'td-row td-' + r.t}>
      <span className="td-ln">{left ? r.aLine : ''}</span>
      <span className={'td-cell' + (left ? '' : ' blank')}>
        {left && <Words pieces={r.aWords} text={r.a} />}
      </span>
      <span className="td-ln">{right ? r.bLine : ''}</span>
      <span className={'td-cell' + (right ? '' : ' blank')}>
        {right && <Words pieces={r.bWords} text={r.b} />}
      </span>
    </div>
  )
}

export function TextDiff({ diff }: { diff: TextDiffResult }) {
  if (diff.tooLarge) {
    return (
      <p className="muted td-note">
        Too large to diff here ({(diff.sizeA ?? 0).toLocaleString('en-US')} and {(diff.sizeB ?? 0).toLocaleString('en-US')} characters).
      </p>
    )
  }
  if (diff.rows.length === 0) return <p className="muted td-note">Empty on both sides.</p>
  return (
    <div className="td">
      <div className="td-head"><span>A</span><span>B</span></div>
      {diff.rows.map((r, i) => <Row key={i} r={r} />)}
    </div>
  )
}

export function RawText({ text }: { text: string | null | undefined }) {
  if (!text) return <p className="muted td-note">Nothing recorded.</p>
  return <pre className="td-raw">{text}</pre>
}
