// ---------------------------------------------------------------------------------------------
// REL-01 release gate: dev-only diagnostics (REQ-BAL-27 / G-08).
//
// Mechanism: compile-time constant HDBF_DEV_DIAGNOSTICS, set by the single MSBuild property
// HdbfDevDiagnostics (Directory.Build.props, default false). A release build does not contain
// the strings below at all — not merely "logging switched off": a player cannot re-enable them
// by editing HDBF_Config.json, and the model/GUI cannot reach them.
//
// Evidence build (one switch, diagnostics back on):
//     powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -DevDiagnostics
//     dotnet build src\HDBF.Gameplay\HDBF.Gameplay.csproj -c Release -p:HdbfDevDiagnostics=true
//
// The call sites in Scan.cs / Simulation.cs / Farming.cs stay unconditional and bind to the
// empty stubs in the release branch below, so the game-free L1 sources and the release build
// stay compilable; build.ps1 G-08 asserts the staged HDBF.Gameplay.dll contains neither the
// HDBF.DevDiagnostics marker nor any diag.* token. Rationale: docs/OPS.md §4.
// ---------------------------------------------------------------------------------------------
#if HDBF_DEV_DIAGNOSTICS

using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Logging;
using Timberborn.Navigation;
using UnityEngine;

// Auditable build marker: exists only in a dev-diagnostics build. build.ps1 asserts its absence
// in a release package (and its presence when it runs with -DevDiagnostics, the positive control).
[assembly: System.Reflection.AssemblyMetadata("HDBF.DevDiagnostics", "HDBF_DEV_DIAGNOSTICS")]

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// TEMPORARY in-game evidence diagnostics (rounds 9-16). Compiled in only when
    /// <c>HDBF_DEV_DIAGNOSTICS</c> is defined; the release build uses the empty stubs in the
    /// <c>#else</c> branch of this file (REQ-BAL-27: no debug output in the shipped build).
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        private string DiagKey(string suffix)
        {
            return "diag." + suffix + "." + _entityId;
        }

        private void DiagScanBegin(Vector3Int origin, int side)
        {
            try
            {
                _log.Write(HdbfLogCategory.Info, DiagKey("scan.begin"),
                    "scan.begin entityId=" + _entityId + " origin=" + origin + " side=" + side
                    + " entrance=" + (_blockObject is null ? "<null>" : _blockObject.HasEntrance.ToString())
                    + " " + DescribeCell(origin));
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, DiagKey("scan.begin.error"),
                    "scan.begin diagnostics failed", exception, _entityId.ToString());
            }
        }

        private void DiagScanEnd(int candidates, int selected, int granted)
        {
            try
            {
                _log.Write(HdbfLogCategory.Info, DiagKey("scan.end"),
                    "scan.end entityId=" + _entityId
                    + " scanned=" + _scanStats.Scanned
                    + " accepted=" + _scanStats.Accepted
                    + " candidates=" + candidates
                    + " selected=" + selected
                    + " granted=" + granted
                    + " registeredNow=" + (_record is null ? -1 : _record.PlotIds.Length)
                    + " " + _scanStats.DescribeSelectionDelta()
                    + " coverageAfterScan="
                    + _coverage.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                    + " rejects[terrain=" + _scanStats.TerrainRejects
                    + ",field=" + _scanStats.FieldRejects
                    + ",block=" + _scanStats.BlockRejects
                    + ",blocker=" + _scanStats.BlockerRejects
                    + ",reach=" + _scanStats.ReachabilityRejects
                    + ",excluded=" + _scanStats.Excluded
                    + ",claimed=" + _scanStats.Claimed + "]");

                LogFieldSweep();
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, DiagKey("scan.end.error"),
                    "scan.end diagnostics failed", exception, _entityId.ToString());
            }
        }

        /// <summary>
        /// Independent probe: walks the same 11x11 window and reports how many columns the game's
        /// planting-area validator accepts, so a failing filter can be told apart from a map with
        /// no arable cells at all.
        /// </summary>
        private void LogFieldSweep()
        {
            var origin = ResolveScanOrigin();
            var side = Math.Max(_profile.WorkRange, 1);
            var half = side / 2;
            var contains = 0;
            var canPlant = 0;
            var samples = new List<string>(6);
            var haveCrop = TryGetScanCropTemplate(out var crop);

            for (var dy = -half; dy <= half; dy++)
            {
                for (var dx = -half; dx <= half; dx++)
                {
                    var x = origin.x + dx;
                    var y = origin.y + dy;
                    if (!_terrainService.Contains(new Vector2Int(x, y)))
                    {
                        continue;
                    }

                    contains++;
                    var surface = _terrainService.GetTerrainHeight(new Vector3Int(x, y, 0));
                    var cell = new Vector3Int(x, y, surface);
                    var accepted = haveCrop && _plantingAreaValidator.CanPlant(cell, crop);
                    if (accepted)
                    {
                        canPlant++;
                        if (samples.Count < 6)
                        {
                            samples.Add("(" + x + "," + y + ",h=" + surface + ")");
                        }
                    }
                }
            }

            _log.Write(HdbfLogCategory.Info, DiagKey("scan.probe"),
                "scan.probe entityId=" + _entityId + " origin=" + origin + " side=" + side
                + " crop=" + (haveCrop ? crop : "<none>")
                + " contains=" + contains + " canPlant=" + canPlant
                + " samples=" + string.Join(" ", samples));
        }

        private void DiagDaily(int registered, int moist, int dry, int tankFed, float coverage)
        {
            try
            {
                _log.Write(HdbfLogCategory.Info, DiagKey("day"),
                    "day.tick entityId=" + _entityId
                    + " day=" + _dayNightCycle.PartialDayNumber.ToString("F2")
                    + " registered=" + registered
                    + " moist=" + moist
                    + " dry=" + dry
                    + " tankFed=" + tankFed
                    + " tank=" + (_record is null ? -1f : _record.TankWater).ToString("F2")
                    + " coverage=" + coverage.ToString("F3")
                    + " workers=" + (_workplace is null ? -1 : _workplace.NumberOfAssignedWorkers)
                    + " shutdown=" + _isShutdown
                    + " dryCells=" + DescribeDryPlots(6));
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, DiagKey("day.error"),
                    "day.tick diagnostics failed", exception, _entityId.ToString());
            }
        }

        /// <summary>
        /// Lists a few registered plots that report dry, with the decoded cell z and the terrain
        /// column ceiling the moisture lookup expects. Round-10 evidence: 40/60 plots were moist on
        /// an all-moisture-16 fixture; the two candidates left are a z mismatch between the stored
        /// plot cell and the column ceiling, or the moisture index lookup failing for that column.
        /// </summary>
        private string DescribeDryPlots(int max)
        {
            if (_record?.PlotIds is null || _record.PlotIds.Length == 0)
            {
                return "-";
            }

            var parts = new List<string>(max);
            for (var i = 0; i < _record.PlotIds.Length && parts.Count < max; i++)
            {
                var id = _record.PlotIds[i];
                var cell = HdbfPlotCellCodec.Decode(id);
                try
                {
                    if (_soilMoistureService.SoilIsMoist(cell))
                    {
                        continue;
                    }

                    var ceiling = _terrainService.GetTerrainHeight(new Vector3Int(cell.x, cell.y, 0));
                    var atCeiling = _terrainService.OnGround(new Vector3Int(cell.x, cell.y, ceiling));
                    parts.Add("(" + cell.x + "," + cell.y + ",z=" + cell.z + ",h=" + ceiling
                              + ",onGround=" + (atCeiling ? "1" : "0") + ")");
                }
                catch (Exception exception)
                {
                    parts.Add("(" + cell.x + "," + cell.y + ",probe-error=" + exception.GetType().Name + ")");
                }
            }

            return parts.Count == 0 ? "-" : string.Join(" ", parts);
        }

        /// <summary>
        /// One-shot farm-plan probe for the P1 path question: is the building's navigation
        /// <c>Accessible</c> wired at all, and can it path from its own entrance to itself? The
        /// round-10 log showed every grass plot failing with "no cached flow field", which can be
        /// either a bad destination node (plot cell) or an uninitialised entrance/access point.
        /// </summary>
        private void DiagFarmPlanProbe(int candidates, int queued)
        {
            try
            {
                var accessible = ResolveAccessible();
                var origin = ResolveScanOrigin();
                var world = NavigationCoordinateSystem.GridToWorld(origin);
                var reachSelf = _navigationService.DestinationIsReachableUnlimitedRange(world, world);
                string pathSelf;
                try
                {
                    if (accessible is null)
                    {
                        pathSelf = "no-accessible";
                    }
                    else if (accessible.FindTerrainPath(world, out var distance))
                    {
                        pathSelf = distance.ToString("F2");
                    }
                    else
                    {
                        pathSelf = "false";
                    }
                }
                catch (Exception exception)
                {
                    pathSelf = "throw:" + exception.GetType().Name;
                }

                _log.Write(HdbfLogCategory.Info, DiagKey("farm.plan"),
                    "farm.plan entityId=" + _entityId
                    + " candidates=" + candidates
                    + " queued=" + queued
                    + " accessible=" + (accessible is not null ? "1" : "0")
                    + " entrance=" + origin
                    + " reachSelf=" + (reachSelf ? "1" : "0")
                    + " pathSelf=" + pathSelf);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, DiagKey("farm.plan.error"),
                    "farm plan diagnostics failed", exception, _entityId.ToString());
            }
        }

        private string DescribeCell(Vector3Int cell)
        {
            try
            {
                var contains = _terrainService.Contains(new Vector2Int(cell.x, cell.y));
                var surface = _terrainService.GetTerrainHeight(new Vector3Int(cell.x, cell.y, 0));
                var below = _terrainService.CellIsField(new Vector3Int(cell.x, cell.y, surface - 1));
                var atSurface = _terrainService.CellIsField(new Vector3Int(cell.x, cell.y, surface));
                var above = _terrainService.CellIsField(new Vector3Int(cell.x, cell.y, surface + 1));
                var block = _blockService.AnyObjectAt(new Vector3Int(cell.x, cell.y, surface));
                return "cell=" + cell + " contains=" + contains + " surface=" + surface
                       + " field[surface-1]=" + below + " field[surface]=" + atSurface
                       + " field[surface+1]=" + above + " blockAtSurface=" + block;
            }
            catch (Exception exception)
            {
                return "cell=" + cell + " probe-error=" + exception.GetType().Name;
            }
        }
    }
}

#else

using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Release build of the diagnostics partial: everything is compiled out (no dev diagnostic
    /// strings, no diagnostics counters, no extra references in the metadata). These four empty
    /// methods exist only so the unconditional call sites in Scan.cs / Simulation.cs /
    /// Farming.cs keep compiling; they are not reachable through any user gesture, UI or config
    /// file. build.ps1 and HDBF.Tests assert this branch carries no dev-only token.
    /// </summary>
    public partial class HdbfBoxFarmController
    {
        private void DiagScanBegin(Vector3Int origin, int side)
        {
        }

        private void DiagScanEnd(int candidates, int selected, int granted)
        {
        }

        private void DiagDaily(int registered, int moist, int dry, int tankFed, float coverage)
        {
        }

        private void DiagFarmPlanProbe(int candidates, int queued)
        {
        }
    }
}

#endif
