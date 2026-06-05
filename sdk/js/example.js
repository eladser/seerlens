import * as seerlens from './index.js'

seerlens.configure(process.env.SEERLENS_URL || 'http://localhost:5005')

// Pretend these came back from your LLM client.
const span = seerlens.trace('answer support ticket', { model: 'gpt-4o' })
const reply = 'Your order shipped and arrives Thursday.'
span.complete({ prompt: 'Where is my order #5521?', completion: reply, inputTokens: 40, outputTokens: 12 })

seerlens.record({
  model: 'gemini-1.5-pro',
  prompt: 'Summarize the update.',
  completion: 'Numbers up, churn down.',
  inputTokens: 90,
  outputTokens: 15,
  durationMs: 300,
})

await seerlens.flush()
console.log('sent traces to Seerlens')
