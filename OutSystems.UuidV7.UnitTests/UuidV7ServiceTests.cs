using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

namespace OutSystems.UuidV7.UnitTests
{
    public class UuidV7ServiceTests
    {
        private readonly UuidV7Service _sut = new();

        [Fact]
        public void GenerateUuidV7_ReturnsParseableGuid()
        {
            _sut.GenerateUuidV7(out var uuid, out var isSuccess, out _, out var errorMessage);

            Assert.True(isSuccess);
            Assert.True(string.IsNullOrEmpty(errorMessage));
            Assert.True(Guid.TryParse(uuid, out _));
        }

        [Fact]
        public void GenerateUuidV7_VersionNibbleIsSeven()
        {
            _sut.GenerateUuidV7(out var uuid, out _, out _, out _);

            // Canonical form: xxxxxxxx-xxxx-Mxxx-Nxxx-xxxxxxxxxxxx
            // The 'M' character (index 14) is the version nibble.
            Assert.Equal('7', uuid[14]);
        }

        [Fact]
        public void GenerateUuidV7_VariantBitsAreRfc4122()
        {
            _sut.GenerateUuidV7(out var uuid, out _, out _, out _);

            // The 'N' character (index 19) holds the variant bits in its top.
            // RFC 4122 variant (10xx) => N must be one of 8, 9, a, b.
            // Guid.ToString("D") is documented to produce lowercase hex.
            Assert.Contains(uuid[19], new[] { '8', '9', 'a', 'b' });
        }

        [Fact]
        public void GenerateUuidV7_TimestampIsCurrentUnixMilliseconds()
        {
            // RFC 9562 §5.7: first 48 bits are unix_ts_ms. Decode and assert it's within
            // ±5 seconds of UtcNow — guards against the library silently switching to a
            // non-time-ordered algorithm.
            var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _sut.GenerateUuidV7(out var uuid, out _, out _, out _);
            var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            long ts = ExtractUnixMillisFromUuidV7(Guid.Parse(uuid));

            Assert.InRange(ts, before - 5000, after + 5000);
        }

        [Fact]
        public void GenerateUuidV7_SequentialCallsAreMonotonicallyIncreasing()
        {
            const int sampleCount = 50;
            var values = new List<Guid>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                _sut.GenerateUuidV7(out var uuid, out _, out _, out _);
                values.Add(Guid.Parse(uuid));
            }

            for (int i = 1; i < values.Count; i++)
            {
                Assert.True(
                    CompareUuidV7(values[i - 1], values[i]) < 0,
                    $"UUIDs not monotonic at index {i}: {values[i - 1]} >= {values[i]}");
            }
        }

        [Fact]
        public void GenerateUuidV7_EmitsFlightPathTelemetry()
        {
            _sut.GenerateUuidV7(out _, out _, out var flightPath, out _);

            Assert.False(string.IsNullOrWhiteSpace(flightPath));

            var batch = JObject.Parse(flightPath);
            Assert.Equal("GenerateUuidV7", (string?)batch["SessionData"]?["MethodName"]);
            Assert.False((bool)batch["SessionData"]!["IsError"]!);

            var stepNames = batch["LogsWithDetails"]!
                .Select(l => (string?)l["LogData"]?["StepName"])
                .ToList();
            Assert.Contains("GenerationStarted", stepNames);
            Assert.Contains("GenerationCompleted", stepNames);
        }

        [Fact]
        public void GenerateUuidV7_PropagatesGoldenThreadIdToFlightRecorder()
        {
            const string goldenThreadId = "test-trace-abc-123";

            _sut.GenerateUuidV7(out _, out _, out var flightPath, out _, goldenThreadId);

            var batch = JObject.Parse(flightPath);
            Assert.Equal(goldenThreadId, (string?)batch["SessionData"]?["CorrelationId"]);
        }

        [Fact]
        public void GenerateUuidV7_WhenGeneratorThrows_ReturnsIsSuccessFalseWithErrorTelemetry()
        {
            const string secretMarker = "secret-payload-do-not-leak";
            var failingSut = new UuidV7Service(() => throw new InvalidOperationException(secretMarker));

            failingSut.GenerateUuidV7(out var uuid, out var isSuccess, out var flightPath, out var errorMessage);

            Assert.False(isSuccess);
            Assert.Equal(string.Empty, uuid);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
            Assert.False(string.IsNullOrWhiteSpace(flightPath));

            // Lock the contract: the exception message must never leak into telemetry or user output.
            Assert.DoesNotContain(secretMarker, errorMessage);
            Assert.DoesNotContain(secretMarker, flightPath);

            var batch = JObject.Parse(flightPath);
            Assert.True((bool)batch["SessionData"]!["IsError"]!);

            var stepNames = batch["LogsWithDetails"]!
                .Select(l => (string?)l["LogData"]?["StepName"])
                .ToList();
            Assert.Contains("GenerationFailed", stepNames);
        }

        [Fact]
        public void GenerateUuidV7Batch_ReturnsRequestedCount()
        {
            const int count = 25;

            _sut.GenerateUuidV7Batch(count, out var uuids, out var isSuccess, out _, out var errorMessage);

            Assert.True(isSuccess);
            Assert.True(string.IsNullOrEmpty(errorMessage));
            Assert.Equal(count, uuids.Count);
            Assert.All(uuids, v => Assert.True(Guid.TryParse(v, out _)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1000)]
        public void GenerateUuidV7Batch_BoundaryCounts_Succeed(int count)
        {
            // Locks the documented 1..1000 inclusive contract against the production constant.
            Assert.Equal(1000, UuidV7Service.MaxBatchSize);

            _sut.GenerateUuidV7Batch(count, out var uuids, out var isSuccess, out _, out var errorMessage);

            Assert.True(isSuccess);
            Assert.True(string.IsNullOrEmpty(errorMessage));
            Assert.Equal(count, uuids.Count);
        }

        [Fact]
        public void GenerateUuidV7Batch_FullBatchIsMonotonicallyIncreasing()
        {
            // RFC 9562 §6.2 method 1: monotonic ordering must hold inside a same-millisecond
            // burst. A full-cap tight loop exercises UUIDNext's in-millisecond rand_a counter.
            _sut.GenerateUuidV7Batch(UuidV7Service.MaxBatchSize, out var uuids, out var isSuccess, out _, out _);

            Assert.True(isSuccess);

            var values = uuids.Select(Guid.Parse).ToList();
            for (int i = 1; i < values.Count; i++)
            {
                Assert.True(
                    CompareUuidV7(values[i - 1], values[i]) < 0,
                    $"Batch UUIDs not monotonic at index {i}: {values[i - 1]} >= {values[i]}");
            }
        }

        [Fact]
        public void GenerateUuidV7Batch_EmitsFlightPathTelemetry()
        {
            _sut.GenerateUuidV7Batch(10, out _, out _, out var flightPath, out _);

            Assert.False(string.IsNullOrWhiteSpace(flightPath));

            var batch = JObject.Parse(flightPath);
            Assert.Equal("GenerateUuidV7Batch", (string?)batch["SessionData"]?["MethodName"]);
            Assert.False((bool)batch["SessionData"]!["IsError"]!);

            var stepNames = batch["LogsWithDetails"]!
                .Select(l => (string?)l["LogData"]?["StepName"])
                .ToList();
            Assert.Contains("BatchStarted", stepNames);
            Assert.Contains("BatchValidated", stepNames);
            Assert.Contains("BatchCompleted", stepNames);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-1000)]
        public void GenerateUuidV7Batch_NonPositiveCount_ReturnsIsSuccessFalse(int count)
        {
            _sut.GenerateUuidV7Batch(count, out var uuids, out var isSuccess, out _, out var errorMessage);

            Assert.False(isSuccess);
            Assert.Empty(uuids);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Theory]
        [InlineData(1001)]
        [InlineData(10_000)]
        public void GenerateUuidV7Batch_AboveCap_ReturnsIsSuccessFalse(int count)
        {
            _sut.GenerateUuidV7Batch(count, out var uuids, out var isSuccess, out _, out var errorMessage);

            Assert.False(isSuccess);
            Assert.Empty(uuids);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1001)]
        public void GenerateUuidV7Batch_InvalidCount_EmitsErrorTelemetry(int count)
        {
            _sut.GenerateUuidV7Batch(count, out _, out var isSuccess, out var flightPath, out _);

            Assert.False(isSuccess);
            Assert.False(string.IsNullOrWhiteSpace(flightPath));

            var batch = JObject.Parse(flightPath);
            Assert.True((bool)batch["SessionData"]!["IsError"]!);

            var stepNames = batch["LogsWithDetails"]!
                .Select(l => (string?)l["LogData"]?["StepName"])
                .ToList();
            Assert.Contains("BatchStarted", stepNames);
            Assert.Contains("BatchValidationFailed", stepNames);
        }

        [Fact]
        public void GenerateUuidV7Batch_WhenGeneratorThrows_ReturnsIsSuccessFalseWithErrorTelemetry()
        {
            const string secretMarker = "secret-payload-do-not-leak";
            var failingSut = new UuidV7Service(() => throw new InvalidOperationException(secretMarker));

            failingSut.GenerateUuidV7Batch(5, out var uuids, out var isSuccess, out var flightPath, out var errorMessage);

            Assert.False(isSuccess);
            Assert.Empty(uuids);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
            Assert.False(string.IsNullOrWhiteSpace(flightPath));

            // Lock the contract: the exception message must never leak into telemetry or user output.
            Assert.DoesNotContain(secretMarker, errorMessage);
            Assert.DoesNotContain(secretMarker, flightPath);

            var batch = JObject.Parse(flightPath);
            Assert.True((bool)batch["SessionData"]!["IsError"]!);

            var stepNames = batch["LogsWithDetails"]!
                .Select(l => (string?)l["LogData"]?["StepName"])
                .ToList();
            Assert.Contains("BatchFailed", stepNames);
        }

        [Fact]
        public void GenerateUuidV7_GoldenThreadIdWithControlChars_StripsThemFromTelemetry()
        {
            // CRLF + tab + DEL embedded — must be stripped to prevent log forging downstream.
            const string hostile = "trace-abc\r\nFAKE LOG: admin_login_success\t";
            const string expected = "trace-abcFAKE LOG: admin_login_success";

            _sut.GenerateUuidV7(out _, out _, out var flightPath, out _, hostile);

            var batch = JObject.Parse(flightPath);
            var correlationId = (string?)batch["SessionData"]?["CorrelationId"];
            Assert.Equal(expected, correlationId);
        }

        [Fact]
        public void GenerateUuidV7_TelemetryDisabled_FlightPathIsEmptyButUuidStillReturned()
        {
            _sut.GenerateUuidV7(out var uuid, out var isSuccess, out var flightPath, out var errorMessage,
                goldenThreadId: "ignored", enableTelemetry: false);

            Assert.True(isSuccess);
            Assert.True(Guid.TryParse(uuid, out _));
            Assert.Equal(string.Empty, flightPath);
            Assert.Equal(string.Empty, errorMessage);
        }

        [Fact]
        public void GenerateUuidV7_TelemetryDisabled_GeneratorThrows_NoFlightPath_ButErrorMessageSet()
        {
            var failingSut = new UuidV7Service(() => throw new InvalidOperationException("boom"));

            failingSut.GenerateUuidV7(out var uuid, out var isSuccess, out var flightPath, out var errorMessage,
                enableTelemetry: false);

            Assert.False(isSuccess);
            Assert.Equal(string.Empty, uuid);
            Assert.Equal(string.Empty, flightPath);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
            Assert.DoesNotContain("boom", errorMessage);
        }

        [Fact]
        public void GenerateUuidV7Batch_TelemetryDisabled_FlightPathIsEmptyButUuidsStillReturned()
        {
            _sut.GenerateUuidV7Batch(10, out var uuids, out var isSuccess, out var flightPath, out var errorMessage,
                enableTelemetry: false);

            Assert.True(isSuccess);
            Assert.Equal(10, uuids.Count);
            Assert.Equal(string.Empty, flightPath);
            Assert.Equal(string.Empty, errorMessage);
        }

        [Fact]
        public void GenerateUuidV7Batch_TelemetryDisabled_InvalidCount_NoFlightPath_ButErrorMessageSet()
        {
            _sut.GenerateUuidV7Batch(0, out var uuids, out var isSuccess, out var flightPath, out var errorMessage,
                enableTelemetry: false);

            Assert.False(isSuccess);
            Assert.Empty(uuids);
            Assert.Equal(string.Empty, flightPath);
            Assert.False(string.IsNullOrWhiteSpace(errorMessage));
        }

        [Fact]
        public void GenerateUuidV7_GoldenThreadIdOverLengthCap_IsTruncated()
        {
            // Cap at 200 chars — 1000-char input must not be persisted in full.
            string oversize = new('a', 1000);

            _sut.GenerateUuidV7(out _, out _, out var flightPath, out _, oversize);

            var batch = JObject.Parse(flightPath);
            var correlationId = (string?)batch["SessionData"]?["CorrelationId"];
            Assert.Equal(200, correlationId!.Length);
        }

        /// <summary>
        /// Compares two UUID v7 values by their natural byte order (big-endian timestamp first).
        /// Guid's default comparison reorders the first 3 fields (Microsoft layout), so we cannot use it.
        /// </summary>
        private static int CompareUuidV7(Guid a, Guid b)
        {
            byte[] ba = ToBigEndianBytes(a);
            byte[] bb = ToBigEndianBytes(b);
            for (int i = 0; i < 16; i++)
            {
                int c = ba[i].CompareTo(bb[i]);
                if (c != 0) return c;
            }
            return 0;
        }

        private static long ExtractUnixMillisFromUuidV7(Guid g)
        {
            byte[] b = ToBigEndianBytes(g);
            // First 48 bits = unix_ts_ms (RFC 9562 §5.7).
            return ((long)b[0] << 40) | ((long)b[1] << 32) | ((long)b[2] << 24)
                 | ((long)b[3] << 16) | ((long)b[4] << 8) | b[5];
        }

        private static byte[] ToBigEndianBytes(Guid g)
        {
            byte[] b = g.ToByteArray();
            // Guid.ToByteArray reverses the first 4, next 2, and next 2 bytes (Microsoft layout).
            // Swap them back to big-endian (RFC 9562 byte order).
            (b[0], b[3]) = (b[3], b[0]);
            (b[1], b[2]) = (b[2], b[1]);
            (b[4], b[5]) = (b[5], b[4]);
            (b[6], b[7]) = (b[7], b[6]);
            return b;
        }
    }
}
