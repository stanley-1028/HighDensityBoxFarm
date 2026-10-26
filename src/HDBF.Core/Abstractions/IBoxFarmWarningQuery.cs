using System;
using System.Collections.Generic;
using HDBF.Core.Warnings;

namespace HDBF.Core.Abstractions
{
    /// <summary>
    /// Read-only cross-building warning aggregation contract (chapter 14.2 "同類警告跨建築聚合",
    /// chapter 14.6 "聚合結果緩存").
    ///
    /// UI consumes this through Core only; the gameplay singleton behind it caches the on-screen
    /// farms' warning subjects and rebuilds the groups at most once per second, so a panel refresh
    /// never scans every building. The returned list is immutable and safe to hold for the frame.
    /// </summary>
    public interface IBoxFarmWarningQuery
    {
        /// <summary>
        /// Groups of warnings with the same localization key across the farms processed by the
        /// most recent aggregation window (on-screen farms only, chapter 14.6). Never null; an
        /// empty list means "no grouped warning right now".
        /// </summary>
        IReadOnlyList<FarmWarningGroup> GetWarningGroups();
    }

    /// <summary>
    /// Locates one farm in the world (chapter 14.2 "点击展开定位列表").
    ///
    /// Implemented by gameplay with the vanilla selection service, so UI never touches a camera or
    /// selection component: it hands over the runtime entity id from
    /// <see cref="BoxFarmSnapshot.RuntimeEntityId"/> and the implementation selects/focuses that
    /// building. Returns false when the farm no longer exists; never throws.
    /// </summary>
    public interface IBoxFarmLocator
    {
        bool TryLocate(Guid entityId);
    }
}
