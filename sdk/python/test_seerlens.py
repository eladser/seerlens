import unittest

import seerlens


class SeerlensTests(unittest.TestCase):
    def setUp(self):
        self.sent = []
        seerlens._send = lambda payload: self.sent.append(payload)  # bypass the network

    def _span(self):
        return self.sent[-1]["resourceSpans"][0]["scopeSpans"][0]["spans"][0]

    def _attrs(self):
        return {a["key"]: a["value"] for a in self._span()["attributes"]}

    def test_record_builds_a_genai_span(self):
        seerlens.record(model="gpt-4o", prompt="hi", completion="hello",
                        input_tokens=10, output_tokens=5, duration_ms=200)

        attrs = self._attrs()
        self.assertEqual(attrs["gen_ai.request.model"]["stringValue"], "gpt-4o")
        self.assertEqual(attrs["gen_ai.system"]["stringValue"], "openai")
        self.assertEqual(attrs["gen_ai.usage.input_tokens"]["intValue"], "10")
        self.assertEqual(attrs["gen_ai.prompt"]["stringValue"], "hi")

    def test_duration_is_reflected_in_span_times(self):
        seerlens.record(model="gpt-4o", duration_ms=200)
        span = self._span()
        elapsed_ns = int(span["endTimeUnixNano"]) - int(span["startTimeUnixNano"])
        self.assertEqual(elapsed_ns, 200 * 1_000_000)

    def test_trace_context_manager_records_with_inferred_provider(self):
        with seerlens.trace("ticket", model="claude-3-5-sonnet") as span:
            span.complete(prompt="q", completion="a", input_tokens=3, output_tokens=2)

        self.assertEqual(self._span()["name"], "ticket")
        self.assertEqual(self._attrs()["gen_ai.system"]["stringValue"], "anthropic")


if __name__ == "__main__":
    unittest.main()
