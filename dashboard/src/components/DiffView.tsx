import { useEffect, useMemo, useState } from 'react'
import { ApiError, getDiff, getStepDiff, getStepRaw } from '../api'
import {
  cacheHit, counts, delta, deltaMoney, deltaMs, deltaPoints, pathMarks, pct, tokenText,
  type Mark, type Tone,
} from '../diff'
import { ago, dur, money } from '../format'
import type { DiffResponse, DiffStep, RunTotals, StepDiff, StepRaw, StepView, TraceSummary } from '../types'
import { RawText, TextDiff } from './TextDiff'

type Props = {
  a: string
  b: string
  onSwap: () => void
  onClose: () => void
}

type Loaded = { key: string; data?: DiffResponse; err?: ApiError | Error }

export function DiffView({ a, b, onSwap, onClose }: Props) {
  const key = a + '\n' + b
  const [res, setRes] = useState<Loaded | null>(null)
  const [onlyChanges, setOnlyChanges] = useState(false)

  useEffect(() => {
    if (!a || !b) return
    let live = true
    getDiff(a, b)
      .then(data => live && setRes({ key, data }))
      .catch(err => live && setRes({ key, err }))
    return () => { live = false }
  }, [a, b, key])

  if (!a || !b) {
    return (
      <div className="diff solo">
        <div className="empty">
          <p className="empty-title">Pick two runs to diff</p>
          <p className="muted">Use the diff toggle in the trace list, or "Diff with previous" on a trace.</p>
          <button className="ghost-btn" onClick={onClose}>Back to traces</button>
        </div>
      </div>
    )
  }

  if (!res || res.key !== key) return <DiffSkeleton />
  if (!res.data) return <DiffError err={res.err!} onClose={onClose} />

  const d = res.data
  const c = counts(d.steps)
  const shown = onlyChanges ? d.steps.filter(s => s.status !== 'same') : d.steps
  const firstChanged = d.steps.findIndex(s => s.status === 'changed')

  return (
    <div className="diff">
      <header className="diff-head">
        <Side tag="A" t={d.a} />
        <button className="ghost-btn swap" onClick={onSwap} title="swap A and B">⇄ swap</button>
        <Side tag="B" t={d.b} />
        <button className="ghost-btn close" onClick={onClose}>Close</button>
      </header>

      <Totals a={d.totalsA} b={d.totalsB} />
      <ToolPath d={d} />

      <div className="diff-steps-head">
        <h3>Steps</h3>
        <span className="muted diff-counts">
          {d.steps.length} · {c.changed} changed · {c.added} added · {c.removed} removed · {c.same} same
        </span>
        {c.same > 0 && (
          <button
            className={'filter-toggle' + (onlyChanges ? ' on' : '')}
            onClick={() => setOnlyChanges(v => !v)}
          >
            hide same
          </button>
        )}
      </div>

      {d.truncated && <p className="muted diff-note">One side has more than 2,000 steps. Only the first 2,000 are lined up.</p>}

      {d.steps.length === 0 ? (
        <p className="muted diff-note">Neither run has any LLM or tool steps.</p>
      ) : (
        <div className="diff-steps">
          {shown.map(s => (
            <StepRow key={(s.a?.spanId ?? '') + '|' + (s.b?.spanId ?? '')} a={a} b={b} step={s} inline={d.steps.indexOf(s) === firstChanged} />
          ))}
        </div>
      )}
    </div>
  )
}

function Side({ tag, t }: { tag: string; t: TraceSummary }) {
  return (
    <div className="diff-side">
      <span className={'side-tag side-' + tag.toLowerCase()}>{tag}</span>
      <div className="diff-side-body">
        <span className="diff-name">{t.name}</span>
        <span className="muted diff-sub">
          {ago(t.startedAt)}
          {t.model && <> · <span className="mono">{t.model}</span></>}
          {t.status !== 'ok' && <> · <span className="bad">{t.status}</span></>}
        </span>
      </div>
    </div>
  )
}

type TRow = { label: string; a: string; b: string; d: string; tone: Tone }

function totalRows(a: RunTotals, b: RunTotals): TRow[] {
  const num = (x: number) => x.toLocaleString('en-US')
  const rows: TRow[] = []
  const add = (label: string, av: string, bv: string, dl: { text: string; tone: Tone } | string) => {
    const x = typeof dl === 'string' ? { text: dl, tone: '' as Tone } : dl
    rows.push({ label, a: av, b: bv, d: x.text, tone: x.tone })
  }
  add('duration', dur(a.durationMs), dur(b.durationMs), deltaMs(a.durationMs, b.durationMs))
  add('cost', money(a.costUsd), money(b.costUsd), deltaMoney(a.costUsd, b.costUsd))
  add('input tokens', num(a.inputTokens), num(b.inputTokens), delta(a.inputTokens, b.inputTokens, false))
  add('output tokens', num(a.outputTokens), num(b.outputTokens), delta(a.outputTokens, b.outputTokens, false))
  if (a.cacheReadTokens + b.cacheReadTokens > 0)
    add('cache read', num(a.cacheReadTokens), num(b.cacheReadTokens), delta(a.cacheReadTokens, b.cacheReadTokens, false))
  if (a.cacheWriteTokens + b.cacheWriteTokens > 0)
    add('cache write', num(a.cacheWriteTokens), num(b.cacheWriteTokens), delta(a.cacheWriteTokens, b.cacheWriteTokens, false))
  if (a.cacheHitRate != null || b.cacheHitRate != null)
    add('cache hit rate', pct(a.cacheHitRate), pct(b.cacheHitRate), deltaPoints(a.cacheHitRate, b.cacheHitRate))
  if (a.reasoningTokens + b.reasoningTokens > 0)
    add('reasoning tokens', num(a.reasoningTokens), num(b.reasoningTokens), delta(a.reasoningTokens, b.reasoningTokens, false))
  add('steps', num(a.steps), num(b.steps), delta(a.steps, b.steps, false))
  add('tool calls', num(a.toolCalls), num(b.toolCalls), delta(a.toolCalls, b.toolCalls, false))
  add('errors', num(a.errors), num(b.errors), delta(a.errors, b.errors, true))
  const ma = a.models.join(', ') || '-'
  const mb = b.models.join(', ') || '-'
  add('models', ma, mb, ma === mb ? '=' : 'changed')
  return rows
}

function Totals({ a, b }: { a: RunTotals; b: RunTotals }) {
  const rows = useMemo(() => totalRows(a, b), [a, b])
  return (
    <table className="compare-table diff-totals">
      <thead>
        <tr><th></th><th>A</th><th>B</th><th>change</th></tr>
      </thead>
      <tbody>
        {rows.map(r => (
          <tr key={r.label}>
            <td className="muted">{r.label}</td>
            <td className={'num' + (r.label === 'models' ? ' wrap' : '')}>{r.a}</td>
            <td className={'num' + (r.label === 'models' ? ' wrap' : '')}>{r.b}</td>
            <td className={'num ' + r.tone}>{r.d}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}

function Path({ tag, marks, names }: { tag: 'A' | 'B'; marks: Mark[]; names: string[] }) {
  const list: Mark[] = marks.length === names.length ? marks : names.map(name => ({ name, status: 'same' }))
  return (
    <div className="path-row">
      <span className={'side-tag side-' + tag.toLowerCase()}>{tag}</span>
      {list.length === 0 && <span className="muted">no tool calls</span>}
      <div className="tool-seq">
        {list.map((m, i) => (
          <span key={i} className={'tool-step path-' + m.status}>
            {m.name}
            {i < list.length - 1 && <span className="tool-arrow">→</span>}
          </span>
        ))}
      </div>
    </div>
  )
}

function ToolPath({ d }: { d: DiffResponse }) {
  if (d.toolsA.length === 0 && d.toolsB.length === 0) return null
  return (
    <div className="diff-path">
      <h3>Tool path</h3>
      <Path tag="A" marks={pathMarks(d.steps, 'a')} names={d.toolsA} />
      <Path tag="B" marks={pathMarks(d.steps, 'b')} names={d.toolsB} />
    </div>
  )
}

function Pair({ a, b, fmt }: { a: string | null; b: string | null; fmt?: string }) {
  if (a != null && b != null && a !== b) return <span className="num">{a}<i className="to">→</i>{b}</span>
  return <span className={'num ' + (fmt ?? '')}>{b ?? a ?? '-'}</span>
}

const tok = (v: StepView | null) => (v && (v.promptTokens != null || v.completionTokens != null)
  ? `${v.promptTokens ?? 0}/${v.completionTokens ?? 0}` : null)
const cost = (v: StepView | null) => (v && v.costUsd != null ? money(v.costUsd) : null)

const CHIPS: Record<string, string> = { input: 'prompt', output: 'output' }

function StepRow({ a, b, step, inline }: { a: string; b: string; step: DiffStep; inline: boolean }) {
  const [open, setOpen] = useState(false)
  const first = step.input && step.output ? { input: step.input, output: step.output } : null
  const [body, setBody] = useState<StepDiff | StepRaw | null>(inline ? first : null)
  const [err, setErr] = useState<string | null>(null)
  const sa = step.a?.spanId
  const sb = step.b?.spanId

  useEffect(() => {
    if (!open || body || err) return
    let live = true
    const req: Promise<StepDiff | StepRaw> = sa && sb
      ? getStepDiff(a, b, sa, sb)
      : sa ? getStepRaw(a, b, 'sa', sa) : getStepRaw(a, b, 'sb', sb!)
    req.then(r => live && setBody(r)).catch(e => live && setErr(e instanceof ApiError && e.missing
      ? 'This step is gone from the stored run.' : "Couldn't load the text for this step."))
    return () => { live = false }
  }, [open, body, err, a, b, sa, sb])

  const x = step.a, y = step.b
  const model = x?.model !== y?.model && x?.model && y?.model
  const tool = step.kind !== 'llm'

  return (
    <div className={'step st-' + step.status + (open ? ' open' : '')}>
      <div className="step-row" onClick={() => setOpen(v => !v)}>
        <span className={'st-tag st-tag-' + step.status}>{step.status}</span>
        <div className="step-main">
          <span className={'kind kind-' + step.kind}>{step.kind}</span>
          <span className="step-name">{step.name}</span>
          {model && <span className="mono step-model">{x!.model}<i className="to">→</i>{y!.model}</span>}
          {step.changes.filter(c => c !== 'model').map(c => <span key={c} className="chip">{CHIPS[c] ?? c}</span>)}
        </div>
        <Pair a={x ? dur(x.durationMs) : null} b={y ? dur(y.durationMs) : null} />
        <Pair a={tok(x)} b={tok(y)} />
        <Pair a={cost(x)} b={cost(y)} />
        <span className="step-caret">{open ? '▾' : '▸'}</span>
      </div>

      {open && (
        <div className="step-body">
          <div className="step-info">
            {x && <Info tag="A" v={x} />}
            {y && <Info tag="B" v={y} />}
          </div>

          {(x?.error || y?.error) && step.changes.includes('error') && (
            <div className="step-errs">
              {x?.error && <pre className="span-error">A: {x.error}</pre>}
              {y?.error && <pre className="span-error">B: {y.error}</pre>}
            </div>
          )}
          {step.status === 'removed' && x?.error && !step.changes.includes('error') && <pre className="span-error">{x.error}</pre>}
          {step.status === 'added' && y?.error && <pre className="span-error">{y.error}</pre>}

          {step.params.length > 0 && (
            <section>
              <h4>Params</h4>
              <table className="param-table">
                <tbody>
                  {step.params.map(p => (
                    <tr key={p.key}>
                      <td className="muted">{p.key}</td>
                      <td className={'mono' + (p.a == null ? ' none' : ' pa')}>{p.a ?? 'not set'}</td>
                      <td className="to">→</td>
                      <td className={'mono' + (p.b == null ? ' none' : ' pb')}>{p.b ?? 'not set'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}

          {err && <p className="muted bad">{err}</p>}
          {!err && !body && <div className="sk step-sk" />}
          {body && (
            <>
              <Text title={tool ? 'Arguments' : 'Prompt'} body={body} part="input" changed={step.changes.includes('input')} paired={!!(sa && sb)} />
              <Text title={tool ? 'Result' : 'Completion'} body={body} part="output" changed={step.changes.includes('output')} paired={!!(sa && sb)} />
            </>
          )}
        </div>
      )}
    </div>
  )
}

function Info({ tag, v }: { tag: string; v: StepView }) {
  const hit = cacheHit(v.promptTokens, v.cacheReadTokens, v.cacheWriteTokens)
  const hasTok = v.promptTokens != null || v.completionTokens != null
  if (!hasTok && !v.model) return null
  return (
    <div className="info-line">
      <span className={'side-tag side-' + tag.toLowerCase()}>{tag}</span>
      {hasTok && <span className="num">{tokenText(v.promptTokens, v.completionTokens, v.cacheReadTokens, v.reasoningTokens)}</span>}
      {hit != null && <span className="num muted">cache hit {pct(hit)}</span>}
      {v.model && <span className="num muted">{v.model}</span>}
    </div>
  )
}

function Text({ title, body, part, changed, paired }: {
  title: string
  body: StepDiff | StepRaw
  part: 'input' | 'output'
  changed: boolean
  paired: boolean
}) {
  const v = body[part]
  if (v == null) return null
  return (
    <section>
      <h4>{title}{paired && !changed && <span className="unchanged"> unchanged</span>}</h4>
      {typeof v === 'string' ? <RawText text={v} /> : <TextDiff diff={v} />}
    </section>
  )
}

function DiffError({ err, onClose }: { err: Error; onClose: () => void }) {
  const api = err instanceof ApiError ? err : null
  let title = "Couldn't load this diff"
  let text = 'The collector may be offline. Try again in a moment.'
  if (api?.status === 404) {
    title = `Run ${api.missing?.toUpperCase() ?? ''} isn't stored any more`.replace('  ', ' ')
    text = 'It was deleted or dropped by retention, so there is nothing to compare.'
  } else if (api?.status === 400) {
    title = "Can't diff these"
    text = api.message.charAt(0).toUpperCase() + api.message.slice(1) + '.'
  }
  return (
    <div className="diff solo">
      <div className="empty">
        <p className="empty-title">{title}</p>
        <p className="muted">{text}</p>
        <button className="ghost-btn" onClick={onClose}>Close</button>
      </div>
    </div>
  )
}

function DiffSkeleton() {
  return (
    <div className="diff">
      <div className="sk sk-title" />
      <div className="sk sk-totals" />
      <div className="sk sk-rows" />
    </div>
  )
}
