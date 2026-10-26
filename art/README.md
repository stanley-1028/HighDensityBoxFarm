# HDBF 盒子農場 — 3D 美術來源資產（`art/`）

> **v2 更新（2026-10-26）：本文件是 ART-01 的歷史說明；現行模型是 v2 3×3×1 室內農場。**
> 現行生成器 = `art/gen_models_v2.py`；驗證器 = `art/validate_models.py` / `art/validate_timbermesh.py`；
> 匯出器 = `art/export_timbermesh.py`；凍結契約 = `data/ART_PLACEHOLDER.md`（v2 版）。
> 本文件下列 3×2×1 尺寸、6 槽/6 端口表與 ART-01 驗證規則**均已過時**，僅作歷史記錄保留。

> **ART-04 更新（2026-10-25）— 本文件多數內容是 ART-01 的歷史說明。**
> 現在 `art/models/**` 是由 **`art/gen_models.py`（ART-04）** 重新生成的：屋頂只剩**單一空平台**
> （6 個作物槽/土壤/槽緣與 `#Slot#Crop_*` 節點已移除）、模型沒有作物視覺、材質改成
> **每個材質一個固定 flat UV patch**（`art/material_patches.py` + `art/material_slots.json`）。
> 因此本文件 §4「四檔造型剪影階梯」、§5「標準化作物槽（6 槽）」、§6「6 個空物件」等段落
> **已不代表現況**；現行匯出/材質/驗證契約請看
> [`README-timbermesh.md`](README-timbermesh.md) 與
> [`../docs/workstreams/art-04-flat-roof-own-material.md`](../docs/workstreams/art-04-flat-roof-own-material.md)。
> `art/validate_models.py`（本文件 §10）仍是 ART-01 契約（含槽體檢查），**未針對 ART-04 更新**。

狀態：**模型來源資產 + 自動驗證完成（離線）**。尚未做 Unity 材質、`.timbermesh` 匯出、Blueprint
路徑替換與實機外觀檢查 —— 那些是 Lead 的 Unity 工作流（見 §11），本目錄不碰 Unity、不部署、不改
`data/` / `src/`。

需求來源：`高密度盒子農場模組需求文件.md` 第 13 章（3D 建模任務提示詞）、第 5.4 節（檔位視覺識別）、
第 2.3 節（50% 縮放輪廓可辨識）。美術契約凍結於 `art/atlas_layout.py`
（sha256 `bf0447bb1bcf92c74d89ed7764e3b369b22e35c60b5d4a5c42acf40d7759472b`）。

---

## 1. 檔案清單

```
art/
├── atlas_layout.py          共用契約：圖集分區、材質槽、端口座標、槽位、LOD 預算（唯一真相來源）
├── gen_models.py            程序化模型生成器（Blender 4.2.1 headless；deterministic）
├── gen_textures.py          2048² PBR 圖集生成器（numpy；deterministic）
├── validate_models.py       獨立驗證器（Blender headless 全檢查；純 Python 可降級跑 GLB）
├── render_previews.py       正交三視圖 / 50% 縮放預覽渲染（Cycles CPU）
├── material_slots.json      材質分配表（4 槽順序、圖集分區、每模型實際材質、三角數）
├── models/
│   ├── Basic.Folktails/        LOD0.fbx LOD0.glb LOD1.fbx LOD1.glb LOD2.fbx LOD2.glb
│   ├── Basic.IronTeeth/        （同上）
│   ├── Intermediate.Folktails/ （同上）
│   ├── Intermediate.IronTeeth/ （同上）
│   ├── Advanced.Folktails/     （同上）
│   ├── Advanced.IronTeeth/     （同上）
│   ├── Top.Folktails/          （同上）
│   ├── Top.IronTeeth/          （同上）
│   └── ConstructionStage0/     ConstructionStage0.fbx / .glb（四檔共用灰盒）
├── textures/
│   ├── atlas_albedo.png      2048×2048 8-bit sRGB
│   ├── atlas_normal.png      2048×2048 8-bit LINEAR，OpenGL +Y
│   └── atlas_mask.png        2048×2048 8-bit LINEAR，RGBA（R=metallic, G=roughness, A=smoothness, B=metallic 重複）
├── previews/                 正交 front/side/top/scale50 + iso 等角預覽（四檔各 5 張 + 灰盒 2 張 = 22 張）
├── validation-report.txt     驗證報告（全 PASS 才 exit 0）
└── README.md                 本檔
```

共 **8 組（4 檔 × 2 陣營）× 3 LOD = 24 個模型** + 1 個共用施工階段灰盒；每個模型同時輸出 FBX 與 GLB。

## 2. 重跑方式

```powershell
# 1) 圖集（先跑，模型材質會參照這三張）
$env:BLENDER_USER_CONFIG = "$env:TEMP\blender-config"
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_textures.py

# 2) 模型 + material_slots.json
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\gen_models.py

# 3) 驗證（全 PASS 才 exit 0；報告寫 art/validation-report.txt）
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\validate_models.py

# 4)（可選）三視圖 / 50% 預覽
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\render_previews.py
```

兩個生成器都是 deterministic（固定 seed / hash 噪聲、無時間戳）；圖集已驗證重跑 byte-identical。
`gen_models.py` 的幾何/UV 亦為純函數（唯一輸入是 `atlas_layout.py`、檔位/陣營/LOD 與內建參數）。

## 3. 座標、尺寸、軸心（硬約束）

- 1 格 = 1 Unity 單位 = 1 m；四檔全部嚴格 **3.00 × 1.00 × 2.00**（X × 高 × 深），pivot 在佔地底面中心
  （X=0, Z=0, Y=0），asset space 為 Unity 慣例：**Y up、正面 −Z**。
- Blender 作者座標為 Z-up、正面 +Y；匯出時 Blender 標準軸轉換得到
  `asset = (blender.x, blender.z, -blender.y)`，所以 `atlas_layout.PORT_SPECS` 的 asset 座標與遊戲
  13.6 表逐位對應。
- 包圍盒精確來自：基礎板底面 y=0 且舖滿 3×2（X ∈ [−1.5, 1.5]、Z ∈ [−1, 1]），通風帽頂面 y=1.0。
  所有端口幾何皆為**內凹/齊平**（不凸出包圍盒），因此四檔、三 LOD 的量測值都是 3.000×1.000×2.000
  （驗證報告逐項列出）。LOD 包圍盒收縮 0%（≤2% 要求）。
- 所有 mesh 物件 transform 已套用（location/rotation 歸零、scale=1），幾何直接落在 asset 座標。
- 施工階段灰盒 `ConstructionStage0` 同尺寸、同 axis、同 pivot，四檔共用。

## 4. 四檔造型（剪影階梯）

| 檔位 | 主體 | 覆層 / 開敞度 | 上方量體（剪影） | 主材（材質槽） |
|---|---|---|---|---|
| Basic | 原木柱樑 + 木板牆、正面全開敞 | 無覆層；正面敞開、半高木牆 + 上部透空框架 | 前方單坡帆布/茅草雨棚（低）；後方低矮服務欄；兩支通風管升到 y=1.0 | Wood + Fabric |
| Intermediate | 木板框架 + 斜撐 | 半開放：側面帆布捲簾（預設捲起）、混合玻璃 | 後段**對稱雙坡**（脊 0.95） | Wood + Glass + Fabric |
| Advanced | 金屬方管框架 + 木構混合 | 玻璃頂 + 帆布側簾、外露法蘭 | 後段**不對稱雙坡**（脊 0.995、偏後 0.58）+ 後右側低翼（頂 0.94）+ 翼上第二支排風管（頂 1.0） | Wood + Metal + Glass + Fabric |
| Top | 重型金屬框架 + 三段式工業量體 | 玻璃帶 + 金屬百葉/鉚接板 | 後段**工業機房**（全寬、頂 1.0）+ 前方檢修龍門架 + 輸送帶罩 | Wood + Metal + Glass + Fabric |

- Basic 的「正面完全開敞」、Intermediate 的半開放、Advanced 的雙坡+翼、Top 的三段式量體，
  在 50% 縮放下以輪廓（skyline）與材質對比可辨識（見 §10 C9；容積同為 3×2×1 時，面積 IoU 必然很高，
  故驗證以輪廓/外緣曲線為判據）。
- 陣營差異只做在裝飾與金屬/帆布比重（IronTeeth 較多方形鉚接板、方通風帽；Folktails 較多倒角、
  帆布綁帶、木桶/木箱），**尺寸、槽位、端口座標兩陣營完全相同**，共用同一張圖集。
- 裝飾三角面 ≤ 各 LOD 預算 25%（13.5）；多餘預算用 44-tri 倒角鉚釘/補強件填到目標，不靠堆裝飾。

## 5. 標準化作物槽（6 槽 = 3 列 × 2 排）

屋頂平面（deck 頂 y=0.80）固定 6 個同尺寸槽；四檔座標、尺寸、順序一致，可共用圖集與 Unity 端槽位腳本。

| 節點名 | row | 槽中心 asset (X, Z) | 狀態變體 |
|---|---|---|---|
| `SM_HDBF_BoxFarm_<Tier>_Trough_01_LOD<n>` | 前 | (−1.00, −0.25) | empty |
| `..._Trough_02_...` | 前 | ( 0.00, −0.25) | full（含幼苗幾何） |
| `..._Trough_03_...` | 前 | ( 1.00, −0.25) | empty |
| `..._Trough_04_...` | 後 | (−1.00, 0.20) | full |
| `..._Trough_05_...` | 後 | ( 0.00, 0.20) | empty |
| `..._Trough_06_...` | 後 | ( 1.00, 0.20) | full |

- 槽外形 0.55 × 0.35（X × Z），底 0.80、緣 0.94、土面 0.925（單位：格）。
- 「空槽 / 滿槽」用 UV 分區實現（`soil_empty` / `soil_full` 圖集區），四檔共用；滿槽有 4 株低模幼苗
  （真正遊戲作物仍由程式隱藏，見 BUILD-05，不在模型裡綁定作物）。
- 槽位物件各自獨立（4 檔、3 LOD 都存在 6 個節點），LOD1/LOD2 幾何簡化但仍保留 6 節點 → 讓 Unity
  端槽位索引不隨 LOD 改變（刻意偏離 13.5「LOD1 併 2 塊、LOD2 併 1 條」的節點合併建議，理由見 §12）。
- 屋頂不可通行：模型無梯子/欄杆/平台；碰撞由 blueprint 的單一 3×1×2 BoxCollider 負責。

## 6. 連接端口（6 個空物件；實際管線幾何由 Unity/遊戲端接）

| 空物件 | asset 座標 (X, Y, Z) | 朝外方向 | 端口尺寸 | 幾何出現檔位 | Blender 座標 / 旋轉 |
|---|---|---|---|---|---|
| `PORT_HDBF_Entrance` | (0.00, 0.00, −1.00) | −Z | 淨寬 1.10 × 淨高 0.60（≥0.8×0.6） | 全部（Basic 開敞門洞；其餘有門片/捲簾） | (0, 1, 0) / X −90° |
| `PORT_HDBF_Power` | (1.00, 0.85, 1.00) | +Z | Ø0.12（接線盒 + 絕緣子） | 僅 Top 外露；其餘蓋板 | (1, −1, 0.85) / X +90° |
| `PORT_HDBF_Pipe` | (−1.50, 0.40, 0.00) | −X | Ø0.16（法蘭；Basic 僅 0.02 深蓋板） | 全部 | (−1.5, 0, 0.4) / Y −90° |
| `PORT_HDBF_Logistics` | (−1.00, 0.35, 1.00) | +Z | 0.80 × 0.40（輸送帶開口） | Intermediate 起；Basic 空白蓋板 | (−1, −1, 0.35) / X +90° |
| `PORT_HDBF_Vent_01` | (0.50, 1.00, −0.30) | +Y | 0.30 × 0.30 | 全部 | (0.5, 0.3, 1.0) / 無旋轉 |
| `PORT_HDBF_Vent_02` | (−0.50, 1.00, 0.30) | +Y | 0.30 × 0.30 | 全部 | (−0.5, −0.3, 1.0) / 無旋轉 |

- 空物件 local **+Z 指向建築外側**（在 Blender 作者座標驗證；FBX/GLB 匯入後保持同一朝向語意）。
  驗證器會把「世界旋轉 × local +Z」與上表 out direction 比對（容差 0.02/軸）。
- 端口幾何全部內凹或齊平於包圍盒（法蘭最多凸到 footprint 邊界內），不會破壞 3×2×1 包圍盒；
  真正的外部管線/輸送帶接頭請在 Unity prefab 端用這些空物件另外接。
- 四檔空物件座標完全相同（升級不跳位）；幾何遮蓋與外露規則依 13.6（Basic 只留管道蓋板、
  Intermediate 起物流口、Top 動力口）。

## 7. LOD 預算與切換

| LOD | 三角預算 | 實測（每組，兩陣營相同） | 保留要素 | 切換距離（格） | 陰影 |
|---|---:|---|---|---:|---|
| LOD0 | 6000 ±20% | 5940 / 5950 / 5942 / 5950（Basic/Int/Adv/Top） | 全細節：框架、鉚釘、6 槽、端口基座、玻璃分塊、幼苗 | 0–18 | 開 |
| LOD1 | 2400 ±20% | 2386 / 2380 / 2376 / 2384 | 輪廓、門洞、覆層分塊、端口基座、槽體簡化 | 18–45 | 開（主輪廓） |
| LOD2 | 800 ±20% | 798 / 800 / 792 / 796 | 剪影、材質分區、門凹槽、端口蓋板、淺槽 | 45–90 | 關（60 格起） |
| 剔除 | — | — | 遠景 | 90+ | — |

- 施工階段灰盒 408 tri（共用、非 LOD 資產）。
- 三角數為三角化後實際值（FBX 與 GLB 一致，驗證報告可核）；陣營差異僅 ±0–2 tri。
- 幾何簡化順序：先刪內部/背面（含隱藏面 culling，見 §9）→ 減鉚釘/小件 → 最後才合併大形；
  不用縮小模型省面（LOD 包圍盒收縮 0%，遠優於 ≤2% 要求）。
- 逐模型 UV 有效密度（texel density）實測：LOD0 128–176 px/格、LOD1 160–208、LOD2 192–208。
  （建議值 256 px/格，實測偏低的說明見 §12。）
- 端口空物件在三個 LOD 都存在且座標相同（避免 LOD 切換造成管線錯位）。

## 8. 材質與 PBR 圖集

四檔、兩陣營、灰盒共用同一張 **2048×2048** 圖集（3 張 PNG：albedo / normal / mask）。
材質槽順序固定，模型只引用自身需要的子集（保持順序）：

| slot | 材質 | 使用檔位 | 圖集分區（材質帶 U） | 參數 |
|---:|---|---|---|---|
| 0 | `M_HDBF_Wood` | 全部 | Wood 帶 0.0000–0.4639（wood / soil_full / soil_empty） | metallic 0、roughness ~0.72 |
| 1 | `M_HDBF_Metal` | Advanced、Top | Metal 帶 0.4639–0.8447 | metallic ~0.92、roughness ~0.48 |
| 2 | `M_HDBF_Glass` | Intermediate、Advanced、Top | Glass 帶 0.8447–0.9014 | metallic 0、roughness ~0.08、alpha 0.62（BLEND） |
| 3 | `M_HDBF_Fabric` | 全部（Basic 雨棚/茅草） | Fabric 帶 0.9014–1.0000（canvas / thatch） | metallic 0、roughness ~0.86 |

圖集分區（top-left origin，px；UV 由 `atlas_layout.region_uv()` 換算）：

| region | 材質 | px rect | UV rect (u0,v0,u1,v1) |
|---|---|---|---|
| wood | Wood | 0, 0, 842, 2048 | 0.0000, 0.0000, 0.4111, 1.0000 |
| soil_full | Wood | 846, 0, 942, 1024 | 0.4131, 0.5000, 0.4600, 1.0000 |
| soil_empty | Wood | 846, 1028, 942, 2048 | 0.4131, 0.0000, 0.4600, 0.4980 |
| metal | Metal | 950, 0, 1729, 2048 | 0.4639, 0.0000, 0.8442, 1.0000 |
| glass | Glass | 1730, 0, 1845, 2048 | 0.8447, 0.0000, 0.9009, 1.0000 |
| canvas | Fabric | 1846, 0, 2048, 1250 | 0.9014, 0.3896, 1.0000, 1.0000 |
| thatch | Fabric | 1846, 1254, 2048, 2048 | 0.9014, 0.0000, 1.0000, 0.3877 |

- 通道：albedo = sRGB 8-bit；normal = LINEAR 8-bit、**OpenGL (+Y up)**（Unity 慣例）；
  mask = LINEAR 8-bit RGBA：**R = metallic、G = roughness、A = smoothness、B = metallic（重複，
  供 glTF metallicRoughness 打包）**。不依賴 MaskMap 或特定渲染管線。
- 圖集為程序化繪製：木紋/木板/鉚釘/金屬板/玻璃污漬/帆布織紋/茅草，全部 patch-safe（任意 30–200 px
  子區塊都可讀，含 90° 旋轉）；4 px gutter；無文字/字母/數字/LOGO/浮水印/簽名（REQ-ART-08）。
- 圖集重跑 deterministic（兩次 byte-identical），SHA-256 見交付回報／`art/validation-report.txt` 環境段。
- 玻璃以材質 alpha（0.62）+ BLEND 表達，不做大面積半透明堆疊；玻璃面積遠低於單檔面數 15%。

## 9. UV 規則

- 只用 UV0；每個模型的所有 UV 落在 [0, 1]，**逐面唯一、互不重疊**（含 body 與 6 個 trough 的整體）。
- 實作：每個面依材質/部位指定語意圖集區，依面在 3D 的投影尺寸以目標 texel density 配置唯一矩形，
  4 px gutter、shelf+shelf-gap-fill 打包，必要時把長條面旋轉 90° 以塞進窄分區；密度在
  256→64 px/格之間自動取「全部模型能塞進圖集」的最大值（實測見 §7）。
- 隱藏面剔除：基礎板底面、deck 底面、牆的室內面、槽底、貼牆背面等不可見面直接不建，
  省下圖集空間（不影響三角預算：省下的面用倒角鉚釘/結構件補回目標三角數）。
- 因為是逐面唯一 UV，木紋方向在極細長面可能旋轉 90°；貼圖為 patch-safe 噪聲，遠看無感。
- `validate_models.py` C7 會用 512² UV coverage grid 檢查越界與重疊（容許 0.2% 邊緣取樣誤差）。

## 10. 驗證（`art/validate_models.py`）

```powershell
& 'C:\Users\user\.hdbf\sdk\blender-4.2.1-windows-x64\blender.exe' -b --factory-startup -noaudio --python art\validate_models.py
# → art/validation-report.txt；全 PASS 才 exit 0
# 無 Blender 時：python art\validate_models.py（GLB-only 降級模式，FBX 標記 REDUCED，不假裝通過）
```

檢查項：
C1 檔案齊全（24+1 模型 × FBX/GLB、3 張圖集、material_slots.json）
C2 asset-space 包圍盒 3.00×1.00×2.00 ±0.02、pivot 在底面中心（GLB 解析 + FBX 重新匯入雙檢）
C3 各 LOD 三角數 vs 預算 ±20%
C4 每 LOD 6 個 trough 節點；LOD0 槽中心 vs §5 契約（±0.02）
C5 6 個端口空物件：座標 ±0.02、local +Z 與朝外方向一致
C6 材質槽 ≤4、名稱/順序、每檔材質集合、3 張 PNG 2048² 8-bit
C7 UV 全部在 [0,1] 且模型內不重疊（coverage grid 檢查）
C8 LOD 包圍盒收縮 ≤2%
C9 四檔輪廓可辨識（50% 縮放；LOD2 為嚴格項）：三視圖輪廓/skyline 差異判據 + 面積 IoU 參考值
C10 `material_slots.json` 與模型/契約一致

最新一次驗證結果見 `art/validation-report.txt`：**`OVERALL VERDICT: PASS`（C1–C10 全 PASS、exit code 0，
2026-10-25 本機實跑，FULL 模式 GLB 解析 + FBX 重新匯入，97 s）**。C9 判據修訂理由：13.3 把四檔容積固定為
3.00×1.00×2.00，面積 IoU 必然接近 1，因此以「50% 縮放下輪廓 skyline 差異」作判據
（mean|Δh| ≥0.04 格 或 max|Δh| ≥0.12 格 或 IoU ≤0.85），面積 IoU 只作資訊列。C9 全部 12 pair PASS，
但 **Intermediate vs Advanced 的 LOD2 只靠 side view 過關（max Δh = 0.125 vs 門檻 0.12，餘量僅 0.005 格）**，
未來重跑生成器時必須保留這個側向 skyline 差異（Advanced 翼上第二支排風管 + 不對稱屋脊就是為此加的）。

預覽：`art/previews/` 共 22 張 512² PNG（四檔 front/side/top/scale50/iso + 灰盒 front/iso；Cycles CPU、
標準視角、亮度已調到可讀 albedo/normal；無文字/標註）。

## 11. Unity 匯入與 TimbermeshTools 匯出（Lead 照著做）

> 本工作流不在本工作區執行（Unity/`.timbermesh` 工具鏈不在交付環境，見 ADR-003）。
> 下列步驟與資料足以讓 Lead 無腦照做；座標已全部給定。

1. **匯入 FBX**（每檔每 LOD 一個）：`art/models/<Tier>.<Faction>/LOD<n>.fbx`。
   Unity 匯入設定：Scale Factor = 1、Convert Units 開、Bake Axis Conversion 關（檔案已 Y-up、正面 −Z）、
   Generate Lightmap UVs 關、Read/Write 關、Mesh Compression = Off、Normals = Import、
   Tangents = Calculate (MikkTSpace)、Animation 全部關。匯入後量測 bounds 應為 X 3、Y 1、Z 2。
2. **材質**：用遊戲的 building shader / `TimbermeshMaterials` 管線建 4 個 `M_HDBF_*`（順序照 §8）。
   - albedo：`art/textures/atlas_albedo.png`（sRGB、不壓縮或 high quality）
   - normal：`atlas_normal.png`（Texture Type = Normal map、Linear、不要 sRGB；OpenGL/Y+）
   - mask：`atlas_mask.png`（Linear，關 sRGB；R→Metallic、G→Roughness、A→Smoothness 或 shader 對應通道）
   - 玻璃：Alpha 0.62、Transparent（若 shader 支援抖動透明/dither 優先，避免排序問題）
   - 一個模型只會引用其材質子集（Basic: Wood/Fabric；Intermediate: Wood/Glass/Fabric；
     Advanced/Top: 全 4 個）；材質槽順序即 §8 的相對順序。
3. **LOD**：每檔建 LODGroup：LOD0 0–18、LOD1 18–45、LOD2 45–90、Cull 90（1 格 = 1 m）；
   LOD2 關 Cast Shadows；LOD1 只保留主輪廓投影（見需求 13.5）。
4. **端口空物件**：匯入後應保留節點名 `PORT_HDBF_Entrance/Power/Pipe/Logistics/Vent_01/Vent_02`，
   local +Z 指向外側；若 TimbermeshTools 轉檔會吃掉空物件，就在 prefab 端依 §6 座標重建同名節點
   （邏輯座標一律以 LOD0 為準）。
5. **作物槽**：6 個 trough 節點（§5 名稱/座標）；若 Unity 端要對槽位做隱藏/顯示，用
   `hdbf_slot_index`（1–6）與 `hdbf_slot_variant`（empty/full）自訂屬性，或直接以節點名索引。
   （遊戲內作物實際由程式隱藏，此處只是土壤/幼苗幾何。）
6. **施工階段**：四檔共用 `art/models/ConstructionStage0/ConstructionStage0.fbx`；藍圖的
   `Children.#Unfinished.Children.ConstructionStage0.TimbermeshSpec.Model` 全部指向同一個
   `Buildings/Food/BoxFarm/ConstructionStage0.Model`。
7. **TimbermeshTools 匯出**：在 Unity 用遊戲附的 TimbermeshTools 將每個 prefab 匯成
   `.timbermesh`（模型路徑不含副檔名，例：`Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model`），
   放到部署模組 `data/Buildings/Food/BoxFarm/`（由 `build.ps1 -Install` 部署）。
   pivot 必須保持「佔地底面中心」、scale=1、rot=0；不要加任何 prefab 位移。
8. **Blueprint 路徑替換**（Lead 範圍）：把 8 個 `BoxFarm.<Tier>.<Faction>.blueprint.json` 的
   `Children.#Finished.TimbermeshSpec.Model` 與 `ConstructionStage0` 路徑換成上一步的模型；
   `CollidersSpec` 保持單一 3×1×2 盒（模型不含碰撞體）；`TransformSlotInitializerSpec` 依
   `data/ART_PLACEHOLDER.md` 的槽位表處理（真實模型目前沒有 Seat/Entrance 動畫插槽 —— 若沿用
   舊 keyword 需在 Unity prefab 補對應 transform，否則放置時會 throw，見 ART_PLACEHOLDER §Transform slot）。
9. **四檔重合自檢**：在空場景把四檔擺在一起（1 格 = 1 單位），確認入口同向（−Z）、六槽同心、
   占地完全重合、最高點 1.0、邊界不互穿。

## 12. 已知偏差與限制（誠實清單）

1. **Texel density 實測 128–208 px/格**（LOD0 128–176），低於 13.4 建議的「約 256 px/格」。
   原因：要求同時「UV 不重疊 + 每模型 ≤6000 tri + 共用 2048² 單一圖集」；逐面唯一 UV 下，
   2048² 對 3×2×1 建築的 6000-tri LOD0 只夠這個密度。已用隱藏面剔除、自適應密度、
   必要時 90° 旋轉、圖集分區重配把密度最大化；貼圖為 patch-safe 程序化材質，遠看可讀。
2. **圖集材質帶分配偏離 13.4 建議值**：Wood 0.0–0.4639、Metal 0.4639–0.8447、Glass 0.8447–0.9014、
   Fabric 0.9014–1.0（13.4 原建議 0.45/0.25/0.15/0.15）。13.4 表註明「圖集區域建議」可調；
   此配置依實測表面積（LOD0 wood ≈48 格² vs metal ≈44 格²）平衡，材質槽順序不變。
3. **作物槽在 LOD1/LOD2 仍保持 6 個節點**（幾何簡化），未採用 13.5 建議的「LOD1 併 2 塊、
   LOD2 併 1 條」節點合併 —— 為了讓 Unity/遊戲端槽位索引不隨 LOD 改變；三角成本僅 144/84 tri。
4. **FBX 版本**：Blender 4.2.1 匯出器固定輸出 FBX 7.4（7400）binary，沒有 FBX 2020 選項；
   Unity 2021+ 正常匯入。交付格式為 FBX + GLB（13.7 要求）。
5. **GLB 內嵌圖集**：glTF GLB 規格會把 3 張 2048 PNG 內嵌（每個 GLB 約 9 MB）；FBX 則外掛參照
   `art/textures/`。若只需要 Unity 工作流，用 FBX 即可，GLB 為附加交付。
6. **未附 `.blend` 源檔**：來源即 `art/gen_models.py` + `art/atlas_layout.py`（deterministic 可重跑），
   不另存數百 MB 的 .blend；13.7「源工程文件」以生成器腳本滿足（偏差已記錄）。
7. **模型不含碰撞體**：13.6 的單一 3×1×2 盒碰撞由 blueprint 的 `CollidersSpec` 提供（ART_PLACEHOLDER
   已凍結該 contract）；模型再加 collider 會與 blueprint 重複。
8. **端口只有空物件 + 內凹/齊平幾何**：外露的管線/輸送帶接頭幾何需在 Unity prefab 端依空物件座標
   延伸（模型不越界是硬約束）。
9. **施工階段是單一四檔共用灰盒**（不是每檔一套），符合本輪交付卡；藍圖目前仍指向不同原版佔位模型，
   替換是 Lead 的 Blueprint 工作。
10. **未做**：Unity 材質/AssetBundle、`.timbermesh`、Blueprint 路徑替換、實機外觀/50% 鏡頭實測
    （預覽圖為離線正交渲染，非遊戲截圖）。本目錄不聲稱「已在遊戲中看到」。
11. `art/previews/` 為正交三視圖與 50% 縮放對照（無格線 overlay；比例依據為 1 格 = 1 m 與已知
    3×2×1 包圍盒）。
12. `art/__pycache__/` 是 Python import 的副作用（不影響交付），可安全刪除。
