using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace HDBF.Core.Logging
{
    /// <summary>
    /// Default <see cref="IHdbfLog"/>: 30 s throttle per key with suppressed-repeat accounting
    /// (chapter 10.4, REQ-SAFE-14, REQ-ARCH-15).
    ///
    /// Behaviour:
    /// * the first entry for a key is always emitted;
    /// * repeats inside the window are counted, never printed;
    /// * the next emitted entry carries the suffix <c>suppressed=&lt;n&gt;</c> when repeats were
    ///   dropped, so the count survives throttling (the log volume rule of chapter 15.3);
    /// * time comes from an injectable clock in seconds, which keeps the type unit-testable.
    ///
    /// The type owns no game state; the mutable throttle table is instance scoped, so it is safe
    /// for a session-scoped singleton and safe to reset between tests.
    /// </summary>
    public sealed class ThrottledLog : IHdbfLog
    {
        /// <summary>Throttle window in seconds (chapter 18.7 <c>log.throttleSeconds</c> default).</summary>
        public const double ThrottleWindowSeconds = 30d;

        /// <summary>Maximum stack lines attached to an error (chapter 10.4: at most 20).</summary>
        public const int MaxStackLines = 20;

        private sealed class KeyState
        {
            public bool Emitted;
            public double LastEmitSeconds;
            public int Suppressed;
        }

        private static readonly Stopwatch SharedClock = Stopwatch.StartNew();

        private readonly Dictionary<string, KeyState> _state = new Dictionary<string, KeyState>(StringComparer.Ordinal);
        private readonly Action<HdbfLogCategory, string>? _sink;
        private readonly Func<double> _clock;
        private readonly object _gate = new object();

        /// <summary>
        /// Creates the log writer.
        /// </summary>
        /// <param name="sink">Destination for accepted entries (category, formatted text). May be null.</param>
        /// <param name="clock">Monotonic clock in seconds; defaults to process uptime.</param>
        public ThrottledLog(Action<HdbfLogCategory, string>? sink, Func<double>? clock = null)
        {
            _sink = sink;
            _clock = clock ?? (() => SharedClock.Elapsed.TotalSeconds);
        }

        /// <inheritdoc />
        public void Write(HdbfLogCategory category, string key, string message)
        {
            Emit(category, key, message);
        }

        /// <inheritdoc />
        public void Error(HdbfLogCategory category, string key, string message, Exception? exception, string? entityId = null)
        {
            Emit(category, key, ComposeError(message, exception, entityId));
        }

        /// <inheritdoc />
        public int SuppressedCount(string key)
        {
            lock (_gate)
            {
                return _state.TryGetValue(key, out var state) ? state.Suppressed : 0;
            }
        }

        /// <inheritdoc />
        public void Reset()
        {
            lock (_gate)
            {
                _state.Clear();
            }
        }

        private void Emit(HdbfLogCategory category, string key, string message)
        {
            var sink = _sink;
            string text;
            lock (_gate)
            {
                if (!_state.TryGetValue(key, out var state))
                {
                    state = new KeyState();
                    _state[key] = state;
                }

                var now = _clock();
                if (state.Emitted && now - state.LastEmitSeconds < ThrottleWindowSeconds)
                {
                    state.Suppressed++;
                    return;
                }

                var suffix = state.Suppressed > 0
                    ? " suppressed=" + state.Suppressed.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;

                state.Suppressed = 0;
                state.Emitted = true;
                state.LastEmitSeconds = now;
                text = key + ": " + (message ?? string.Empty) + suffix;
            }

            // The sink is outside the lock: a game logger must never be able to stall the
            // throttle table, and it is documented not to throw (Unity Debug.Log does not).
            sink?.Invoke(category, text);
        }

        private static string ComposeError(string message, Exception? exception, string? entityId)
        {
            var builder = new StringBuilder(message ?? string.Empty);

            if (!string.IsNullOrEmpty(entityId))
            {
                builder.Append("; entityId=").Append(entityId);
            }

            if (exception is null)
            {
                return builder.ToString();
            }

            builder.Append("; exception=")
                .Append(exception.GetType().FullName)
                .Append(": ")
                .Append(exception.Message);

            var stack = exception.StackTrace;
            if (string.IsNullOrEmpty(stack))
            {
                return builder.ToString();
            }

            var lines = stack!.Split('\n');
            var count = Math.Min(lines.Length, MaxStackLines);
            builder.Append("; stack=");
            for (var i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                builder.Append(lines[i].Trim());
            }

            return builder.ToString();
        }
    }
}
