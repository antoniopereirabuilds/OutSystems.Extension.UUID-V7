UuidV7 lets ODC applications generate RFC 9562 UUID v7 identifiers. UUID v7 values are 128-bit IDs whose first 48 bits encode a Unix-millisecond timestamp, so they sort by creation time and work well as database primary keys without the index fragmentation of random UUIDs.

Key capabilities:
- Generate a single UUID v7 - returns one canonical hyphenated string per call.
- Generate a batch of UUID v7 values - returns up to 1000 IDs in monotonically increasing order in a single call.
- Built-in distributed-tracing audit trail - every call returns a Flight Recorder JSON blob you can persist for replay or join to ODC Monitoring.

Validation failures (for example, a batch count outside 1-1000) are returned as isSuccess = false with an errorMessage; the actions never throw to the ODC runtime.

Ideal for apps that need time-ordered primary keys, correlation IDs, or any sortable unique identifier without round-tripping to a database sequence.
