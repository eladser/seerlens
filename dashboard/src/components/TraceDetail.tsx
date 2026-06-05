import { useEffect, useState } from 'react'
import { getTrace } from '../api'
import { dur, money, tokens } from '../format'
import type { Span, TraceDetail as Detail } from '../types'
import { Waterfall } from './Waterfall'

export function TraceDetail({ traceId }: { traceId: string }) {
  const [detail, setDetail] = useState<Detail | null>(null)
  const [spanId, setSpanId] = useState<string | null>(null)

  useEffect(() => {
    let live = true
    setDetail(null)
    setSpanId(null)
    getTrace(traceId).then(d => {
      if (!live) return
      setDetail(d)
      setSpanId(d.spans[0]?.id ?? null)
    })
    return () => {
      live = false
    }
  }, [traceId])

  if (!detail) return <DetailSkeleton />

  const { trace, spans } = detail
  const span = spans.find(s => s.id === spanId) ?? null

  return (
    <div className="detail">
      <header className="detail-head">
        <h2>{trace.name}</h2>
        <div className="detail-stats">
          <Stat label="duration" value={dur(trace.durationMs)} />
          <Stat label="cost" value={money(trace.costUsd)} />
          <Stat label="tokens" value={tokens(trace.promptTokens, trace.completionTokens)} />
          {trace.model && <Stat label="model" value={trace.model} />}
          <Stat label="status" value={trace.status} bad={trace.status !== 'ok'} />
        </div>
      </header>

      <Waterfall
        spans={spans}
        traceStart={trace.startedAt}
        traceDuration={trace.durationMs}
        selectedId={spanId}
        onSelect={setSpanId}
      />

      {span && <SpanView span={span} />}
    </div>
  )
}

function SpanView({ span }: { span: Span }) {
  return (
    <div className="span-view">
      <div className="span-meta muted">
        <span>{dur(span.durationMs)}</span>
        {span.model && <span>{span.model}</span>}
        {span.kind === 'llm' && <span>{tokens(span.promptTokens, span.completionTokens)} tokens</span>}
        {span.costUsd != null && <span>{money(span.costUsd)}</span>}
      </div>

      {span.error && <pre className="span-error">{span.error}</pre>}

      {span.promptText && (
        <section>
          <h4>Prompt</h4>
          <pre>{span.promptText}</pre>
        </section>
      )}
      {span.completionText && (
        <section>
          <h4>Completion</h4>
          <pre>{span.completionText}</pre>
        </section>
      )}
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
