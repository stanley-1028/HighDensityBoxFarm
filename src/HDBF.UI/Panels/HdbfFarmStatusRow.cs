using HDBF.Core.Abstractions;

namespace HDBF.UI.Panels
{
    /// <summary>
    /// One already-localized status/warning row for the entity panel (chapter 14.2).
    /// <see cref="Severity"/> is the P0..P4 priority: the panel always renders rows sorted by
    /// this value, and never shows more than three at once (REQ-UI-03).
    /// </summary>
    public sealed record HdbfFarmStatusRow
    {
        public string Text { get; init; } = string.Empty;

        public FarmWarningSeverity Severity { get; init; }
    }
}
