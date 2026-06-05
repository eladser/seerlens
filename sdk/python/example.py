import os

import seerlens

seerlens.configure(os.environ.get("SEERLENS_URL", "http://localhost:5005"))

# Pretend these came back from your LLM client.
with seerlens.trace("answer support ticket", model="gpt-4o") as span:
    reply = "Your order shipped and arrives Thursday."
    span.complete(prompt="Where is my order #5521?", completion=reply,
                  input_tokens=40, output_tokens=12)

seerlens.record(model="claude-3-5-sonnet", prompt="Summarize the update.",
                completion="Numbers up, churn down.", input_tokens=120,
                output_tokens=18, duration_ms=430)

seerlens.flush()
print("sent traces to Seerlens")
