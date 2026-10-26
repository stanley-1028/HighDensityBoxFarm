using System;
using System.Collections.Generic;
using HDBF.Core;
using HDBF.Core.Abstractions;
using HDBF.Core.Logging;
using HDBF.Core.Warnings;
using Timberborn.SingletonSystem;
using UnityEngine;

namespace HDBF.Gameplay.Components
{
    /// <summary>
    /// Session singleton that caches the warning subjects of the farms the world-icon pass has
    /// processed and rebuilds the cross-building groups at most once per second (chapter 14.2
    /// cross-building grouping of the same warning (chapter 14.2), and the warning list re-sorted at most once per second (chapter 14.6: cached result / stable order).
    ///
    /// Why a separate cache:
    /// * UI must not scan every building on refresh; <see cref="IBoxFarmWarningQuery"/> returns the
    ///   immutable list built by the last rebuild;
    /// * only on-screen buildings are reported (chapter 14.6), so entries expire after
    ///   <c>ui.warningAggregateTtlSeconds</c> instead of turning into a whole-map warning list;
    /// * the rebuild is time-budgeted to once per second, independent of panel refresh rate.
    ///
    /// No static mutable state: the map, buffers and timers are instance fields owned by the DI
    /// singleton. All calls are main-thread only.
    /// </summary>
    public sealed class HdbfWarningBoard : IBoxFarmWarningQuery, IUpdatableSingleton, IUnloadableSingleton
    {
        private const string LogKey = "ui.warning-board";

        private readonly IHdbfGameConfigProvider _configProvider;
        private readonly IHdbfLog _log;

        private readonly Dictionary<Guid, Entry> _entries = new Dictionary<Guid, Entry>();
        private readonly List<FarmWarningSubject> _subjectBuffer = new List<FarmWarningSubject>();

        private IReadOnlyList<FarmWarningGroup> _groups = Array.Empty<FarmWarningGroup>();
        private double _nextRebuildTime;

        public HdbfWarningBoard(IHdbfGameConfigProvider configProvider, IHdbfLog log)
        {
            _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <inheritdoc />
        public IReadOnlyList<FarmWarningGroup> GetWarningGroups() => _groups;

        /// <summary>
        /// Publishes one farm's current warning subject. Called only for farms the world-icon pass
        /// considered on screen; re-reporting the same farm refreshes its timestamp.
        /// </summary>
        public void Report(FarmWarningSubject? subject)
        {
            if (subject is null || subject.EntityId == Guid.Empty)
            {
                return;
            }

            _entries[subject.EntityId] = new Entry(subject, Now());
        }

        /// <summary>Drops one farm (off screen, deleted or icon-disabled).</summary>
        public void Remove(Guid entityId)
        {
            if (entityId != Guid.Empty)
            {
                _entries.Remove(entityId);
            }
        }

        /// <summary>
        /// Rebuilds the cached group list if the one-second window elapsed. Isolated so a broken
        /// build can never reach the game loop; the previous list stays visible on failure.
        /// </summary>
        public void UpdateSingleton()
        {
            var now = Now();
            if (now < _nextRebuildTime)
            {
                return;
            }

            _nextRebuildTime = now + WorldIconPolicy.DefaultWarningListIntervalSeconds;
            try
            {
                Rebuild(now);
            }
            catch (Exception exception)
            {
                _log.Error(HdbfLogCategory.Error, LogKey,
                    "cross-building warning aggregation failed; keeping the previous group list",
                    exception);
            }
        }

        /// <inheritdoc />
        public void Unload()
        {
            _entries.Clear();
            _subjectBuffer.Clear();
            _groups = Array.Empty<FarmWarningGroup>();
            _nextRebuildTime = 0d;
        }

        private void Rebuild(double now)
        {
            var ttl = TtlSeconds();
            // Always start from an empty buffer: after the last farm is removed the cached list
            // must become empty, not keep the previously reported subjects.
            _subjectBuffer.Clear();
            if (_entries.Count > 0)
            {
                var expired = default(List<Guid>);
                foreach (var pair in _entries)
                {
                    if (now - pair.Value.LastSeen > ttl)
                    {
                        (expired ??= new List<Guid>()).Add(pair.Key);
                        continue;
                    }

                    _subjectBuffer.Add(pair.Value.Subject);
                }

                if (expired is not null)
                {
                    for (var i = 0; i < expired.Count; i++)
                    {
                        _entries.Remove(expired[i]);
                    }
                }
            }

            _groups = _subjectBuffer.Count == 0
                ? Array.Empty<FarmWarningGroup>()
                : FarmWarningAggregator.BuildGroups(_subjectBuffer);
        }

        private float TtlSeconds()
        {
            var configured = _configProvider.Config.Ui.WarningAggregateTtlSeconds;
            return configured > 0f ? configured : WorldIconPolicy.DefaultAggregateTtlSeconds;
        }

        private static double Now() => Time.unscaledTime;

        private sealed class Entry
        {
            public Entry(FarmWarningSubject subject, double lastSeen)
            {
                Subject = subject;
                LastSeen = lastSeen;
            }

            public FarmWarningSubject Subject { get; }

            public double LastSeen { get; }
        }
    }
}
