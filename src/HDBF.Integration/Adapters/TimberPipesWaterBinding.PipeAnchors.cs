// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Optional anchor resolution of the Timber Pipes pipe/port surface used by the
//          farm-adjacent intake and the tank network-connection fix. Every member is optional:
//          a missing one only disables that half, never the existing tank water anchors.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-COMPAT-02, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Reflection;
using HDBF.Integration.Reflection;

namespace HDBF.Integration.Adapters
{
    internal sealed partial class TimberPipesWaterBinding
    {
        private MethodInfo? _tryGetPipe;
        private MethodInfo? _tryGetConnectedBuilding;
        private MethodInfo? _getIsTransportPipe;
        private MethodInfo? _getPipeIsFinished;
        private MethodInfo? _getIsContaminated;
        private MethodInfo? _getNetworkGoodId;
        private MethodInfo? _getFluidHeight;
        private MethodInfo? _removeFluid;
        private MethodInfo? _getPorts;
        private MethodInfo? _getPortValues;
        private MethodInfo? _portIsConnected;

        /// <summary>True when every anchor the farm-adjacent intake needs was resolved.</summary>
        private bool IsIntakeBound => _tryGetPipe is not null && _getIsTransportPipe is not null &&
                                      _getPipeIsFinished is not null && _getIsContaminated is not null &&
                                      _getNetworkGoodId is not null && _getFluidHeight is not null &&
                                      _removeFluid is not null;

        /// <summary>
        /// Resolves the optional intake/port anchors. Never fails the main binding: a null member
        /// only disables the optional half, exactly like an older Timber Pipes version would.
        /// All signatures were read from the real TimberPipes 11.4.0 assembly (see the binding
        /// class summary); the same guarded ExternalProxy cache is used, so a miss is cached too.
        /// </summary>
        private void BindPipeAnchors(Type registryType, Type pipeType)
        {
            try
            {
                var portType = ExternalProxy.FindType(AssemblyName, PortTypeName);
                _tryGetPipe = ExternalProxy.FindMethodBySignature(registryType, "TryGetPipe",
                    new[] { "UnityEngine.Vector3Int", "TimberPipes.Components.BuildingPipe" }, "System.Boolean");
                _tryGetConnectedBuilding = ExternalProxy.FindMethodBySignature(registryType,
                    "TryGetConnectedBuilding",
                    new[] { "TimberPipes.Models.PipePort", "TimberPipes.Components.BuildingPipe" },
                    "System.Boolean");
                _getIsTransportPipe = ExternalProxy.FindMethodBySignature(pipeType, "get_IsTransportPipe",
                    NoParameters, "System.Boolean");
                _getPipeIsFinished = ExternalProxy.FindMethodBySignature(pipeType, "get_IsFinished",
                    NoParameters, "System.Boolean");
                _getIsContaminated = ExternalProxy.FindMethodBySignature(pipeType, "get_IsContaminated",
                    NoParameters, "System.Boolean");
                _getNetworkGoodId = ExternalProxy.FindMethodBySignature(pipeType, "get_NetworkGoodId",
                    NoParameters, "System.String");
                _getFluidHeight = ExternalProxy.FindMethodBySignature(pipeType, "get_FluidHeight",
                    NoParameters, "System.Single");
                _removeFluid = ExternalProxy.FindMethodBySignature(pipeType, "RemoveFluid",
                    new[] { "System.Single" }, "System.Void");
                _getPorts = ExternalProxy.FindMethodBySignature(pipeType, "get_Ports", NoParameters, null);
                _getPortValues = _getPorts is null
                    ? null
                    : ExternalProxy.FindMethodBySignature(_getPorts.ReturnType, "get_Values", NoParameters, null);
                _portIsConnected = portType is null
                    ? null
                    : ExternalProxy.FindMethodBySignature(portType, "get_IsConnected", NoParameters,
                        "System.Boolean");
            }
            catch (Exception exception)
            {
                // REQ-V2-SAFE-05: an optional anchor probe must never abort the main binding.
                SetFailure("water-pipe-anchor-bind-exception", exception);
            }
        }
    }
}
