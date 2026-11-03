using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Compat;

namespace HDBF.Tests
{
    /// <summary>
    /// L1 tests for the per-adapter compatibility state machine: the chapter 8.2 lifecycle, the
    /// three-strikes disable rule (REQ-COMPAT-07) and the neutral "missing mod" degradation
    /// (REQ-COMPAT-09/12). Pure logic, no game assemblies needed (REQ-TEST-03).
    /// </summary>
    public static class CompatStateMachineTests
    {
        public static void TestInitialStateIsUnknownAndRetryable()
        {
            var machine = new CompatStateMachine();

            TestAssert.Equal(HdbfAdapterHealth.Unknown, machine.Health);
            TestAssert.Equal(0, machine.FailureCount);
            TestAssert.False(machine.IsDetected);
            TestAssert.False(machine.IsActive);
            TestAssert.True(machine.CanRetry);
            TestAssert.Null(machine.LastFailureReason);
        }

        public static void TestDetectMovesToDetectedNeutralState()
        {
            var machine = new CompatStateMachine();

            TestAssert.True(machine.Detect());
            TestAssert.True(machine.IsDetected);
            TestAssert.False(machine.IsActive);
            TestAssert.Equal(HdbfAdapterHealth.Disabled, machine.Health);
            TestAssert.True(machine.CanRetry);
        }

        public static void TestInitializeSucceededActivates()
        {
            var machine = new CompatStateMachine();
            machine.Detect();

            TestAssert.True(machine.InitializeSucceeded());
            TestAssert.True(machine.IsActive);
            TestAssert.Equal(HdbfAdapterHealth.Ok, machine.Health);
            TestAssert.False(machine.CanRetry, "an active adapter needs no retry");
        }

        public static void TestInitializeBeforeDetectFails()
        {
            var machine = new CompatStateMachine();

            TestAssert.False(machine.InitializeSucceeded());
            TestAssert.False(machine.IsActive);
            TestAssert.Equal(HdbfAdapterHealth.Unknown, machine.Health);
        }

        public static void TestInitializeSucceededWhileActiveIsHarmless()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            machine.InitializeSucceeded();

            TestAssert.True(machine.InitializeSucceeded());
            TestAssert.Equal(HdbfAdapterHealth.Ok, machine.Health);
            TestAssert.Equal(0, machine.FailureCount);
        }

        public static void TestSingleFailureDegradesAndDeactivates()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            machine.InitializeSucceeded();

            machine.Fail("reflection-missing");

            TestAssert.Equal(1, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Degraded, machine.Health);
            TestAssert.False(machine.IsActive, "a faulted adapter must not keep serving traffic");
            TestAssert.True(machine.IsDetected);
            TestAssert.True(machine.CanRetry);
            TestAssert.Equal("reflection-missing", machine.LastFailureReason);
        }

        public static void TestThreeConsecutiveFailuresFailTheAdapter()
        {
            var machine = NewDetectedAdapter();

            machine.Fail("first");
            machine.Fail("second");
            TestAssert.Equal(2, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Degraded, machine.Health);
            TestAssert.True(machine.CanRetry);

            machine.Fail("third");
            TestAssert.Equal(CompatStateMachine.FailureThreshold, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health);
            TestAssert.False(machine.CanRetry, "REQ-COMPAT-07: no retry after the threshold");
            TestAssert.False(machine.Detect(), "a failed adapter cannot be re-detected this session");
            TestAssert.Equal("third", machine.LastFailureReason);
        }

        public static void TestFailureCountStopsAtThreshold()
        {
            var machine = NewDetectedAdapter();
            for (var attempt = 0; attempt < 5; attempt++)
            {
                machine.Fail("attempt " + attempt);
            }

            TestAssert.Equal(CompatStateMachine.FailureThreshold, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health);
        }

        public static void TestFailuresBelowThresholdCanRecover()
        {
            var machine = NewDetectedAdapter();
            machine.Fail("transient-1");
            machine.Fail("transient-2");

            TestAssert.True(machine.Detect());
            TestAssert.True(machine.InitializeSucceeded());

            TestAssert.Equal(0, machine.FailureCount, "a success resets the consecutive failure count");
            TestAssert.Null(machine.LastFailureReason);
            TestAssert.Equal(HdbfAdapterHealth.Ok, machine.Health);
            TestAssert.True(machine.IsActive);
        }

        public static void TestFailureWithoutDetectStillCounts()
        {
            var machine = new CompatStateMachine();

            machine.Fail("no-mod");

            TestAssert.Equal(1, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Degraded, machine.Health);
            TestAssert.False(machine.IsActive);
            TestAssert.True(machine.CanRetry);
        }

        public static void TestShutdownReturnsActiveAdapterToDetected()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            machine.InitializeSucceeded();

            machine.Shutdown();

            TestAssert.False(machine.IsActive);
            TestAssert.True(machine.IsDetected);
            TestAssert.Equal(HdbfAdapterHealth.Disabled, machine.Health);
            TestAssert.True(machine.CanRetry);
        }

        public static void TestShutdownIsIdempotent()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            machine.InitializeSucceeded();

            machine.Shutdown();
            machine.Shutdown();
            TestAssert.False(machine.IsActive);
            TestAssert.True(machine.IsDetected);

            var fresh = new CompatStateMachine();
            fresh.Shutdown();
            fresh.Shutdown();
            TestAssert.Equal(HdbfAdapterHealth.Unknown, fresh.Health);
            TestAssert.Equal(0, fresh.FailureCount);
        }

        public static void TestShutdownDoesNotReviveFailedAdapter()
        {
            var machine = NewDetectedAdapter();
            machine.Fail("a");
            machine.Fail("b");
            machine.Fail("c");

            machine.Shutdown();

            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health);
            TestAssert.False(machine.CanRetry);
        }

        public static void TestMarkMissingIsNeutralDisabledState()
        {
            // REQ-COMPAT-12: a missing mod is a grey state, not an error.
            var machine = new CompatStateMachine();

            machine.MarkMissing("mod-not-installed");

            TestAssert.Equal(HdbfAdapterHealth.Disabled, machine.Health);
            TestAssert.Equal(0, machine.FailureCount, "missing is not a failure");
            TestAssert.False(machine.IsDetected);
            TestAssert.True(machine.CanRetry);
            TestAssert.Equal("mod-not-installed", machine.LastFailureReason);

            TestAssert.True(machine.Detect(), "installing the mod must allow detection again");
            TestAssert.Equal(HdbfAdapterHealth.Disabled, machine.Health);
            TestAssert.True(machine.IsDetected);
        }

        public static void TestMarkMissingNeverOverwritesFailedState()
        {
            var machine = NewDetectedAdapter();
            machine.Fail("a");
            machine.Fail("b");
            machine.Fail("c");

            machine.MarkMissing("ignored");

            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health);
        }

        public static void TestDetectWhileActiveKeepsAdapterActive()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            machine.InitializeSucceeded();

            TestAssert.True(machine.Detect());
            TestAssert.True(machine.IsActive);
            TestAssert.Equal(HdbfAdapterHealth.Ok, machine.Health);
        }

        public static void TestFailureThresholdMatchesChapterEighteenDefault()
        {
            // chapter 18.8: compat.failureThreshold default 3.
            TestAssert.Equal(3, CompatStateMachine.FailureThreshold);
        }

        public static void TestThreeFailuresInsideTheWindowFailTheAdapter()
        {
            // L-02(c): the caller supplies the monotonic time, so the policy stays pure Core logic.
            var machine = NewDetectedAdapter();

            machine.Fail("first", 100.0);
            machine.Fail("second", 130.0);
            TestAssert.Equal(2, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Degraded, machine.Health);
            TestAssert.True(machine.CanRetry, "one farm's failed draw degrades, it does not fail the adapter");

            machine.Fail("third", 159.0);
            TestAssert.Equal(CompatStateMachine.FailureThreshold, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health);
            TestAssert.False(machine.CanRetry, "three failures inside the window disable the adapter");
            TestAssert.Equal("third", machine.LastFailureReason);
        }

        public static void TestFailuresOutsideTheWindowDoNotAccumulate()
        {
            var machine = NewDetectedAdapter();

            machine.Fail("old-1", 0.0);
            machine.Fail("old-2", 10.0);
            machine.Fail("fresh", 100.0);

            TestAssert.Equal(1, machine.FailureCount,
                "failures older than the window must be dropped before counting");
            TestAssert.Equal(HdbfAdapterHealth.Degraded, machine.Health);
            TestAssert.True(machine.CanRetry);

            machine.Fail("fresh-2", 140.0);
            machine.Fail("fresh-3", 150.0);
            TestAssert.Equal(CompatStateMachine.FailureThreshold, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health,
                "three failures inside one window still disable the adapter");
        }

        public static void TestWindowBoundaryIsInclusive()
        {
            var exact = NewDetectedAdapter();
            exact.Fail("a", 0.0);
            exact.Fail("b", 30.0);
            exact.Fail("c", CompatStateMachine.FailureWindowSeconds);
            TestAssert.Equal(HdbfAdapterHealth.Failed, exact.Health,
                "a failure exactly FailureWindowSeconds old still belongs to the window");

            var outside = NewDetectedAdapter();
            outside.Fail("a", 0.0);
            outside.Fail("b", 30.0);
            outside.Fail("c", CompatStateMachine.FailureWindowSeconds + 0.001);
            TestAssert.Equal(2, outside.FailureCount, "a failure just outside the window is dropped");
            TestAssert.Equal(HdbfAdapterHealth.Degraded, outside.Health);
        }

        public static void TestSuccessClearsTheFailureWindow()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            machine.InitializeSucceeded();
            machine.Fail("a", 10.0);
            machine.Fail("b", 20.0);
            TestAssert.Equal(2, machine.FailureCount);

            TestAssert.True(machine.Detect());
            TestAssert.True(machine.InitializeSucceeded());
            TestAssert.Equal(0, machine.FailureCount, "a success clears every counted failure");
            TestAssert.Equal(HdbfAdapterHealth.Ok, machine.Health);

            machine.Fail("c", 30.0);
            TestAssert.Equal(1, machine.FailureCount,
                "failures from before the success must not come back");
        }

        public static void TestNonFiniteTimestampFallsBackToTheLegacyRule()
        {
            var machine = NewDetectedAdapter();

            machine.Fail("nan", double.NaN);
            machine.Fail("infinite", double.PositiveInfinity);
            machine.Fail("third", 0.0);

            TestAssert.Equal(CompatStateMachine.FailureThreshold, machine.FailureCount);
            TestAssert.Equal(HdbfAdapterHealth.Failed, machine.Health);
        }

        public static void TestFailureWindowIsSixtySeconds()
        {
            TestAssert.Equal(60.0, CompatStateMachine.FailureWindowSeconds, 1e-9,
                "L-02(c): the default 3-strike window is 60 s, not the whole session");
        }

        private static CompatStateMachine NewDetectedAdapter()
        {
            var machine = new CompatStateMachine();
            machine.Detect();
            return machine;
        }
    }
}
