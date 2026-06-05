"""Send your Python app's LLM calls to Seerlens.

    import seerlens
    seerlens.configure("http://localhost:5005")

    with seerlens.trace("answer ticket", model="gpt-4o") as span:
        reply = my_llm(prompt)
        span.complete(prompt=prompt, completion=reply, input_tokens=40, output_tokens=12)

    seerlens.flush()  # before a short script exits

Traces are sent as OpenTelemetry GenAI spans, on a background thread. If the
collector is down the trace is dropped; it never blocks or breaks your app.
"""

import json
import os
import threading
import time
import urllib.request

__all__ = ["configure", "record", "trace", "flush"]

_endpoint = None
_threads = []


def configure(collector_url):
    global _endpoint
    _endpoint = collector_url.rstrip("/") + "/v1/traces"


def record(model, prompt="", completion="", input_tokens=None, output_tokens=None,
           duration_ms=0.0, system=None, name=None):
    """Record one finished LLM call."""
    end = time.time_ns()
    start = end - int(duration_ms * 1_000_000)
    span = {
        "traceId": _hexid(16),
        "spanId": _hexid(8),
        "parentSpanId": "",
        "name": name or f"chat: {model}",
        "startTimeUnixNano": str(start),
        "endTimeUnixNano": str(end),
        "attributes": _attrs(model, prompt, completion, input_tokens, output_tokens, system),
        "status": {"code": 1},
    }
    _send({"resourceSpans": [{"scopeSpans": [{"spans": [span]}]}]})


class trace:
    """Context manager that times a call and records it on exit."""

    def __init__(self, name, model, system=None):
        self._name = name
        self._model = model
        self._system = system
        self._prompt = ""
        self._completion = ""
        self._in = None
        self._out = None

    def __enter__(self):
        self._t0 = time.perf_counter()
        return self

    def complete(self, prompt="", completion="", input_tokens=None, output_tokens=None):
        self._prompt = prompt
        self._completion = completion
        self._in = input_tokens
        self._out = output_tokens

    def __exit__(self, *exc):
        ms = (time.perf_counter() - self._t0) * 1000
        record(self._model, self._prompt, self._completion, self._in, self._out,
               ms, self._system, self._name)
        return False


def flush(timeout=3.0):
    """Wait for any in-flight traces to finish sending. Call before a short script exits."""
    for t in list(_threads):
        t.join(timeout)


def _send(payload):
    if not _endpoint:
        return
    t = threading.Thread(target=_post, args=(payload,), daemon=True)
    _threads.append(t)
    t.start()


def _post(payload):
    try:
        data = json.dumps(payload).encode()
        req = urllib.request.Request(_endpoint, data=data, headers={"Content-Type": "application/json"})
        urllib.request.urlopen(req, timeout=5).close()
    except Exception:
        pass  # never break the host app


def _attrs(model, prompt, completion, in_tokens, out_tokens, system):
    pairs = [
        ("gen_ai.system", system or _provider(model)),
        ("gen_ai.request.model", model),
        ("gen_ai.prompt", prompt),
        ("gen_ai.completion", completion),
    ]
    out = [{"key": k, "value": {"stringValue": str(v)}} for k, v in pairs if v]
    for k, v in (("gen_ai.usage.input_tokens", in_tokens), ("gen_ai.usage.output_tokens", out_tokens)):
        if v is not None:
            out.append({"key": k, "value": {"intValue": str(v)}})
    return out


def _provider(model):
    m = (model or "").lower()
    if m.startswith(("gpt", "o1", "o3")):
        return "openai"
    if "claude" in m:
        return "anthropic"
    if "gemini" in m:
        return "google"
    return None


def _hexid(n):
    return os.urandom(n).hex()
