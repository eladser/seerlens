// Send your JS app's LLM calls to Seerlens.
//
//   import * as seerlens from 'seerlens'
//   seerlens.configure('http://localhost:5005')
//
//   const span = seerlens.trace('answer ticket', { model: 'gpt-4o' })
//   const reply = await myLlm(prompt)
//   span.complete({ prompt, completion: reply, inputTokens: 40, outputTokens: 12 })
//
//   await seerlens.flush() // before a short script exits
//
// Traces go out as OpenTelemetry GenAI spans. Sends are fire-and-forget and
// errors are swallowed, so this never blocks or breaks your app.

import { randomBytes } from 'node:crypto'

let endpoint = null
const pending = new Set()

export function configure(collectorUrl) {
  endpoint = collectorUrl.replace(/\/+$/, '') + '/v1/traces'
}

export function record({
  model,
  prompt = '',
  completion = '',
  inputTokens,
  outputTokens,
  durationMs = 0,
  system,
  name,
} = {}) {
  send(buildPayload({ model, prompt, completion, inputTokens, outputTokens, durationMs, system, name }))
}

// Times a call. Call complete() once you have the response.
export function trace(name, { model, system } = {}) {
  const start = performance.now()
  return {
    complete({ prompt = '', completion = '', inputTokens, outputTokens } = {}) {
      record({ model, system, name, prompt, completion, inputTokens, outputTokens, durationMs: performance.now() - start })
    },
  }
}

export async function flush() {
  await Promise.allSettled([...pending])
}

export function buildPayload({ model, prompt, completion, inputTokens, outputTokens, durationMs, system, name }) {
  const end = BigInt(Date.now()) * 1_000_000n
  const start = end - BigInt(Math.round((durationMs || 0) * 1e6))
  const span = {
    traceId: hexId(16),
    spanId: hexId(8),
    parentSpanId: '',
    name: name || `chat: ${model}`,
    startTimeUnixNano: start.toString(),
    endTimeUnixNano: end.toString(),
    attributes: attrs({ model, prompt, completion, inputTokens, outputTokens, system }),
    status: { code: 1 },
  }
  return { resourceSpans: [{ scopeSpans: [{ spans: [span] }] }] }
}

function send(payload) {
  if (!endpoint) return
  const p = fetch(endpoint, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(payload),
  })
    .then(() => {})
    .catch(() => {}) // never break the host
    .finally(() => pending.delete(p))
  pending.add(p)
}

function attrs({ model, prompt, completion, inputTokens, outputTokens, system }) {
  const out = []
  const str = (k, v) => { if (v) out.push({ key: k, value: { stringValue: String(v) } }) }
  const int = (k, v) => { if (v != null) out.push({ key: k, value: { intValue: String(v) } }) }
  str('gen_ai.system', system || provider(model))
  str('gen_ai.request.model', model)
  str('gen_ai.prompt', prompt)
  str('gen_ai.completion', completion)
  int('gen_ai.usage.input_tokens', inputTokens)
  int('gen_ai.usage.output_tokens', outputTokens)
  return out
}

function provider(model) {
  const m = (model || '').toLowerCase()
  if (m.startsWith('gpt') || m.startsWith('o1') || m.startsWith('o3')) return 'openai'
  if (m.includes('claude')) return 'anthropic'
  if (m.includes('gemini')) return 'google'
  return null
}

function hexId(n) {
  return randomBytes(n).toString('hex')
}
