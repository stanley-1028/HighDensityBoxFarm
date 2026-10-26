namespace HDBF.Core
{
    /// <summary>Four box-farm tiers, ordered from cheapest to most dense (REQ-CORE-01).</summary>
    public enum HdbfTier
    {
        Basic = 0,
        Intermediate = 1,
        Advanced = 2,
        Top = 3,
    }

    /// <summary>Player-selectable operating mode (REQ-BAL-19..21, chapter 6.7).</summary>
    public enum HdbfMode
    {
        Normal = 0,
        Dense = 1,
        Fallow = 2,
    }

    /// <summary>The three external connection ports every tier may expose (chapter 8.3).</summary>
    public enum HdbfPortKind
    {
        Pipe = 0,
        Power = 1,
        Logistics = 2,
    }

    /// <summary>Port state machine from chapter 8.3: Hidden -> Available -> Connected -> Fault.</summary>
    public enum HdbfPortState
    {
        Hidden = 0,
        Available = 1,
        Connected = 2,
        Fault = 3,
    }

    /// <summary>How a tier pays for upkeep (chapter 6.5).</summary>
    public enum HdbfMaintenanceKind
    {
        IrrigationOnly = 0,
        Plank = 1,
        Metal = 2,
        PowerOrOffGridMetal = 3,
    }

    /// <summary>Health of a soft-dependency adapter (chapter 8.2).</summary>
    public enum HdbfAdapterHealth
    {
        Unknown = 0,
        Ok = 1,
        Degraded = 2,
        Disabled = 3,
        Failed = 4,
    }

    /// <summary>Mod log categories (REQ-SAFE-14 / REQ-ARCH-15).</summary>
    public enum HdbfLogCategory
    {
        Info = 0,
        Warn = 1,
        Error = 2,
        Compat = 3,
    }
}
