// ============================================================================
// HDBF v2 | Layer: Integration/Adapters | Owner: V2-INTEG-01
// Purpose: Tank network-connection test of the Timber Pipes binding. The mod assigns
//          TubeGraph only to transport pipes, so a liquid tank's Pipe.Graph is always null;
//          walking the tank pipe's ports to a connected transport pipe is the honest test.
// Requirements: REQ-V2-WATER-02, REQ-V2-COMPAT-01, REQ-V2-SAFE-05
// ============================================================================
using System;
using System.Collections;
using System.Reflection;

namespace HDBF.Integration.Adapters
{
    internal sealed partial class TimberPipesWaterBinding
    {
        /// <summary>
        /// True when a tank's own BuildingPipe is linked to a transport pipe (a real network
        /// node). This replaces the old <c>Pipe.Graph != null</c> filter for liquid tanks: the mod
        /// only assigns the graph to transport pipes, so the graph check alone was always false
        /// for a tank and made the whole tank draw path unreachable. Never throws.
        /// </summary>
        private bool PipeHasTransportLink(object? pipe)
        {
            if (pipe is null || _getPorts is null || _portIsConnected is null ||
                _tryGetConnectedBuilding is null || _getIsTransportPipe is null)
            {
                return false;
            }

            try
            {
                if (!TryResolveRegistry() || _registry is null)
                {
                    return false;
                }

                var ports = _getPorts.Invoke(pipe, null);
                var values = ports is null ? null : ReadPortValues(ports);
                if (values is null)
                {
                    return false;
                }

                foreach (var port in values)
                {
                    if (port is null)
                    {
                        continue;
                    }

                    if (!(_portIsConnected.Invoke(port, null) is bool connected) || !connected)
                    {
                        continue;
                    }

                    var arguments = new object?[] { port, null };
                    if (!(_tryGetConnectedBuilding.Invoke(_registry, arguments) is bool found) || !found)
                    {
                        continue;
                    }

                    if (arguments[1] is object other &&
                        _getIsTransportPipe.Invoke(other, null) is bool transport && transport)
                    {
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                SetFailure("water-pipe-port-read-exception", exception);
            }

            return false;
        }

        /// <summary>
        /// Non-generic values of the mod's FrozenDictionary port map: <see cref="IDictionary"/> when
        /// the runtime implements it, otherwise the reflected <c>get_Values</c> as the fallback.
        /// </summary>
        private IEnumerable? ReadPortValues(object ports)
        {
            if (ports is IDictionary dictionary)
            {
                return dictionary.Values as IEnumerable;
            }

            return _getPortValues?.Invoke(ports, null) as IEnumerable;
        }
    }
}
