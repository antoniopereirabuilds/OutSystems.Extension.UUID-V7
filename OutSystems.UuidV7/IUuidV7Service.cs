using System.Collections.Generic;
using OutSystems.ExternalLibraries.SDK;

namespace OutSystems.UuidV7
{
    /// <summary>
    /// OutSystems ODC External Library that exposes RFC 9562 UUID v7 (time-ordered, 128-bit)
    /// generation as Server Actions. Wraps the UUIDNext NuGet package because .NET 8 has no
    /// native UUID v7 support.
    /// </summary>
    [OSInterface(
        Description = "Generates RFC 9562 UUID v7 values (time-ordered, lexicographically sortable) using the UUIDNext library.",
        Name = "UuidV7",
        IconResourceName = "OutSystems.UuidV7.resources.UUIDv7_icon.png"
    )]
    public interface IUuidV7Service
    {
        /// <summary>
        /// Generates a single UUID v7 in canonical hyphenated string form (RFC 9562).
        /// The first 48 bits encode a Unix-millisecond timestamp, making the value lexicographically sortable.
        /// </summary>
        /// <param name="uuid">The generated UUID v7 in canonical 8-4-4-4-12 hyphenated form (e.g. <c>018f1c4a-...</c>).</param>
        /// <param name="isSuccess">True if the operation completed without errors.</param>
        /// <param name="flightPath">Structured JSON audit trail written by the Flight Recorder. Non-empty on every call.</param>
        /// <param name="errorMessage">Error details if isSuccess is false; empty otherwise.</param>
        /// <param name="goldenThreadId">Optional Flight Recorder trace ID for distributed tracing. Defaults to empty string if omitted. Ignored when <paramref name="enableTelemetry"/> is false.</param>
        /// <param name="enableTelemetry">When true (default), the Flight Recorder audit trail is produced and returned in <paramref name="flightPath"/>. When false, telemetry is skipped entirely (no allocation, no JSON serialization) and <paramref name="flightPath"/> is empty. Use false in hot paths where the ~5 µs telemetry cost is not justified.</param>
        /// <remarks>
        /// ODC mapping: Server Action <c>GenerateUuidV7</c> in the <c>UuidV7</c> external library.
        /// Backed by <c>UUIDNext.Uuid.NewSequential()</c>; in-millisecond monotonicity is preserved by the library's rand_a counter.
        /// </remarks>
        [OSAction(
            Description = "Generates a single RFC 9562 UUID v7 in canonical hyphenated string form.",
            IconResourceName = "OutSystems.UuidV7.resources.UUIDv7_icon.png"
        )]
        void GenerateUuidV7(
            [OSParameter(Description = "The generated UUID v7 in canonical 8-4-4-4-12 hyphenated form.")]
            out string uuid,

            [OSParameter(Description = "True if the operation completed without errors.")]
            out bool isSuccess,

            [OSParameter(Description = "Structured JSON audit trail written by the Flight Recorder. Non-empty when telemetry is enabled; empty when enableTelemetry is false.")]
            out string flightPath,

            [OSParameter(Description = "Error details if isSuccess is false; empty otherwise.")]
            out string errorMessage,

            [OSParameter(Description = "Optional Flight Recorder trace ID. Ignored when enableTelemetry is false.")]
            string goldenThreadId = "",

            [OSParameter(Description = "Set to False to skip the Flight Recorder telemetry and avoid its overhead. Default True.")]
            bool enableTelemetry = true
        );

        /// <summary>
        /// Generates a batch of UUID v7 values. <paramref name="count"/> must be between 1 and 1000 inclusive.
        /// Out-of-range values produce <c>isSuccess = false</c> with a populated <paramref name="errorMessage"/> rather than throwing.
        /// </summary>
        /// <param name="count">Number of UUIDs to generate (1 to 1000 inclusive).</param>
        /// <param name="uuids">List of generated UUID v7 values, in monotonically increasing order. Empty when isSuccess is false.</param>
        /// <param name="isSuccess">True if the operation completed without errors.</param>
        /// <param name="flightPath">Structured JSON audit trail written by the Flight Recorder. Non-empty on every call.</param>
        /// <param name="errorMessage">Error details if isSuccess is false; empty otherwise.</param>
        /// <param name="goldenThreadId">Optional Flight Recorder trace ID for distributed tracing. Defaults to empty string if omitted. Ignored when <paramref name="enableTelemetry"/> is false.</param>
        /// <param name="enableTelemetry">When true (default), the Flight Recorder audit trail is produced and returned in <paramref name="flightPath"/>. When false, telemetry is skipped entirely (no allocation, no JSON serialization) and <paramref name="flightPath"/> is empty. The Flight Recorder cost is fixed (~5 µs) per call regardless of batch size, so for batches of 100+ UUIDs disabling telemetry rarely pays off.</param>
        /// <remarks>
        /// ODC mapping: Server Action <c>GenerateUuidV7Batch</c> in the <c>UuidV7</c> external library.
        /// The 1000 cap keeps a single ODC Lambda invocation comfortably within typical 256 MB / 30 s constraints.
        /// All UUIDs in the returned list satisfy the RFC 9562 §6.2 method 1 monotonicity guarantee.
        /// </remarks>
        [OSAction(
            Description = "Generates a batch of UUID v7 values. Count must be between 1 and 1000.",
            IconResourceName = "OutSystems.UuidV7.resources.UUIDv7_icon.png"
        )]
        void GenerateUuidV7Batch(
            [OSParameter(Description = "Number of UUIDs to generate (1 to 1000 inclusive).")]
            int count,

            [OSParameter(Description = "List of generated UUID v7 values, in monotonically increasing order.")]
            out List<string> uuids,

            [OSParameter(Description = "True if the operation completed without errors.")]
            out bool isSuccess,

            [OSParameter(Description = "Structured JSON audit trail written by the Flight Recorder. Non-empty when telemetry is enabled; empty when enableTelemetry is false.")]
            out string flightPath,

            [OSParameter(Description = "Error details if isSuccess is false; empty otherwise.")]
            out string errorMessage,

            [OSParameter(Description = "Optional Flight Recorder trace ID. Ignored when enableTelemetry is false.")]
            string goldenThreadId = "",

            [OSParameter(Description = "Set to False to skip the Flight Recorder telemetry and avoid its overhead. Default True.")]
            bool enableTelemetry = true
        );
    }
}
