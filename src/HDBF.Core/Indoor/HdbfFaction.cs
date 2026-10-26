// ============================================================================
// HDBF v2 | Layer: Core/Indoor | Owner: V2-CORE-01
// Purpose: Faction enum for indoor farms; selects which crop table applies.
//          Holds no balance values and no gameplay logic.
// Requirements: REQ-V2-CORE-01, REQ-V2-READ-02
// ============================================================================
namespace HDBF.Core.Indoor
{
    /// <summary>
    /// Faction that owns an indoor box farm (section 19.5). The faction selects the crop table
    /// (<see cref="ICropCatalog.ForFaction"/>); it never changes the simulation formulas.
    /// </summary>
    public enum HdbfFaction
    {
        /// <summary>Folktails: Carrot, Potato, Wheat, Sunflower.</summary>
        Folktails = 0,

        /// <summary>IronTeeth: Canola, Cassava, Corn, Eggplant, Kohlrabi, Soybean.</summary>
        IronTeeth = 1,
    }
}
