import assert from 'node:assert/strict'
import test from 'node:test'
import { buildPayload } from '../index.js'

const spanOf = p => p.resourceSpans[0].scopeSpans[0].spans[0]
const attrsOf = span => Object.fromEntries(span.attributes.map(a => [a.key, a.value]))

test('builds a genai span with model, tokens, and provider', () => {
  const span = spanOf(buildPayload({
    model: 'gpt-4o', prompt: 'hi', completion: 'hello',
    inputTokens: 10, outputTokens: 5, durationMs: 200,
  }))
  const attrs = attrsOf(span)

  assert.equal(attrs['gen_ai.request.model'].stringValue, 'gpt-4o')
  assert.equal(attrs['gen_ai.system'].stringValue, 'openai')
  assert.equal(attrs['gen_ai.usage.input_tokens'].intValue, '10')
  assert.equal(attrs['gen_ai.prompt'].stringValue, 'hi')
})

test('reflects duration in the span timestamps', () => {
  const span = spanOf(buildPayload({ model: 'gpt-4o', durationMs: 200 }))
  const elapsed = BigInt(span.endTimeUnixNano) - BigInt(span.startTimeUnixNano)
  assert.equal(elapsed, 200n * 1_000_000n)
})

test('infers the provider from the model name', () => {
  const span = spanOf(buildPayload({ model: 'claude-3-5-sonnet' }))
  assert.equal(attrsOf(span)['gen_ai.system'].stringValue, 'anthropic')
})
