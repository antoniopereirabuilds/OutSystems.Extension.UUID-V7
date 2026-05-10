using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ODCFlightRecorder.SDK;
using UUIDNext;

[assembly: InternalsVisibleTo("OutSystems.UuidV7.UnitTests")]

namespace OutSystems.UuidV7
{
    /// <summary>
    /// UUIDNext-backed implementation of <see cref="IUuidV7Service"/>. Generates RFC 9562
    /// UUID v7 values and emits a Flight Recorder JSON audit trail on every call.
    /// </summary>
    /// <remarks>
    /// Stateless: each invocation is independent and safe for the AWS Lambda execution model
    /// used by ODC External Libraries. The class never throws to the ODC runtime — all failures
    /// are surfaced through the <c>isSuccess</c> / <c>errorMessage</c> output pair.
    /// </remarks>
    public class UuidV7Service : IUuidV7Service
    {
        /// <summary>
        /// Upper bound for <c>GenerateUuidV7Batch</c>. Keeps a single ODC Lambda invocation
        /// comfortably within typical 256 MB / 30 s constraints.
        /// </summary>
        internal const int MaxBatchSize = 1000;

        /// <summary>
        /// Minimal JSON envelope returned when the Flight Recorder itself fails to serialize.
        /// Preserves the documented contract that <c>flightPath</c> is non-empty on every call.
        /// </summary>
        private const string DegradedFlightPath = "{\"SessionData\":{\"IsError\":true},\"LogsWithDetails\":[]}";

        /// <summary>
        /// Maximum length of <c>goldenThreadId</c> retained after sanitization. Caller-supplied
        /// values are echoed into telemetry, so the value is capped and stripped of control
        /// characters to prevent log-line forging in downstream consumers.
        /// </summary>
        private const int MaxGoldenThreadIdLength = 200;

        /// <summary>
        /// UUID generator delegate. Defaults to <c>UUIDNext.Uuid.NewSequential()</c>; replaced
        /// in unit tests via the internal constructor to exercise the failure path.
        /// </summary>
        private readonly Func<string> _uuidGenerator;

        /// <summary>
        /// Production constructor. Wires the generator to <c>UUIDNext.Uuid.NewSequential()</c>,
        /// which returns RFC 9562 UUID v7 values in canonical 8-4-4-4-12 hyphenated form.
        /// </summary>
        public UuidV7Service() : this(() => Uuid.NewSequential().ToString("D")) { }

        /// <summary>
        /// Test seam constructor. Allows the unit test assembly (granted access via
        /// <see cref="InternalsVisibleToAttribute"/>) to inject a generator that throws,
        /// so the <c>catch</c> branch in both Server Actions can be exercised deterministically.
        /// </summary>
        /// <param name="uuidGenerator">Delegate returning a UUID string on each call.</param>
        /// <remarks>Not part of the public API. Do not use from ODC application code.</remarks>
        internal UuidV7Service(Func<string> uuidGenerator)
        {
            _uuidGenerator = uuidGenerator;
        }

        /// <inheritdoc />
        public void GenerateUuidV7(
            out string uuid,
            out bool isSuccess,
            out string flightPath,
            out string errorMessage,
            string goldenThreadId = "",
            bool enableTelemetry = true)
        {
            uuid = string.Empty;
            isSuccess = false;
            errorMessage = string.Empty;
            flightPath = string.Empty;

            FlightRecorder? recorder = enableTelemetry
                ? new FlightRecorder("GenerateUuidV7", SanitizeGoldenThreadId(goldenThreadId))
                : null;

            try
            {
                recorder?.AddStep("GenerationStarted", "START");
                uuid = _uuidGenerator();
                recorder?.AddStep("GenerationCompleted", "END", uuid);
                isSuccess = true;
                flightPath = SafeFinalize(recorder, hasError: false);
            }
            catch (Exception ex)
            {
                // Only the exception type name is recorded — never ex.Message or ex.StackTrace —
                // to prevent information disclosure via the flightPath telemetry.
                recorder?.AddStep("GenerationFailed", "ERROR", ex.GetType().Name);
                errorMessage = "Failed to generate UUID v7: an unexpected error occurred.";
                flightPath = SafeFinalize(recorder, hasError: true);
            }
        }

        /// <inheritdoc />
        public void GenerateUuidV7Batch(
            int count,
            out List<string> uuids,
            out bool isSuccess,
            out string flightPath,
            out string errorMessage,
            string goldenThreadId = "",
            bool enableTelemetry = true)
        {
            uuids = [];
            isSuccess = false;
            errorMessage = string.Empty;
            flightPath = string.Empty;

            FlightRecorder? recorder = enableTelemetry
                ? new FlightRecorder("GenerateUuidV7Batch", SanitizeGoldenThreadId(goldenThreadId))
                : null;

            try
            {
                recorder?.AddStep("BatchStarted", "START", $"count={count}");

                if (count < 1 || count > MaxBatchSize)
                {
                    errorMessage = $"Count must be between 1 and {MaxBatchSize}.";
                    recorder?.AddStep("BatchValidationFailed", "ERROR", $"count={count}; {errorMessage}");
                    flightPath = SafeFinalize(recorder, hasError: true);
                    return;
                }

                recorder?.AddStep("BatchValidated", "INFO", $"count={count}");

                var result = new List<string>(count);
                for (int i = 0; i < count; i++)
                {
                    result.Add(_uuidGenerator());
                }

                uuids = result;
                isSuccess = true;
                recorder?.AddStep("BatchCompleted", "END", $"count={count}");
                flightPath = SafeFinalize(recorder, hasError: false);
            }
            catch (Exception ex)
            {
                // Only the exception type name is recorded — never ex.Message or ex.StackTrace —
                // to prevent information disclosure via the flightPath telemetry.
                recorder?.AddStep("BatchFailed", "ERROR", ex.GetType().Name);
                errorMessage = "Failed to generate UUID v7 batch: an unexpected error occurred.";
                flightPath = SafeFinalize(recorder, hasError: true);
            }
        }

        /// <summary>
        /// Finalizes the Flight Recorder session to JSON, swallowing any serialization failure
        /// so the caller receives the documented <c>flightPath</c> shape.
        /// </summary>
        /// <param name="recorder">The active Flight Recorder for this invocation, or <c>null</c> when telemetry was disabled by the caller.</param>
        /// <param name="hasError">True when the call is being finalized after a failure path.</param>
        /// <returns>
        /// Empty string when <paramref name="recorder"/> is <c>null</c> (telemetry disabled);
        /// the Flight Recorder JSON on success; or <see cref="DegradedFlightPath"/> if
        /// finalization itself throws.
        /// </returns>
        private static string SafeFinalize(FlightRecorder? recorder, bool hasError)
        {
            if (recorder is null) return string.Empty;
            try
            {
                return recorder.FinalizeBatchAsJson(hasError: hasError);
            }
            catch
            {
                return DegradedFlightPath;
            }
        }

        /// <summary>
        /// Normalizes a caller-supplied <c>goldenThreadId</c> before it is propagated to the
        /// Flight Recorder as the session <c>CorrelationId</c>.
        /// </summary>
        /// <param name="id">The raw value passed by the ODC caller. May be null or empty.</param>
        /// <returns>
        /// Empty string when input is null or empty; otherwise a copy truncated to
        /// <see cref="MaxGoldenThreadIdLength"/> characters with C0 control characters
        /// (0x00-0x1F) and DEL (0x7F) removed.
        /// </returns>
        /// <remarks>
        /// Stripping CR/LF and other control characters prevents a hostile caller from forging
        /// log lines in any downstream consumer that renders the value unescaped.
        /// </remarks>
        private static string SanitizeGoldenThreadId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;

            int len = Math.Min(id.Length, MaxGoldenThreadIdLength);
            var buffer = new char[len];
            int j = 0;
            for (int i = 0; i < len; i++)
            {
                char c = id[i];
                // Strip C0 controls (0x00-0x1F) and DEL (0x7F) so embedded CR/LF can't forge log lines downstream.
                if (c >= 0x20 && c != 0x7F) buffer[j++] = c;
            }
            return new string(buffer, 0, j);
        }
    }
}
