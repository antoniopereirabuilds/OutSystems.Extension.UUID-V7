## What it does

UuidV7 generates RFC 9562 UUID v7 identifiers for ODC applications. UUID v7 values are 128-bit IDs whose first 48 bits encode a Unix-millisecond timestamp, so a list of them sorts naturally by creation time. That makes them well-suited as database primary keys — index pages stay densely packed instead of fragmenting the way random UUIDs cause — and as correlation IDs across distributed calls. This library packages UUID v7 generation as a Server Action you can drag into any flow.

Every call also returns a Flight Recorder JSON audit trail, even on failure, so you can persist a per-call diagnostic record alongside the result.

## Actions

| Action | What it does | Key inputs | Key outputs |
|---|---|---|---|
| GenerateUuidV7 | Generates one UUID v7 in canonical hyphenated form. | goldenThreadId: Text (optional), enableTelemetry: Boolean (default True) | uuid: Text, isSuccess: Boolean, flightPath: Text, errorMessage: Text |
| GenerateUuidV7Batch | Generates a list of UUID v7 values in monotonically increasing order. | count: Integer (1-1000), goldenThreadId: Text (optional), enableTelemetry: Boolean (default True) | uuids: List of Text, isSuccess: Boolean, flightPath: Text, errorMessage: Text |

Both actions follow a uniform contract: validation problems return isSuccess = false with a populated errorMessage; the actions never throw to the ODC runtime.

## How to use

In ODC Studio, add the library as an app dependency. The two Server Actions appear under the UuidV7 group. Drag GenerateUuidV7 (or GenerateUuidV7Batch) into a flow, leave goldenThreadId empty for one-off calls, or pass a trace ID generated upstream when you want the call linked to a wider distributed trace. Use the uuid (or uuids) output as your identifier and check isSuccess before consuming it.

For auditing or troubleshooting, persist the flightPath JSON to an entity. The same goldenThreadId you supplied is echoed back inside the JSON as the session CorrelationId, and can be pasted into the Trace ID filter on the ODC Portal Monitoring tab to jump straight to the matching distributed trace.

## Constraints

- Batch size is capped at 1000 per call; values outside the 1-1000 range return isSuccess = false with errorMessage populated.
- Stateless: each call is independent. There is no shared in-memory state between invocations, which matches the AWS Lambda execution model used by ODC External Libraries.
- The flightPath output is non-empty whenever telemetry is enabled, including on the failure path. When the caller passes enableTelemetry = False, the Flight Recorder is skipped entirely and flightPath comes back empty.
- goldenThreadId is sanitized before propagation: it is capped at 200 characters and stripped of control characters and line separators to prevent log-line forging in downstream consumers. It is not redacted for sensitive content.
- Do not pass PII, secrets, or session tokens in goldenThreadId. It is written verbatim into flightPath and forwarded to CloudWatch; the value is persisted in plain text, so apply a retention policy to any stored flightPath data.
- UUID v7 is time-ordered, not sortable across system clocks. If two ODC nodes have skewed clocks, IDs they emit may interleave; this is acceptable for primary keys but be deliberate when using UUID v7 ordering as a logical timestamp.
