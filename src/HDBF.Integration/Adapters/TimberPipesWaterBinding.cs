// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: The single, guarded reflection binding of the Timber Pipes water API used by
//          the v2 pipe water source: anchor resolution, registry lookup and the tank list.
//          No third-party type at compile time; every miss degrades, nothing throws.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections;
using System.Reflection;
using Bindito.Core;
using HDBF.Integration.Reflection;

namespace HDBF.Integration.Adapters
{
    /// <summary>
    /// Owns every Timber Pipes reflection lookup the v2 water source needs and nothing else
    /// (REQ-V2-COMPAT-01, REQ-V2-SAFE-05). The tank read/drain half is in
    /// <c>TimberPipesWaterBinding.Tank.cs</c>.
    ///
    /// All anchors below were read from the real Workshop assembly
    /// <c>TimberPipes.dll</c> (Timber Pipes 11.4.0, Workshop 3801726769, game 1.1.2.8) with
    /// <c>tools/ApiIndex</c> metadata/IL dumps and a reflection probe:
    /// <list type="bullet">
    /// <item><c>TimberPipes.Services.PipeRegistry.get_Tanks()</c>: the mod's own tank list;</item>
    /// <item><c>TimberPipes.Components.PipeTank.get_IsFinished()</c> / <c>get_VolumeM3()</c> /
    /// <c>get_FluidGoodId()</c> / <c>get_Pipe()</c> / <c>TakesGood(string)</c> and the public
    /// <c>ApplyVolume(float, string)</c> absolute-volume setter the mod's fluid solver uses;</item>
    /// <item><c>TimberPipes.Components.BuildingPipe.get_Graph()</c>: network membership
    /// (<c>null</c> = not connected to a pipe network);</item>
    /// <item><c>TimberPipes.Models.PipeFlowSolver.GoodsToVolume(int)</c>: <c>goods x 0.2 m3</c>
    /// (IL: <c>ldc.r4 0.2; mul</c>), i.e. one vanilla water good is 0.2 m3 of pipe fluid.</item>
    /// </list>
    ///
    /// No lookup throws: every miss is cached by <see cref="ExternalProxy"/> and turns into
    /// <c>false</c> plus <see cref="LastFailureReason"/>/<see cref="WaterGoodId"/>, so the caller
    /// can degrade to manual refill (REQ-V2-COMPAT-02). The binding owns no timer and never
    /// deducts water on its own (REQ-V2-WATER-02).
    /// </summary>
    internal sealed partial class TimberPipesWaterBinding
    {
        internal const string AssemblyName = "TimberPipes";
        internal const string RegistryTypeName = "TimberPipes.Services.PipeRegistry";
        internal const string TankTypeName = "TimberPipes.Components.PipeTank";
        internal const string PipeTypeName = "TimberPipes.Components.BuildingPipe";
        internal const string GraphTypeName = "TimberPipes.Models.PipeGraph";
        internal const string SolverTypeName = "TimberPipes.Models.PipeFlowSolver";
        internal const string FluidsTypeName = "TimberPipes.Models.PipeFluids";

        /// <summary>Public const of <c>TimberPipes.Models.PipeFluids</c>; read at bind time.</summary>
        internal const string WaterGoodIdField = "WaterGoodId";

        /// <summary>
        /// Value of <c>PipeFluids.WaterGoodId</c> in Timber Pipes 11.4.0, verified by a reflection
        /// probe against the real DLL. Used only when the const field cannot be read, so a renamed
        /// field alone cannot stop a correctly named water good from flowing.
        /// </summary>
        internal const string WaterGoodIdFallback = "Water";

        private const BindingFlags AnyStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        private static readonly string[] NoParameters = Array.Empty<string>();

        private readonly IContainer? _container;

        private Type? _registryType;
        private object? _registry;
        private float _volumePerUnit;
        private bool _scaleResolved;
        private string _waterGoodId = WaterGoodIdFallback;

        private MethodInfo? _getTanks;
        private MethodInfo? _getIsFinished;
        private MethodInfo? _getVolumeM3;
        private MethodInfo? _getFluidGoodId;
        private MethodInfo? _getPipe;
        private MethodInfo? _takesGood;
        private MethodInfo? _applyVolume;
        private MethodInfo? _getGraph;
        private MethodInfo? _goodsToVolume;

        /// <summary>Creates the binding; the DI container is only used to resolve the mod registry.</summary>
        internal TimberPipesWaterBinding(IContainer? container)
        {
            _container = container;
        }

        /// <summary>Most recent reflection failure reason, or <c>null</c> when the last call worked.</summary>
        internal string? LastFailureReason { get; private set; }

        /// <summary>Good id of the water the source draws; from the real mod const when readable.</summary>
        internal string WaterGoodId => _waterGoodId;

        /// <summary>Volume of one HDBF water unit in m3, from the real <c>GoodsToVolume(1)</c>.</summary>
        internal float VolumePerUnit
        {
            get
            {
                if (_scaleResolved)
                {
                    return _volumePerUnit;
                }

                _scaleResolved = true;
                _volumePerUnit = 0f;
                if (!TryBind() || _goodsToVolume is null)
                {
                    return _volumePerUnit;
                }

                try
                {
                    var value = _goodsToVolume.Invoke(null, new object[] { 1 });
                    if (value is float volume && volume > 0f && !float.IsNaN(volume))
                    {
                        _volumePerUnit = volume;
                    }
                }
                catch (Exception exception)
                {
                    SetFailure("water-volume-scale-exception", exception);
                }

                return _volumePerUnit;
            }
        }

        /// <summary>Resolves every anchor once; true when the mod water API is fully bound.</summary>
        internal bool TryBind()
        {
            try
            {
                if (_getTanks is not null && _applyVolume is not null && _getVolumeM3 is not null)
                {
                    LastFailureReason = null;
                    return true;
                }

                var registryType = ExternalProxy.FindType(AssemblyName, RegistryTypeName);
                var tankType = ExternalProxy.FindType(AssemblyName, TankTypeName);
                var pipeType = ExternalProxy.FindType(AssemblyName, PipeTypeName);
                var solverType = ExternalProxy.FindType(AssemblyName, SolverTypeName);
                if (registryType is null || tankType is null || pipeType is null || solverType is null)
                {
                    SetFailure("water-anchor-type-missing");
                    return false;
                }

                var getTanks = ExternalProxy.FindMethodBySignature(registryType, "get_Tanks", NoParameters, null);
                var isFinished = ExternalProxy.FindMethodBySignature(tankType, "get_IsFinished", NoParameters, "System.Boolean");
                var volumeM3 = ExternalProxy.FindMethodBySignature(tankType, "get_VolumeM3", NoParameters, null);
                var fluidGoodId = ExternalProxy.FindMethodBySignature(tankType, "get_FluidGoodId", NoParameters, null);
                var getPipe = ExternalProxy.FindMethodBySignature(tankType, "get_Pipe", NoParameters, null);
                var takesGood = ExternalProxy.FindMethodBySignature(tankType, "TakesGood",
                    new[] { "System.String" }, "System.Boolean");
                var applyVolume = ExternalProxy.FindMethodBySignature(tankType, "ApplyVolume",
                    new[] { "System.Single", "System.String" }, "System.Void");
                var getGraph = ExternalProxy.FindMethodBySignature(pipeType, "get_Graph", NoParameters, null);
                var goodsToVolume = ExternalProxy.FindMethodBySignature(solverType, "GoodsToVolume",
                    new[] { "System.Int32" }, "System.Single");
                if (getTanks is null || isFinished is null || volumeM3 is null || fluidGoodId is null ||
                    getPipe is null || takesGood is null || applyVolume is null || getGraph is null ||
                    goodsToVolume is null)
                {
                    // REQ-V2-COMPAT-01: the mod exists but its public water surface changed:
                    // degrade, never guess around a missing member (chapter 8.2, R-04).
                    SetFailure("water-anchor-signature-changed");
                    return false;
                }

                _registryType = registryType;
                _getTanks = getTanks;
                _getIsFinished = isFinished;
                _getVolumeM3 = volumeM3;
                _getFluidGoodId = fluidGoodId;
                _getPipe = getPipe;
                _takesGood = takesGood;
                _applyVolume = applyVolume;
                _getGraph = getGraph;
                _goodsToVolume = goodsToVolume;
                _waterGoodId = ReadWaterGoodId() ?? WaterGoodIdFallback;
                LastFailureReason = null;
                return true;
            }
            catch (Exception exception)
            {
                SetFailure("water-bind-exception", exception);
                return false;
            }
        }

        /// <summary>Resolves the mod's own session registry through the game DI container.</summary>
        internal bool TryResolveRegistry()
        {
            try
            {
                if (_registry is not null)
                {
                    LastFailureReason = null;
                    return true;
                }

                if (!TryBind() || _registryType is null || _container is null)
                {
                    SetFailure("water-registry-type-missing");
                    return false;
                }

                var registry = _container.GetInstance(_registryType);
                if (registry is null)
                {
                    SetFailure("water-registry-missing");
                    return false;
                }

                _registry = registry;
                LastFailureReason = null;
                return true;
            }
            catch (Exception exception)
            {
                SetFailure("water-registry-exception", exception);
                return false;
            }
        }

        /// <summary>Returns the mod's tank list as an untyped enumerable, or false on failure.</summary>
        internal bool TryGetTanks(out IEnumerable? tanks)
        {
            tanks = null;
            try
            {
                if (!TryResolveRegistry() || _registry is null || _getTanks is null)
                {
                    return false;
                }

                var value = _getTanks.Invoke(_registry, null);
                if (value is not IEnumerable enumerable)
                {
                    SetFailure("water-tanks-shape");
                    return false;
                }

                tanks = enumerable;
                LastFailureReason = null;
                return true;
            }
            catch (Exception exception)
            {
                SetFailure("water-tanks-exception", exception);
                return false;
            }
        }

        private string? ReadWaterGoodId()
        {
            try
            {
                var fluidsType = ExternalProxy.FindType(AssemblyName, FluidsTypeName);
                var field = fluidsType?.GetField(WaterGoodIdField, AnyStatic);
                return field?.GetValue(null) as string;
            }
            catch (Exception)
            {
                // The fallback constant is verified against the real DLL; a field read issue only
                // means the mod moved the constant, not that the water good changed.
                return null;
            }
        }

        private void SetFailure(string reason, Exception? exception = null)
        {
            LastFailureReason = exception is null ? reason : reason + ":" + exception.GetType().Name;
        }
    }
}
