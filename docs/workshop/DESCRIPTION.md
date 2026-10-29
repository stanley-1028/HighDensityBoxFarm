# High-Density Box Farm — Steam Workshop 页面文案（终稿 v1.0）

> 对象：Steam Workshop item **3828809784**（Timberborn，app 1062090）
> 研究依据：[brief-growth.md](brief-growth.md)（流量机制/标签/社区）、[brief-art.md](brief-art.md)（封面视觉）
> 用法：§1 直接粘贴进 Workshop 页面描述编辑器（支持 BBCode）；§0 是页面元数据；§3 是中文社区推广文案。

---

## 0. 页面元数据（已在 `data/` 落地，走游戏内上传器）

| 项目 | 值 | 依据 |
|---|---|---|
| 标题 | `High-Density Box Farm — 3x3x1 Indoor Stackable Farm` | 保留品牌名；补上会被人搜的词（Steam 搜索匹配标题**或**描述）；`x` 用 ASCII，玩家不会打 `×` |
| 标签 | `Update 1.1` / `New content` / `Buildings`（不动） | 该细分共 121 个物品，是唯一不用买量的货架位；多余标签永久不可删，别加 |
| 预览图 | 新封面，见 [COVER_PROMPTS.md](COVER_PROMPTS.md) §0.5 | 浏览网格里只有 322×181，必须半秒可读 |
| 页面内嵌图 | 3–5 张 16:9 实机图 | 游戏内上传器不能内嵌图片，只能在 Steam 网页编辑器补 |
| manifest Description | = §1 的完整文案，**但删掉了 `[img]` 图和图注** | 游戏内上传器直接把它当 Workshop 描述 |

> **关于「manifest 里写 BBCode」的更正**：早先这页建议不要在 manifest 写 BBCode（担心 mod 管理器原样显示标签）。
> 2026-10-29 检查了游戏程序集 `Timberborn.Modding.dll / Timberborn.ModdingUI.dll`：mod 管理器没有读取
> manifest `Description` 的展示代码；真正读它的是 `Timberborn.SteamWorkshopModUploadingUI.dll`（上传器）。
> 所以 `data/manifest.json` 现在直接放 BBCode 文案，游戏内点一次更新即可让 Steam 页面变成富文本。
> 如果实测 mod 管理器确实显示了原始标签，改用网页编辑器路线（§1 原文粘贴）并清空 manifest 描述即可。

---

## 1. Workshop 页面描述（英文，BBCode，复制即用）

> ⚠️ 2026-10-29 13:22 已贴上页面，但 `[img]PASTE_IMAGE_x_URL[/img]` 三行占位符也一起贴了，页面现在有 3 个裂图。
> 粘贴前：**有图片 URL 就替换，没有就整行删掉（连同下一行 `[i]` 图注）**。
> 无占位符的干净版：[PAGE_COPY.txt](PAGE_COPY.txt)。

[h1]600 crops. One 3×3 box.[/h1]
[b]Four tiers of stackable indoor farms for Timberborn 1.1. No Harmony dependency, no Mod Settings requirement, no required companion mods — it keeps working when the game updates.[/b]

Land is the one resource Timberborn never gives back. Every carrot field you plant is a hole in your city plan. High-Density Box Farm is vertical farming the beaver way: four tiers of 3x3x1 farm buildings that simulate up to [b]600 crop slots[/b] on a single foundation — and their flat roofs are real floors, so the next farm can go on top.

[img]PASTE_IMAGE_1_URL[/img]
[i]Four tiers, one footprint — Basic, Intermediate, Advanced and Top.[/i]

[h2]What you get[/h2]
[list]
[*][b]Four tiers on the same 3x3x1 footprint[/b] — Basic 60 slots · Intermediate 150 · Advanced 300 · Top 600.
[*][b]Farming that happens inside[/b] — farmers enter the building and work at indoor slots. Every slot grows a vanilla crop with its real growth time, yield and products; you are not feeding a fake counter.
[*][b]Water is a system, not a decoration[/b] — the internal tank drains with game time. Refill it automatically through a pipe (Timber Pipes), by hauler trips, or with one click on the manual refill button.
[*][b]A roof you can build on[/b] — the top surface is a real floor: stack another box farm, a tank, or a vanilla building on it.
[*][b]Vanilla-style panel[/b] — slots, tank, coverage, maintenance, power, crop policy and warnings, plus world warning icons so a stalled farm is visible from the map.
[*][b]English · 简体中文 · 繁體中文[/b] — all UI text and building descriptions are localized.
[/list]

[h2]How it works — no free food[/h2]
The building is designed to be strong, not to be a cheat.
[list]
[*][b]Water[/b] — every slot drinks a little each day, and the tank holds about 3–4 days. Tank coverage sets the growth multiplier; growth slows below the minimum coverage and stops at a quarter of it.
[*][b]Labour[/b] — one farmer tends 38 slots, and every tier's crew is sized to cover its box exactly. 600 slots with 2 farmers is a very expensive empty box.
[*][b]Maintenance[/b] — Intermediate and above consume upkeep materials on a cycle (planks first, then metal blocks). Miss it and the farm stalls until a beaver performs maintenance.
[*][b]Power[/b] — the Top tier needs 150 power for full growth.
[*][b]Soil[/b] — [b]Normal[/b] is neutral, [b]Dense planting[/b] trades more water, more work and faster fatigue for +35% yield, and [b]Fallow[/b] stops production while the soil recovers. Rotate crops automatically or lock one crop per building.
[/list]

[img]PASTE_IMAGE_2_URL[/img]
[i]The panel: slots, tank coverage, maintenance, power and crop policy in one place.[/i]

[h2]The four tiers[/h2]
[list]
[*][b]Basic[/b] — 60 slots · 2 farmers · 40 logs · 200 science. Hand-fed water, no material upkeep. Your first box.
[*][b]Intermediate[/b] — 150 slots · 4 farmers · 40 planks + 20 logs · 600 science. Plank-frame greenhouse with plank upkeep.
[*][b]Advanced[/b] — 300 slots · 8 farmers · 50 planks + 30 metal blocks · 1000 science. Metal-and-glass house with metal upkeep.
[*][b]Top[/b] — 600 slots · 16 farmers · 100 metal blocks + 50 planks · 3000 science · 150 power · 96 in-game hours to build. The industrial farm complex.
[/list]

[h2]Compatibility[/h2]
[list]
[*]Built and tested against [b]Timberborn 1.1.2.8[/b] (Update 1.1).
[*][b]Zero Harmony patches[/b] — this is content, not a code patch. It does not fight other mods and it has no external dependencies.
[*]Safe to add to an existing save; it only adds new buildings. Back up the save before removing any mod mid-game.
[*][b]Optional integrations[/b], detected automatically and never required: [b]Timber Pipes[/b] for automatic tank refill, [b]Power Lines[/b] for easier power hook-up, [b]Conveyor Belts V2[/b] for output logistics (experimental, off by default in [i]HDBF_Config.json[/i]).
[*]Works with both [b]Folktails[/b] and [b]IronTeeth[/b].
[/list]

[h2]Install[/h2]
[list]
[*]Subscribe, open [b]Mods[/b] in the main menu, enable [b]High-Density Box Farm[/b], restart the game.
[*]The four buildings appear at the end of the [b]Food[/b] category.
[*]That's it — no dependencies, no extra downloads.
[/list]

[img]PASTE_IMAGE_3_URL[/img]
[i]Two box farms stacked on one footprint, with vanilla buildings on the roof level.[/i]

[h2]Honest limits[/h2]
[list]
[*]The interior is dressed as crop racks and the slots are simulated — you will not see a separate plant model for every slot. That is the trade that keeps a 600-slot farm as cheap as a normal building for your CPU.
[*]No Harmony means no patching of vanilla farm behaviour; existing fields work exactly as before.
[*]The Top tier is a late-game building. It is supposed to be expensive.
[/list]

[h2]Feedback[/h2]
Bug reports, balance feedback and screenshots are welcome in the comments and in the [b]Discussions[/b] tab — I read both and ship fixes. Changelog is on [b]Change Notes[/b].
Bugs are easiest to fix with: game version, mod version, and the relevant part of your save.

[h2]Credits[/h2]
Design, code and art direction: [b]stanley1028[/b], in collaboration with [b]DeepSeek V4.1 Flash 1017[/b].
Cover illustration generated with Seedream 5.0 Lite and finished by hand.

[h2]中文简介[/h2]
3×3×1 的室内高密度农场，共四档：基础 60 / 中级 150 / 高级 300 / 顶级 600 个作物槽位。屋顶是真正的平屋顶，下一座农场或原版建筑可以直接盖上去。农民在建筑内部作业；内部水罐随游戏时间消耗，可用管道（Timber Pipes）、工人搬运或面板上的手动补水按钮补充。维护材料、电力（顶级 150 动力）与土壤轮作都会影响产量——它不是作弊建筑，而是把土地成本换成水、人力和维护成本。支持原版两个阵营，无 Harmony 补丁、无前置依赖，英文 / 简体中文 / 繁体中文三语。本模组由 stanley1028 与 DeepSeek V4.1 Flash 1017 共同完成。

> 镜像到 mod.io 后，在页面末尾加一段链接，例如：
> `[h2]Links[/h2][list][*][url=https://mod.io/g/timberborn/m/PASTE_SLUG]mod.io mirror[/url][/list]`
> 没有镜像就删掉这一段，别留占位符。

---

## 2. 纯文本短描述（跨平台搬运 / mod.io / 帖子导语用，≤600 字符）

600 crops in one 3×3 building. High-Density Box Farm adds four tiers of stackable indoor farms to Timberborn 1.1 — Basic (60 slots), Intermediate (150), Advanced (300) and Top (600) — all on the same 3×3×1 foundation with a flat, buildable roof. Farmers work inside; the internal tank drains with game time and refills by pipe, hauler or a manual button; maintenance and power (150 for the Top tier) keep the economy honest. No Harmony patches, no dependencies, English / 简体中文 / 繁體中文, works with Folktails and IronTeeth. Made by stanley1028 with DeepSeek V4.1 Flash 1017.

---

## 3. 中文社区推广文案（贴吧 / B站 / 群 / 巴哈，按平台删改）

**标题**：`【模组】高密度盒子农场 — 3×3 里塞 600 个作物槽位，屋顶还能继续盖`

**正文**
玩 Timberborn 最缺的从来不是木头，是地。种一片胡萝卜，城就少一块。

这个模组把种地搬进室内：四档 3×3×1 的「盒子农场」，同一块地基上从 60 个槽位一路做到 600 个，屋顶是真正的平屋顶——下一座农场、水箱、甚至原版建筑都可以直接盖上去。

它不是白给粮食的作弊建筑：
· 内部水罐会随游戏时间见底，可以接 Timber Pipes 自动补水、让工人去搬、或者点面板上的手动补水；
· 高级/顶级要维护材料，顶级还要 150 电力；
· 有正常/密植/休耕三种模式，密植产量 +35% 但耗水和土壤疲劳更快；
· 每档满编工人刚好顾满整个盒子（每人 38 个槽位），缺人就是缺产量。

兼容性：Timberborn 1.1，无 Harmony 补丁、无前置依赖，两个阵营都能用，自带简体/繁体中文。加进现有存档即可，不用重开。

本模组由 stanley1028 与 DeepSeek V4.1 Flash 1017 共同完成；封面由 Seedream 5.0 Lite 生成后手工处理。

创意工坊搜「High-Density Box Farm」，或直接打开：
https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784

（图：四档同框 / 屋顶叠放实拍 / 面板截图）

---

## 4. manifest 描述

**现在 `data/manifest.json` 的 Description = §1 的完整文案（去掉 `[img]` 行与图注）**，
因为 Timberborn 游戏内上传器直接把它当作 Workshop 描述（证据见 §0）。改完重新部署即同步。

下面这段是**纯文本短版**，留给 mod.io 镜像、帖子导语、以及万一要回退成「短描述 + 网页长描述」时使用：

```
Four tiers of stackable 3x3x1 indoor farms (60/150/300/600 crop slots). Farmers work inside, the internal tank is refilled by pipe, hauler or a manual button, and maintenance and power keep output honest. No Harmony, no dependencies. English / 简体中文 / 繁體中文. Built by stanley1028 with DeepSeek V4.1 Flash 1017.
```

> 注意：走网页编辑器加了内嵌截图之后，下次游戏内更新会用 manifest 的无图版覆盖描述。
> 两个办法二选一：① 把截图 URL 补进 manifest 的 `[img]` 行后重新部署；② 网页改完后把
> `data/workshop_data.json` 的 `UpdateDescription` 改成 `false`。

---

## 5. 发页面前必须确认的 4 件事（诚实清单）

1. **屋顶叠放**：`docs/VERIFICATION.md` 把「屋顶叠放 A/B」标为待实机确认。**发布这条卖点前，亲自在游戏里把一座原版建筑放到屋顶上**；如果放不上去，把页面里的「A roof you can build on」删掉，封面也删掉叠放元素。
2. **研究数字**（成本/科学点/建造 96 小时/维护周期）来自 `data/Buildings/Food/BoxFarm/*.blueprint.json`；如果改过平衡，先核对再发。
3. **1.0 兼容**：`manifest.json` 写 `MinimumGameVersion 1.0.0.0`，但只有 1.1 验证过。页面不要写「兼容 1.0」，标签也不要加 `Update 1.0`。
4. **预览图文件**：预览图会占 Steam 云存储配额，换图会多留一份旧图；上传前把 16:9 主体裁成 1:1 检查一次（工坊网格可能方裁）。
