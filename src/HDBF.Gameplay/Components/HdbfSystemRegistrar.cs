using System;
using System.Collections.Generic;
using System.IO;
using HDBF.Core;
using HDBF.Core.Balance;
using HDBF.Core.Config;
using HDBF.Core.Logging;
using UnityEngine;
using Timberborn.SingletonSystem;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Game-context singleton that loads <c>HDBF_Config.json</c> once per session and exposes the
    /// resolved configuration, the resolved tier table and the throttled log (chapters 10.3,
    /// 10.4, 18; REQ-IMPL-03). It implements <see cref="IHdbfLog"/> itself so every other
    /// singleton can depend on the interface without a second binding to keep in sync.
    ///
    /// Failure policy (REQ-SAFE-10): a missing, unreadable or invalid file never throws; the
    /// compiled-in <see cref="HdbfConfig.Default"/> and <see cref="TierCatalog"/> table are used
    /// and the fallback is logged at Warn level.
    /// </summary>
    public sealed class HdbfSystemRegistrar : ILoadableSingleton, IHdbfLog, IHdbfGameConfigProvider
    {
        private const string LogKeyStartup = "startup";

        private static readonly string[] ConfigFileCandidates =
        {
            "HDBF_Config.json",
            "Hdbf_Config.json",
            Path.Combine("Data", "HDBF_Config.json"),
            Path.Combine("Data", "Hdbf_Config.json"),
        };

        private readonly object _gate = new object();
        private readonly ThrottledLog _log;
        private HdbfConfig _config = HdbfConfig.Default;
        private IReadOnlyList<TierProfile> _profiles = TierCatalog.All;
        private bool _loaded;
        private string? _configPath;

        public HdbfSystemRegistrar()
        {
            _log = new ThrottledLog(WriteToGameLog);
        }

        /// <inheritdoc />
        public HdbfConfig Config
        {
            get
            {
                EnsureLoaded();
                return _config;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<TierProfile> Profiles
        {
            get
            {
                EnsureLoaded();
                return _profiles;
            }
        }

        /// <inheritdoc />
        public bool TryGetTierProfile(string? blueprintId, HdbfTier fallbackTier, out TierProfile profile)
        {
            EnsureLoaded();
            if (TierCatalog.TryGetByBlueprintId(blueprintId, out profile))
            {
                return true;
            }

            var index = (int)fallbackTier;
            if (index >= 0 && index < _profiles.Count)
            {
                profile = _profiles[index];
                return false;
            }

            profile = TierCatalog.Get(HdbfTier.Basic);
            return false;
        }

        /// <inheritdoc />
        public void Load()
        {
            EnsureLoaded();
            _log.Write(HdbfLogCategory.Info, LogKeyStartup,
                "HDBF " + HdbStartupContext.ModVersion + " ready; tiers=" + _profiles.Count
                + "; modPath=" + (HdbStartupContext.ModPath ?? "<unknown>")
                + "; config=" + (_configPath ?? "<compiled defaults>"));
        }

        // ------------------------------------------------------------------ IHdbfLog

        /// <inheritdoc />
        public void Write(HdbfLogCategory category, string key, string message)
        {
            _log.Write(category, key, message);
        }

        /// <inheritdoc />
        public void Error(HdbfLogCategory category, string key, string message, Exception? exception,
            string? entityId = null)
        {
            _log.Error(category, key, message, exception, entityId);
        }

        /// <inheritdoc />
        public int SuppressedCount(string key)
        {
            return _log.SuppressedCount(key);
        }

        /// <inheritdoc />
        public void Reset()
        {
            _log.Reset();
        }

        // ------------------------------------------------------------------ config loading

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            lock (_gate)
            {
                if (_loaded)
                {
                    return;
                }

                LoadInternal();
                _loaded = true;
            }
        }

        private void LoadInternal()
        {
            var path = FindConfigPath();
            if (path is null)
            {
                _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.ConfigMissing,
                    "HDBF_Config.json not found next to the mod; compiled-in defaults are used");
                ApplyDefaults();
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                _config = HdbfConfigCodec.Parse(json, WarnConfig);
                _profiles = HdbfConfigCodec.ResolveTiers(_config, WarnConfig);
                _configPath = path;
                if (_profiles.Count < 4)
                {
                    _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.ConfigInvalid,
                        "resolved tier table has " + _profiles.Count + " entries; falling back to defaults");
                    ApplyDefaults();
                }
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Warn, HdbfLogKeys.ConfigInvalid,
                    "HDBF_Config.json could not be read; compiled-in defaults are used", exception);
                ApplyDefaults();
            }
        }

        private void ApplyDefaults()
        {
            _config = HdbfConfig.Default;
            _profiles = HdbfConfigCodec.ResolveTiers(_config, WarnConfig);
        }

        private void WarnConfig(string message)
        {
            _log.Write(HdbfLogCategory.Warn, HdbfLogKeys.ConfigInvalid, message);
        }

        private static string? FindConfigPath()
        {
            var modPath = HdbStartupContext.ModPath;
            if (string.IsNullOrEmpty(modPath) || !Directory.Exists(modPath!))
            {
                return null;
            }

            for (var i = 0; i < ConfigFileCandidates.Length; i++)
            {
                var candidate = Path.Combine(modPath!, ConfigFileCandidates[i]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void WriteToGameLog(HdbfLogCategory category, string text)
        {
            var line = "[HDBF/" + CategoryName(category) + "] " + text;
            switch (category)
            {
                case HdbfLogCategory.Error:
                    Debug.LogError(line);
                    break;
                case HdbfLogCategory.Warn:
                    Debug.LogWarning(line);
                    break;
                default:
                    Debug.Log(line);
                    break;
            }
        }

        private static string CategoryName(HdbfLogCategory category)
        {
            switch (category)
            {
                case HdbfLogCategory.Warn:
                    return "Warn";
                case HdbfLogCategory.Error:
                    return "Error";
                case HdbfLogCategory.Compat:
                    return "Compat";
                default:
                    return "Info";
            }
        }
    }
}
