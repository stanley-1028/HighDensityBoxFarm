# ART-07 provenance — why the shipped UVs never leave the material canvas

This file records the **game-side evidence** behind the ART-07 no-`UV>1` decision
(contract: `art/ART07_UV_TILING.md`). Everything below was read from the
installed game (Timberborn `1.1.2.8-ffc2de0-sw`, Unity 6000.5.11f1) on
2026-10-27; nothing here is a guess from documentation. The extraction tools are
already vendored in this workspace: `tools/ApiIndex` (Mono.Cecil IL/member dump)
and the AssetStudioModCLI build in `%USERPROFILE%\.hdbf\sdk\assetstudio`.

## 1. The textures really are Repeat - but that is not the whole story

All vanilla `_D` albedo `Texture2D` assets have (dump of the `Texture2D`
serialized fields):

```text
m_Width: 1024            m_Height: 1024
m_TextureSettings: { m_FilterMode: 2 (Trilinear), m_Aniso: 16, m_MipMap: false, m_MipCount: 11,
                     m_WrapMode: 0 }        # Unity TextureWrapMode: 0 = Repeat
```

So a direct material render would tile at `uv > 1`. The problem is that our
meshes never render with the material directly: the runtime prefab optimizer
re-maps every UV into a faction atlas **before** the building is instantiated.

## 2. Every material we ship is a fragment of the faction auto-atlas

`resources.assets` contains the text asset
`configurations/autoatlaser.blueprint` (extracted 2026-10-27, quoted verbatim,
paths trimmed to the `Materials/...` suffix):

```json
"AutoAtlaserSpec": {
  "AutoAtlases": [
    { "Name": "FactionAtlas.Folktails", "IsUnique": true, "Fragments": [
        ".../BaseWood_Brown.Folktails", ".../BaseWood_White.Folktails",
        ".../Details.Folktails", ".../Paper.Folktails", ".../ThatchedRoof.Folktails",
        ".../IrregularPlanks_Brown.Folktails", ".../IrregularPlanks_White.Folktails",
        ".../IrregularPlanks_Mossy.Folktails", ".../PlasteredWood_White.Folktails",
        ".../Plaster_White.Folktails", ".../WindowsAtlas.Folktails",
        ".../BaseMetal.Folktails", ".../PaintedMetal.Folktails", ".../Sand.Folktails",
        ".../Moss.Folktails" ] },
    { "Name": "FactionAtlas.IronTeeth", "IsUnique": true, "Fragments": [
        ".../BaseWood_Indigo.IronTeeth", ".../BaseWood_Grey.IronTeeth",
        ".../Details.IronTeeth", ".../Paper.IronTeeth", ".../RoofPlanks.IronTeeth",
        ".../IrregularPlanks_Indigo.IronTeeth", ".../IrregularPlanks_Grey.IronTeeth",
        ".../IrregularPlanks_MetalForged.IronTeeth", ".../PlasteredWood_Orange.IronTeeth",
        ".../Plaster_White.IronTeeth", ".../WindowsAtlas.IronTeeth",
        ".../BaseMetal.IronTeeth", ".../PaintedMetal.IronTeeth" ] },
    { "Name": "CommonAtlas.MapEditor", "IsUnique": false, "Fragments": [ ... ] }
  ]
}
```

All 17 vanilla materials used by `art/timbermesh_common.py MATERIAL_MAP` are in
one of the two faction atlases (the only ones used in normal gameplay).

## 3. The optimizer chain processes our timbermesh renderers

`Timberborn.PrefabOptimization.PrefabOptimizationChainProvider::Get` (IL) builds
the processor list in this order:

```text
IL_0006  ldarg.0 ; ldfld _timbermeshPrefabOptimizer          -> List.Add
IL_0012  ldsfld AutoAtlasing  ; brfalse                      -> (flag = true)
IL_001a  ldfld _autoAtlasingPrefabOptimizer                  -> List.Add
IL_0025  ldsfld VertexColor                                  -> List.Add(_vertexColorPrefabOptimizer)
IL_0038  ldsfld MergeMeshesByMaterial                        -> List.Add(_mergeMeshesByMaterialPrefabOptimizer)
IL_004b  ldsfld DestroyEmptyChildren                         -> List.Add(_destroyEmptyChildrenPrefabOptimizer)
```

and `PrefabOptimizationChain::ProcessPrefab` instantiates a copy and calls
`IPrefabOptimizer.Optimize(gameObject)` for every entry in that list order.
`TimbermeshPrefabOptimizer::Optimize` calls
`ImportTimbermeshModels(GetComponentsInChildren<TimbermeshDescription>(true))`,
and `Timberborn.Timbermesh.StaticMeshBuilder::CreateMeshComponents` ends with:

```text
IL_0001  callvirt GameObject::AddComponent<UnityEngine.MeshFilter>()
IL_0007  callvirt MeshFilter::set_sharedMesh(UnityEngine.Mesh)
IL_000d  callvirt GameObject::AddComponent<UnityEngine.MeshRenderer>()
IL_0013  ldfld _materialsToSetCache ; callvirt Renderer::set_sharedMaterials(Material[])
```

i.e. after the timbermesh step our building is a plain hierarchy of
`MeshRenderer`s with the faction `MaterialCollection` materials. The
`AutoAtlasingPrefabOptimizer` (next in the chain) then does
`GetComponentsInChildren<MeshRenderer>(true)` and rewrites every renderer whose
material shader is `Shader Graphs/EnvironmentURP` (the `EnvironmentShaderName`
static constant, IL_0000 `ldstr "Shader Graphs/EnvironmentURP"`) and whose
texture set matches an atlas fragment key. That is exactly our case.

## 4. The atlas keeps native 1024 cells, and the UVs are scaled into the cell

`AutoAtlasingPrefabOptimizer::OptimizeMeshRenderer` (IL, tail):

```text
IL_0172  ldloc.3 (mesh.uv)  ldelem.any Vector2
IL_017f  callvirt AutoAtlasFragment::get_UVScale()   ; Vector2
IL_0184  call     Vector2::op_Multiply
IL_0189  callvirt AutoAtlasFragment::get_UVOffset()
IL_0190  call     Vector2::op_Addition
IL_0195  stelem.any Vector2                          ; mesh uv = uv * UVScale + UVOffset
```

`AutoAtlaser::GenerateAutoAtlasFragments` (IL) computes the fragment transform:

```text
IL_0019  AutoAtlasSpec::get_Fragments() ; get_Length
IL_001e  call AutoAtlaser::CalculateSizeMultiplier(int)
...
IL_01a6  call Vector2::get_one()
IL_01ab  ldloc.1 (sizeMultiplier) conv.r4
IL_01ad  call Vector2::op_Division            ; UVScale = (1/sizeMultiplier, 1/sizeMultiplier)
...
IL_01d4  ldloc.s V_8 (fragment index) conv.r4
IL_01d7  ldloc.1 conv.r4 ; div                 ; fragX / sizeMultiplier
IL_01da  ldloc.s V_9 (fragment row)  conv.r4
IL_01dd  ldloc.1 conv.r4 ; div                 ; fragY / sizeMultiplier
IL_01e0  newobj Vector2::.ctor                 ; UVOffset
```

`AutoAtlaser::CombineFragments` (IL) creates the combined texture and copies
each fragment **at native resolution** into a cell:

```text
IL_00db  fragSize (Vector2Int) * sizeMultiplier  -> combined size
IL_0169  TextureFactory::CreateTexture(TextureSettings/Builder ... SetSize(...))
IL_01e1  loop: fragX = i % sizeMultiplier ; fragY = i / sizeMultiplier
IL_01eb  Texture2D::SetPixels32(x = fragX * width, y = fragY * height, width, height, pixels)
```

Consequences (exactly the ART-07 premises):

1. each material's atlas cell is its **native 1024x1024** `_D` texture, so the
   maximum legal texel density is the native one - no quality is lost to the
   atlas itself;
2. a mesh UV component **> 1** is scaled to `> 1/sizeMultiplier`, i.e. it leaves
   the fragment's cell and samples a **different material** (the neighbouring
   cell of the combined atlas). Repeat wrap is never reached.
3. Therefore the shipped UVs must stay inside the material's own canvas
   (`uv_canvas` in `art/material_texture_map.json`). ART-07 maximises the
   density under that constraint: `1024 / 3.0 m = 341 px/m` for a full-bleed
   tile texture and `canvas_px / 3.0 m` for a curated patch canvas.

## 5. Reproduction commands

```powershell
# 1) IL dump (Mono.Cecil, no network): build the tool outside the workspace then run it
$work = "$env:TEMP\hdbf-art07-il"
$managed = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed'
dotnet build tools\ApiIndex\ApiIndex.csproj -c Release -m:1 -p:HdbfWorkRoot="$work\work"
$dll = "$work\work\bin\ApiIndex\Release\net9.0\ApiIndex.dll"
dotnet $dll il $managed "$work\prefabopt-il.txt" 'PrefabOptimization'
dotnet $dll il $managed "$work\timbermesh-il.txt" 'Timbermesh|StaticMeshBuilder'

# 2) Material shader / texture settings + AutoAtlaser.blueprint (AssetStudioModCLI)
#    The matprobe helper used in ART-05/06 reads Material.m_Shader and
#    Texture2D.m_TextureSettings; the AutoAtlaser text asset is extracted by a
#    byte scan of Timberborn_Data/resources.assets for b'AutoAtlaser.blueprint'
#    followed by the embedded '\r\n  "AutoAtlaserSpec"' JSON (see ART-05 report;
#    the same extraction produced the fragment lists quoted in section 2).
```

The exact extracted artifacts (`prefabopt-il.txt`, `timbermesh-il.txt`, the
AutoAtlaser JSON excerpt) are regeneration products, not shipped assets; the
game's own files are read-only inputs.
