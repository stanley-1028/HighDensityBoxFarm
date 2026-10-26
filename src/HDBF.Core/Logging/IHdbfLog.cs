using System;

namespace HDBF.Core.Logging
{
    /// <summary>
    /// The only logging surface HDBF code may depend on. Implementations route to the game log
    /// and enforce the throttling rules of chapter 10.4 / REQ-SAFE-14, without leaking Unity or
    /// Timberborn types into core logic (REQ-ARCH-01).
    ///
    /// Contract:
    /// * <see cref="Write"/> and <see cref="Error"/> never throw, even when a sink fails;
    /// * the same <c>key</c> is emitted at most once per throttle window, suppressed repeats are
    ///   counted and reported on the next emission (chapter 10.4);
    /// * <see cref="Error"/> attaches the diagnostic context required by REQ-SAFE-15
    ///   (entity id, exception type, trimmed stack) to the message.
    /// </summary>
    public interface IHdbfLog
    {
        /// <summary>Emits one throttled entry; <paramref name="key"/> is the throttle identity.</summary>
        void Write(HdbfLogCategory category, string key, string message);

        /// <summary>
        /// Emits one throttled error entry, appending entity id, exception type and a trimmed
        /// stack trace so a player report identifies the failing building (REQ-SAFE-15).
        /// </summary>
        void Error(HdbfLogCategory category, string key, string message, Exception? exception, string? entityId = null);

        /// <summary>Number of repeats currently suppressed for <paramref name="key"/> since its last emission.</summary>
        int SuppressedCount(string key);

        /// <summary>Clears every throttle timestamp and suppressed counter (used by tests and hot reload).</summary>
        void Reset();
    }
}
