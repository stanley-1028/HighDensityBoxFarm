# workstream: art-01-models — 盒子農場四檔 3D 模型來源資產（Lead）

Task: shared task `task-3`（Lead；依賴 `task-1` 圖集、`task-2` 驗證）。
狀態：**模型來源資產 + 離線自動驗證完成（PASS）**。
**未做**：Unity 材質 / AssetBundle、`.timbermesh` 匯出、Blueprint 路徑替換、實機外觀與 50% 鏡頭實測。
本輪只聲稱「模型來源資產與驗證完成」，不聲稱「已在遊戲中看到」。

---

## 0. 摘要（可先看這段）

* 產出 **4 檔 × 2 陣營 × 3 LOD = 24 個模型**（FBX + GLB 各一）＋ **1 個四檔共用施工階段灰盒**
  （FBX + GLB），以及 **1 張 2048×2048 PBR 圖集**（albedo / normal / mask 三張 PNG）。
* 所有 25 個模型 asset-space 包圍盒 = **3.000 × 1.000 × 2.000**（X × 高 × 深）、pivot 在佔地底面中心、
  Y-up、正面 −Z；三 LOD 包圍盒收縮 0%。
* 三角面：LOD0 5940–5950、LOD1 2376–2386、LOD2 792–800（預算 6000/2400/800 ±20%）；灰盒 408。
* 全驗證 **C1–C10 PASS、exit 0**（`art/validation-report.txt`，`OVERALL VERDICT: PASS`，FULL 模式
  GLB 解析 + FBX 重新匯入，實跑 97 s）。
* UV：每模型逐面唯一、全部落在 [0,1]、無重疊；有效 texel density 128–208 px/格
  （低於 13.4 建議 256，原因與處置見 §10，已誠實列為偏差）。
* 端口 6 個空物件、作物槽 6 個節點、四檔座標完全一致；端口幾何全部內凹/齊平，不破壞 3×2×1 包圍盒。
* 未修改 `src/`、`data/`、`tests/`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、`tools/validate`；
  未部署、未啟動遊戲、未跑 `build.ps1`、未碰 Unity。

## 1. 需求出處（只讀來源）

* `高密度盒子農場模組需求文件.md` 第 13 章（3D 建模任務提示詞：13.1–13.8）、第 5.4 節（檔位視覺識別）、
  第 2.3 節（四檔在 50% 縮放鏡頭下輪廓可區分）、第 18 章 art.lod* 切換距離。
* `data/ART_PLACEHOLDER.md`（佔地/pivot/入口/端口/命名凍結契約、TransformSlot 注意事項）、
  `docs/ARCHITECTURE.md` ADR-003（v0.1.0 無 `.timbermesh`，美術以 placeholder 契約先行）。
* 本輪交付卡（Team Lead 派工）：四檔外觀演進、6 槽、三端口、LOD0-2、2048 圖集、驗證報告、README、
  workstream 報告；不得碰 Unity / 部署 / 遊戲。

## 2. 產出物清單（全部在工作區）

```
art/
├── atlas_layout.py          共用契約（圖集分區/材質槽/端口/槽位/LOD 預算；sha256 bf0447bb…）
├── gen_models.py            程序化模型生成器（Blender 4.2.1 headless、deterministic）
├── gen_textures.py          2048² PBR 圖集生成器（numpy、deterministic；texture-artist）
├── validate_models.py       獨立驗證器（Blender 全檢查；純 Python 降級 GLB-only；validator）
├── render_previews.py       正交三視圖 + 50% 縮放預覽（Cycles CPU；validator）
├── material_slots.json      材質分配表（4 槽順序、圖集分區、每模型材質/三角數）
├── models/<Tier>.<Faction>/LOD0.fbx|.glb … LOD2.fbx|.glb   （8 組 × 3 LOD × 2 格式 = 48 檔）
├── models/ConstructionStage0/ConstructionStage0.fbx|.glb    （四檔共用灰盒，2 檔）
├── textures/atlas_albedo.png、atlas_normal.png、atlas_mask.png
├── previews/                22 張 PNG（每檔 front/side/top/scale50/iso + 灰盒 front/iso）
├── validation-report.txt    驗證報告（C1–C10，PASS）
└── README.md                模型清單、座標、節點、LOD、Unity 匯入與 TimbermeshTools 匯出步驟
```

檔案量：25 FBX（3.1 MB）＋ 25 GLB（212.5 MB，GLB 內嵌 2048² 圖集）＋ 3 PNG（8.6 MB）＋ 預覽/腳本/報告；
`art/` 總計約 227.5 MB。設計檔案（`.blend`）不另存：**可重跑的生成器腳本就是來源**（偏差見 §11）。

## 3. 造型決策

1. **精確 3×2×1 包圍盒優先**：四檔容積固定且不得越界（13.3 硬約束），因此所有「越界」造型都改成
   內凹/齊平：端口法蘭、物流輸送帶、動力接線盒、門片全部收在 footprint 內；包圍盒極值由
   **(a) 舖滿 3×2 的基礎板底面 y=0** 與 **(b) 兩支通風帽頂面 y=1.0** 精確給出。
   結果：25 個模型量測皆 3.000×1.000×2.000，LOD 切換不會「跳一下」。
2. **剪影階梯（13.2 / 5.4）而不是只靠貼圖**：Basic 低矮單坡雨棚（無後段高量體）→ Intermediate 後段
   對稱雙坡（脊 0.95）→ Advanced 不對稱雙坡（脊 0.995、脊偏後 0.58）+ 後右低翼 + 翼上第二支排風管
   → Top 三段式工業量體（後段全寬機房頂 1.0 + 前檢修龍門架 + 輸送帶罩）。四檔各有一個明顯不同的
   側向 skyline；細節（鉚釘/木板/帆布/玻璃分塊）都用「面數預算內」的生成方式補足，不靠堆裝飾。
3. **作物槽與通風口共存**：6 槽（3 列 × 2 排）固定在 y=0.80–0.94 的屋頂面；x = −1.0/0/+1.0、z = −0.25/+0.20，
   槽與槽間留 0.45 寬的橫向空隙；兩支通風口正好落在 x=±0.5 的空隙、z=∓0.3，**不與槽體穿插**，
   風帽頂面 = 1.0 兼作包圍盒頂點。四檔槽位座標完全一致（升級不跳位、可共用圖集/Unity 槽腳本）。
4. **四檔共用一套交界線**：基礎板、牆面、入口中線、端口高度、槽位、通風口座標四檔一致；差異只做在
   牆體開敞度、覆層材質、上方量體與端口外露程度。
5. **LOD 不用 Decimate，而是顯式簡化**：各 LOD 由同一套生成器以不同細節旗標重建（牆板/槽體/風口/
   框架/覆層逐級簡化），保留輪廓與槽位；三角面用「倒角鉚釘/補強件」自動填到目標（避免整體縮小造成
   輪廓失真，符合 13.5 的簡化順序要求）。
6. **隱藏面剔除 + 逐面唯一 UV**：為了在 2048² 單一圖集裡塞下 6000-tri LOD0 且 UV 不重疊，
   生成器會剔除不可見面（基礎板底、deck 底、牆的室內面、貼牆背面、槽底…），並把每個面配到
   語意圖集區的唯一矩形（4 px gutter、必要時 90° 旋轉、自動選最大可行密度）。
7. **陣營差異**：尺寸/槽位/端口/材質槽順序兩陣營完全相同，只差裝飾與金屬/帆布比重
   （Folktails 倒角、木桶木箱、帆布綁帶；IronTeeth 方形鉚接板、方通風帽、金屬補強件）。
8. **施工階段**：四檔共用 1 個灰盒（基礎板 + 柱樑骨架 + 半高牆 + 木護板 + 6 個槽位框 + 6 個端口空物件），
   尺寸/軸心/包圍盒與成品相同；藍圖的 ConstructionStage0 由 Lead 換成同一個路徑。

## 4. 四檔差異與剪影數據

| 檔位 | 剪影/量體 | 覆層與開敞度 | 主材（槽） | 脊/最高非風口 | 端口外露 |
|---|---|---|---|---:|---|
| Basic | 低矮木棚：前方單坡帆布雨棚 + 平坦槽面 + 兩支細通風管 | 正面全開敞、上半透空框架（半高木牆） | Wood + Fabric | 0.80（風帽 1.0） | 管道口蓋板 |
| Intermediate | 後段對稱雙坡（帆布/玻璃混合） | 半開放：側面帆布捲簾、混合玻璃 | Wood + Glass + Fabric | 0.95 | 管道 + 物流 |
| Advanced | 不對稱雙坡 + 後右低翼 + 翼上第二排風管 | 金屬框 + 玻璃 + 帆布、法蘭外露 | Wood + Metal + Glass + Fabric | 0.995（風帽 1.0） | 管道 + 物流（動力蓋板） |
| Top | 三段式：後段全寬工業機房（頂 1.0）+ 前龍門架 + 輸送帶罩 | 玻璃帶 + 金屬百葉/鉚接板 | 四種全用 | 1.0 | 管道 + 物流 + 動力 |

驗證器 C9（50% 縮放、LOD2 嚴格 / LOD0 參考）以「輪廓 skyline 差異」為判據：
pair 在任一視圖滿足 `mean|Δh| ≥ 0.04 格` 或 `max|Δh| ≥ 0.12 格` 或面積 IoU ≤ 0.85 即 PASS；
**12/12 pair 全部 PASS**（兩陣營），LOD0 亦全部 PASS。面積 IoU 因四檔共用同一 3×1×2 包圍盒而必然偏高
（front 0.93–1.00、top 1.00），已標為 informational，不是判據——這點在報告與 README 都誠實寫明。
**最緊的一對**：Intermediate vs Advanced 的 LOD2 只靠 side view 過關（max |Δh| = 0.125，門檻 0.12，
餘量僅 0.005 格）；這個側向 skyline 差異由「Advanced 不對稱屋脊（脊偏後 0.58）+ 翼上第二支排風管」提供，
未來改生成器時不可移除，否則 C9 會回歸 FAIL。其餘 pair 餘量充足（如 Basic vs Top side max 0.219）。
`art/previews/*_scale50.png` 為 50% 縮放對照圖；`*_iso.png` 為等角檢視。

## 5. 三角面 / 邊界 / UV 密度數據（逐模型）

| 模型 | LOD0 tris（UV px/格） | LOD1 | LOD2 | bounds |
|---|---:|---:|---:|---|
| Basic.Folktails | 5940（144） | 2386（176） | 798（192） | 3.000×1.000×2.000 |
| Basic.IronTeeth | 5940（128） | 2386（176） | 798（192） | 3.000×1.000×2.000 |
| Intermediate.Folktails | 5950（128） | 2380（160） | 800（192） | 3.000×1.000×2.000 |
| Intermediate.IronTeeth | 5950（128） | 2380（160） | 800（192） | 3.000×1.000×2.000 |
| Advanced.Folktails | 5942（176） | 2376（208） | 792（208） | 3.000×1.000×2.000 |
| Advanced.IronTeeth | 5942（176） | 2376（208） | 792（208） | 3.000×1.000×2.000 |
| Top.Folktails | 5950（144） | 2384（176） | 796（192） | 3.000×1.000×2.000 |
| Top.IronTeeth | 5950（144） | 2384（176） | 796（192） | 3.000×1.000×2.000 |
| ConstructionStage0 | 408（160） | — | — | 3.000×1.000×2.000 |

* 預算：LOD0 6000、LOD1 2400、LOD2 800（±20%，即 4800–7200 / 1920–2880 / 640–960）；全部在範圍內。
* 三角數以三角化後實際值計（FBX 與 GLB 一致，C3 雙格式各檢一次）；碰撞體不計（模型不含 collider）。
* 包圍盒 pivot：X/Z 中心誤差 0.0000、minY=0；LOD0→1→2 收縮 0%（要求 ≤2%）。
* UV 有效密度 128–208 px/格（LOD0 128–176），原因見 §11 偏差 1。

## 6. 貼圖與材質數據

* 圖集：**2048×2048、四檔/兩陣營/灰盒共用**、3 張 PNG（albedo sRGB／normal LINEAR OpenGL +Y／
  mask LINEAR RGBA：R=metallic、G=roughness、A=smoothness、B=metallic 重複給 glTF）。
* 圖集由 texture-artist 以 numpy 程序化生成（木紋/木板、濕土/乾土、金屬板/鉚釘/刮痕、管件、
  玻璃污漬、帆布織紋、茅草），patch-safe（任意 30–200 px 子區塊可讀、含 90° 旋轉）、4 px gutter、
  7/7 區域全繪、無文字/LOGO/浮水印。SHA-256（deterministic，double-run 一致）：

  | 檔案 | bytes | SHA-256 |
  |---|---:|---|
  | atlas_albedo.png | 3,451,158 | `048b267c34af327acc59b2279d23bf4b916ffbdb5c06edc4ab8b5b2dea991677` |
  | atlas_normal.png | 2,729,832 | `9241e73a945b5647e5cb7569ec465ae387352d7bc84aea7885839ed51e2fe740` |
  | atlas_mask.png | 2,416,267 | `e780ab0298aa74ff5db0d49b746ad41fefbeaa1d1c352bae21f70c7bedf8aae5` |

* 材質槽順序固定 0 Wood / 1 Metal / 2 Glass / 3 Fabric；每檔只引用自身需要的子集（順序不變）：
  Basic = Wood+Fabric；Intermediate = Wood+Glass+Fabric；Advanced/Top = 全 4 個；灰盒 = Wood+Metal。
* 材質帶（U，偏離 13.4 建議值，見 §11）：Wood 0.0000–0.4639、Metal 0.4639–0.8447、
  Glass 0.8447–0.9014、Fabric 0.9014–1.0000。
* 圖集分區：wood / soil_full / soil_empty / metal / glass / canvas / thatch（像素 rect 與 UV rect 見
  `art/README.md` §8 與 `art/material_slots.json`）。

## 7. 端口與槽位清單（給 Unity 端轉空物件標記）

### 7.1 端口（6 個空物件，四檔/三 LOD 座標完全相同）

| 空物件 | asset 座標 (X,Y,Z) | 朝外 | 接口尺寸 | 幾何外露 | Blender 位置/旋轉 |
|---|---|---|---|---|---|
| `PORT_HDBF_Entrance` | (0.00, 0.00, −1.00) | −Z | 淨寬 1.10 × 淨高 0.60 | 全部 | (0,1,0) / X −90° |
| `PORT_HDBF_Power` | (1.00, 0.85, 1.00) | +Z | Ø0.12（接線盒+絕緣子） | Top 外露；其餘蓋板 | (1,−1,0.85) / X +90° |
| `PORT_HDBF_Pipe` | (−1.50, 0.40, 0.00) | −X | Ø0.16（法蘭；Basic 僅 0.02 深蓋板） | 全部 | (−1.5,0,0.4) / Y −90° |
| `PORT_HDBF_Logistics` | (−1.00, 0.35, 1.00) | +Z | 0.80 × 0.40（輸送帶開口） | Intermediate 起；Basic 蓋板 | (−1,−1,0.35) / X +90° |
| `PORT_HDBF_Vent_01` | (0.50, 1.00, −0.30) | +Y | 0.30 × 0.30 | 全部 | (0.5,0.3,1.0) / 無旋轉 |
| `PORT_HDBF_Vent_02` | (−0.50, 1.00, 0.30) | +Y | 0.30 × 0.30 | 全部 | (−0.5,−0.3,1.0) / 無旋轉 |

* 空物件 local +Z 指向外側（Blender 作者座標驗證）；C5 量測座標與方向誤差皆 **0.0000**。
* 端口幾何全部在 3×2×1 內（內凹/齊平）；外接管線/輸送帶接頭由 Unity prefab 端依這些空物件延伸。

### 7.2 作物槽（6 個 mesh 節點，LOD0–2 都存在；四檔位置一致）

| 節點名 | 排 | asset (X, Z) | 變體 |
|---|---|---|---|
| `SM_HDBF_BoxFarm_<Tier>_Trough_01_LOD<n>` | 前 | (−1.00, −0.25) | empty |
| `..._Trough_02_...` | 前 | (0.00, −0.25) | full（幼苗幾何） |
| `..._Trough_03_...` | 前 | (1.00, −0.25) | empty |
| `..._Trough_04_...` | 後 | (−1.00, 0.20) | full |
| `..._Trough_05_...` | 後 | (0.00, 0.20) | empty |
| `..._Trough_06_...` | 後 | (1.00, 0.20) | full |

槽外形 0.55×0.35（X×Z）、底 0.80、緣 0.94、土面 0.925；空/滿槽用 UV 分區
（`soil_empty`/`soil_full`）實現；滿槽 4 株低模幼苗（遊戲作物仍由程式隱藏，見 BUILD-05）。
每個槽節點另帶自訂屬性 `hdbf_slot_index`（1–6）、`hdbf_slot_variant`（empty/full）、`hdbf_slots_total`=6，
body 節點帶 `hdbf_tier/faction/lod/slot_count`；端口空物件帶 `hdbf_port_class/hdbf_port_outward`。

## 8. LOD 與切換

| LOD | 預算 | 實測 | 切換距離（格） | 陰影 | 保留 |
|---|---:|---|---:|---|---|
| LOD0 | 6000 ±20% | 5940–5950 | 0–18 | 開 | 全細節 |
| LOD1 | 2400 ±20% | 2376–2386 | 18–45 | 開 | 輪廓、門洞、覆層分塊、端口基座、簡化槽 |
| LOD2 | 800 ±20% | 792–800 | 45–90 | 關（60 格起） | 剪影、材質分區、門凹槽、端口蓋板、淺槽 |
| 剔除 | — | — | 90+ | — | — |

* 槽位節點 LOD1/2 保留 6 個（幾何簡化而非合併節點）→ Unity 槽位索引不隨 LOD 改變（刻意偏離 13.5 的
  節點合併建議，成本僅 144/84 tri，見 §11 偏差 3）。
* 端口空物件三個 LOD 都存在且座標相同（13.5 要求，避免 LOD 切換造成管線錯位）。

## 9. 驗證（可重跑）

```powershell
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_textures.py
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_models.py
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\validate_models.py
```

實跑結果（本機）：
`mode: FULL (GLB parsed + FBX re-imported)`；預期 54 檔（24 模型×2 格式 + 灰盒×2 + 3 PNG + material_slots.json）；
**C1–C10 全部 PASS、`OVERALL VERDICT: PASS`、exit code 0**（`art/validation-report.txt`）。
檢查內容：檔案的完整性、bounds/pivot（GLB+FBX 雙檢）、三角預算、6 槽節點與座標、6 端口座標與
local +Z、材質槽與圖集 PNG、UV 越界/重疊（512² coverage grid；1024² grid + 0.5 px inset 的 epsilon
診斷亦全 0）、LOD 包圍盒收縮、50% 輪廓可辨識（C9）、`material_slots.json` 一致性。
無 Blender 時 `python art\validate_models.py` 走 GLB-only 降級模式，FBX 會被標記 `REDUCED`，不會假裝通過。
非失敗 warning：LOD2 簡化後 C5 的「端口凹槽鄰近啟發式」在 Logistics（全 LOD2）與 Entrance（Advanced/Top
LOD2）找不到凹槽頂點，已列報告資訊列（規格明定 C5 不得因此 FAIL）；GLB 內嵌圖集 image refs 屬預期。

預覽：`art/render_previews.py` 用 Cycles CPU 輸出 **22 張 512² PNG**（四檔 front/side/top/scale50/iso +
灰盒 front/iso），無文字/標註；燈光已調亮（sun 3.6 + fill 1.4 + world 0.65、Standard view transform
exposure +0.5），可作為三視圖比例與材質核對（無格線 overlay，比例依據為 1 格 = 1 m + 已知 3×2×1 包圍盒）。

## 10. 團隊協作

| Task | Owner | 產出 | 結果 |
|---|---|---|---|
| task-1 | texture-artist | `art/gen_textures.py` + 3 張 2048² PBR PNG | 完成；deterministic（double-run byte-identical）、7/7 區全繪、SHA-256 見 §6 |
| task-2 | validator | `art/validate_models.py`、`art/render_previews.py`、`art/validation-report.txt`、`art/previews/` | 完成；C1–C10 PASS、22 張預覽、附 C9 修訂判據 |
| task-3 | Lead | `art/gen_models.py`、`art/atlas_layout.py`、`art/material_slots.json`、`art/models/`、`art/README.md`、本報告 | 完成；25 模型生成、驗證 PASS、README 交接完成 |

協作介面：`art/atlas_layout.py` 是凍結契約（sha256 `bf0447bb1bcf92c74d89ed7764e3b369b22e35c60b5d4a5c42acf40d7759472b`），
三個腳本都 runtime import 它；寫入範圍互斥（textures / validator / models），沒有互相覆蓋。
過程中因 UV 容量/剪影判據迭代過 3 次圖集分區與 2 次線條高度，最終凍結；最後一輪完整生成 + 驗證通過。

## 11. 偏差與限制（誠實）

1. **Texel density 128–208 px/格**（13.4 建議 ~256）。原因：同時要求「UV 逐面不重疊 + LOD0 6000 tri
   + 共用單一 2048² 圖集」；已用隱藏面剔除、圖集分區重配（Wood/Metal 兩大帶）、自適應密度、
   必要時 90° 旋轉把密度最大化。貼圖 patch-safe，遠看可讀；這是已量測的取捨，不是沒做。
2. **圖集材質帶分配偏離 13.4 建議值**（Wood 0–0.4639、Metal 0.4639–0.8447、Glass 0.8447–0.9014、
   Fabric 0.9014–1.0）。13.4 該表註明「圖集區域建議」可調；依實測 LOD0 表面積（wood ≈48 格²、
   metal ≈44 格²）平衡，材質槽順序不變。
3. **LOD1/2 作物槽保留 6 節點**（幾何簡化），未採 13.5「LOD1 併 2 塊 / LOD2 併 1 條」的節點合併；
   為避免 Unity/遊戲端槽位索引隨 LOD 改變，成本 144/84 tri。
4. **FBX 版本**：Blender 4.2.1 匯出器固定 FBX 7.4（7400）binary，無 FBX 2020 選項；Unity 2021+ 可匯入。
5. **GLB 內嵌圖集**：glTF GLB 規格內嵌 3 張 2048² PNG（每檔約 9 MB，合計 212.5 MB）；FBX 則外掛參照
   `art/textures/`。若只做 Unity 流程用 FBX 即可。
6. **未附 `.blend`**：來源即 deterministic 的 `art/gen_models.py` + `art/atlas_layout.py`，不另存大檔；
   13.7「源工程文件」以生成器滿足（偏差已記錄）。
7. **模型不含碰撞體**：13.6 的單一 3×1×2 盒碰撞由 blueprint `CollidersSpec` 提供（ART_PLACEHOLDER
   已凍結該契約）；模型再加 collider 會重複。
8. **端口只有空物件 + 內凹/齊平幾何**：外露管線/輸送帶接頭需 Unity prefab 端依空物件延伸（不越界是硬約束）。
9. **施工階段是單一四檔共用灰盒**（不是每檔一套）；藍圖目前仍指向不同原版佔位模型，替換是 Lead 工作。
10. **未做**：Unity 材質/AssetBundle、`.timbermesh`、Blueprint 替換、實機外觀/50% 鏡頭實測。
11. 預覽圖為離線正交渲染（無格線 overlay），非遊戲截圖。
12. `art/__pycache__/` 為 Python import 副作用，已刪除；`art/` 內沒有其他暫存檔（暫存都在 `%TEMP%`）。

## 12. Unity 匯入與 TimbermeshTools 匯出步驟（Lead 照著做）

完整版見 `art/README.md` §11；摘要：

1. 匯入 `art/models/<Tier>.<Faction>/LOD<n>.fbx`：Scale Factor 1、Bake Axis Conversion 關（檔已 Y-up、
   正面 −Z）、Lightmap UV 關、Read/Write 關、Mesh Compression Off、Normals Import、Tangents Calculate。
   量測 bounds 應為 X=3, Y=1, Z=2、pivot 在底面中心。
2. 建 4 個材質 `M_HDBF_Wood/Metal/Glass/Fabric`（順序固定）：albedo sRGB、normal（Normal map / Linear /
   OpenGL +Y）、mask（Linear；R→Metallic、G→Roughness、A→Smoothness 或 shader 對應）；
   玻璃 alpha 0.62 + 透明（優先抖動透明）。
3. 建 LODGroup：0–18 / 18–45 / 45–90、Cull 90；LOD2 關 Cast Shadows。
4. 匯入後確認 `PORT_HDBF_*` 空物件名稱與 local +Z；若 TimbermeshTools 吃掉空物件，依 §7.1 座標重建。
5. 6 個 trough 節點用 §7.2 名稱/座標/屬性索引；作物由程式隱藏。
6. 施工階段四檔共用 `ConstructionStage0.fbx`。
7. 用遊戲附的 TimbermeshTools 匯出 `.timbermesh`（模型路徑例
   `Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model`），pivot/scale/rot 不動。
8. 替換 8 個 blueprint 的 `#Finished` 與 `ConstructionStage0` 模型路徑；`CollidersSpec` 保持單一 3×1×2；
   `TransformSlotInitializerSpec` 依 `data/ART_PLACEHOLDER.md` 處理（真實模型目前沒有 Seat/Entrance
   動畫插槽，若沿用舊 keyword 需在 prefab 補對應 transform，否則放置時會 throw）。
9. 空場景四檔重合自檢（入口同向、六槽同心、占地重合、最高點 1.0、不互穿）。

## 13. 仍未做 / 下一步（非本輪範圍）

* Unity 材質與 AssetBundle、`.timbermesh` 匯出（需遊戲工具鏈；ADR-003）。
* 8 個 Blueprint 的模型路徑替換與 `TransformSlotInitializerSpec` 對齊（Lead）。
* 實機外觀、50% 鏡頭、LOD 切換、陰影/透明排序的遊戲內驗證（目前只有離線渲染與幾何驗證）。
* 若 Lead 需要 per-tier 不同施工階段外觀，再加第二個灰盒變體。
* 效能實測（同屏多棟 LOD0 的 GPU/overdraw）需在遊戲內量測。

## 14. 重跑與證據

* 生成命令：見 §9（textures → models → validate；全部 Blender 4.2.1 `--factory-startup`、headless）。
* 生成器 deterministic：固定 hash 噪聲、無時間戳；圖集已 double-run byte-identical；模型幾何/UV 為純函數，
  atlas_layout sha256 已凍結。
* 驗證證據：`art/validation-report.txt`（C1–C10、每模型 tris/bounds/ports/UV、C9 矩陣、`OVERALL VERDICT: PASS`）。
* 預覽證據：`art/previews/`（22 張 512² PNG，含 `*_scale50.png` 與 `*_iso.png`）。
* 工作區限制遵守：未寫 `src/`、`data/`、`tests/`、`docs/VERIFICATION.md`、`docs/REQ_TRACEABILITY.md`、
  `tools/validate`；未部署、未啟動遊戲、未跑 `build.ps1`、未碰 Unity；暫存檔都在 `%TEMP%`。
