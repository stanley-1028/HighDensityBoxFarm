using System;
using HDBF.Core;
using HDBF.Core.Compat;
using HDBF.Core.Growth;
using HDBF.Core.Logging;
using HDBF.Integration.Harmony;
using Timberborn.SingletonSystem;

namespace HDBF.Integration
{
    /// <summary>
    /// Central register/unregister of every Harmony patch of the mod (REQ-COMPAT-16: "由
    /// HDBF.Integration.HarmonyBootstrap 统一注册与注销"). Registered as a game-context singleton by
    /// <see cref="HdbfIntegrationConfigurator"/>; Bindito calls <see cref="Load"/> when the session
    /// container is ready and <see cref="Unload"/> when it is torn down.
    ///
    /// This class holds no HarmonyLib types and no static state. The install session
    /// (<see cref="IHdbfPatchSession"/>) owns the single Harmony instance and the runtime bridge; if
    /// the Harmony mod is absent or the target signature changed, installation logs one
    /// <c>HDBF/Warn</c> and the mod keeps its pre-patch D-2 behaviour (chapter 8.4, REQ-COMPAT-17/18).
    ///
    /// Feature switch: <c>features.growthMultiplierPatch</c> (default on). When off, nothing is
    /// installed and no reflection into Harmony happens — the shipped D-2 behaviour is byte-for-byte
    /// the pre-patch behaviour.
    /// </summary>
    public sealed class HarmonyBootstrap : ILoadableSingleton, IUnloadableSingleton
    {
        private readonly IHdbfLog _log;
        private readonly IGrowthMultiplierSource _growthSource;
        private readonly ICompatRegistry _compat;

        private IHdbfPatchSession? _session;

        public HarmonyBootstrap(IHdbfLog log, IGrowthMultiplierSource growthSource, ICompatRegistry compat)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _growthSource = growthSource ?? throw new ArgumentNullException(nameof(growthSource));
            _compat = compat ?? throw new ArgumentNullException(nameof(compat));
        }

        /// <summary>True while the approved patch is installed for this session (diagnostics).</summary>
        public bool IsPatchInstalled => _session is not null && _session.IsInstalled;

        /// <inheritdoc />
        public void Load()
        {
            try
            {
                if (!_growthSource.IsPatchEnabled)
                {
                    _log.Write(HdbfLogCategory.Compat, HdbfLogKeys.HarmonyPatchUnavailable,
                        "features.growthMultiplierPatch=false; no Harmony patch is installed and the "
                        + "pre-patch D-2 behaviour is used");
                    return;
                }

                _session = HdbfHarmonyInstaller.TryInstall(_log, _growthSource, _compat);
                if (_session is null)
                {
                    _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.HarmonyPatchUnavailable,
                        "the approved growth Postfix is not active; managed crops keep vanilla growth "
                        + "time (D-2 behaviour, no error)");
                }
            }
            catch (Exception exception)
            {
                _session = null;
                try
                {
                    _log.Error(HdbfLogCategory.Error, HdbfLogKeys.HarmonyPatchFailed,
                        "Harmony patch bootstrap failed; pre-patch D-2 behaviour is kept", exception);
                }
                catch (Exception)
                {
                    // Bootstrap must never throw into the game loop (REQ-SAFE-02).
                }
            }
        }

        /// <inheritdoc />
        public void Unload()
        {
            try
            {
                _session?.Shutdown();
            }
            catch (Exception exception)
            {
                try
                {
                    _log.Error(HdbfLogCategory.Error, HdbfLogKeys.HarmonyPatchFailed,
                        "Harmony patch shutdown failed; the process is exiting this session", exception);
                }
                catch (Exception)
                {
                    // Unload must never throw.
                }
            }
            finally
            {
                _session = null;
            }
        }
    }
}
