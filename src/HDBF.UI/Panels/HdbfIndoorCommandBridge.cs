// ============================================================================
// HDBF v2 | Layer: UI/Panels | Owner: V2-UI-01
// Purpose: Duck-typed read/command bridge from the UI to the gameplay singletons. UI holds only
//          the Core IBoxFarmQuery / IBoxFarmCommand instances; this bridge resolves the v2 read
//          member on the query singleton and the two v2 write members gameplay adds to the
//          command singleton by name once, then calls them through cached MethodInfo. No
//          HDBF.Gameplay type is referenced (the UI assembly may only reference HDBF.Core), and
//          every call degrades instead of throwing.
// Requirements: REQ-V2-UI-01/02, REQ-V2-SAFE-01/04, REQ-V2-READ-04
// ============================================================================
using System;
using System.Reflection;
using HDBF.Core.Indoor;
using HDBF.UI.Localization;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// Small reflection adapter for the three v2 members the panel needs beyond the frozen Core
    /// interfaces:
    /// <list type="bullet">
    /// <item><c>IndoorFarmSnapshot? GetIndoorSnapshot(Guid)</c> on the query singleton - the panel read model;</item>
    /// <item><c>bool TrySetCropPolicy(Guid, CropPolicy, string, out string)</c> on the command singleton - crop strategy;</item>
    /// <item><c>bool TryRequestManualRefill(Guid, out string)</c> on the command singleton - the refill action.</item>
    /// </list>
    /// Why reflection instead of a compile-time call: HDBF.UI references HDBF.Core only (dependency
    /// rule of docs/v2/ARCHITECTURE.md section 1), and the Core read/command interfaces are frozen
    /// for this card. The bridge resolves everything once (not per frame), isolates every failure,
    /// and returns null/false with the generic failure key when a member is missing (older build or
    /// a broken gameplay side). This is the only place UI names gameplay members by text; all data
    /// still flows through Core DTOs.
    /// </summary>
    public sealed class HdbfIndoorCommandBridge
    {
        private readonly object? _queryService;
        private readonly object? _commandService;
        private readonly MethodInfo? _getSnapshot;
        private readonly MethodInfo? _setCropPolicy;
        private readonly MethodInfo? _requestRefill;

        /// <summary>Resolves the v2 members of both DI singletons once; null arguments are allowed.</summary>
        /// <param name="queryService">The <c>IBoxFarmQuery</c> instance injected by DI.</param>
        /// <param name="commandService">The <c>IBoxFarmCommand</c> instance injected by DI.</param>
        public HdbfIndoorCommandBridge(object? queryService, object? commandService)
        {
            _queryService = queryService;
            _commandService = commandService;
            if (queryService is not null)
            {
                _getSnapshot = Find(queryService.GetType(), "GetIndoorSnapshot", new[] { typeof(Guid) });
            }

            if (commandService is null)
            {
                return;
            }

            var type = commandService.GetType();
            _setCropPolicy = Find(type, "TrySetCropPolicy", new[]
            {
                typeof(Guid), typeof(CropPolicy), typeof(string), typeof(string).MakeByRefType(),
            });
            _requestRefill = Find(type, "TryRequestManualRefill", new[]
            {
                typeof(Guid), typeof(string).MakeByRefType(),
            });
        }

        /// <summary>True when the read model member was found (the panel can render).</summary>
        public bool IsAvailable => _queryService is not null && _getSnapshot is not null;

        /// <summary>
        /// Reads the Core panel snapshot of one farm, or null when the farm is unknown, not
        /// registered, the member is missing, or the gameplay side threw (all isolated).
        /// </summary>
        /// <param name="entityId">Runtime entity id of the selected building.</param>
        /// <returns>Immutable Core snapshot, or null when no data is available right now.</returns>
        public IndoorFarmSnapshot? GetIndoorSnapshot(Guid entityId)
        {
            if (_queryService is null || _getSnapshot is null || entityId == Guid.Empty)
            {
                return null;
            }

            try
            {
                return _getSnapshot.Invoke(_queryService, new object?[] { entityId }) as IndoorFarmSnapshot;
            }
            catch (Exception)
            {
                // A missing/renamed member or a broken farm hides the panel instead of crashing it.
                return null;
            }
        }

        /// <summary>
        /// Applies the crop strategy (auto rotation or one fixed crop) and returns the specific
        /// gameplay failure key on rejection (REQ-V2-UI-02).
        /// </summary>
        /// <param name="entityId">Runtime entity id of the selected building.</param>
        /// <param name="policy">Rotation or fixed.</param>
        /// <param name="fixedCropId">Crop id for <see cref="CropPolicy.Fixed"/>; ignored for rotation.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when gameplay applied the policy.</returns>
        public bool TrySetCropPolicy(Guid entityId, CropPolicy policy, string? fixedCropId, out string? failureKey)
        {
            failureKey = null;
            if (_commandService is null || _setCropPolicy is null || entityId == Guid.Empty)
            {
                failureKey = HdbfUiLocKeys.ErrorActionFailed;
                return false;
            }

            var args = new object?[] { entityId, policy, fixedCropId ?? string.Empty, null };
            try
            {
                var applied = _setCropPolicy.Invoke(_commandService, args) is bool result && result;
                failureKey = applied ? null : args[3] as string;
                return applied;
            }
            catch (Exception)
            {
                failureKey = HdbfUiLocKeys.ErrorActionFailed;
                return false;
            }
        }

        /// <summary>
        /// Requests one manual refill trip and returns the specific failure key when gameplay
        /// rejects it (no worker, blocked entrance, no source, unreachable/contaminated water,
        /// empty action budget or a pending request - see <c>HdbfIndoorCommandFailures</c>).
        /// </summary>
        /// <param name="entityId">Runtime entity id of the selected building.</param>
        /// <param name="failureKey">Specific failure key when false.</param>
        /// <returns>True when a refill was requested/performed.</returns>
        public bool TryRequestManualRefill(Guid entityId, out string? failureKey)
        {
            failureKey = null;
            if (_commandService is null || _requestRefill is null || entityId == Guid.Empty)
            {
                failureKey = HdbfUiLocKeys.ErrorActionFailed;
                return false;
            }

            var args = new object?[] { entityId, null };
            try
            {
                var performed = _requestRefill.Invoke(_commandService, args) is bool result && result;
                failureKey = performed ? null : args[1] as string;
                return performed;
            }
            catch (Exception)
            {
                failureKey = HdbfUiLocKeys.ErrorActionFailed;
                return false;
            }
        }

        private static MethodInfo? Find(Type type, string name, Type[] parameters)
        {
            try
            {
                return type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, parameters, null);
            }
            catch (AmbiguousMatchException)
            {
                return null;
            }
        }
    }
}
