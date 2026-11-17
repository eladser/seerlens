import { describe, expect, it } from 'vitest'
import { cacheHit, counts, delta, deltaMoney, hasLlmBelow, pathMarks, pickPair, previousTrace, tokenText, togglePick, traceCache } from './diff'
import { money } from './format'
import { diffUrl, parseRoute } from './route'
import type { DiffStep, Span, TraceSummary } from './types'

const trace = (id: string, name: string, startedAt: number): TraceSummary => ({
  id, name, startedAt, durationMs: 1, provider: null, model: null, status: 'ok',
  promptTokens: null, completionTokens: null, costUsd: null,
})

const span = (o: Partial<Span> & { id: string }): Span => ({
  parentId: null, name: o.id, kind: 'llm', startedAt: 0, durationMs: 1, model: null,
  promptTokens: null, completionTokens: null, costUsd: null, promptText: null, completionText: null, error: null, ...o,
})

const step = (status: DiffStep['status'], kind: string, name: string): DiffStep => ({
  status, kind, name,
  a: status === 'added' ? null : ({} as DiffStep['a']),
  b: status === 'removed' ? null : ({} as DiffStep['b']),
  changes: [], params: [],
})

describe('cacheHit', () => {
  it('is null without cache data', () => expect(cacheHit(100, null, null)).toBeNull())
  it('reads inclusive counts', () => expect(cacheHit(1000, 800, 0)).toBe(0.8))
  it('folds exclusive counts in so it stays under 100%', () => {
    expect(cacheHit(100, 900, 0)).toBe(0.9)
    expect(cacheHit(0, 500, 0)).toBe(1)
  })
})

describe('traceCache', () => {
  it('ignores spans with no cache data and non-llm spans', () => {
    const t = traceCache([
      span({ id: 'a', promptTokens: 1000, cacheReadTokens: 500 }),
      span({ id: 'b', promptTokens: 1000 }),
      span({ id: 'c', kind: 'agent', promptTokens: 9999, cacheReadTokens: 9999, reasoningTokens: 7 }),
      span({ id: 'd', promptTokens: 1000, cacheReadTokens: 0, reasoningTokens: 40 }),
    ])
    expect(t.rate).toBe(0.25)
    expect(t.read).toBe(500)
    expect(t.reasoning).toBe(40)
  })
  it('has no rate when nothing reported cache', () => {
    expect(traceCache([span({ id: 'a', promptTokens: 10 })]).rate).toBeNull()
  })
})

describe('tokenText', () => {
  it('adds cached and reasoning parts only when present', () => {
    expect(tokenText(1240, 312, 980, 200)).toBe('1,240 in (980 cached) / 312 out (200 reasoning)')
    expect(tokenText(10, 5)).toBe('10 in / 5 out')
    expect(tokenText(null, null)).toBe('n/a')
  })
})

describe('hasLlmBelow', () => {
  const spans = [
    span({ id: 'ag', kind: 'agent' }),
    span({ id: 'inner', kind: 'agent', parentId: 'ag' }),
    span({ id: 'chat', parentId: 'inner' }),
    span({ id: 'lone', kind: 'agent' }),
  ]
  it('finds an llm through nested agents', () => expect(hasLlmBelow(spans, spans[0])).toBe(true))
  it('is false for a childless agent', () => expect(hasLlmBelow(spans, spans[3])).toBe(false))
  it('is false for non-agent spans', () => expect(hasLlmBelow(spans, spans[2])).toBe(false))
})

describe('deltas', () => {
  it('flags a rise in a lower-is-better metric', () => {
    expect(delta(2, 3, true)).toEqual({ text: '+1 (+50%)', tone: 'bad' })
    expect(delta(3, 2, true).tone).toBe('good')
    expect(delta(3, 2, false).tone).toBe('')
  })
  it('handles equal and missing', () => {
    expect(delta(4, 4, true).text).toBe('=')
    expect(delta(null, 4, true).text).toBe('')
  })
  it('formats money', () => {
    expect(deltaMoney(0.01, 0.015)).toEqual({ text: '+$0.005 (+50%)', tone: 'bad' })
    expect(deltaMoney(0.02, null).text).toBe('')
  })
})

describe('money', () => {
  it('uses 4 significant digits below $1 and trims zeros', () => {
    expect(money(0.0157)).toBe('$0.0157')
    expect(money(0.002962)).toBe('$0.002962')
    expect(money(0.0039)).toBe('$0.0039')
    expect(money(0.5)).toBe('$0.5')
    expect(money(0.00000123)).toBe('$0.00000123')
  })
  it('uses cents from $1 up', () => {
    expect(money(1)).toBe('$1.00')
    expect(money(12.3456)).toBe('$12.35')
  })
  it('handles zero and missing', () => {
    expect(money(0)).toBe('$0')
    expect(money(null)).toBe('n/a')
  })
})

describe('pathMarks and counts', () => {
  const steps = [step('same', 'llm', 'llm'), step('removed', 'tool', 'read'), step('same', 'tool', 'search'), step('added', 'tool', 'fetch')]
  it('marks the calls the other side lacks', () => {
    expect(pathMarks(steps, 'a')).toEqual([{ name: 'read', status: 'removed' }, { name: 'search', status: 'same' }])
    expect(pathMarks(steps, 'b')).toEqual([{ name: 'search', status: 'same' }, { name: 'fetch', status: 'added' }])
  })
  it('counts statuses', () => expect(counts(steps)).toEqual({ same: 2, changed: 0, removed: 1, added: 1 }))
})

describe('picking', () => {
  const ts = [trace('new', 'x', 200), trace('old', 'x', 100), trace('mid', 'y', 150)]
  it('puts the older run first', () => {
    expect(pickPair(['new', 'old'], ts)).toEqual(['old', 'new'])
    expect(pickPair(['old'], ts)).toBeNull()
  })
  it('toggles and caps at two', () => {
    expect(togglePick(['a'], 'b')).toEqual(['a', 'b'])
    expect(togglePick(['a', 'b'], 'c')).toEqual(['b', 'c'])
    expect(togglePick(['a', 'b'], 'a')).toEqual(['b'])
  })
  it('finds the previous trace with the same name', () => {
    expect(previousTrace(ts, ts[0])?.id).toBe('old')
    expect(previousTrace(ts, ts[1])).toBeNull()
    expect(previousTrace(ts, ts[2])).toBeNull()
  })
})

describe('route', () => {
  it('round-trips refs with colons', () => {
    const url = diffUrl('eval:r1:2', 'abc')
    const [path, search] = url.split('?')
    expect(parseRoute(path, '?' + search)).toEqual({ a: 'eval:r1:2', b: 'abc' })
  })
  it('only claims /diff', () => {
    expect(parseRoute('/', '')).toBeNull()
    expect(parseRoute('/diff/', '?a=1&b=2')).toEqual({ a: '1', b: '2' })
    expect(parseRoute('/diff', '')).toEqual({ a: '', b: '' })
  })
})
