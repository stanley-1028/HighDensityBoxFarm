# Cover art brief — "High-Density Box Farm" (Steam Workshop 3828809784)

> **Status**: research + art direction, 2026-10-28. **Deliverable = prompts, not images.** No image is generated here.
> **Owner**: art-style. **Audience**: whoever runs the Seedream 5.0 Lite generations and uploads the cover.
> **Goal**: replace [data/thumbnail.png](../../data/thumbnail.png) with a workshop cover that reads as Timberborn at 200 px and sells the mod's one idea — a dense indoor farm in a box that you can stack.

Contents: §1 Timberborn identity → §2 what workshop covers actually are → §3 diagnosis of the current cover → §4 art direction → §5 Seedream rules → §6 prompts A/B/C/D → §7 do-not list → §8 checklist.

---

## 1. Timberborn visual identity (from real sources)

### 1.1 Sources actually opened for this brief

| What | URL | What it showed |
|---|---|---|
| Store page (text: "lumberpunk beavers", vertical architecture, droughts/toxic waste) | https://store.steampowered.com/app/1062090/Timberborn/ | Positioning language: **lumberpunk**, **vertical architecture**, water physics, "contains high amounts of wood" |
| Store metadata JSON (17 screenshots, header, capsule) | https://store.steampowered.com/api/appdetails?appids=1062090&l=english | Screenshot set + official header/capsule URLs |
| Official header art (painted key art) | https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1062090/eeb2babf15cbf4ebc175316a5d3e42a060f3d97c/header.jpg | Two beavers seen **from behind** on a wooden platform, looking out over a lush valley settlement; big white chunky "TIMBERBORN" logotype across the top |
| Screenshot — river settlement, temperate | https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1062090/154bdaa6fc267e89d57592015268f8c0b59924e1/ss_154bdaa6fc267e89d57592015268f8c0b59924e1.1920x1080.jpg | High 3/4 aerial camera, stepped wooden beehive-ish roofs, water wheel, bright grass plateau, grey rock terraces, teal river |
| Screenshot — vertical stacked district | https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1062090/6d19ce417b52a9dc9bd75c8c9ff821a139de27f8/ss_6d19ce417b52a9dc9bd75c8c9ff821a139de27f8.1920x1080.jpg | Buildings stacked on stone plateaus, ziplines, water tower/aqueduct, golden wheat blocks — the game's "stack it up" fantasy |
| Screenshot — drought / badwater mood | https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/1062090/bca4903d7302643347debc70389d7cec0fe80a25/ss_bca4903d7302643347debc70389d7cec0fe80a25.1920x1080.jpg | Rust-red dry soil, dead crops, cracked fields, blue-grey metal tanks/pipes against the brown — the game's "dry season" palette |
| Workshop app config for this game | https://steamcommunity.com/workshop/browse/?appid=1062090&browsesort=totaluniquesubscribers&section=readytouseitems | `"aspect_ratio":"16x9"`, `"usesquarethumbnails":true` — covers are authored 16:9 but some lists square-crop them |
| Local references from the mod | [art/references/README.md](../../art/references/README.md), [data/thumbnail.png](../../data/thumbnail.png), [art/previews/tiers_lineup_iso_100.png](../../art/previews/tiers_lineup_iso_100.png) | Prior Seedream direction for the four tiers; current cover; flat-render lineup |
| Seedream 5.0 Lite prompting guide | https://fal.ai/learn/tools/how-to-use-seedream-5-lite | 2–4 sentence sweet spot, no negative prompt/CFG/steps/seed, text strong at display size but unreliable when dense, size presets incl. Landscape 16:9 / Auto 2K / Auto 3K |

### 1.2 Form language
Low-poly but **not** faceted or "voxel": chunky rounded-mass buildings, flat-ish painted textures, exaggerated silhouettes, visible plank/beam structure, stepped or beehive roofs, awnings and little wooden decks. Every structure is hand-built-looking — nothing industrial-sleek except IronTeeth metal. Colour is applied in **flat zones with painterly noise**, not photoreal PBR. Steam store text calls the genre **lumberpunk**: wood is the hero material, metal reads as salvaged/expensive.

### 1.3 Palette (approximate sightings from the screenshots above; sample on your own export before locking)
| Role | Colour | Notes |
|---|---|---|
| Sky / water | soft sky blue → teal `#7FBFD8` / `#2E7E8F` | water is always saturated teal, never muddy |
| Foliage / grass | warm green `#6E9E3E` / `#8FB447` | bright, sunny, slightly yellow-green |
| Wood (hero) | warm tan `#C9A46A`, dark plank `#6B4A32` | the single most repeated material on the store art |
| Stone / terraces | grey-brown `#8A7B6E`, shadowed `#5C5148` | terraces are the game's "terrain" signature |
| Metal (IronTeeth) | blue-grey `#5C6B78`, rust accents `#A85434` | cold contrast against wood |
| Dry season | dust `#C89A6B`, rust soil `#A85A33`, bleached crop `#B9A45E` | use only as an accent, not the base |
| UI gold | `#E0B75A` | the game's panel trim; **do not** paint fake panels with it |

### 1.4 Camera and light
- Cameras on the store shots are **high three-quarter aerial** (roughly 35–50° down), long-lens-ish / near-orthographic: parallel edges, minimal perspective convergence. That is the "isometric" read players recognise, even though the game is true 3D.
- Lighting is a **warm low sun** with soft ambient bounce, cool shadows. Never dramatic/neon.
- Compositions are dense and **top-lit readable**: buildings sit on grass/stone, with water as a value break.

### 1.5 Season mood — pick one, deliberately
- **Temperate (recommended)**: lush green, teal water, warm wood, sunny. This is what the store capsule sells and what a "look at my nice farm" cover needs.
- **Drought**: rust/ochre ground, dead fields, metal and pipes. Useful for drama, but on a small thumbnail it turns muddy and reads as "another survival mod". Use at most as a background gradient hint.

### 1.6 Characters
Beavers are **small relative to buildings**, upright, round-bodied, simple hats/vests, big teeth, dark brown to grey fur, and they are usually seen mid-task (hauling, building, walking). On the official header they are used as **foreground scale + POV** (backs to camera). A cover with 2–4 tiny beavers doing farm work instantly reads "this is a Timberborn thing".

---

## 2. What Timberborn workshop covers actually look like

Method: pulled the app's workshop item ids, then queried Steam's file-details API and downloaded the **actual uploaded preview images** (sizes below are the originals). Subscriber counts from the same API, 2026-10-28.

| Item | Subs | Preview | What the thumbnail actually is | Text on image |
|---|---|---|---|---|
| [Ladder](https://steamcommunity.com/sharedfiles/filedetails/?id=3286476486) | 125,398 | 800×450 | In-game close-up of the mod object on grass, trees behind, **parchment frame** around the edges | Hand-lettered "Ladder" + joke subtitle "It's a ladder" |
| [TimberUi](https://steamcommunity.com/sharedfiles/filedetails/?id=3430626620) | 70,547 | 979×613 | In-game UI mockup: a dialog with visible panel/button labels | none (the UI is the subject) |
| [KnatteMaterials](https://steamcommunity.com/sharedfiles/filedetails/?id=3281290867) | 49,317 | 1920×1080 | Flat grey background, **no game scene at all**; huge hand-scrawled white logotype + a tiny Unity asset-browser screenshot at the bottom | "KNATTES BASE / MATERIAL" |
| [Goods Statistics](https://steamcommunity.com/sharedfiles/filedetails/?id=3321521358) | 41,818 | 960×540 | Game screenshot background (yellow crops) + pasted in-game chart panels | Hand-lettered "Goods Statistics" |
| [Flywheels](https://steamcommunity.com/sharedfiles/filedetails/?id=3322428023) | 35,650 | 1920×1080 | In-game machinery scene, **parchment frame**, "1.0" diamond badge, blue watercolour band at the bottom | Big white scrawled "fLywhEEls" on the band |
| [Staircase](https://steamcommunity.com/sharedfiles/filedetails/?id=3277416566) | 33,732 | 1920×1080 | In-game village screenshot from the usual 3/4 angle | White scrawled "STAIRCASE" top-left, no frame |
| [Emberpelts](https://steamcommunity.com/sharedfiles/filedetails/?id=3346318229) | 28,575 | 1920×1080 | In-game village aerial + orange starburst + two character cut-outs (mascot beaver, fox) | "New Faction" starburst + giant red script "Emberpelts" |
| [1x1x2Storage](https://steamcommunity.com/sharedfiles/filedetails/?id=3281653061) | 24,022 | 566×340 | Clean in-game screenshot of the mod building next to a vanilla building | none |

**Patterns that convert here**
1. **7 of 8 are real in-game captures or UI captures** — not Blender renders, not illustrations. Players want to see what the thing looks like *in their colony*.
2. **6 of 8 add hand-lettered/display text.** The type is chunky, white or cream, hand-painted/scrawled — i.e. it *looks like it was drawn in the same universe*, and it is big enough to read at browse-grid size.
3. **Text sits in a corner or on a band**, never over the subject's centre. Flywheels and Ladder both solve the "text needs contrast" problem with a frame/band rather than by flattening the art.
4. **16:9 dominates** (1920×1080, 800×450, 960×540). Manual uploads are often not exactly 16:9 (566×340, 979×613) — those are the ones that look sloppy in the grid.
5. **Busy multi-element covers still work when one word dominates** (Emberpelts is cluttered but "Emberpelts" wins). Covers without text rely on a very readable silhouette (1x1x2Storage).
6. Content mods show **the object large and close**, with a vanilla building nearby for scale. Nobody shows a whole colony unless the mod *is* a faction.

**Implication for us**: our mod is a building. The cover must (a) look like an in-game Timberborn scene, (b) show the box farm large, (c) show *why* it's different (crops stacked inside; a second building on its roof), and (d) carry one short, big, hand-painted title.

---

## 3. Diagnosis: why the current cover is weak

Current [data/thumbnail.png](../../data/thumbnail.png) (uploaded 1280×720) and its live page https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784:

- It is a **flat Blender render of one Basic box** floating on a white/grey page with ~40 % dead margin on every side. On the item page Steam letterboxes it at 637×358 inside a black card, so the building ends up occupying roughly **a third of a third** of the image.
- **No game context**: no grass, no water, no beavers, no crops, no other buildings. Nothing says "Timberborn"; it reads as an unfinished asset preview.
- **No text** — the workshop shows the item name *below* the image, so leaving the image wordless throws away the one place you can win a browser's attention.
- **Palette clash**: grey/white field vs the game's saturated green/teal/warm wood. It looks like a different product.
- **No stack, no interior** — the two features the mod actually sells (flat stackable roof, dense indoor crop racks) are invisible.
- Live stats at time of writing: **16 unique visitors, 5 current subscribers, 0 favourites, "Not enough ratings"** — consistent with a cover that is not doing its job.

Keep the render pipeline for in-game icons/documentation; the cover should be **art**, not a turntable render.

---

## 4. Art direction for the new cover

### 4.1 Concept (one line)
**A single hero box farm on a sunny meadow, seen from the game's 3/4 aerial camera, with crops glowing behind its opening, tiny beavers hauling crates in front, and a second smaller wooden hut stacked on its flat roof** — the "you can build up" promise, shown not told.

### 4.2 Composition (variant A, primary)
- **Subject**: one 3×3×1 box farm, front-left 3/4 view, occupying ~55–65 % of frame width and centred slightly left-of-centre (Steam crops and overlays sit right/bottom).
- **Roof**: flat plank roof with a **second, smaller building sitting on it** (this is the mod's hook — see the stacking reference in [art/references/README.md](../../art/references/README.md) §1, `ref-roof-stacking.jpg`). Keep the stacked hut visibly smaller and same-style, not a tower.
- **Interior seen through openings**: open windows / big glass panes show **dense crop racks silhouetted against warm interior light** (carrots, wheat, potato crates). Do not fully open the wall — partial glimpses read as "packed inside".
- **Water**: round internal tank visible through the doorway in shadow, plus a small teal pond/stream edge at the bottom edge of frame for colour.
- **Foreground**: 2–4 tiny beavers, one carrying a crate, one at the door, crop crates stacked on the grass — these are the scale cue. Keep them small; do not centre them.
- **Background**: soft, low-contrast — green meadow, grey stone terraces with pines, teal river band. Push it out of focus / lower contrast so the box stays the hero.
- **Safe area**: keep the building inside the **central square**. The workshop config sets `usesquarethumbnails: true`, so the 16:9 image must survive a centre 1:1 crop, and the app's own preview card is letterboxed — leave ≥8 % margin on all sides.

### 4.3 Camera
Match the game: three-quarter aerial ~40° down, near-orthographic (parallel verticals), mild perspective only for the stacked hut. No fisheye, no low hero shot, no cinematic depth-of-field blur on the subject.

### 4.4 Palette assignment
| Area | Colour family | Why |
|---|---|---|
| Box farm walls | warm tan + dark plank, slight moss | matches vanilla Folktails buildings |
| Crop racks / windows | saturated carrot orange, wheat gold, leaf green, warm interior glow | the "dense farm" read at thumbnail size |
| Roof + second hut | same wood family, slightly lighter | stacking must look native, not bolted on |
| Ground | bright grass green with darker green shadow | the value break under the building |
| Water | teal, one clean band | instant "Timberborn" tell |
| Background | desaturated grey-brown stone, muted pines | keeps contrast in the centre |
| Metal accents (advanced tier) | blue-grey + rivets | only if a tier is shown; do not make the hero metal |

### 4.5 Lighting
Warm late-afternoon sun from the **left/front-left**, soft long shadows to the right, warm bounce inside the openings. This is both the game's look and it makes the flat roof read as flat.

### 4.6 Text overlay recommendation (do this in an editor, not the model)
- **Line 1 (must)**: `HIGH-DENSITY BOX FARM` — display size, hand-painted cream/white with a dark rough-plank or dark-teal band behind it, bottom-left. Type should look *carved/painted wood*, not a system font (the top workshop covers all use hand-lettered type).
- **Line 2 (optional)**: `60 → 600 SLOTS` **or** `STACK IT UP`. Pick one; never both. At browse-grid size (≈200 px wide) only about 3–4 short words survive.
- **Never**: sentence-length feature lists, "AI generated", watermarks, fake UI panels.
- Rationale: Seedream renders display-size type well but is unreliable with dense small text (see §5), so the *authoritative* title should be composited, and the model either gets no text (A/B/C) or exactly one short line (D).
- Keep the **textless master** as the deliverable of record; export the titled version from it. This also lets you re-title for a future update without regenerating.

### 4.7 Steam sizing notes
- Target **1920×1080 (16:9)** for upload; Seedream's `Landscape 16:9` preset lands near 2560×1440, so downscale — do not upscale a 1280×720 render.
- Workshop grid may show the image square-cropped; check the centre 1:1 crop before upload.
- The item page letterboxes the preview (our page shows 637×358 inside a black card), so avoid critical detail in the outer 8 %.

---

## 5. Seedream 5.0 Lite prompting rules (verified)

From the fal guide (fetched for this brief, https://fal.ai/learn/tools/how-to-use-seedream-5-lite):
- **Natural, conversational language; 2–4 sentences is the sweet spot.** Very short prompts leave too much to defaults; long prompts get tail-truncated in attention.
- **No negative prompts, no CFG / steps / seed** — the API strips them; write what you *want*, not what you don't.
- **No keyword spam.** "masterpiece, best quality, 8K, ultra-detailed" distracts the reasoning pass.
- **Describe relationships, not lists** ("a hut stacked on the plank roof", not "hut, roof, planks").
- **Text**: strong for display-size typography, unreliable for dense small text — spell text exactly and say where it goes.
- **Sizes**: presets include Landscape 16:9, Auto 2K, Auto 3K; total pixels must land in ~3.7–9.4 MP (so a 16:9 preset is roughly 2560×1440; anything below the floor is silently scaled up).
- The model runs a Chain-of-Thought pass and handles **cross-sections / architectural cutaways** coherently — that is why variant B is worth trying.
- Vendor-side Chinese guide (referenced by the project, not fetched in this pass): https://console.volcengine.com/ark/region:cn-beijing/docs/seedream-4-0-5-0-prompt-guide

---

## 6. Ready-to-paste prompts

All prompts are English, 2–4 sentences, no quality tokens, no negative instructions. **Size setting for all: `Landscape 16:9`** (fallback `Auto 2K`).

### Variant A — hero isometric diorama (PRIMARY RECOMMENDATION)
**Goal**: the cover that ships. One hero building + stacked hut + tiny beavers + crop glow + meadow/river background. Best balance of "looks like Timberborn" and "shows the hook".
**Prompt**
> Isometric game-art diorama of a compact square wooden farm building, three blocks wide and one block tall, dark rough planks, big open windows showing dense shelves of carrots and wheat inside. A small wooden hut sits stacked on its flat plank roof, tiny beaver farmers carry crop crates across the grass in front, and a round water tank shows through the doorway. Green meadow, teal river, grey stone terraces and pines behind, warm late-afternoon sun, chunky low-poly shapes, hand-painted textures, cheerful storybook mood.
**Main risk**: the model invents an extra storey under the hut, or the "stacked hut" reads as a chimney/tower.
**Fallback tweak**: add "the hut on the roof is much smaller than the building and keeps the same square footprint edge" and re-run once.

### Variant B — interior cutaway
**Goal**: sell "dense indoor farm" — the actual gameplay idea. Good as the **second** image on the item page (workshop items can carry several), or as a redesign if A feels too plain.
**Prompt**
> Cutaway game-art illustration of a small square wooden farm building, front-left corner removed to reveal one single-storey room: long wooden racks packed with carrots, wheat and potatoes under warm lamp light, a round water tank with a copper pipe against the back wall, two tiny beaver farmers in simple hats tending the crops. Thick rough planks and iron brackets on the cut edges, flat plank roof above. The building sits on green meadow at dusk, cold blue exterior against golden light spilling from the opening, chunky low-poly shapes, hand-painted textures.
**Main risk**: the model turns it into a dollhouse with a second floor, or into a storehouse with no crops.
**Fallback tweak**: repeat "one single-storey room, one room tall" and remove the copper pipe (pipes invite industrial clutter).

### Variant C — four-tier lineup
**Goal**: communicate progression 60/150/300/600 slots in one image. Strong for players choosing a tier; weaker as a hero because each building is small.
**Prompt**
> Four identical square farm buildings, each three blocks wide and one block tall with a flat roof, stand evenly spaced in a row on one strip of green meadow, same isometric three-quarter view. From left to right the materials escalate: dark rough wood planks with open windows, then honey wood framing big greenhouse glass, then blue-grey riveted metal with tall glass panes, then darker metal and glass with heavy industrial framing. Crop shelves glow behind every window, one tiny beaver works at each door, afternoon sun from the left, chunky low-poly shapes, hand-painted textures.
**Main risk**: the four boxes drift in size/style and stop reading as one product line; or metal tiers dominate the palette.
**Fallback tweak**: add "four buildings exactly the same size, one continuous grass strip, equal gaps" and lower the sun to reduce metal glare.

### Variant D — optional, title rendered in-image
**Goal**: only if you want the title burned into the art (e.g. a version for the workshop description header). Otherwise skip it and composite text yourself (§4.6).
**Prompt**
> A game-art isometric illustration of a compact square wooden farm building with a flat plank roof, big open windows showing dense crop shelves, a small hut stacked on the roof and tiny beavers carrying crates on the grass in front. Large hand-painted cream lettering across the top reads exactly "HIGH-DENSITY BOX FARM" on dark plank boards, above a green meadow, teal river and grey stone terraces in warm afternoon light, chunky low-poly shapes, hand-painted textures.
**Main risk — read this before using D**: typo risk is high. Three capitalised words, a hyphen, and 18 characters is well past the model's reliable display-text zone; expect dropped letters ("HIGH-DENSITY BOX FAR"), broken hyphens, or a second ghost line. A failed letter costs a whole generation, and a cover with a misspelled title is worse than no title.
**Fallback tweak**: shorten the text to `BOX FARM` (two words, 8 characters) — or drop the text, take variant A, and set the title in an editor.

---

## 7. Do-not list (hard requirements)

1. **No fake game UI / interface elements.** No panels, toolbars, cursors, tooltips, resource counters, progress bars, status icons, or "selected building" highlight rings. Real UI belongs in separate screenshots, not the cover (and the mod's own panel screenshots already exist in [docs/in-game/](../../docs/in-game/)).
2. **No burned-in small text** — no stat rows, no feature bullet lists, no pseudo-localisation, no gibberish lettering. Any in-image text is one short, large line (and preferably composited later; see D's typo risk).
3. **Do not claim or imply "screenshot of the game".** This is **illustrative cover art** for a mod. Do not describe it as a capture, do not add screenshot chrome (window borders, FPS counters, watermark), and do not present it as official Timberborn material.
4. **No copyrighted Timberborn assets or logos**: no "TIMBERBORN" wordmark (the official header uses a distinctive chunky logotype), no Mechanistry logos, no copied vanilla building meshes, no mascot characters from other mods (e.g. Emberpelts' fox/beaver cut-outs). Homage in *style* is fine; brand marks are not.
5. **No third-party watermarks.** The prior reference generations came back with a "豆包 AI 生成" watermark burned into the corner (see [art/references/ref-basic-folktails.jpg](../../art/references/ref-basic-folktails.jpg)) — crop or regenerate, never upload anything carrying a mark.
6. **No false architecture.** The real building is **3×3×1 with a flat, stackable roof**: one storey, no slanted/tiered glass roof, no roof pipes or rooftop machinery, no protruding exterior steps, no multi-floor interior. If the art shows a second storey *inside* the box, the cover is lying about the mod.
7. **No fake new features** — no beaver NPCs as an advertised feature (they are workers, not characters), no conveyor belts, no pipes on the roof, no "power plant" fantasy.
8. **No grimdark/drought cover as the primary.** Dry-season rust is an accent at most; the hero must read lush and warm, matching the store capsule.

---

## 8. Handoff checklist

- [ ] Generate A at `Landscape 16:9`; check the **centre 1:1 crop** still reads as a box farm with a hut on top.
- [ ] Confirm no text, no UI, no watermark anywhere in the image; confirm one storey + flat roof.
- [ ] Composite `HIGH-DENSITY BOX FARM` bottom-left, ≥8 % margin, cream hand-painted type over a dark plank/teal band; optionally add `60 → 600 SLOTS`.
- [ ] Downscale to 1920×1080, keep the textless master in `docs/workshop/` (or the art pipeline) for future updates.
- [ ] Upload as workshop preview; the item already sets `"UpdatePreview": true` in [data/workshop_data.json](../../data/workshop_data.json).
- [ ] Keep B (cutaway) as a second image on the item page — it explains the mechanic faster than any paragraph.

---

### Citation index
- Timberborn store page — https://store.steampowered.com/app/1062090/Timberborn/
- Store API (screenshots/header/capsule) — https://store.steampowered.com/api/appdetails?appids=1062090&l=english
- Workshop app config (`aspect_ratio` 16x9, `usesquarethumbnails`) — https://steamcommunity.com/workshop/browse/?appid=1062090&browsesort=totaluniquesubscribers&section=readytouseitems
- This item's live page (subscriber/visitor counts) — https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784
- Surveyed items — Ladder https://steamcommunity.com/sharedfiles/filedetails/?id=3286476486 · TimberUi …?id=3430626620 · KnatteMaterials …?id=3281290867 · Goods Statistics …?id=3321521358 · Flywheels …?id=3322428023 · Staircase …?id=3277416566 · Emberpelts …?id=3346318229 · 1x1x2Storage …?id=3281653061
- Seedream 5.0 Lite prompting guide — https://fal.ai/learn/tools/how-to-use-seedream-5-lite
- Seedream vendor prompt guide (Chinese, not fetched here) — https://console.volcengine.com/ark/region:cn-beijing/docs/seedream-4-0-5-0-prompt-guide
