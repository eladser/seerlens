# seerlens (Python)

Send your Python app's LLM calls to [Seerlens](https://github.com/eladser/seerlens). Traces go out as OpenTelemetry GenAI spans, so they land in the same dashboard as the .NET ones.

```bash
pip install seerlens
```

```python
import seerlens

seerlens.configure("http://localhost:5005")

with seerlens.trace("answer ticket", model="gpt-4o") as span:
    reply = my_llm(prompt)
    span.complete(prompt=prompt, completion=reply, input_tokens=40, output_tokens=12)

seerlens.flush()  # before a short script exits
```

Or record a call you already made:

```python
seerlens.record(model="gpt-4o", prompt="hi", completion="hello",
                input_tokens=10, output_tokens=5, duration_ms=820)
```

Traces are sent on a background thread. If the collector is down the trace is dropped; it never blocks or throws into your app.

No third-party dependencies. Run the example against a running collector:

```bash
python example.py
```
