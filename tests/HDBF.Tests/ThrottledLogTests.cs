using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Logging;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the throttled log: 30 s per-key window, suppressed-repeat accounting and
    /// error context (chapter 10.4, REQ-SAFE-14/15). The clock is injected so the throttle is
    /// verified deterministically without sleeping.
    /// </summary>
    public static class ThrottledLogTests
    {
        public static void TestFirstWriteIsEmitted()
        {
            var sink = new RecordingSink();
            var clock = new FakeClock();
            var log = new ThrottledLog(sink.Write, clock.Read);

            log.Write(HdbfLogCategory.Info, "config.invalid", "using defaults");

            TestAssert.Equal(1, sink.Entries.Count);
            TestAssert.Equal(HdbfLogCategory.Info, sink.Entries[0].Category);
            TestAssert.Equal("config.invalid: using defaults", sink.Entries[0].Text);
        }

        public static void TestRepeatsWithinWindowAreSuppressedAndCounted()
        {
            var sink = new RecordingSink();
            var clock = new FakeClock();
            var log = new ThrottledLog(sink.Write, clock.Read);

            log.Write(HdbfLogCategory.Warn, "k", "same");
            clock.AdvanceSeconds(5);
            log.Write(HdbfLogCategory.Warn, "k", "same");

            TestAssert.Equal(1, sink.Entries.Count);
            TestAssert.Equal(1, log.SuppressedCount("k"));
        }

        public static void TestSuppressionCountAccumulates()
        {
            var clock = new FakeClock();
            var log = new ThrottledLog(null, clock.Read);

            log.Write(HdbfLogCategory.Warn, "k", "same");
            clock.AdvanceSeconds(1);
            log.Write(HdbfLogCategory.Warn, "k", "same");
            clock.AdvanceSeconds(1);
            log.Write(HdbfLogCategory.Warn, "k", "same");

            TestAssert.Equal(2, log.SuppressedCount("k"));
        }

        public static void TestNextEmissionCarriesSuppressedSuffix()
        {
            var sink = new RecordingSink();
            var clock = new FakeClock();
            var log = new ThrottledLog(sink.Write, clock.Read);

            log.Write(HdbfLogCategory.Warn, "save.degraded", "repair");
            clock.AdvanceSeconds(10);
            log.Write(HdbfLogCategory.Warn, "save.degraded", "repair");
            clock.AdvanceSeconds(10);
            log.Write(HdbfLogCategory.Warn, "save.degraded", "repair");
            TestAssert.Equal(1, sink.Entries.Count);

            clock.AdvanceSeconds(10); // now 30 s after the first emission
            log.Write(HdbfLogCategory.Warn, "save.degraded", "repair");

            TestAssert.Equal(2, sink.Entries.Count);
            TestAssert.True(sink.Entries[1].Text.EndsWith(" suppressed=2", StringComparison.Ordinal),
                "text was: " + sink.Entries[1].Text);
            TestAssert.Equal(0, log.SuppressedCount("save.degraded"), "counter resets after reporting");
        }

        public static void TestExactlyAtThirtySecondsIsEmitted()
        {
            var sink = new RecordingSink();
            var clock = new FakeClock();
            var log = new ThrottledLog(sink.Write, clock.Read);

            log.Write(HdbfLogCategory.Info, "k", "one");
            clock.SetSeconds(30);
            log.Write(HdbfLogCategory.Info, "k", "two");

            TestAssert.Equal(2, sink.Entries.Count);
        }

        public static void TestWindowSlidesFromLastActualEmission()
        {
            var sink = new RecordingSink();
            var clock = new FakeClock();
            var log = new ThrottledLog(sink.Write, clock.Read);

            log.Write(HdbfLogCategory.Info, "k", "one");
            clock.SetSeconds(20);
            log.Write(HdbfLogCategory.Info, "k", "two"); // suppressed
            clock.SetSeconds(31);
            log.Write(HdbfLogCategory.Info, "k", "three"); // 31 s after emission -> emitted

            TestAssert.Equal(2, sink.Entries.Count);
            TestAssert.True(sink.Entries[1].Text.Contains("suppressed=1"), sink.Entries[1].Text);
        }

        public static void TestKeysThrottleIndependently()
        {
            var sink = new RecordingSink();
            var log = new ThrottledLog(sink.Write, new FakeClock().Read);

            log.Write(HdbfLogCategory.Warn, "a", "first");
            log.Write(HdbfLogCategory.Warn, "b", "second");
            log.Write(HdbfLogCategory.Warn, "a", "first again");

            TestAssert.Equal(2, sink.Entries.Count);
            TestAssert.Equal(1, log.SuppressedCount("a"));
            TestAssert.Equal(0, log.SuppressedCount("b"));
        }

        public static void TestUnknownKeySuppressedCountIsZero()
        {
            var log = new ThrottledLog(null);
            TestAssert.Equal(0, log.SuppressedCount("never-written"));
        }

        public static void TestErrorIncludesEntityIdExceptionTypeAndStack()
        {
            var sink = new RecordingSink();
            var log = new ThrottledLog(sink.Write, new FakeClock().Read);

            Exception captured;
            try
            {
                throw new InvalidOperationException("boom");
            }
            catch (Exception exception)
            {
                captured = exception;
            }

            log.Error(HdbfLogCategory.Error, "save.degraded", "failed to load", captured, "12345");

            TestAssert.Equal(1, sink.Entries.Count);
            TestAssert.Equal(HdbfLogCategory.Error, sink.Entries[0].Category);
            var text = sink.Entries[0].Text;
            TestAssert.True(text.Contains("entityId=12345"), text);
            TestAssert.True(text.Contains("exception=System.InvalidOperationException: boom"), text);
            TestAssert.True(text.Contains("; stack="), text);
            TestAssert.True(text.Length > 20);
        }

        public static void TestErrorStackIsTrimmedToTwentyLines()
        {
            var sink = new RecordingSink();
            var log = new ThrottledLog(sink.Write, new FakeClock().Read);

            Exception captured;
            try
            {
                throw new InvalidOperationException("deep");
            }
            catch (Exception exception)
            {
                captured = exception;
            }

            log.Error(HdbfLogCategory.Error, "k", "ctx", captured);

            var text = sink.Entries[0].Text;
            var separatorCount = 0;
            foreach (var character in text)
            {
                if (character == '|')
                {
                    separatorCount++;
                }
            }

            TestAssert.True(separatorCount <= ThrottledLog.MaxStackLines - 1,
                "stack must be trimmed to at most 20 lines");
        }

        public static void TestErrorWithoutExceptionOrEntityHasNoContextNoise()
        {
            var sink = new RecordingSink();
            var log = new ThrottledLog(sink.Write, new FakeClock().Read);

            log.Error(HdbfLogCategory.Error, "k", "plain error", null);
            TestAssert.Equal("k: plain error", sink.Entries[0].Text);
        }

        public static void TestResetClearsThrottleAndCounters()
        {
            var sink = new RecordingSink();
            var log = new ThrottledLog(sink.Write, new FakeClock().Read);

            log.Write(HdbfLogCategory.Info, "k", "one");
            log.Write(HdbfLogCategory.Info, "k", "two");
            TestAssert.Equal(1, log.SuppressedCount("k"));

            log.Reset();

            TestAssert.Equal(0, log.SuppressedCount("k"));
            log.Write(HdbfLogCategory.Info, "k", "three");
            TestAssert.Equal(2, sink.Entries.Count);
        }

        public static void TestNullSinkNeverThrows()
        {
            var log = new ThrottledLog(null);

            log.Write(HdbfLogCategory.Info, "k", "no sink");
            log.Write(HdbfLogCategory.Info, "k", "throttled");
            log.Error(HdbfLogCategory.Error, "other", "failure", new InvalidOperationException("x"), "1");

            TestAssert.Equal(1, log.SuppressedCount("k"));
        }

        public static void TestLogKeyConstantsAreStable()
        {
            // Log keys are part of the support contract (chapter 9.5 conventions).
            TestAssert.Equal("config.invalid", HdbfLogKeys.ConfigInvalid);
            TestAssert.Equal("config.missing", HdbfLogKeys.ConfigMissing);
            TestAssert.Equal("config.migrated", HdbfLogKeys.ConfigMigrated);
            TestAssert.Equal("config.ignored-key", HdbfLogKeys.ConfigIgnoredKey);
            TestAssert.Equal("compat.missing", HdbfLogKeys.CompatMissing);
            TestAssert.Equal("compat.failed", HdbfLogKeys.CompatFailed);
            TestAssert.Equal("port.fault", HdbfLogKeys.PortFault);
            TestAssert.Equal("ui.world-icons", HdbfLogKeys.WorldIconsUnavailable);
            TestAssert.Equal("ui.world-icon-toggle", HdbfLogKeys.WorldIconToggleFailed);
        }

        public static void TestThrottleWindowIsThirtySeconds()
        {
            TestAssert.Equal(30d, ThrottledLog.ThrottleWindowSeconds);
        }

        private sealed class FakeClock
        {
            private double _seconds;

            public double Read() => _seconds;

            public void SetSeconds(double seconds) => _seconds = seconds;

            public void AdvanceSeconds(double seconds) => _seconds += seconds;
        }

        private sealed class RecordingSink
        {
            public List<(HdbfLogCategory Category, string Text)> Entries { get; } =
                new List<(HdbfLogCategory Category, string Text)>();

            public void Write(HdbfLogCategory category, string text) => Entries.Add((category, text));
        }
    }
}
