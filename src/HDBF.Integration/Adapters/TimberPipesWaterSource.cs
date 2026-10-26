// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: HDBF.Core.Indoor.IWaterSource backed by Timber Pipes (INTERFACES section 6):
//          "draw the daily demand from the reachable pipe network", never tick, never conjure.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections;
using Bindito.Core;
using HDBF.Core;
using HDBF.Core.Indoor;
using HDBF.Core.Logging;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// The v2 pipe water source (INTERFACES section 6). Gameplay requests the daily demand once per
    /// day and adds only the returned amount to its tank, so this class is deliberately not a
    /// timer: it offers one atomic "draw up to the requested amount" operation (REQ-V2-WATER-02).
    ///
    /// Degradation matrix (REQ-V2-COMPAT-02, REQ-V2-SAFE-05):
    /// <list type="bullet">
    /// <item>mod missing / adaptor disabled/failed: the host never hands this source out;</item>
    /// <item>API anchors missing/signature changed: <see cref="IsAvailable"/> false and
    /// <see cref="TryDraw"/> false with actual 0, one throttled <c>compat.degraded</c> warning, and
    /// the failure is counted by the v1 three-strike state machine of the pipe adaptor;</item>
    /// <item>network present but dry: false with actual 0 and a throttled warning, never a failure;</item>
    /// <item>tank read/drain throws: per-tank isolation, the remaining tanks are still tried.</item>
    /// </list>
    /// Unit mapping: one HDBF water unit is one vanilla water good, whose pipe volume is
    /// <c>GoodsToVolume(1) = 0.2 m3</c> (real IL). The mod's own <c>ApplyVolume</c>/<c>pending</c>
    /// quantisation absorbs sub-good remainders, so a 1.2 unit day is not rounded up to two goods.
    /// </summary>
    public sealed class TimberPipesWaterSource : IWaterSource
    {
        /// <summary>Throttle identity: one water warning line per 30 s (REQ-V2-READ-03, chapter 10.4).</summary>
        internal const string LogKey = IndoorLogKeys.CompatDegraded + ":HDBF.Adapter.TimberPipes:water";

        private readonly TimberPipesWaterBinding _binding;
        private readonly Func<HdbfAdapterHealth>? _health;
        private readonly Action<string>? _reportFailure;
        private readonly IHdbfLog? _log;
        /// <summary>
        /// Creates the source; every dependency is optional so a broken DI graph or test host
        /// degrades to "no water" instead of breaking building operation (REQ-V2-SAFE-04).
        /// </summary>
        internal TimberPipesWaterSource(IHdbfLog? log, IContainer? container,
            Func<HdbfAdapterHealth>? health, Action<string>? reportFailure)
        {
            _log = log;
            _binding = new TimberPipesWaterBinding(container);
            _health = health;
            _reportFailure = reportFailure;
        }

        /// <inheritdoc />
        public WaterSupplyKind Kind => WaterSupplyKind.Pipe;
        /// <summary>
        /// True when at least one finished pipe tank is connected to a network, takes the water
        /// good and currently holds drainable water. A query only: it never drains and never
        /// counts an adaptor failure (only <see cref="TryDraw"/> does that).
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                try
                {
                    if (!IsAdaptorServing() || !TryEnumerateFeedableTanks(out var tanks))
                    {
                        TryLog("no usable pipe water is available; manual refill stays possible");
                        return false;
                    }

                    foreach (var tank in tanks!)
                    {
                        if (TryDescribeFeedableTank(tank, out var state) && state.VolumeM3 > 0f)
                        {
                            return true;
                        }
                    }
                }
                catch (Exception)
                {
                    // A query must never throw into the panel/tick (REQ-V2-SAFE-01).
                }

                TryLog("no usable pipe water is available; manual refill stays possible");
                return false;
            }
        }

        /// <summary>
        /// True when a finished, network-connected pipe tank that accepts water exists at all (even
        /// while dry). Used by the adaptor host for <c>IsPipeConnected</c>.
        /// </summary>
        internal bool HasNetwork
        {
            get
            {
                try
                {
                    if (!IsAdaptorServing() || !TryEnumerateFeedableTanks(out var tanks))
                    {
                        return false;
                    }

                    foreach (var tank in tanks!)
                    {
                        if (TryDescribeFeedableTank(tank, out _))
                        {
                            return true;
                        }
                    }
                }
                catch (Exception)
                {
                    // Query only; never throw into Gameplay/UI (REQ-V2-SAFE-01).
                }
                return false;
            }
        }

        /// <inheritdoc />
        public bool TryDraw(float amount, out float actual)
        {
            actual = 0f;
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
            {
                return false;
            }

            try
            {
                if (!IsAdaptorServing())
                {
                    TryLog("pipe water draw skipped: adaptor not serving");
                    return false;
                }

                if (!_binding.TryBind())
                {
                    FailDraw("water-api-unavailable");
                    return false;
                }

                var scale = _binding.VolumePerUnit;
                if (!(scale > 0f) || float.IsNaN(scale))
                {
                    FailDraw("water-unit-scale-missing");
                    return false;
                }

                if (!TryEnumerateFeedableTanks(out var tanks))
                {
                    FailDraw(_binding.LastFailureReason ?? "water-tanks-unavailable");
                    return false;
                }

                var wantedM3 = amount * scale;
                var drawnM3 = 0f;
                var anyTank = false;
                foreach (var tank in tanks!)
                {
                    if (drawnM3 >= wantedM3 - (scale * 0.001f))
                    {
                        break;
                    }

                    if (!TryDescribeFeedableTank(tank, out var state))
                    {
                        if (_binding.LastFailureReason is not null)
                        {
                            FailDraw(_binding.LastFailureReason);
                        }

                        continue;
                    }

                    anyTank = true;
                    var needM3 = wantedM3 - drawnM3;
                    if (state.VolumeM3 <= 0f || needM3 <= 0f)
                    {
                        continue;
                    }

                    // REQ-V2-WATER-02: one bounded draw per request; the mod's ApplyVolume is the
                    // only thing that removes water, and it removes real pipe fluid (no conjuring).
                    if (_binding.TryDrawVolume(tank, needM3, out var tankDrawnM3) && tankDrawnM3 > 0f)
                    {
                        drawnM3 += tankDrawnM3;
                    }
                    else if (_binding.LastFailureReason is not null)
                    {
                        FailDraw(_binding.LastFailureReason);
                    }
                }

                if (!(drawnM3 > 0f) || float.IsNaN(drawnM3))
                {
                    actual = 0f;
                    if (anyTank)
                    {
                        // Network exists but nothing drainable: a dry pipe is a warning, not a
                        // failure of the external API.
                        TryLog("pipe network is dry; no drainable water this request");
                    }
                    else
                    {
                        // "Nothing built yet" is a play state, not an external API failure, so it
                        // must not consume the three-strike budget.
                        TryLog("no water tank in the pipe network; manual refill stays possible");
                    }

                    return false;
                }

                actual = drawnM3 / scale;
                return actual > 0f && !float.IsNaN(actual);
            }
            catch (Exception exception)
            {
                actual = 0f;
                FailDraw("water-draw-exception:" + exception.GetType().Name);
                return false;
            }
        }

        private bool TryEnumerateFeedableTanks(out IEnumerable? tanks)
        {
            tanks = null;
            if (!_binding.TryBind() || !_binding.TryGetTanks(out var all) || all is null)
            {
                return false;
            }

            tanks = all;
            return true;
        }

        private bool TryDescribeFeedableTank(object? tank, out PipeTankState state)
        {
            state = default;
            if (!_binding.TryDescribeTank(tank, out var candidate) || !candidate.IsFinished ||
                !candidate.HasPipeGraph || !candidate.TakesWaterGood)
            {
                return false;
            }

            // A contaminated/badwater tank must not feed the indoor water tank.
            if (candidate.FluidGoodId is not null &&
                !string.Equals(candidate.FluidGoodId, _binding.WaterGoodId, StringComparison.Ordinal))
            {
                return false;
            }

            state = candidate;
            return true;
        }

        private bool IsAdaptorServing()
        {
            if (_health is null)
            {
                return true;
            }

            var health = _health();
            // Degraded still serves so the v1 three-strike machine can reach Failed on its own;
            // Disabled/Failed/Unknown degrade to manual refill immediately (REQ-V2-COMPAT-02).
            return health == HdbfAdapterHealth.Ok || health == HdbfAdapterHealth.Degraded;
        }

        private void FailDraw(string reason)
        {
            TryLog("pipe water draw failed (" + reason + "); falling back to manual refill");
            try
            {
                _reportFailure?.Invoke(reason);
            }
            catch (Exception)
            {
                // REQ-V2-SAFE-01: failure reporting must never escape the water draw.
            }
        }

        private void TryLog(string message)
        {
            try
            {
                _log?.Write(HdbfLogCategory.Compat, LogKey, message);
            }
            catch (Exception)
            {
                // Diagnostics must never break the simulation (REQ-V2-SAFE-01).
            }
        }
    }
}
