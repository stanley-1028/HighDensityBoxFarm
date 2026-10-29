# Steam Workshop 封面 — Seedream 5.0 Lite 提示词

> 交付物：**只给提示词**，不产出图片。
> 依据：[brief-art.md](brief-art.md)（8 个 Timberborn 热门工坊封面的原图分析 + Seedream 5.0 Lite 提示词规则）。
> 目标：替换 `data/thumbnail.png`（当前是白底灰框的单体渲染图，在浏览网格里只占三分之一画面，
> 没有任何「这是 Timberborn」的线索；页面现状 16 访客 / 5 订阅 / 0 收藏）。

---

## 0.5 实际封面处理结论（这张图已生成，2026-10-28）

素材：[cover/seedream-raw.jpg](cover/seedream-raw.jpg)（2732×1535，右下角带「豆包AI生成」水印）。
成品与 QA 都在 `docs/workshop/cover/`。

**判断：美术方向是对的，可以直接用，但上传前必须处理 3 件事。**

| 项目 | 结论 |
|---|---|
| 主体/氛围 | ✅ 木质方盒农场、屋顶上叠了一座小屋（叠放卖点）、两个海狸搬胡萝卜箱、室内水罐、密集作物货架、暖色夕阳、河与松树——一眼是 Timberborn，符合 brief |
| 水印 | ❌ 右下角「豆包AI生成」不能带上工坊。已用裁切去掉（`clean-1920x1080.jpg`）；**更推荐**用 API/关闭水印重生成一张原图 |
| 标题 | ❌ 原图无字。已做压字样张 `mockup-A-1920x1080.jpg`（Impact 大字，左下会被裁，故放左上） |
| 层高诚实性 | ⚠️ 画面读起来像两层货架，而模组是 **3×3×1 单层**（室内为抽象槽位）。作为插画封面可接受；若要重生成，加一句 `a single-storey building, exactly one block tall` |
| 屋顶小屋 | ✅ 是「原版建筑叠在平屋顶上」的正确表达（模组屋顶是平的、可叠放） |
| 322×181 可读性 | ✅ 已实测：建筑轮廓 + 两行标题在浏览网格尺寸仍可读（见 `cover/qa-322-comparison.png`） |
| 方裁风险 | ⚠️ 工坊若方裁，左上标题会被切（`cover/qa-mockupA-square540.png`）。实测工坊列表是 16:9（322×181），风险低；介意就上传无字版 `clean-1920x1080.jpg`，标题靠 Steam 标题栏 |

**这次实际用到的压字参数**（想复现或微调直接改 `cover/make_cover_qa.py`）：
左上角 x=62 / y=58 起，`HIGH-DENSITY` + `BOX FARM` 两行（Impact 122 px，奶油白 `#F7ECD0` + 深棕描边 7 px + 投影），
副标题 `600 CROPS · 3x3 · STACKABLE`（Arial Black 40 px，金色 `#FFE2A0`）。
B 版（深色底板）与 C 版（底部横条）都更差：底板挡住森林、横条压住两只海狸，已弃用。

**建议**：上传 `mockup-A-1920x1080.jpg`；同时保留 `clean-1920x1080.jpg` 作为无字母版（以后换版本号、换字体不用重生成）。
如果你有手写木质字体（如 Rye / Alfa Slab One / Cabin Sketch），把 Impact 换掉会更像 Timberborn 工坊的顶流封面。

**已落地**（2026-10-29）：压字版已转成 1280×720 PNG（256 色、475 KB，避开常见 1 MB 预览图上限）写进
`data/thumbnail.png`，并随元数据一起部署到 `Documents\Timberborn\Mods\HDBF`，等你在游戏内点一次更新。

---

## 0. 先看结论（30 秒版）

1. 用 **Variant A**（主图）生成 16:9 / 2K，一次出 2–4 张挑一张。
2. 挑中的图**保持无字**，用编辑器压上 `HIGH-DENSITY BOX FARM` 大字（Seedream 生成标题会拼错，不值得赌）。
3. 导出 **1920×1080 JPG**，先缩到 **322×181** 看是否还能读出「方盒子 + 屋顶上还盖了一座」。
4. 上传前检查 **中心 1:1 方裁**（Timberborn 工坊配置是 `usesquarethumbnails: true`）。
5. Variant B（剖面）不要当封面，作为页面第二张图，用来解释「里面真的在种地」。

> Steam 上工坊大图渲染只有 **322×181**（列表）/ 637×358（物品页），所以「一眼看懂」比「细节丰富」重要。

---

## 1. Seedream 5.0 Lite 参数

| 参数 | 值 |
|---|---|
| 模型 | Seedream 5.0 Lite（豆包/火山方舟，或 fal `bytedance/seedream/v5/lite`） |
| 尺寸 | **Landscape 16:9**（约 2560×1440）；备选 Auto 2K |
| 数量 | 每次 2–4 张（无 seed，同一提示词每次不同，多抽几张再挑） |
| 负面提示词 | **没有这个字段**。想要什么就写什么，不要写 "no ugly, no blur" 这类词 |
| 质量词 | **不要**写 masterpiece / 8K / ultra-detailed，会干扰它的推理阶段 |
| 提示词长度 | 2–4 句自然语言；把「谁在哪里、和什么发生关系」写清楚 |
| 提示词语言 | 下面的提示词是英文（Seedream 对中英都支持，英文描述关系更稳；豆包/即梦的中文输入框照样能粘） |
| 文字 | 大字可靠、小字不可靠；我们不在图里烧字（见 §4） |

---

## 2. Variant A — 主封面（推荐，直接用）

**目标**：一眼看出「这是 Timberborn 的建筑 + 一个方盒子农场 + 屋顶上还能再盖」。
**参数**：Landscape 16:9 / 2K。

```
Isometric game-art diorama of a compact square timber farm building, three blocks wide and one block tall, dark rough planks with mossy edges, big open windows showing dense shelves of carrots, wheat and potatoes glowing warmly inside. A much smaller wooden hut sits flat on its plank roof, and tiny beaver farmers carry crop crates across the grass in front while a round water tank shows through the doorway. Green meadow, a teal river, grey stone terraces and pines behind, warm late-afternoon sun from the left, chunky low-poly shapes, hand-painted textures, cheerful storybook mood, no text and no logos.
```

**必须检查**（不合格就重抽，不要将就）：
- [ ] 建筑是**单层**、**平屋顶**，没有斜屋顶/玻璃穹顶/二层；
- [ ] 屋顶上的小屋**明显更小**、平放，不是烟囱、不是塔、不是第二层楼；
- [ ] 窗户里**能看到密集作物**（哪怕只是暖光里的货架剪影）；
- [ ] 屋顶上**没有**管道、通风机、烟囱；
- [ ] 没有文字、Logo、UI 面板、水印；
- [ ] 缩小到 322×181 时，「方盒子 + 屋顶小屋」的轮廓还成立。

**常见失败与补丁**：
| 失败 | 改一句再抽 |
|---|---|
| 屋顶小屋变成了塔/烟囱 | 末尾加 `The hut is much smaller than the building and stays flat on the roof.` |
| 成了两层楼 | 把 `three blocks wide and one block tall` 改成 `a single-storey square building, exactly one storey tall` |
| 窗户里看不到作物 | 加 `the open windows are filled with bright carrot orange and wheat gold shelves, clearly visible from outside` |
| 太写实/太像照片 | 加 `flat painted textures, illustrated game concept art, soft brush strokes` |
| 画面太满，没地方放字 | 加 `the building sits slightly right of centre, open sky and soft green field on the left` |

---

## 3. Variant B / C — 页面内第二、第三张图

### B. 剖面图（解释「室内农场」这个核心机制，强烈建议放到物品页第二张）
**参数**：Landscape 16:9 / 2K。

```
Cutaway game-art illustration of a small square wooden farm building, front-left corner removed to reveal one single-storey room: long wooden racks packed with carrots, wheat and potatoes under warm lamp light, a round water tank with a copper pipe against the back wall, two tiny beaver farmers in simple hats tending the crops. Thick rough planks and iron brackets on the cut edges, flat plank roof above. Green meadow at dusk, cold blue exterior against golden light spilling from the opening, chunky low-poly shapes, hand-painted textures, no text and no logos.
```

风险：模型会给你盖出二层、或者画成没有作物的仓库。补丁：重复 `one single-storey room, one room tall`；删掉 copper pipe（容易引出工业管线）。

### C. 四档同框（放进页面说明四档递进；**不建议**当封面，四栋都会变小）
**参数**：Landscape 16:9 / 2K。

```
Four identical square farm buildings, each three blocks wide and one block tall with a flat roof, stand evenly spaced in a row on one strip of green meadow, same isometric three-quarter view. From left to right the materials escalate: dark rough wood planks with open windows, then honey wood framing big greenhouse glass, then blue-grey riveted metal with tall glass panes, then darker metal and glass with heavy industrial framing. Crop shelves glow behind every window, one tiny beaver works at each door, afternoon sun from the left, chunky low-poly shapes, hand-painted textures, no text and no logos.
```

风险：四栋大小/风格跑偏；金属档抢色。补丁：加 `four buildings exactly the same size, one continuous grass strip, equal gaps`。

---

## 4. 封面文字：在编辑器里压，不要交给模型

**不要用 Variant D（图内生成标题）当正式封面**：18 个字符 + 连字符，超出它稳定渲染大字的区间，
拼错就废一张图；封面拼错比没字更糟。真要试，只试两词：`BOX FARM`。

### 排版规格（1920×1080 画布）

| 元素 | 规格 |
|---|---|
| 主标题 | `HIGH-DENSITY BOX FARM`，左下角，占宽 45–55%，字高约 110–130 px |
| 字体气质 | 手绘/木刻感粗字：Alfa Slab One、Rye、Bungee、Cabin Sketch 一类；**不要**细体无衬线 |
| 颜色 | 奶油白 `#F3E6C8` + 深棕描边 `#241610` + 轻微投影；或压在深木色/深青色横条上 |
| 副标题（可选） | `600 CROPS · 3×3` 或 `STACK IT UP`，字高约 45–55 px，最多一行 |
| 安全区 | 四周留 ≥8%；文字不压建筑主体；中心 1:1 方裁后标题不丢 |
| 中文 | **不要**在图上放中文（322 px 宽时中文糊成一团）；中文靠标题栏和描述页 |
| 禁止 | "AI generated"、水印、句号长句、功能列表、假 UI 框 |

### 素材文件
- `cover-A-master.png`：无字原图（未来改标题/换版本不用重生成）
- `workshop-preview-1920x1080.jpg`：压字后的上传版，JPG 质量 ~90（控制体积，工坊预览图占 Steam 云配额）

---

## 5. 上传前 QA（照着点）

1. **比例**：正好 16:9，1920×1080；文件体积尽量 <1 MB。
2. **小图测试**：缩到 322×181 → 3 秒内能认出「一座方建筑、屋顶上还有一座」；文字可读。
3. **方裁测试**：中心裁 1080×1080 → 主体和标题都在。
4. **合规**：无 Timberborn 官方 Logo / 标题字；无他人模组素材；无水印（豆包 App 生成常带角标，用 API 或裁掉）。
5. **诚实**：不画二层、不画屋顶管道、不画假 UI、不宣称「游戏截图」——这是风格化封面插画。
6. **描述对应**：封面出现「屋顶叠放」，页面里就必须有屋顶叠放实拍图；如果叠放功能实测不成立，两边一起删。

---

## 6. 页面第二张图起：用真截图，不要用 AI 图

封面负责点击，页面里的图负责转化。上传顺序建议：

1. 封面（本文 A + 压字）
2. 四档同框实拍（真实游戏内，同镜头同背景）
3. 面板特写（槽位 / 水罐 / 覆盖度 / 维护 / 电力）——证明「有系统」
4. **屋顶叠放实拍**（如果实测成立）：一座箱式农场 + 屋顶上的原版建筑
5. 水链路：Timber Pipes 接管道 / 手动补水按钮
6. （可选）剖面插画 B，用来解释内部逻辑

每张都配一行 BBCode 图注；全部 16:9；不要九宫格拼图（列表页会糊）。
