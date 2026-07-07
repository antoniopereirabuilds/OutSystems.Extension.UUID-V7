# OutSystems.Extension.UUID-V7

OutSystems Developer Cloud (ODC) External Library that exposes **UUID v7** (RFC 9562, time-ordered) generation to ODC apps. It wraps the [UUIDNext](https://github.com/mareek/UUIDNext) NuGet package and surfaces it as a Server Action.

UUID v7 values are 128-bit identifiers whose first 48 bits encode a Unix-millisecond timestamp, making them lexicographically sortable and ideal as database primary keys (better index locality than random UUIDs, no privacy leak from MAC addresses like UUID v1).

## Actions

Both actions follow the standard ODC contract used across this portfolio: a single optional input (`goldenThreadId`) plus a uniform set of output parameters (`isSuccess`, `flightPath`, `errorMessage`) on top of the action-specific results. Validation failures are reported via `isSuccess = false` with a populated `errorMessage` — the actions never throw to the ODC runtime.

### `GenerateUuidV7`

Generates a single RFC 9562 UUID v7 in canonical hyphenated string form (e.g. `018f1c4a-...`).

| Parameter | Direction | Type | Description |
|---|---|---|---|
| `goldenThreadId` | in | Text | Optional Flight Recorder trace ID for distributed tracing (defaults to empty). Ignored when `enableTelemetry` is False. |
| `enableTelemetry` | in | Boolean | When True (default), the Flight Recorder audit trail is produced. Set False to skip telemetry entirely (no allocation, no JSON serialization) — `flightPath` then comes back empty. Use False in hot paths where the ~5 µs telemetry cost is not justified. |
| `uuid` | out | Text | The generated UUID v7. |
| `isSuccess` | out | Boolean | True if the operation completed without errors. |
| `flightPath` | out | Text | Structured Flight Recorder JSON. Non-empty when telemetry is enabled; empty when `enableTelemetry` is False. |
| `errorMessage` | out | Text | Error details if `isSuccess` is false; empty otherwise. |

### `GenerateUuidV7Batch`

Generates a batch of UUID v7 values. `count` must be between 1 and 1000 inclusive; out-of-range values produce `isSuccess = false` with a populated `errorMessage` (no exception). The 1000 cap keeps a single ODC Lambda invocation comfortably within typical 256 MB / 30 s constraints. All UUIDs in the returned list satisfy the RFC 9562 §6.2 method 1 monotonicity guarantee.

| Parameter | Direction | Type | Description |
|---|---|---|---|
| `count` | in | Integer | Number of UUIDs to generate (1 to 1000 inclusive). |
| `goldenThreadId` | in | Text | Optional Flight Recorder trace ID (defaults to empty). Ignored when `enableTelemetry` is False. |
| `enableTelemetry` | in | Boolean | When True (default), the Flight Recorder audit trail is produced. Set False to skip telemetry — `flightPath` then comes back empty. The Flight Recorder cost is fixed (~5 µs) per call regardless of batch size, so for batches of 100+ UUIDs disabling telemetry rarely pays off. |
| `uuids` | out | List Text | The generated UUID v7 values, monotonically increasing. Empty when `isSuccess` is false. |
| `isSuccess` | out | Boolean | True if the operation completed without errors. |
| `flightPath` | out | Text | Structured Flight Recorder JSON. Non-empty when telemetry is enabled; empty when `enableTelemetry` is False. |
| `errorMessage` | out | Text | Error details if `isSuccess` is false; empty otherwise. |

## Structures

This library exposes no `[OSStructure]` types. All inputs and outputs use ODC primitive types (`Text`, `Integer`, `Boolean`) plus a `List Text` for the batch result (`uuids`).

## Telemetry

Both actions are instrumented with the [ODC Flight Recorder SDK](https://github.com/michaeldeguzman/odc-flight-recorder) (`ODC.FlightRecorder.SDK` 1.0.2). Every call populates the `flightPath` output — a JSON blob describing every step of the invocation — even on error. Persist `flightPath` to an entity for auditing and replay.

The `goldenThreadId` you pass in is propagated as the Flight Recorder session's `CorrelationId`, so the same value can be used to join `flightPath` rows to your own logs or to the **Trace ID** filter on the ODC Portal **Monitoring** tab. The library caps `goldenThreadId` at 200 characters and strips C0 control characters (0x00-0x1F), `DEL` (0x7F), and Unicode line separators (`U+0085`, `U+2028`, `U+2029`) before propagation, to prevent log-line forging in any downstream consumer that renders the value unescaped.

> **Security note (pentest finding F2).** `goldenThreadId` is written verbatim into telemetry: it flows into `flightPath` -> the Flight Recorder session `CorrelationId` -> CloudWatch. Sanitization removes control characters and line separators only — it does **not** redact sensitive content. Callers **must not** pass PII, secrets, or session tokens in `goldenThreadId`. The value is persisted in plain text inside `flightPath` and may be retained per your ODC retention policy. If you persist `flightPath` rows to an entity (see schema below), apply a retention policy / timer (e.g. delete rows where `CreatedAt < AddDays(CurrDateTime(), -90)`) to comply with GDPR Art. 5(1)(c) data minimisation.

Steps emitted:

* `GenerateUuidV7` -> `GenerationStarted`, `GenerationCompleted` (or `GenerationFailed` on unexpected exception)
* `GenerateUuidV7Batch` -> `BatchStarted`, `BatchValidated` (or `BatchValidationFailed` on out-of-range count), `BatchCompleted` (or `BatchFailed` on unexpected exception)

Elapsed time for every call is exposed as `SessionData.DurationMs` in the `flightPath` JSON.

Validation failures emit `BatchValidationFailed` and return `isSuccess = false` with `errorMessage` populated; the action does **not** throw, matching the standard contract used across this portfolio.

### ODC entity schema

Suggested entity for storing `flightPath` rows. All sizes are recommendations:

| Attribute | Data Type | Length | Notes |
|---|---|---|---|
| `Id` | Long Integer (Auto-Number) | - | Primary key. |
| `SessionId` | Text | 50 | GUID from the `SessionData.Id` field of the JSON. |
| `MethodName` | Text | 100 | `GenerateUuidV7` or `GenerateUuidV7Batch`. |
| `CorrelationId` | Text | 100 | The `goldenThreadId` value passed to the action (empty if the caller passed an empty string). |
| `GoldenThreadId` | Text | 200 | Mirror of `CorrelationId`; the trace ID used to link this row to ODC Monitoring or to caller-side logs. |
| `IsError` | Boolean | - | Mirrors `SessionData.IsError`. |
| `DurationMs` | Integer | - | Mirrors `SessionData.DurationMs`. |
| `StartedAtUtc` | Date Time | - | Mirrors `SessionData.StartTime`. |
| `FlightPathJson` | Text | 1000000 | Raw JSON; query-able as text. |
| `CreatedAt` | Date Time | - | Default `CurrDateTime()`. |

### Wiring guide for ODC Studio

After uploading the new revision in **ODC Portal -> External Libraries**, refresh the dependency in ODC Studio. The two actions appear under the `UuidV7` group with `goldenThreadId` as an optional `Text` input and `uuid`/`uuids`, `isSuccess`, `flightPath`, `errorMessage` as outputs. Drag a **Create<EntityName>** action below your call to `GenerateUuidV7` (or `GenerateUuidV7Batch`), bind `flightPath` to `FlightPathJson` and the input `goldenThreadId` to `GoldenThreadId`, and parse the JSON with **JSONDeserialize** if you want to populate the structured columns (`SessionId`, `MethodName`, `IsError`, `DurationMs`, `StartedAtUtc`). For ad-hoc debugging, the `goldenThreadId` you passed in can be pasted into the **Trace ID** filter on the ODC Portal **Monitoring** tab to jump straight to the matching distributed trace.

## Build

```bash
dotnet build
dotnet test
```

## Package for ODC

Build a framework-dependent `linux-x64` publish artifact and zip the contents (DLLs at the zip root, which is what ODC expects):

```bash
dotnet publish OutSystems.UuidV7/OutSystems.UuidV7.csproj \
  -c Release -r linux-x64 --self-contained false -o ./publish

cd ./publish && zip -r ../UuidV7_Asset.zip . && cd ..
```

A push of a `v*` Git tag triggers `.github/workflows/release.yml`, which runs the same steps and attaches the zip to a GitHub Release.

## Installation

1. Open the **ODC Portal** -> **External Libraries**.
2. Click **Upload new library** (or **Upload new revision** when updating).
3. Upload the asset zip produced by the `Build, Test, Package` workflow (attached to the matching GitHub Release).
4. In **ODC Studio**, add the library as an app dependency. The actions above appear under the `UuidV7` group.

## Constraints

- **Batch bounds:** `GenerateUuidV7Batch` accepts `count` from 1 to 1000 inclusive (`MaxBatchSize = 1000`). Out-of-range values fail gracefully — `isSuccess = false` with `errorMessage` populated, no exception thrown to the ODC runtime.
- **Never throws:** both actions surface all failures through the `isSuccess` / `errorMessage` pair; they never throw to the ODC runtime.
- **Stateless:** each invocation is independent. Do not rely on any in-memory state between calls — this matches the AWS Lambda execution model used by ODC External Libraries.
- **Telemetry cost:** the Flight Recorder adds ~5 µs per call and populates `flightPath` on every invocation (including the failure path) when `enableTelemetry` is True. Set `enableTelemetry = False` to skip it entirely; `flightPath` then returns empty.
- **`goldenThreadId` sensitivity (F2):** the value is echoed verbatim into `flightPath` -> `CorrelationId` -> CloudWatch. It is sanitized for control characters and line separators only, **not** for sensitive content. Do not pass PII, secrets, or session tokens; apply a retention policy to any stored `flightPath` data.
- **Target framework:** `net10.0` (see below).

## Technical Details

- **Wrapped library:** UUIDNext 4.2.4 (`Uuid.NewSequential()` returns RFC 9562 UUID v7).
- **Telemetry:** ODC.FlightRecorder.SDK 1.0.2.
- **Target:** .NET 10 (`net10.0`), `linux-x64`, framework-dependent.
- **ODC SDK:** `OutSystems.ExternalLibraries.SDK` 1.5.0.
- **License:** see repository.
