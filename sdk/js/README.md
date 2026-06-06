# seerlens (JavaScript)

Send your Node app's LLM calls to [Seerlens](https://github.com/eladser/seerlens). Traces go out as OpenTelemetry GenAI spans, so they land in the same dashboard as the .NET and Python ones.

```bash
npm install seerlens
```

```js
import * as seerlens from 'seerlens'

seerlens.configure('http://localhost:5005')

const span = seerlens.trace('answer ticket', { model: 'gpt-4o' })
const reply = await myLlm(prompt)
span.complete({ prompt, completion: reply, inputTokens: 40, outputTokens: 12 })

await seerlens.flush() // before a short script exits
```

Or record a call you already made:

```js
seerlens.record({ model: 'gpt-4o', prompt: 'hi', completion: 'hello', inputTokens: 10, outputTokens: 5, durationMs: 820 })
```

Sends are fire-and-forget and errors are swallowed, so this never blocks or throws into your app. No dependencies, needs Node 18+.

```bash
node example.js   # against a running collector
npm test
```
