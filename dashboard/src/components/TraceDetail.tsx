import { useEffect, useState } from 'react'
import { addCase, getSets, getTrace, scoreTools } from '../api'
import { downloadJson } from '../download'
import { hasLlmBelow, pct, previousTrace, spanCache, tokenText, traceCache } from '../diff'
import { dur, money, tokens } from '../format'
import type { Span, ToolScoreResult, TraceDetail as Detail, TraceSummary } from '../types'
import { Waterfall } from './Waterfall'

type Props = { traceId: string; traces: TraceSummary[]; onDiff: (a: string, b: string) => void }

export function TraceDetail({ traceId, traces, onDiff }: Props) {
  const [detail, setDetail] = useState<Detail | null>(null)
  const [spanId, setSpanId] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)

  useEffect(() => {
    let live = true
    setDetail(null)
    setSpanId(null)
    setFailed(false)
    getTrace(traceId)
      .then(d => {
        if (!live) return
        setDetail(d)
        setSpanId(d.spans[0]?.id ?? null)
      })
      .catch(() => live && setFailed(true))
    return () => {
      live = false
    }
  }, [traceId])

  if (failed) return <div className="empty">Couldn't load this trace.</div>
  if (!detail) return <DetailSkeleton />

  const { trace, spans } = detail
  const span = spans.find(s => s.id === spanId) ?? null
  const toolCalls = spans.filter(s => s.kind === 'tool' || s.kind === 'mcp')
  const cache = traceCache(spans)
  const prev = previousTrace(traces, trace)

  return (
    <div className="detail">
      <header className="detail-head">
        <div className="detail-title">
          <h2>{trace.name}</h2>
          <div className="title-actions">
            {prev && <button className="ghost-btn" onClick={() => onDiff(prev.id, trace.id)}>Diff with previous</button>}
            <button className="ghost-btn" onClick={() => downloadJson(`trace-${trace.id}.json`, detail)}>Export JSON</button>
          </div>
        </div>
        <div className="detail-stats">
          <Stat label="duration" value={dur(trace.durationMs)} />
          <Stat label="cost" value={money(trace.costUsd)} />
          <Stat label="tokens" value={tokens(trace.promptTokens, trace.completionTokens)} />
          {cache.rate != null && <Stat label="cache hit" value={`${pct(cache.rate)} (${cache.read.toLocaleString('en-US')} read)`} />}
          {cache.reasoning > 0 && <Stat label="reasoning" value={cache.reasoning.toLocaleString('en-US')} />}
          {toolCalls.length > 0 && <Stat label="tool calls" value={String(toolCalls.length)} />}
          {trace.model && <Stat label="model" value={trace.model} />}
          <Stat label="status" value={trace.status} bad={trace.status !== 'ok'} />
        </div>
      </header>

      {toolCalls.length > 0 && (
        <>
          <div className="tool-seq">
            {toolCalls.map((t, i) => (
              <span key={t.id} className="tool-step" onClick={() => setSpanId(t.id)}>
                <span className={'kind kind-' + t.kind}>{t.kind}</span>
                {t.name}
                {i < toolCalls.length - 1 && <span className="tool-arrow">→</span>}
              </span>
            ))}
          </div>
          <ToolScore traceId={trace.id} actual={toolCalls.map(t => t.name)} />
        </>
      )}

      <Waterfall
        spans={spans}
        traceStart={trace.startedAt}
        traceDuration={trace.durationMs}
        selectedId={spanId}
        onSelect={setSpanId}
      />

      {span && <SpanView span={span} counted={hasLlmBelow(spans, span)} />}
    </div>
  )
}

function SpanView({ span, counted }: { span: Span; counted: boolean }) {
  const isTool = span.kind === 'tool' || span.kind === 'mcp'
  const hit = spanCache(span)
  return (
    <div className="span-view">
      <div className="span-meta muted">
        <span>{dur(span.durationMs)}</span>
        {span.model && <span>{span.model}</span>}
        {(span.kind === 'llm' || span.kind === 'agent') && (span.promptTokens != null || span.completionTokens != null) && (
          <span>{tokenText(span.promptTokens, span.completionTokens, span.cacheReadTokens, span.reasoningTokens)}</span>
        )}
        {hit != null && <span>cache hit {pct(hit)}</span>}
        {span.costUsd != null && (
          counted && (span.costUsd ?? 0) > 0
            ? <span className="faint-cost">{money(span.costUsd)} (already counted in the steps below)</span>
            : <span>{money(span.costUsd)}</span>
        )}
      </div>

      {span.error && <pre className="span-error">{span.error}</pre>}

      {span.promptText && (
        <section>
          <h4>{isTool ? 'Arguments' : 'Prompt'}</h4>
          <pre>{span.promptText}</pre>
        </section>
      )}
      {span.completionText && (
        <section>
          <h4>{isTool ? 'Result' : 'Completion'}</h4>
          <pre>{span.completionText}</pre>
        </section>
      )}

      {span.kind === 'llm' && span.promptText && <PromoteCase prompt={span.promptText} />}
    </div>
  )
}

// Run-level eval on a recorded agent trace: did it call the tools you expected,
// in order? Prefilled with what actually happened, so you edit to set the bar.
function ToolScore({ traceId, actual }: { traceId: string; actual: string[] }) {
  const [open, setOpen] = useState(false)
  const [expected, setExpected] = useState(actual.join(', '))
  const [result, setResult] = useState<ToolScoreResult | null>(null)

  async function run() {
    const tools = expected.split(',').map(t => t.trim()).filter(Boolean)
    setResult(await scoreTools(traceId, tools).catch(() => null))
  }

  if (!open) return (
    <button className="ghost-btn tool-score-btn" onClick={() => setOpen(true)}>Score tool usage</button>
  )

  return (
    <div className="tool-score">
      <div className="tool-score-row">
        <input
          className="models-input"
          value={expected}
          onChange={e => setExpected(e.target.value)}
          placeholder="expected tools in order, comma separated"
          spellCheck={false}
        />
        <button className="run-btn" onClick={run}>Score</button>
        <button className="ghost-btn" onClick={() => setOpen(false)}>Close</button>
      </div>
      {result && (
        <div className="tool-score-result">
          <span className={'num ' + (result.score >= 0.8 ? 'good' : result.score >= 0.5 ? 'mid' : 'bad')}>
            {Math.round(result.score * 100)}%
          </span>
          <span className="muted">{result.orderOk ? 'right tools, right order' : 'order or tools off'}</span>
          {result.missing.length > 0 && <span className="muted">missing: {result.missing.join(', ')}</span>}
        </div>
      )}
    </div>
  )
}

// Turn a real prompt you just looked at into a golden-set case. Closes the loop
// from "saw a bad answer" to "covered by a test".
function PromoteCase({ prompt }: { prompt: string }) {
  const [open, setOpen] = useState(false)
  const [sets, setSets] = useState<string[]>([])
  const [set, setSet] = useState('')
  const [keywords, setKeywords] = useState('')
  const [done, setDone] = useState(false)

  useEffect(() => {
    if (!open) return
    getSets().then(i => {
      setSets(i.sets)
      setSet(s => s || i.sets[0] || '')
    }).catch(() => {})
  }, [open])

  async function add() {
    if (!set) return
    await addCase(set, {
      id: '',
      input: prompt,
      keywords: keywords.split(',').map(k => k.trim()).filter(Boolean),
      criteria: null,
    }).catch(() => {})
    setDone(true)
  }

  if (!open) return (
    <button className="ghost-btn promote" onClick={() => setOpen(true)}>Save as eval case</button>
  )

  if (done) return <p className="muted promote">Added to <b>{set}</b>.</p>

  return (
    <div className="promote-form">
      <select value={set} onChange={e => setSet(e.target.value)}>
        {sets.length === 0 && <option value="">no sets yet</option>}
        {sets.map(s => <option key={s} value={s}>{s}</option>)}
      </select>
      <input
        className="models-input"
        value={keywords}
        onChange={e => setKeywords(e.target.value)}
        placeholder="keywords a good answer needs (optional)"
        spellCheck={false}
      />
      <button className="run-btn" onClick={add} disabled={!set}>Add</button>
      <button className="ghost-btn" onClick={() => setOpen(false)}>Cancel</button>
    </div>
  )
}

function DetailSkeleton() {
  return (
    <div className="detail">
      <div className="sk sk-title" />
      <div className="detail-stats">
        {Array.from({ length: 4 }).map((_, i) => <div key={i} className="sk sk-stat" />)}
      </div>
      <div className="sk sk-rows" />
    </div>
  )
}

function Stat({ label, value, bad }: { label: string; value: string; bad?: boolean }) {
  return (
    <div className="stat">
      <span className="stat-label muted">{label}</span>
      <span className={'stat-value' + (bad ? ' bad' : '')}>{value}</span>
    </div>
  )
}
