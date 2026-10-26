namespace HDBF.Core.Plots
{
    /// <summary>
    /// The one place that maps between the cell stored as the plot id and the cell handed to the
    /// game's soil-moisture query. <b>Both are the same cell</b> in this build of Timberborn; the
    /// functions exist so that invariant is explicit, unit-tested and can only be changed in one
    /// place (never inline in a gameplay partial).
    ///
    /// Why this file exists at all: the P0 symptom "panel irrigation coverage is always 0 %" was
    /// partly blamed on the plot id storing a wrong z. Reading the installed assembly IL showed
    /// the opposite — the plant cell and the moisture sample cell are the same z, the terrain
    /// column ceiling. The evidence chain, from the real assemblies in
    /// <c>Timberborn_Data\Managed</c> (dumped with <c>tools/ApiIndex il</c>):
    ///
    /// 1. <c>Timberborn.TerrainSystem.TerrainService::GetTerrainHeight(Vector3Int)</c>:
    ///    <code>
    ///    ctx = TerrainMap::TryGetRelativeHeight(coordinates, out relativeHeight);
    ///    return ctx ? coordinates.z + relativeHeight : 0;
    ///    </code>
    ///    and <c>TerrainMap::TryGetRelativeHeight</c> returns, for a column whose
    ///    <c>coordinates.z</c> is inside a solid run, the distance from that voxel to the top of
    ///    the run (number of solid voxels including both ends). With the common
    ///    <c>new Vector3Int(x, y, 0)</c> probe this is <c>topSolidZ + 1</c>.
    /// 2. <c>Timberborn.TerrainSystem.ColumnTerrainMap::GetColumn(int index, int height)</c>
    ///    returns a column only while <c>height &gt;= Floor &amp;&amp; height &lt; Ceiling</c>, so
    ///    <c>TerrainColumn.Ceiling</c> is exclusive: the solid run is <c>Floor..Ceiling-1</c> and
    ///    <c>Ceiling</c> is exactly the first air cell above the column.
    /// 3. <c>Timberborn.PlantingUI.PlantingSelectionService::InMapLeveledCoordinates</c> (the
    ///    coordinate source of the Fields tool) yields
    ///    <c>new Vector3Int(block.x, block.y, pickedTerrainVoxel.z + 1)</c> only for columns where
    ///    <c>ITerrainService::OnGround(block.Above())</c> holds. The same coordinates go to
    ///    <c>PlantingAreaValidator::CanPlant(coordinates, name)</c> (highlight predicate) and to
    ///    <c>PlantingService::SetPlantingCoordinates(coordinates, name)</c> (paint action), i.e.
    ///    <b>the plant cell passed to CanPlant is the surface cell, not the terrain voxel</b>.
    /// 4. <c>Timberborn.Planting.PlantingAreaValidator::CanPlant</c> delegates to
    ///    <c>SpawnValidationService::IsUnobstructed(coordinates, name)</c> →
    ///    <c>BlockValidator::BlocksValid(BlockObjectSpec, new Placement(coordinates))</c>, which
    ///    rejects blocks whose cell is terrain
    ///    (<c>BlockConflictsWithTerrain</c> → <c>ITerrainService::Underground</c>) and applies
    ///    <c>MatterBelowValidator</c> (the crop must stand on ground), so a crop is only accepted
    ///    when <c>coordinates</c> is the air cell directly above solid terrain.
    /// 5. <c>Timberborn.Planting.PlantingSoilValidator::Validate(PlantingSpot spot)</c> calls
    ///    <c>ISoilMoistureService::SoilIsMoist(spot.Coordinates)</c> — the game itself passes the
    ///    plant cell (the one painted in step 3) to the moisture query.
    /// 6. <c>Timberborn.SoilMoistureSystem.SoilMoistureService::SoilIsMoist(coordinates)</c>:
    ///    <code>
    ///    index2D = MapIndexService::CellToIndex(VectorExtensions::XY(coordinates));
    ///    if (IThreadSafeColumnTerrainMap::TryGetIndexAtCeiling(index2D, coordinates.z, out index3D))
    ///        return _threadSafeMoistureLevels[index3D] &gt; 0;
    ///    return false;
    ///    </code>
    ///    and <c>ThreadSafeColumnTerrainMap::TryGetIndexAtCeiling</c> succeeds only for a column
    ///    whose <c>ReadOnlyTerrainColumn.Ceiling == coordinates.z</c>; when no column matches it
    ///    returns false, which <c>SoilIsMoist</c> reports as "dry". So the moisture query expects
    ///    <c>coordinates.z == TerrainColumn.Ceiling</c> = the surface cell.
    ///
    /// Therefore: plant cell z == column ceiling == moisture sample cell z, for every plot the
    /// scan can register (the scan also checks <c>ITerrainService::OnGround(plantCell)</c>, the
    /// same predicate the Fields tool requires). If a future game build separates the two cells,
    /// this class is the only place to change.
    /// </summary>
    public static class PlotCellHeights
    {
        /// <summary>
        /// Vertical offset applied when converting a plant cell into the cell handed to
        /// <c>SoilIsMoist</c>. <b>Must stay 0</b>: the IL evidence above shows both APIs take the
        /// terrain column ceiling (the surface cell). The constant is public so the regression
        /// test can pin it and any future change becomes a deliberate, reviewed decision.
        /// </summary>
        public const int PlantingToMoistureCellOffset = 0;

        /// <summary>
        /// Converts the terrain height reported by <c>TerrainService.GetTerrainHeight</c> (the
        /// column ceiling) into the z of the plant cell stored in the plot id. Identity, by the
        /// IL evidence chain in the type summary.
        /// </summary>
        public static int ResolvePlantCellZ(int terrainColumnCeiling)
        {
            return terrainColumnCeiling;
        }

        /// <summary>
        /// Converts the plant cell z stored in the plot id into the <c>coordinates.z</c> handed to
        /// <c>ISoilMoistureService.SoilIsMoist</c>. Identity plus the pinned offset.
        /// </summary>
        public static int ResolveMoistureSampleZ(int plantCellZ)
        {
            return plantCellZ + PlantingToMoistureCellOffset;
        }

        /// <summary>
        /// End-to-end mapping used by tests and documentation: terrain column ceiling →
        /// moisture sample z. The production scan stores
        /// <see cref="ResolvePlantCellZ"/> and the sampler reads
        /// <see cref="ResolveMoistureSampleZ"/>, so the pair is exactly this composition.
        /// </summary>
        public static int ResolveMoistureSampleZFromCeiling(int terrainColumnCeiling)
        {
            return ResolveMoistureSampleZ(ResolvePlantCellZ(terrainColumnCeiling));
        }
    }
}
