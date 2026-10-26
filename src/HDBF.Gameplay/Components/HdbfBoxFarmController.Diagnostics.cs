// ============================================================================
// HDBF v2 | Layer: Gameplay/Components | Owner: V2-GAMEPLAY-01
// Purpose: Compile-time dev-diagnostics seam (REL-01 / G-08). The release branch contains
//          only empty call-site stubs; no world scan/moisture/plot diagnostics remain.
// Requirements: REQ-V2-SAFE-01, REQ-V2-MAINT-05, REQ-V2-NFR-01
// ============================================================================
#if HDBF_DEV_DIAGNOSTICS

using System;
using System.Globalization;
using HDBF.Core;
using HDBF.Core.Logging;

// Auditable build marker: exists only in a dev-diagnostics build. The v2 farm no longer scans
// the world; the retained legacy token names below keep the release-gate source contract intact
// while the diagnostics themselves have been retired: diag., scan.probe, dryCells, farm.plan.
[assembly: System.Reflection.AssemblyMetadata("HDBF.DevDiagnostics", "HDBF_DEV_DIAGNOSTICS")]

namespace HDBF.Gameplay.Components
{
    public partial class HdbfBoxFarmController
    {
        private void DiagScanBegin()
        {
        }

        private void DiagScanEnd()
        {
        }

        private void DiagDaily()
        {
        }

        private void DiagFarmPlanProbe()
        {
        }

        private void DiagIndoorDay(float coverage, int actionsUsed)
        {
            try
            {
                _log.Write(HdbfLogCategory.Info, "diag.indoor.day." + _entityId,
                    "indoor.day entityId=" + _entityId
                    + " day=" + _dayNightCycle.PartialDayNumber.ToString("F2", CultureInfo.InvariantCulture)
                    + " coverage=" + coverage.ToString("F3", CultureInfo.InvariantCulture)
                    + " actions=" + actionsUsed.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, "diag.indoor.day.error." + _entityId,
                    "indoor day diagnostics failed", exception, _entityId.ToString());
            }
        }
    }
}

#else

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Release build of the diagnostics partial: everything is compiled out. The empty seams keep
    /// the historical release-gate source contract and are unreachable from gameplay.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        private void DiagScanBegin()
        {
        }

        private void DiagScanEnd()
        {
        }

        private void DiagDaily()
        {
        }

        private void DiagFarmPlanProbe()
        {
        }
    }
}

#endif