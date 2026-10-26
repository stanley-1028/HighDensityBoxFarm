# workstream: art-04-flat-roof-own-material — 自製一致材質外觀（flat UV patch）+ 單一空屋頂平台

Task: shared task `task-2`（docs-art04）。Lead 已重寫 Blender 生成器（`art/gen_models.py`）與匯出/驗證
管線，並把量測事實包 `.tmp-art04/FACTS-ART04.md` 交給本文件作者；本文的 `PENDING(...)` 欄位已依該
事實包與 machine-generated 證據（`art/timbermesh/export-manifest.json`、
`art/timbermesh-validation.txt`、`art/material_slots.json`、`art/icons/icons-manifest.json`）回填。
狀態：**使用者指令解讀、決策記錄、幾何/材質規格、實測數字、差異與誠實清單均已完成**；`tools/validate`
與 L1 的最終數字已由 csharp-gate 驗證並回填。**只剩 `PENDING(deploy-evidence)`**（部署，本卡不做）尚未回填。
**未做**：`build.ps1`、部署、啟動遊戲、實機外觀驗證（本卡明令不得執行）；自訂 PBR 材質 AssetBundle。
本報告**不聲稱**任何實機已驗證項目；所有離線數字都引自 FACTS-ART04.md、csharp-gate 的最終 gate 結果
及其列出的 machine-generated 證據，本文件作者未自行執行 Blender / 驗證器（唯一自行核對的是
`Get-FileHash` 的 16/16 SHA-256 相符）。

前置卡：ART-01（模型來源/座標契約）、ART-02（16 檔 `.timbermesh` 離線匯出與驗證）、
ART-03（8 藍圖接線 / 驗證器 / 圖示 / 文件）。
本卡觸發：使用者 2026-10-25 對 ART-03 上線後外觀的指示（§1，逐字保留）。

---

## 0. 摘要（可先看這段）

* 使用者要求三件事：(1) **自己造一個質感一致的材質**，不要 ART-02/03 的原版 atlas 拼布；
  (2) **屋頂不要放東西，只留一個空平台**；(3) **農作物的顯示不需要、也沒必要**（模型不帶作物/土壤視覺）。
* **決策 1 — 自製材質外觀（flat UV patch）**：沿用原版 `MaterialCollection` 材質資產名（分陣營，
  名稱是唯一合法來源），但把**同一材質槽的所有面映射到同一個固定 UV 貼片**，不再逐面把每個面塞進
  atlas 的不同矩形。實測貼片大小是 **88–384 texels**（`PATCH_TEXELS_PER_GRID = 64` 是 texel/grid
  密度，**不是**貼片尺寸；10 個 patch id 涵蓋 21 個原版材質名（其中 20 個實際用於 16 檔），見 §4.1）。外觀的一致性改由**幾何
  細節**承擔（木板接縫、框料、鉚釘、腳座、門框），不靠逐面 UV 拼貼。**這一步不需要 Unity /
  AssetBundle**：`.timbermesh` 只帶 uv0 與材質名字串，改 UV 分配即可，不需要新的材質資產。
* **決策 2 — 屋頂 = 單一空平台**：移除 6 個槽體 mesh（含土壤）與 6 個 `#Slot#Crop_0..5` 空節點；
  屋頂只留一片平的、空的平台。實測（16 檔一致，export == contract）：平台頂面 **Y = 0.96**；邊緣收邊
  高 **0.04**、寬 **0.04**、頂點 **Y = 1.0**；平台頂面以上 **0.06**（即 Y = 1.02）內無其它幾何（驗證
  C6 對 16 檔皆 `above=0`）。8 個 ConstructionStage0 灰盒也一樣只有一個空平台。只保留 3 個標記空節點：
  `#Port#Power`、`#Port#Pipe`、`#Port#Logistics`。
* **決策 3 — 作物視覺不需要**：模型本體沒有任何作物/土壤視覺；BUILD-05 的地面作物隱藏照舊
  （那是 gameplay 端的視覺抑制，不是模型內容）。
* 幾何信封不變：export space **3.000（X，寬）× 1.000（Y，高）× 2.000（Z，深）**、min = (0, 0, 0)、
  **block-corner 原點**（非置中）、正面在 Z = 0；3 個埠座標沿用 ART-02，實測位置偏差 0.0000。
  平台頂面高度 16 檔一律 **Y = 0.96**；平台平面大小 per tier 不同（Basic 2.05×1.25、
  Intermediate 2.45×1.55、Advanced 2.78×1.82、Top 2.96×1.96、ConstructionStage0 2.86×1.86）。
  貼片表、16 檔 bytes/三角數、驗證摘要、SHA-256 均已回填（§0.1、§4、§5、§6）。
* 幾何與檔案結構變簡單：節點數從 **10**（1 root + 6 slot + 3 port）降到 **4**（1 root + 3 port）。
  ART-04 實測合計 **1,266,760 bytes / 50,344 tris / 121,366 vertices**（成品 8 檔 1,193,582 B /
  47,560 tris；施工 8 檔 73,178 B / 2,784 tris）；舊 ART-02/03 為 1,172,971 B / 50,828 tris（僅供對照）；
  16 個檔案路徑不變。
* **誠實**：本卡只寫規格與文件；離線驗證（`python art\validate_timbermesh.py`、`tools/validate`、
  L1）與實機外觀都不是本文件作者執行的；**實機完全未驗**（§9、§11）。

### 0.1 `PENDING` 清單現況（事實包 + csharp-gate 最終 gate 已回填 11 項；只剩 `PENDING(deploy-evidence)`）

下列原 `PENDING(...)` 欄位已由 `.tmp-art04/FACTS-ART04.md`（量測值）與 csharp-gate 的最終 gate（`tools/validate` / L1）回填；本文件作者未自行量測，值見各節：

| 原 PENDING 名稱 | 回填值 / 出處 |
|---|---|
| `PENDING(tier-design)` | FACTS §6「Tier design under the empty flat roof」→ 本文 §3.3（四檔材質集 + body 處理 + 平台平面 + 細節密度） |
| `PENDING(tier-platform-height)` | FACTS §3：平台頂面 **Y = 0.96**（16 檔一致，export == contract）；收邊高 0.04、寬 0.04、頂點 Y = 1.0 → §2.2、§3.1 |
| `PENDING(flat-patch-table)` | FACTS §4 / `art/material_slots.json`：10 個 patch id，貼片 **88–384 texels**（64 texels/grid 密度）→ §4.1 |
| `PENDING(material-map)` | FACTS §5（unchanged from ART-02）→ §4.2；validator C5 對 16 檔全 PASS |
| `PENDING(port-coords)` | FACTS §3 + validation C6：3 埠沿用 ART-02 座標，16 檔 max position dev 0.0000（容差 0.020）→ §3.2 |
| `PENDING(file-list-tris)` | FACTS §1：16 檔 bytes/tris/vertices/SHA-256，合計 1,266,760 B / 50,344 tris / 121,366 vertices → §5 |
| `PENDING(validation-summary)` | FACTS §7：`16/16 present; 16 PASS, 0 FAIL`、`128 PASS, 0 FAIL, 0 NOT RUN`、`OVERALL VERDICT: PASS`、exit 0 → §6.1 |
| `PENDING(c9-definition)` | 已釐清：`art/validate_timbermesh.py` 只有 **C1–C8**，空平台檢查在 **C6**；C9 50% 剪影指標**保留**，另以未修改的 ART-01 指標函式/門檻對 ART-04 模型離線重跑：**LOD2（strict）12/12、LOD0 12/12 tier pairs PASS**（證據 `art/c9-art04-silhouette-probe.txt`；離線、非實機）→ §6.1、§9 |
| `PENDING(sha256)` | FACTS §1/§2：16 檔 SHA-256 全列於 §5；二次完整重跑 16/16 SHA 相同 → §6.3 |
| `PENDING(icons)` | FACTS §8 + `art/icons/icons-manifest.json`：ART-04 離線重渲染 512/128 圖示，4 張 128 已複製到 `data/Icons/`（SHA 相符）→ §7 |
| `PENDING(validate-l1)`（已回填） | **`tools/validate`: `RESULT: OK - data/ is valid.`**（game assemblies 660、spec types 506、mod assemblies 5/5 OK = HDBF.Core/Gameplay/Integration/Persistence/UI、blueprints 10、WARNINGS: none、exit 0）＋ **L1 `PASSED 512 FAILED 0`**（exit 0；README baseline 510 + 2 個 ART-04 tests）→ §6.2 |
| `PENDING(deploy-evidence)` | **仍未做（永久 out of scope）**：本卡不執行 `build.ps1`、不部署、不開遊戲；沒有部署大小 / REL-01 / deploy==stage SHA 證據 |

---

## 1. 需求出處（使用者原話，逐字保留）

> 「你还是自己造一个材質一样的吧,至於农作物的顯示就不用且沒必要了屋頂还是別放東西,並只有一個空平台吧。」

（2026-10-25 使用者對 ART-03 上線後外觀的指示。原文簡繁混用、標點不統一，**逐字保留不修正**。）

### 1.1 拆解成 3 個決策

| # | 使用者原話片段 | 解讀 | 本卡做法 |
|---|---|---|---|
| 1 | 「你还是自己造一个材質一样的吧」 | 不要目前原版 atlas 拼布；要一個**自己造的、質感一致**的材質外觀 | 每個 material slot 的所有面固定採樣同一個小 UV 貼片（flat patch）；細節交給幾何 |
| 2 | 「屋頂还是別放東西,並只有一個空平台吧」 | 屋頂**不要放任何東西**，只留**一個空平台** | 移除 6 槽體 mesh/土壤/槽緣與 6 個 `#Slot#` 節點；屋頂只留單一空平台；灰盒同样 |
| 3 | 「至於农作物的顯示就不用且沒必要了」 | 不需要作物視覺 | 模型不帶作物/土壤；BUILD-05 地面作物隱藏照舊 |

**範圍**：本卡是**美術契約**變更（模型內容 + UV/材質取樣方式 + 文件/驗證契約），**不改**
`HdbfBoxFarmSpec`、成本、產量、研究解鎖或任何 gameplay 數值（與 ART-03 的平衡資料無關）。

---

## 2. 決策記錄

### 2.1 決策 1：自製材質外觀 = flat UV patch（不需 Unity）

**機制（`.timbermesh` 這一層到底有什麼）**

* `.timbermesh`（`zlib(protobuf)`）每個 mesh 只帶兩個跟材質有關的資料：mesh 的**材質名字串**與頂點的
  **uv0**（`art/validate_timbermesh.py` C5/C7；格式見 ART-02 §4）。
* 遊戲載入時把材質名拿到**當前陣營**的 `MaterialCollection` 倉庫解析成原版材質，再用 uv0 去採樣
  該材質的貼圖。**我們沒有、也無法在這一步替換那張貼圖**。
* ART-02/03 的做法是 ART-01 的「逐面唯一 UV0」：每個面在 2048² 佈局裡有自己的矩形，但遊戲採樣的
  是**原版 atlas**，所以近看是拼布（ART-03 已誠實記錄為 deviation）。
* ART-04 改成：**同一 material slot 的所有面共用同一個固定 UV 矩形**（flat patch；實測貼片
  **88–384 texels**、密度 **64 texels/grid**，見 §4.1）。
  同一個材質的每個面都採樣同一小塊原版貼圖 → 顏色/紋理在整個材質上一致；變化的來源改成幾何
  （接縫、框料、鉚釘、腳座、門框）。**不再需要 per-face atlas packing，也不需要 2048² 的 ART-01 圖集
  來決定 UV 佈局**（該圖集僅作為 patch 的來源座標系保留，不進 `.timbermesh` 的 per-face 選擇邏輯）。

**為什麼這一步不需要 Unity / AssetBundle**

* 我們**沒有新增材質資產、貼圖、shader**；改的只是生成器寫進 uv0 的數值，以及沿用既有的原版材質名。
* `.timbermesh` 格式不變、載入路徑不變、`MaterialCollection` 解析方式不變；
  **不需要 Unity 匯入、不需要 AssetBundle、不需要 TimbermeshTools**。
* 真正的「自訂 PBR 材質」需要 Unity/AssetBundle（`docs/NEXT_ROUND.md`：Unity 6000.5.11f1 安裝器在
  China CDN 不可得、本機授權為中國區 2022.3 → 目前不可行），本卡**沒有**做，也不假裝做了。

**遊戲內取樣是什麼意思 / 誠實的不確定性**

* 「flat patch」不是「我們設計了一張均勻貼圖」；它是指**我們把 UV 固定到原版材質貼圖上的某一個小矩形**。
  畫面上每個材質會像「該材質在原版 atlas 裡的那一小塊顏色/紋理」——一致但不一定是我們想要的圖案。
* 選哪個 rect 會直接決定顏色與紋理；pixel rect 與 UV rect 已定案並列於 §4.1（權威來源：
  `art/material_patches.py` 的 `PATCH_PIXELS`，machine table 在 `art/material_slots.json`）。
* **未在遊戲中看過**：88–384 texels 的貼片放大到整面牆後的實際觀感（模糊/拉伸/條紋重複）、不同材質間的
  對比、法線/roughness/mask 取樣是否造成奇怪高光、貼片邊緣是否出現可見接縫或 banding、以及
  mipmap 下的閃爍，**全部 `待實機確認`**。本文件不聲稱 flat patch 在遊戲裡「看起來好」。
* 誠實定位：本卡把使用者要的「**質感一致**」做到 UV 層（消除拼布、每個材質一個固定 swatch），
  但嚴格意義的「**自己造一個材質**」（自訂貼圖/材質資產）仍要等 Unity——這是一個**部分滿足**，
  不是完整自訂 PBR。

**考慮過但未採用**

| 方案 | 為何不用 |
|---|---|
| 維持逐面唯一 UV0（ART-02/03 現況） | 就是使用者否掉的拼布 |
| 自訂 PBR 材質 / 2048² atlas / AssetBundle | 需要 Unity，目前工具鏈不可得（見上） |
| **flat UV patch（本卡）** | 不需 Unity，能消除拼布，立即改善一致性 |

### 2.2 決策 2：屋頂 = 單一空平台（不放任何東西）

* **移除**：6 個槽體 mesh（含土壤面）、槽緣/收邊框、屋頂上的通風管/風帽、雨棚、山牆、龍門架、
  輸送帶罩等一切屋頂構件。
* **保留**：一片平的、空的屋頂平台。實測平台頂面（walking surface）**Y = 0.96**（16 檔一致，
  export == contract）；平台**邊緣收邊**（rim lip）高 **0.04**、寬 **0.04**、頂點 **Y = 1.0**；
  **平台頂面以上 0.06（即 Y = 1.02）內不得有其它幾何**——驗證 C6 對 16 檔皆 `face=<n> above=0`。
  也就是平台上面唯一的東西就是 0.04 高的收邊，沒有槽、沒有土壤、沒有機具、沒有裝飾。
* **8 個 ConstructionStage0 灰盒**（4 tier × 2 faction，各自一個檔）也一樣：只有一個空平台
  （平面 **2.86 × 1.86**），不再有 6 個槽位框；只留 3 個埠標記；每個 348 tris / 834 vertices /
  2 materials。
* **空節點**：0 個 `#Slot#`；只留 `#Port#Power` / `#Port#Pipe` / `#Port#Logistics` 3 個。
  每檔節點數 = **4**（1 root + 3 port）。
* 幾何仍必須落在既有 **3×1×2 信封**內：平台+收邊的最高點 = 1.0（收邊頂點）；平台頂面 0.96 低於 1.0，
  「頂面以上 0.06」與「Y ≤ 1.0」兩條同時成立。平台平面大小 per tier 不同（Basic 2.05×1.25、
  Intermediate 2.45×1.55、Advanced 2.78×1.82、Top 2.96×1.96、ConstructionStage0 2.86×1.86），
  差異不影響「屋頂單一空平台」契約。

### 2.3 決策 3：作物視覺不需要（模型不帶作物/土壤）

* 模型不再有作物（模型本來就沒有作物 mesh，只有槽體/土壤）與土壤視覺；槽體移除後，
  **模型上不存在任何作物/土壤外觀**。
* BUILD-05 的「隱藏受管地塊上的作物視覺」照舊（那是 runtime 對 vanilla 作物的視覺抑制，
  見 `docs/workstreams/build-05-hide-managed-crops.md`；round-29 實機證據
  `gameplay.crop-visibility hidden=76 registered=300 hideFailures=0` 記在 `docs/NEXT_ROUND.md`）。
  **注意**：該證據是 BUILD-05 gameplay 隱藏系統的實機結果，**不是** ART-04 模型的實機結果；
  ART-04 的外觀仍須重新實機確認（§9）。
* 作物生長/收穫邏輯不變（gameplay），只是不顯示。

---

## 3. 幾何規格（凍結契約）

### 3.1 信封 / 邊界

| | 需求 13.3/13.6（contract） | 遊戲 `.timbermesh`（export） |
|---|---|---|
| 原點 | 佔地底面中心 | **block 角落**（左下前角） |
| X（寬） | [−1.5, +1.5] | **[0, 3]** |
| Y（高） | [0, 1] | [0, 1]（向上） |
| Z（深） | [−1, +1]，正面 −Z | **[0, 2]，正面在 Z = 0** |

* 換算：`export = contract + (1.5, 0, 1.0)`（ART-02 §3，軸向測試以負控制證實匯出器補償）。
* **size = (3.000, 1.000, 2.000)**、**min = (0, 0, 0)**，容差 ±0.05（驗證 C3）。四檔 × 兩陣營與 8 個灰盒
  共 16/16 全部 `size_dev=0.0000 min_dev=0.0000`；LOD 切換不會「跳一下」。
* 屋頂平台：頂面高度 **Y = 0.96**（16 檔一致）；收邊高 0.04、寬 0.04、頂點 Y = 1.0；
  **平台頂面以上 0.06（Y = 1.02）內無其它幾何**。此「平台/空頂」規則由驗證器 **C6** 的
  empty-roof-platform 檢查強制（`face=<n> above=0` per file；詳見 §6.1）。**`validate_timbermesh.py`
  只有 C1–C8，沒有獨立 C9。**
* 三角面預算：LOD0 **≤ 6,000**（驗證 C4；與 ART-02 相同預算）。實測最高 5,948（Top 兩檔），
  其餘成品 5,944；施工灰盒 348。只有 LOD0 進 `.timbermesh`（LOD1/LOD2 由遊戲端自動生成，見 §7）。
* 每檔節點數 **4**（1 root + 3 ports）；`slots` 在每個檔案都是空的；**0 個 `#Slot#`**。
* 頂點屬性：`position/normal/tangent/uv0` 必備；`color` 亦可存在（驗證 C7 的 superset 規則）。
  exporter 回報 **16/16** 檔案帶非白 `color`（BYTE_COLOR 通過 FBX round-trip），但**材質 shader
  是否消費頂點色未確認**（§9）；`uv1/uv2` 不使用。

### 3.2 3 個埠（保留；16 檔實測座標與 ART-02 相同，偏差 0.0000）

| 空節點 | contract（中心軸心） | export／遊戲（角原點） | 朝外 | 說明 |
|---|---|---|---|---|
| `#Port#Power` | (1.00, 0.85, 1.00) | (2.50, 0.85, 2.00) | +Z | 背面右側，Ø0.12 接線盒 |
| `#Port#Pipe` | (−1.50, 0.40, 0.00) | (0.00, 0.40, 1.00) | −X | 左短邊中心，Ø0.16 法蘭 |
| `#Port#Logistics` | (−1.00, 0.35, 1.00) | (0.50, 0.35, 2.00) | +Z | 背面左端，0.8×0.4 輸送帶開口 |

* 3 個埠是**空節點**（vertexCount = 0、無 mesh），位置偏差容差 ±0.02；驗證 C6 對 16 檔實測
  `ports 3/3 present exactly once`、`max position dev=0.0000`（tolerance 0.020），即座標與上表完全一致。
  即使屋頂清空，埠仍必須存在（`tools/validate` + L1 對自製模型的容器層標記檢查會驗證名稱存在，見 §6）。
* 需求 13.6 另列入口（−Z）與屋頂兩支通風口（+Y）；ART-04 的標記契約仍**只要求 3 個埠**
  （與 ART-02 相同），入口/通風口不是本卡的標記契約。
* 空節點 rotation 皆 identity（local +Z 朝外只記在座標語意，未寫進 transform）——沿用 ART-02 的
  已知限制，本卡未處理。

### 3.3 四檔設計差異：空屋頂下靠什麼區分

四檔共用同一個 3×1×2 信封與同一個空屋頂之後，**不能再靠屋頂剪影**區分（舊版 Basic 雨棚 /
Intermediate 雙坡 / Advanced 不對稱坡+翼 / Top 龍門架全部移除）。可用的區分手段只剩：

1. **材質類別與顏色**（flat patch 選的 swatch）：木/灰泥/玻璃/金屬的組合，同陣營兩檔之間也不同；
2. **牆體比例與開敞度**：實牆 vs 半開放框架、門洞/窗帶大小；
3. **幾何細節密度**（在 6,000 tri 預算內）：木板接縫、框架料、鉚釘/補強片、腳座、門框、屋簷收邊；
4. **陣營裝飾**（Folktails 圓角木作 vs IronTeeth 方形鉚接板）。

已定案方向（FACTS §6「Tier design under the empty flat roof」；`art/gen_models.py` 的 `tier_style`）：

| Tier | 設計語意 | 材質（實測，見 §4.2） | 平台平面 | 空屋頂下的主要區分點 |
|---|---|---|---:|---|
| Basic | 原木棚、最簡單的盒體 | Wood + Thatch/Roof（2） | 2.05×1.25 | 正面全開（立柱 + 橫樑，無門板）、無玻璃、木腳/飾條、單一木桶/木箱裝飾、最少框料 |
| Intermediate | 木板 + 灰泥 + 玻璃 | Planks/Wood + Glass + Plaster（3） | 2.45×1.55 | 木門、背牆 3 窗玻璃帶、白/橙灰泥牆帶、框料細 |
| Advanced | 白/灰木 + 烤漆金屬框 + 玻璃 | Wood + PaintedMetal + Glass + Details（4） | 2.78×1.82 | 金屬框 + 金屬門、背牆 4 窗 + 側面玻璃帶、鉚接細節板帶（算第 4 材質） |
| Top | 素金屬 + 玻璃 + 木板/鍛金屬 | Metal + Glass + Riveted plate + Timber deck（4） | 2.96×1.96 | 金屬牆/框/基座、8 片玻璃（背 4 + 側 2×2）、鉚接板帶 + 重型金屬裝飾、最多鉚釘 |
| ConstructionStage0 | 半成屋灰盒 | raw wood + bare metal（2） | 2.86×1.86 | 同一片空平台、半成牆面/地板；8 檔共用同一幾何 |

* 幾何細節由 geometry 承擔（不是貼圖）：board seams、frames、rivets（小方塊）、feet/base pads、
  door frame、platform curb/fascia rivets、platform supports/joists。
* 平台頂面 **16 檔一律 Y = 0.96**（不是 per-tier 變數）；可區分的是**平台平面大小**與上方設計，
  且不得破壞 3×1×2 信封與「平台以上只有 0.04 收邊、0.06 淨空」的規則。
* **C9 指標保留（離線重跑 PASS）**：舊 ART-01 `art/validate_models.py` 的 50% 剪影指標**沒有被刪**；
  它以**未修改的** ART-01 指標函式/門檻（`silhouette_views` / `outline_profiles`；
  `OUTLINE_MEAN_MIN=0.04`、`OUTLINE_MAX_MIN=0.12`、`SIL_IOU_MAX=0.85`、50% downsample）對新 ART-04
  模型離線重跑（`python .tmp-art04\c9_probe.py`，輸入 `art/models/<Tier>.<Faction>/LOD{0,2}.glb`）：
  **LOD2（strict）12/12 tier pairs PASS、LOD0 12/12 tier pairs PASS**，兩陣營、deciding views =
  **front+side**；證據 [`art/c9-art04-silhouette-probe.txt`](../../art/c9-art04-silhouette-probe.txt)。
  這是 **50% 鏡頭的離線幾何證據，不是實機**；`art/validate_models.py` 本身仍是 ART-01 契約
  （含已移除的 C4 槽體檢查）**未被更新**，所以**不得寫成「validate_models.py 通過」**；實機 50% 可讀性
  仍待實機確認（§9）。ART-04 的 `art/validate_timbermesh.py` 本身沒有 C9（空平台檢查在 C6）。

---

## 4. 平面貼片材質表

### 4.1 flat patch 表（實測；來源 `art/material_slots.json` + `art/material_patches.py`）

每個 material slot 對應**一個固定 UV patch**；同一 slot 的所有面共用它。ART-04 共有 **10 個 patch id**，
貼片大小 **88–384 texels**（`PATCH_TEXELS_PER_GRID = 64` 是 texel/grid 密度，不是貼片尺寸；最小
`glass` 為 88×88、最大 wood/metal 為 384×384）。權威機器表是 `art/material_slots.json`
（`material_patches` 陣列）與 `art/material_patches.py` 的 `PATCH_PIXELS`；下表逐值引自 FACTS §4：

| patch id | atlas region（ART-01 2048²） | pixel rect x0,y0,x1,y1 | UV rect (u0,v0,u1,v1) | texels | 使用的原版材質 |
|---|---|---|---:|---:|---|
| `wood_plain` | wood | 96,352,480,736 | 0.046875,0.640625,0.234375,0.828125 | 384×384 | `BaseWood_Brown.Folktails`, `BaseWood_DarkBrown.IronTeeth` |
| `wood_light` | wood | 96,1056,480,1440 | 0.046875,0.296875,0.234375,0.484375 | 384×384 | `BaseWood_LightBrown.Folktails`, `BaseWood_White.Folktails`, `BaseWood_Grey.IronTeeth` |
| `planks_light` | wood | 96,1568,480,1952 | 0.046875,0.046875,0.234375,0.234375 | 384×384 | `IrregularPlanks_LightBrown.Folktails`, `IrregularPlanks_DarkBrown.IronTeeth` |
| `planks_brown` | wood | 528,352,784,608 | 0.257812,0.703125,0.382812,0.828125 | 256×256 | `IrregularPlanks_Brown.Folktails` |
| `metal_base` | metal | 1056,384,1440,768 | 0.515625,0.625,0.703125,0.8125 | 384×384 | `BaseMetal.Folktails`, `BaseMetal.IronTeeth` |
| `metal_painted` | metal | 1056,1088,1440,1472 | 0.515625,0.28125,0.703125,0.46875 | 384×384 | `PaintedMetal.Folktails`, `PaintedMetal.IronTeeth` |
| `metal_details` | metal | 1056,1568,1312,1824 | 0.515625,0.109375,0.640625,0.234375 | 256×256 | `Details.Folktails`, `Details.IronTeeth`, `IrregularPlanks_MetalForged.IronTeeth` |
| `glass` | glass | 1744,704,1832,792 | 0.851562,0.613281,0.894531,0.65625 | 88×88 | `WindowsAtlas.Folktails`, `WindowsAtlas.IronTeeth` |
| `plaster` | canvas | 1872,288,2000,416 | 0.914062,0.796875,0.976562,0.859375 | 128×128 | `Plaster_White.Folktails`, `Plaster_Orange.IronTeeth` |
| `roof` | thatch | 1872,1472,2000,1600 | 0.914062,0.21875,0.976562,0.28125 | 128×128 | `ThatchedRoof.Folktails`, `RoofPlanks.IronTeeth` |

* **映射方式**（`art/material_patches.py` docstring）：`gen_models.py` 把每個面以固定 texel 密度
  （64 px/grid）投影進該材質 patch 內的一個 sub-rect，並用 deterministic offset；同一材質的所有面
  都留在同一 patch 內（patch 內小範圍 tiling 是預期的），**不會跨到別的材質 region**。
* **pixel rect 的語意**：貼片落在**原版材質貼圖 / ART-01 atlas 佈局**上的位置（top-left pixel origin，
  2048²）；顏色/紋理由原版資產決定，不是我們畫的。4 px inset 避免跨 region gutter。
* **UV rect 的語意**：`atlas_layout.px_rect_to_uv` 把 pixel rect 轉成 uv0 範圍（v 軸上下翻轉），
  也就是遊戲收到的那組 uv0 值。遊戲把材質名解析成 faction 的原版材質，再把這組 uv0 採樣到該材質的
  貼圖上；貼圖內容**不是**我們能替換的。
* `art/material_slots.json` 另有 `roof_platform`、`atlas`、`material_order`、`material_patches` 等
  機器可讀欄位與 `used_by_models` 清單；本文件只引用其權威值。

### 4.2 材質名（實測：沿用 ART-02，未變）

FACTS §5「Material map (unchanged from ART-02)」；validator C5 對 16 檔全部 PASS，且
`MATERIAL_MAP` 的 INFO 行 `extra=[] missing=[]`。材質集合如下（每列 `#` 為 distinct mesh.material 數）：

| Tier | Faction | # | Materials（原版資產名） |
|---|---|---:|---|
| Basic | Folktails | 2 | `BaseWood_Brown.Folktails`, `ThatchedRoof.Folktails` |
| Basic | IronTeeth | 2 | `BaseWood_DarkBrown.IronTeeth`, `RoofPlanks.IronTeeth` |
| Intermediate | Folktails | 3 | `IrregularPlanks_LightBrown.Folktails`, `WindowsAtlas.Folktails`, `Plaster_White.Folktails` |
| Intermediate | IronTeeth | 3 | `IrregularPlanks_DarkBrown.IronTeeth`, `WindowsAtlas.IronTeeth`, `Plaster_Orange.IronTeeth` |
| Advanced | Folktails | 4 | `BaseWood_White.Folktails`, `PaintedMetal.Folktails`, `WindowsAtlas.Folktails`, `Details.Folktails` |
| Advanced | IronTeeth | 4 | `BaseWood_Grey.IronTeeth`, `PaintedMetal.IronTeeth`, `WindowsAtlas.IronTeeth`, `Details.IronTeeth` |
| Top | Folktails | 4 | `IrregularPlanks_Brown.Folktails`, `BaseMetal.Folktails`, `WindowsAtlas.Folktails`, `Details.Folktails` |
| Top | IronTeeth | 4 | `BaseWood_DarkBrown.IronTeeth`, `BaseMetal.IronTeeth`, `WindowsAtlas.IronTeeth`, `IrregularPlanks_MetalForged.IronTeeth` |
| 施工（8 檔） | 各自陣營 | 2 | Folktails: `BaseWood_Brown.Folktails`, `BaseMetal.Folktails` / IronTeeth: `BaseWood_DarkBrown.IronTeeth`, `BaseMetal.IronTeeth` |

* 材質名必須是**當前陣營**的原版 `MaterialCollection` 資產名（faction-scoped；跨陣營名會在 prefab
  建立時拋 `Material <name> not found in repository`，ART-03/`docs/VERIFICATION.md` §9 已證實）。
  白名單與 `MATERIAL_MAP` 檢查沿用 `art/timbermesh_common.py`，ART-04 全數通過（C5：16/16 PASS）。

---

## 5. 16 檔清單與三角數（實測；`export-manifest.json` + `timbermesh-validation.txt`）

16 個檔案路徑與檔名**不變**（8 成品 LOD0 + 8 ConstructionStage0），全部在
`data/Buildings/Food/BoxFarm/`。16 個 `data/` 副本已是 ART-04 匯出物：本文件作者以 `Get-FileHash`
核對 `art/timbermesh/*.timbermesh` ↔ `data/Buildings/Food/BoxFarm/*.timbermesh`，**16/16 SHA-256 相符**。
下表 bytes / tris / vertices / SHA-256 逐值引自 FACTS §1（同一份 `export-manifest.json`）：

| # | 檔案 | 類型 | bytes | tris | vertices | SHA-256 |
|---|---|---|---:|---:|---:|---|
| 1 | `BoxFarm.Basic.Folktails.Model.timbermesh` | 成品 LOD0 | 148999 | 5944 | 14380 | `f3213b70d452e02cbc35d9c35541c4d87bcb63bc4503af3d3585f3b66b4857a0` |
| 2 | `BoxFarm.Basic.IronTeeth.Model.timbermesh` | 成品 LOD0 | 148660 | 5944 | 14372 | `57e66a0aa93d1afab0fa0be464454cbb8a04b9400774fb2732275e843f116829` |
| 3 | `BoxFarm.Intermediate.Folktails.Model.timbermesh` | 成品 LOD0 | 147832 | 5944 | 14326 | `5b78cd896b554ad1277e23d4e478b3830813243e336f6cfd7490f1f913f3f9d7` |
| 4 | `BoxFarm.Intermediate.IronTeeth.Model.timbermesh` | 成品 LOD0 | 146779 | 5944 | 14254 | `7cb96ad49fa67538bd4109a7f23def3a45ab4c56c24cd43a5e38b41ce9caae86` |
| 5 | `BoxFarm.Advanced.Folktails.Model.timbermesh` | 成品 LOD0 | 150326 | 5944 | 14355 | `59982415ad3e67bc55b11103c380e1119024ca4049fcafe1e48d7e3c3a01886b` |
| 6 | `BoxFarm.Advanced.IronTeeth.Model.timbermesh` | 成品 LOD0 | 149746 | 5944 | 14333 | `e515e80a386e4fb761003ef2e2edd8f82bd8a4d0fd9a600fbce3f09da2579461` |
| 7 | `BoxFarm.Top.Folktails.Model.timbermesh` | 成品 LOD0 | 150934 | 5948 | 14342 | `cf202759c0291007b4616694399613102952b0a29ac4c21428d219a0a7716b9a` |
| 8 | `BoxFarm.Top.IronTeeth.Model.timbermesh` | 成品 LOD0 | 150306 | 5948 | 14332 | `9b1282e815f41e753aee9e50a1b69b51882d12c7deb41be8f8109b2e40372036` |
| 9 | `BoxFarm.Basic.Folktails.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9142 | 348 | 834 | `3ab11bc49f67ca95e314fc6f52180ea913e9f6080bb4a5e4c93e6f16c39b6697` |
| 10 | `BoxFarm.Basic.IronTeeth.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9146 | 348 | 834 | `399729d296b5bf9fad1bea5bf616e0c490ec033ae29f662a55ab72dfc3fdc3a9` |
| 11 | `BoxFarm.Intermediate.Folktails.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9151 | 348 | 834 | `053c9e8fed9e6e3f4e969d5cbddc1d975b7ab2d48a73071cb925fde5577411cc` |
| 12 | `BoxFarm.Intermediate.IronTeeth.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9156 | 348 | 834 | `15129d0cbd5eb7d158dad65cdb5dc12a58696202d848b552ab8a2ae8ccd56c42` |
| 13 | `BoxFarm.Advanced.Folktails.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9147 | 348 | 834 | `7b79d1787cf8ba209c4538fe7f951ebbdcab231be8bc9ab078564aedd9bc2513` |
| 14 | `BoxFarm.Advanced.IronTeeth.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9151 | 348 | 834 | `a79bcb89994633670839a021f96a3f27327bf7f7dacf4be280fb25bb111dbe8e` |
| 15 | `BoxFarm.Top.Folktails.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9141 | 348 | 834 | `ce3e1469dd8da02b765b92b50c0a0b0fca01a33d4063d3df6db320d75dac86ef` |
| 16 | `BoxFarm.Top.IronTeeth.ConstructionStage0.Model.timbermesh` | 施工灰盒 | 9144 | 348 | 834 | `8f28229c413082ad8febe5307d5a498771faf33ee8bc1f675a0a8240fd8f4753` |

* **合計：1,266,760 bytes / 50,344 tris / 121,366 vertices**（FACTS §1）。
  成品 8 檔 **1,193,582 bytes / 47,560 tris**；施工 8 檔 **73,178 bytes / 2,784 tris**。
* 舊版（ART-02/03）供對照：合計 **1,172,971 bytes / 50,828 tris**（成品 47,564 tris、施工 3,264 tris）；
  ART-04 三角數少 **484 tris**，但 zlib bytes 多 **93,789 bytes**（+8.0%）；兩者皆為實測值，如實記錄，
  不推測原因。舊值不可用於 shipped 檔案。
* 8 個灰盒幾何/材質名相同（348 tris / 834 vertices / 2 materials each，平台平面 2.86×1.86），
  只有路徑與 `Model.Name` 不同（沿用 ART-02/03 的 8 檔慣例）。
* 每個檔案的節點清單固定 4 個：`[root, #Port#Power, #Port#Pipe, #Port#Logistics]`；`slots` 皆為空。
  LOD0 預算 6,000；實測最高 5,948（Top 兩檔）。

---

## 6. 驗證證據（離線；本卡未執行，Lead 提供 machine-generated 結果）

**原則**：`art/timbermesh-validation.txt` 必須全 PASS 才算離線幾何/格式完成；`tools/validate` 必須
`RESULT: OK`、L1 全綠才算資料契約完成。**兩者都已 PASS**（離線證據，非實機；數字由 csharp-gate
最終 gate 提供，見 §6.2）。

### 6.1 `python art\validate_timbermesh.py`（純 Python，不需 Blender）

`art/timbermesh-validation.txt`（**最新一次**：generated 2026-10-25T21:39:28+0800，純 Python / Blender-free，
CPython 3.14.7 on Windows，elapsed 0.71 s，schema `art/tools/timbermesh_blender_plugin/model_pb2.py`；
FACTS §7 引用的 5.92 s 是先前一次執行，FILES/CHECKS 結果相同）：

```text
FILES:   16/16 present; 16 PASS, 0 FAIL
CHECKS:  128 PASS, 0 FAIL, 0 NOT RUN (of 128 = 16 files x 8 checks)
OVERALL VERDICT: PASS
exit code: 0
```

> 誠實註記：報告檔頭標題字串仍是舊的 `HDBF ART-02 - .timbermesh validation report`（驗證器標題字串
> 未更新），但產生時間 2026-10-25T21:39 與內容都是本輪 ART-04 匯出後的執行；不影響 FILES/CHECKS 結果。

檢查項（ART-04 版 `art/validate_timbermesh.py`；**只有 C1–C8，沒有獨立 C9**）：

| Check | ART-04 內容 |
|---|---|
| C1 | zlib 解壓 + protobuf 解析 |
| C2 | `Model.name` == 檔名 stem、`Model.version` == 0 |
| C3 | 包圍盒 size (3.0, 1.0, 2.0)、min (0,0,0)、±0.05（block-corner） |
| C4 | 三角面 ≤ 6,000 |
| C5 | 材質數 1–4 且全在該陣營原版白名單（`MATERIAL_MAP` INFO 行 `extra=[] missing=[]`） |
| C6 | 空節點：**恰好 3 個埠**（`#Port#Power/Pipe/Logistics`，vertexCount=0、無 mesh、位置 ±0.02）、**0 個 `#Slot#`**，**加上 empty-roof-platform 檢查**（平台頂面 ±0.01 有面；平台頂面以上 0.06 內無頂點；per file 結果 `face=<n> above=0`） |
| C7 | 頂點屬性名稱/型別/維度/長度/無 NaN/Inf |
| C8 | 拓樸（索引範圍、parent、單一 root、uv0 ∈ [0,1]） |

* **C9 的釐清（重要）**：ART-04 的 `art/validate_timbermesh.py` **沒有 C9**；屋頂空平台檢查是 **C6**
  的一部分。但 ART-01 的 C9「50% 縮放剪影」指標**沒有被刪除**，且已對 ART-04 模型離線重跑：
  `python .tmp-art04\c9_probe.py` 匯入 `art/validate_models.py` 自己的 rasteriser 與門檻
  （`OUTLINE_MEAN_MIN=0.04`、`OUTLINE_MAX_MIN=0.12`、`SIL_IOU_MAX=0.85`、50% downsample），
  對 `art/models/<Tier>.<Faction>/LOD{0,2}.glb` 執行 → **LOD2（strict）12/12 tier pairs PASS、
  LOD0（report level）12/12 tier pairs PASS**，兩陣營，deciding views = front+side。證據：
  [`art/c9-art04-silhouette-probe.txt`](../../art/c9-art04-silhouette-probe.txt)。
* **C9 的 caveat**：這是 **50% 鏡頭的離線幾何證據，不是實機**。`art/validate_models.py` 本體仍是
  ART-01 契約（其 C4 槽體檢查已 stale）、**沒有為 ART-04 更新**，所以**不得寫成
  「`validate_models.py` 通過」**，只能寫「C9 指標以未修改的 ART-01 函式/門檻離線重跑：12/12 PASS」。
  實機 50% 可讀性仍須實機確認（§9）。

### 6.2 `tools/validate`（資料契約；task-1 實作）

* 8 藍圖的 `#Finished` / `#Unfinished.Children.ConstructionStage0` **Model 路徑不變**（與 ART-03 相同）。
  （另：8 份 blueprint 的 `BlockObjectSpec.Size` 已由並行的 block-axis 修正卡改為
  `{ X: 3, Y: 2, Z: 1 }`，那是佔格軸向修正，**不影響**本卡的 Model 路徑 / ART 契約。）
* `.timbermesh` 容器檢查（zlib header/解壓/Adler-32）**不弱化**；新增自製模型標記檢查：
  解壓後 payload 必須含 3 個埠名、且**不得含 `#Slot#` 子字串**（task-1）。
* `data/ART_PLACEHOLDER.md` 必須列出 16 條完整 model 路徑，且含 5 條 `ART_CONTRACT:` 機器可讀行
  （本卡已在 `data/ART_PLACEHOLDER.md` 加上並保持逐字不變）。
* 最終 gate（csharp-gate，2026-10-25 21:43，canonical `build.ps1` 呼叫方式，工作區根目錄，
  `$w = Join-Path $env:TEMP 'hdbf-card-work'`）：
  * `dotnet build tools\validate\validate.csproj -c Release -m:1 -p:HdbfWorkRoot=$w` → **0 warnings /
    0 errors, exit 0**；`src` 的 HDBF.Gameplay / Integration / UI 也各自 **0 warnings / 0 errors**
    （validator 斷言 5/5 mod assemblies；輸出導向 WorkRoot）。
  * `dotnet $w\bin\validate\Release\net9.0\HdbfValidate.dll data --assemblies $w\bin` → **exit 0**：
    game assemblies **660**、spec types **506**、mod assemblies **5/5 OK**（HDBF.Core / Gameplay /
    Integration / Persistence / UI）、blueprints **10**、`WARNINGS: none`、
    **`RESULT: OK - data/ is valid.`**
  * **注意**：`-- data`（裸形式）**不是** build.ps1 的呼叫方式；build.ps1 會傳
    `--assemblies $workRoot\bin`。少了它會因 legacy assembly layout 找不到而 exit 1，**不是** ART-04
    資料有問題。
  * **L1**：`dotnet build tests\HDBF.Tests\HDBF.Tests.csproj -c Release -m:1 -p:HdbfWorkRoot=$w` →
    0 warnings / 0 errors；`dotnet $w\bin\HDBF.Tests\Release\net9.0\HDBF.Tests.dll` →
    **`PASSED 512  FAILED 0`，exit 0**（README baseline 510 + 2 個 ART-04 tests；多次執行一致）。
  * **Negative controls**（在 `$env:TEMP` 的 data 副本上做，real `data/` 未動；全部 exit 1）：
    (A) 注入 `#Slot#Crop_0` 到解壓後 payload → exit 1；(B) 刪除 model 檔 → exit 1；
    (C) 半截斷 stream → exit 1（Adler-32 trailer mismatch）；(D) 移除最後 4 bytes trailer → exit 1
    （Adler-32 mismatch）；(E) 把 `#Port#Logistics` 改名 → exit 1（missing required port marker）。
    zlib/Adler-32 檢查未被放寬；ART-04 標記 gate 確實會觸發。
  * **data ↔ art**：16/16 SHA-256 相同；另一支獨立 python 解壓檢查：16/16 payload 可解、
    `#Slot#` 總數 **0**、每個檔案 `#Port#Power`/`#Port#Pipe`/`#Port#Logistics` 各恰好 1 次（3/3）。
* 以上皆為**離線資料契約驗證**；**不是**實機，也不含部署（部署 `PENDING(deploy-evidence)`，本卡不做）。

### 6.3 確定性（實測：16/16 相同 SHA-256）

* 重跑命令（工作區根目錄；FACTS §Pipeline）：

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_models.py
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\export_timbermesh.py
python art\validate_timbermesh.py
```

* FACTS §2：同一 `art/models/` 上連續兩次完整 `art/export_timbermesh.py` 執行產生 **16/16 identical
  SHA-256**（以程式比較 `art/timbermesh/export-manifest.json`；run-1 = run-2）。16 個 hash 列於 §5。
  另有 machine-generated 證據 [`art/timbermesh/export-determinism.txt`](../../art/timbermesh/export-determinism.txt)：
  `IDENTICAL: 16/16`、`RESULT: PASS - deterministic`（run-1 vs run-2 manifest 比較）。
* 這與 ART-02 的「同機、同 Blender、同來源 → 二次重跑相同」一致；**本文件作者沒有跑過生成器**，
  不另行宣稱 hash；數字引自 Lead 的事實包 / manifest。

---

## 7. LOD、圖示與資料層

* **LOD**：仍只交付 **LOD0**；沒有 LOD1/LOD2 `.timbermesh`，靠 `Timberborn.PrefabOptimization`
  的 `TimbermeshPrefabOptimizer` / `AutoAtlaser` 自動生成（class 名稱已於本機 DLL 證實，
  ART-03 §4）。**實際 LOD 切換品質/距離未實機確認**。
* **圖示（ART-04 已重渲染，離線）**：Lead 以 ART-04 模型用 `art/render_icons.py`（Blender headless
  Cycles CPU，非實機截圖）重渲染 `art/icons/*`，並把 4 張 **128×128** PNG 複製到
  `data/Icons/BoxFarm<Basic|Intermediate|Advanced|Top>.png`；本文件作者以 `Get-FileHash` 核對
  `data/Icons/*.png` ↔ `art/icons/BoxFarm.<Tier>.128.png` **4/4 SHA-256 相符**。
  `art/icons/icons-manifest.json` 的 `verification` 為 **PASS（8/8 files ok）**。實測 128 檔：

  | Tier | `data/Icons` 檔（128×128） | bytes | SHA-256 |
  |---|---|---:|---|
  | Basic | `BoxFarmBasic.png` | 14092 | `c611c815492587a80de3bca9f9f41febb97ed48a10e2766385ca1ddd784ad12d` |
  | Intermediate | `BoxFarmIntermediate.png` | 13200 | `1eb50aeeaaa3cc4c458387c755551c373a23a340a73df58e868be1e60d7c3244` |
  | Advanced | `BoxFarmAdvanced.png` | 14676 | `6d4b8012a7b2cbeeb9dab783eb83dcd8ee14fee40cead4632cf160ade4521bc7` |
  | Top | `BoxFarmTop.png` | 13102 | `ce70af287b9e4c44e408e8eac375625cc82072515efec16c3036230ace6e70a2` |

  `art/icons/` 另有 4 張 512×512 版；`.meta.json` 未變（`{"isSprite": true}`）。
  `art/previews/*` 也已用 ART-04 模型重渲染（離線；非實機）。圖示為 faction-independent，8 份藍圖
  共用 4 個 tier 圖示；**建造選單的實機呈現仍 `待實機確認`**。
* **Blueprint / 碰撞 / 擺放 / 平衡**：本卡不改 ART 以外的資料；模型路徑/平衡數值不變
  （`HdbfBoxFarmSpec`）。碰撞仍是單一 `CollidersSpec` box（`Size { X: 3.0, Y: 1.0, Z: 2.0 }`，
  Y 是高度）。佔格 `BlockObjectSpec.Size` 已由並行的 block-axis 修正卡改為
  `{ X: 3, Y: 2, Z: 1 }`（Y 是深度，3 寬 × 2 深 × 1 高），非本卡改動，詳見
  `docs/workstreams/bug-08-block-axis-size.md`。
* **8 份 blueprint**（每 tier × 陣營一份，材質 faction-scoped）維持不變，只有模型內容與上述佔格
  軸向修正變。
* **模板名** `BoxFarm.<Tier>.<Faction>` 不變。

---

## 8. 與舊版差異（ART-03 → ART-04）

| 項目 | 舊版（ART-01/02/03） | ART-04（實測） |
|---|---|---|
| 屋頂 | 6 個抬高槽體（含土壤/槽緣）+ 通風管/風帽 + 檔位專屬構件（Basic 雨棚、Intermediate 雙坡、Advanced 不對稱坡+翼、Top 龍門架+輸送帶罩） | **單一空平台**：頂面 **Y = 0.96**、收邊高/寬 **0.04/0.04**、頂點 Y = 1.0；平台頂面以上 0.06（Y = 1.02）內無其它幾何（C6 `above=0`） |
| 平台平面 | 各檔不同（隨槽體佈局） | Basic 2.05×1.25、Intermediate 2.45×1.55、Advanced 2.78×1.82、Top 2.96×1.96、ConstructionStage0 2.86×1.86 |
| 土壤/作物視覺 | 槽體土壤面；作物於地面隱藏 | **模型完全沒有土壤/作物視覺** |
| 空節點 | 10 個：6 × `#Slot#Crop_0..5` + 3 × `#Port#*` | **4 個**：1 root + 3 × `#Port#*`；0 個 `#Slot#`；`slots` 空 |
| UV 策略 | ART-01 逐面唯一 UV0；遊戲採樣原版 atlas → **近看拼布** | 每個 material slot **所有面共用一個固定 patch**：貼片 **88–384 texels**、密度 **64 texels/grid**（64 是密度，不是貼片邊長）→ 同材質一致 |
| 材質名 | 原版 MaterialCollection 名（分陣營） | **不變**（仍原版名；flat patch 只改 UV 分配）；C5 16/16 PASS |
| 頂點色 | （ART-02/03 未列） | exporter 回報 **16/16** 帶非白 `color`（BYTE_COLOR 通過 FBX round-trip）；shader 是否消費**未知** |
| 需要 Unity？ | 否 | **否**（自訂 PBR 仍需要，未做） |
| 三角面 | 每檔 5,940–5,950（總 50,828） | 成品 5,944 ×6 + 5,948 ×2（總 **47,560**）；施工 348 ×8（總 **2,784**）；合計 **50,344** |
| 檔案 bytes | 1,172,971 | **1,266,760**（+93,789；實測，如實記錄） |
| 施工灰盒 | 408 tris / 2 材質 / 含槽位框+埠 | **348 tris / 834 vertices / 2 材質**、空平台、節點 4 |
| 驗證器 | C1–C8（C6 檢查 6 槽+3 埠） | **C1–C8 only**（C6 = 3 埠 + 0 `#Slot#` + **empty-roof-platform 檢查**）；**沒有**獨立 C9 |
| C9 50% 剪影 | ART-01 舊模型 12/12 pair PASS | 以**未修改的 ART-01 指標函式/門檻**離線重跑：**LOD2（strict）12/12、LOD0 12/12 tier pairs PASS**（deciding front+side；`art/c9-art04-silhouette-probe.txt`）；**離線、非實機**；ART-01 validator 本體未更新 |
| 離線驗證 | ART-02 曾全 PASS | **16/16 PASS；128/128 checks PASS、0 FAIL、0 NOT RUN；OVERALL VERDICT: PASS；exit 0** |
| 確定性 | ART-02 二次重跑 16/16 SHA 相同 | 二次完整匯出 **16/16 identical SHA-256** |
| `data/` 副本 | 舊 ART-02/03 檔 | **ART-04 16/16 SHA-256 相符**（`art/timbermesh/*` ↔ `data/Buildings/Food/BoxFarm/*`） |
| 文件契約 | 16 條 model 路徑 | 16 條路徑 **+ 5 條 `ART_CONTRACT:` 行** |
| 圖示 | ART-02 舊外觀（含 6 槽） | **ART-04 離線重渲染**（128 已入 `data/Icons/`，4/4 SHA 相符；512 在 `art/icons/`）；實機呈現待確認 |

**變簡單的地方**：節點數 10 → 4；不再需要 per-face UV packing/2048 atlas 的逐面佈局決策；屋頂幾何從
多量體+槽體變成單一平面；生成器與驗證器的槽位邏輯可以刪掉（改驗「沒有槽」）。

---

## 9. 仍未做 / 待實機確認（誠實清單）

| 項目 | 狀態 |
|---|---|
| 實機外觀（四檔、兩陣營） | **待實機確認**；本卡只做離線文件/規格，未跑遊戲 |
| flat patch 的實際觀感（模糊/拉伸/圖案重複/接縫/高光/mipmap） | **待實機確認**；只知 UV 分配機制與貼片 rect，未看過遊戲成品 |
| 「自己造一個材質」的完整實作（自訂 PBR 貼圖/材質資產） | **未做**；目前是「UV 一致化 + 原版材質名」，真正的自訂 PBR 需要 Unity AssetBundle（工具鏈目前不可得） |
| 頂點色（`color`）是否被材質 shader 使用 | **未知**；exporter 回報 16/16 帶非白 color（BYTE_COLOR 通過 FBX round-trip），但 shader 支援與否未驗證，**待實機**；本卡不在模型上依賴頂點色 |
| 施工階段外觀（空平台灰盒）與完工替換 | **未在遊戲中看過**，待實機 |
| C9 / 50% 剪影 | ART-04 `validate_timbermesh.py` **沒有 C9**（空平台檢查在 C6）。C9 指標**保留**並以未修改的 ART-01 函式/門檻離線重跑：**LOD2（strict）12/12、LOD0 12/12 tier pairs PASS**（deciding front+side，`art/c9-art04-silhouette-probe.txt`）；**這是離線幾何證據，不是實機**；`art/validate_models.py` 本體仍是 ART-01 契約且未更新，**不得寫成「validate_models.py 通過」** |
| LOD1/LOD2 | 未交付；只 LOD0 進 `.timbermesh`，靠遊戲自動 LOD；切換品質未驗 |
| 圖示 | **ART-04 已離線重渲染並入 `data/Icons/`（4/4 SHA 相符）**；但那是 Blender Cycles CPU render，**不是實機截圖**，建造選單呈現待實機 |
| `art/previews/*` | 已用 ART-04 模型重渲染（離線）；非實機截圖 |
| 3 埠之外的入口/通風口標記 | 不在本卡契約（與 ART-02 相同）；空節點 rotation 仍 identity |
| 50% 鏡頭四檔可辨（需求 2.3/5.4） | **離線 C9 probe PASS**（LOD2 12/12、LOD0 12/12 pairs；deciding front+side）；但屋頂剪影差異已移除，區分來自身體/材質/細節，**實機 50% 可讀性仍待實機確認** |
| `tools/validate` / L1 最終結果 | **已驗證 PASS（離線）**：`tools/validate` exit 0、`RESULT: OK - data/ is valid.`（遊戲組件 660、spec types 506、mod assemblies 5/5、blueprints 10、0 warnings）；L1 **`PASSED 512 FAILED 0`**、exit 0。詳見 §6.2 |
| 本卡是否跑過 `build.ps1` / 部署 / 遊戲 | **沒有**（卡面明令禁止）；部署證據 `PENDING(deploy-evidence)` 永久 not done |

---

## 10. Lead 的建置 / 部署 / 實機步驟（本卡**未執行**，照卡面移交）

前置：`art/timbermesh-validation.txt` **已全 PASS**（16/16、128/128、exit 0）；`tools/validate`
**`RESULT: OK - data/ is valid.`（exit 0）**、L1 **`PASSED 512 FAILED 0`（exit 0）** 也都已由
csharp-gate 驗證（§6.2）。以下步驟**本卡仍未執行**。

1. **建置 + 部署**（完整 gate：5 組件 → L1 → tools/validate → staging → REL-01 gate → 部署到
   `C:\Users\user\Documents\Timberborn\Mods\HDBF`）：

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Install
   ```

   由**後續部署卡**回填 `PENDING(deploy-evidence)`（本卡不做部署）：部署大小、REL-01 gate 通過、
   工作區 `data/` 與部署 `Data/` 的 SHA-256 相等（16 個 `.timbermesh` + 8 藍圖 + 4 圖示）。

2. **實機前檢查清單**（開遊戲後）：
   * 建造選單四檔都出現（科學 200/600/1000/3000）；`Player.log` 無
     `Material ... not found in repository`、無 prefab/blueprint 轉換錯誤。
   * **每檔放一座**（Basic/Intermediate/Advanced/Top；兩陣營各一次，共 8 座），每座截圖：
     `docs/in-game/round<NN>-ART04-<Tier>-<Faction>.png`（實際輪次由 Lead 定）。
3. **要看什麼（驗收看點）**：
   * [ ] **材質一致**：同一材質槽的所有面是**同一個顏色/紋理 swatch**，沒有 ART-02/03 的
         per-face atlas 拼布；四檔材質類別可辨（木/灰泥/玻璃/金屬）。
   * [ ] **屋頂是單一空平台**：上面**沒有**槽、土壤、通風管、雨棚、山牆、龍門架、輸送帶罩；
         只有一片平的空平台 + 矮收邊。
   * [ ] **沒有作物/土壤視覺**：模型與屋頂看不到作物/土壤（地面作物仍由 BUILD-05 隱藏）。
   * [ ] **3×2×1 佔地**：貼齊、不穿地/不懸空、正面（−Z）朝入口同側。
   * [ ] **3 埠存在**：動力/管道/物流的接頭幾何與空節點對得上（外露程度依 tier 如舊）。
   * [ ] **施工階段**：放置瞬間不拋 slot 例外；灰盒是空平台、完工切成新成品模型。
   * [ ] **圖示**：建造選單四個 tier 圖示與新模型外觀一致（圖示已用 ART-04 模型離線重渲染並複製到
         `data/Icons/`；實機呈現仍待確認，非截圖證據）。
   * [ ] **50% 鏡頭**：四檔彼此可辨（材質/牆體/細節，不是屋頂剪影），與相鄰建築不混淆。
         （離線 C9 probe 已 PASS：LOD2 12/12、LOD0 12/12 pairs；此項仍是實機確認。）
   * [ ] **LOD 拉遠近**：無明顯跳動/破面（自動 LOD，待實機）。
4. **證據歸檔**：截圖 + `Player.log` → `docs/in-game/`；Lead 在 `docs/VERIFICATION.md` /
   `docs/REQ_TRACEABILITY.md` 記新一輪（本卡依規定不動那兩份）。

---

## 11. 誠實聲明

* 本文件作者**只寫文件**（`docs/workstreams/art-04-flat-roof-own-material.md` 與
  `data/ART_PLACEHOLDER.md`）；**沒有**執行 `build.ps1`、部署、啟動遊戲，也**沒有**跑 Blender 生成器、
  `art/validate_timbermesh.py`、`tools/validate` 或 L1。
* 所有「PASS」句子的來源是 Lead 的離線產出（`.tmp-art04/FACTS-ART04.md`、
  `art/timbermesh/export-manifest.json`、`art/timbermesh-validation.txt`、`art/material_slots.json`、
  `art/icons/icons-manifest.json`，以及 C9 probe 的
  [`art/c9-art04-silhouette-probe.txt`](../../art/c9-art04-silhouette-probe.txt)）與
  **csharp-gate 的最終 gate**（`tools/validate` / L1 數字，§6.2）。
  本文件作者唯一自行執行的是 `Get-FileHash`：核對 `art/timbermesh`
  ↔ `data/Buildings/Food/BoxFarm` **16/16 SHA-256 相符**、`data/Icons` ↔ `art/icons/*.128.png`
  **4/4 相符**。
* **只有離線驗證**（Lead / 其他 task 的產出）可以作為 ART-04 完成的依據；**沒有任何實機聲明**。
  部署 / `build.ps1` / 開遊戲**不在本卡範圍**，也沒有做。
* 數字（bytes/tris/vertices/驗證統計/SHA-256/貼片 rect/平台高度/圖示 bytes-hash）以 FACTS-ART04.md
  為準；`tools/validate` / L1 的數字取自 csharp-gate 的最終 gate 結果（本文件作者**沒有執行**
  它們）。**仍未回填的只剩 `PENDING(deploy-evidence)`**（部署；本卡不做，永久 not done）。

---

## 12. 團隊協作

| Task | Owner | 寫入範圍 | 產出/狀態 |
|---|---|---|---|
| 本卡 `task-2` | docs-art04 | `docs/workstreams/art-04-flat-roof-own-material.md`、`data/ART_PLACEHOLDER.md` | 本文件 + ART 契約文件更新；FACTS 與 csharp-gate gate 可回答的 `PENDING` 已全部回填，**只剩 `PENDING(deploy-evidence)`**（§0.1） |
| `task-1` | csharp-gate | `tools/validate/DataChecks.cs`、`tests/HDBF.Tests/ArtModelContractTests.cs`（必要時 `tools/validate/Program.cs`） | 標記檢查（3 埠存在、無 `#Slot#`）+ 5 條 `ART_CONTRACT` tags 的 L1；**最終 gate PASS**：`tools/validate RESULT: OK`（exit 0）、L1 `PASSED 512 FAILED 0`、negative controls A–E 全部 exit 1（§6.2） |
| Lead | lead | `art/gen_models.py`、`art/validate_timbermesh.py`、`art/material_slots.json`、`art/timbermesh/*`、匯出/驗證/圖示、`art/README-timbermesh.md` | 已交付 FACTS-ART04.md（量測數字）、16 檔匯出與驗證、ART-04 圖示 / previews；部署 / 實機步驟移交（未執行） |

寫入範圍互斥：本卡只碰兩個文件檔，`art/`、`src/`、`tools/`、`tests/`、其他 `docs/` 皆未動
（`data/` 只改 `ART_PLACEHOLDER.md`）。`data/ART_PLACEHOLDER.md` 的 `BlockObjectSpec.Size` 段落在本卡
期間由並行的 block-axis 修正卡更新為 `{ X: 3, Y: 2, Z: 1 }`，本文件作者原樣保留該段、未回退。
回填流程（已完成）：Lead 傳 FACTS 數字 → 本文件作者回填量測值；csharp-gate 最終 gate 完成後，
`tools/validate` / L1 數字已補入 §6.2 / §9 / §12。**只剩 `PENDING(deploy-evidence)` 永久 not done**
（本卡不做 build / 部署 / 開遊戲）。
