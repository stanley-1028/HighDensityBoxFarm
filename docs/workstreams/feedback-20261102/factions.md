# Feedback 2026-11-02｜Ember-pelts（工作坊自訂陣營）不能用：調查報告

| 項目 | 值 |
|---|---|
| 共享任務 | task-4（Feed-Bug-04） |
| 調查員 | factions |
| 版本前提 | HDBF v2.5.3（`data/manifest.json` Version=2.5.3，2026-10-29 發佈）、Timberborn **1.1.2.8** |
| 玩家回報 | saqlain141（2026-11-01）：「found it sadly it doesn't work for the Ember-pelts」 |
| 可寫範圍 | 本檔（`docs/workstreams/feedback-20261102/factions.md`）；其餘全部唯讀 |
| 方法 | 唯讀：`src/`、`data/`、`tests/`、`tools/`、`docs/`；本機工作坊內容（`C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090\`）；`Timberborn_Data/Managed` 660 個 DLL 的 Mono.Cecil IL dump（臨時工具建在 `%TEMP%`，不寫 repo）；官方藍圖 `Timberborn_Data/StreamingAssets/Modding/Blueprints.zip`（1,067 檔）。**未啟動遊戲、未跑 `build.ps1`、未修改任何程式碼/資料。** |
| 證據標記 | **【已證實】**＝本機 1.1.2.8 IL、官方藍圖資料或本機工作坊檔案可直接驗證；**【高度可能】**＝機制成立＋多條間接證據，缺最後一次實機確認；**【推測】**＝需安裝 Emberpelts 主模組才能確定 |
| 關鍵前提 | **本機沒有 Emberpelts 主模組（Steam id 3346318229 不在本機工作坊內容）。** 本機只有它的前置 `3416879061`（Bobingabout Script Pack）。因此「Emberpelts 的 FactionSpec 實際寫了什麼」屬需實機驗證的項目；本檔只以本機可證的遊戲機制＋生態系既有做法推導。 |

---

## 0. 結論摘要（先看這段）

1. **這不是「支援了但壞掉」，主因是「未支援」的功能缺口：HDBF 的 8 份建築藍圖只註冊在 `Buildings.Folktails` / `Buildings.IronTeeth` 兩個 template collection，而自訂陣營只會載入「`Common` ＋它自己 FactionSpec 宣告的 collection」。** 依 1.1.2.8 IL：建造選單 = `PlaceableBlockObjectSpecService → TemplateService.GetAll<PlaceableBlockObjectSpec> → TemplateCollectionService.AllTemplates`，而 `AllTemplates` 只由 `ITemplateCollectionIdProvider` 決定：`CommonTemplateCollectionIdProvider`（固定回傳 `"Common"`）＋`FactionTemplateCollectionIdProvider`（回傳 `FactionService.Current.TemplateCollectionIds`）。**【已證實：遊戲機制】→【高度可能：這就是玩家症狀】** 玩家在 Emberpelts 開局看建造選單，HDBF 四檔建築根本不存在，因此「不能用」。
2. **即使玩家另外裝了 `ModdedFactionsBuildingSupport`，HDBF 現在也不會出現**：該支援 mod 在生態系裡的慣例是讓自訂陣營加入 collection id `Buildings.Support.ModdedFactions`（本機 Conveyor Belts v2、Timber Pipes 的檔案可證），而 HDBF 的 `data/` 完全沒有這個 collection id（`data/TemplateCollections/` 只有 Folktails/IronTeeth 兩份）。**【已證實：HDBF 資料側】**
3. **如果建築被其他機制塞進 Emberpelts（clone 陣營 collection、手動改檔、或其他 faction 支援 mod），會踩到「材質」這個更嚴重的坑**：HDBF 的 8 個模型是 **faction-scoped 原版材質**（`BoxFarm.Basic.Folktails.Model` 內含 `BaseWood_Brown.Folktails`、`Moss.Folktails`；IronTeeth 版內含 `BaseWood_DarkBrown.IronTeeth`、`RoofPlanks.IronTeeth`），而 1.1.2.8 的 `MaterialRepository.GetMaterial` 對找不到的材質**直接丟 `ArgumentException: Material <name> not found in repository.`**；既有實機證據（`docs/VERIFICATION.md` §9、`docs/API_FACTS.md` §8）顯示這個例外發生在**建造選單建立 tool preview 時，會讓「整個建造選單」載入失敗**，不只 HDBF。也就是說：把 Folktails 藍圖直接 append 進自訂陣營 collection，只有在該陣營的 `MaterialCollectionIds` 包含 `"Folktails"`（或提供同名材質）時才安全。**【已證實：材質機制與失敗樣態；條件本身=高度可能/需 Emberpelts 實機】**
4. **若建築真的出現，HDBF 的陣營語意仍會出錯（bug 群）**：`GameCropCatalog.ResolveFaction` 只以名稱含 `IronTeeth` 判斷（`src/HDBF.Gameplay/Components/GameCropCatalog.cs:120-129`），所以 Emberpelts 一律被當成 **Folktails**；控制器在 `Awake` 就把 `_faction` 定死（`HdbfBoxFarmController.cs:163`），存檔載入時再用藍圖解析值**強制覆寫** record 的 Faction（`IndoorEntitySaveBridge.cs:180-185`、`IndoorEntitySaveBridge.Keyed.cs:239-243`）；面板副標、日誌、固定作物下拉、作物警告都因此顯示/驗證錯誤的陣營與作物（詳見 §3、§5）。
5. **好消息**：建造材料與水其實**不受陣營限制**。1.1.2.8 永遠會載入 `GoodCollection.Common`（`CommonGoodCollectionIdsProvider` cctor 固定 `"Common"`，IL 已證實），其內容包含 `Log`、`Plank`、`MetalBlock`、`Water`（官方 `Blueprints.zip:GoodCollections/GoodCollection.Common.blueprint.json`），所以 HDBF 四檔的建造成本與水貨物在任何陣營都拿得到；`WorkerTypeSpec` 沒有 faction 欄位（IL），`Beaver` 是全域 spec，就業預設值 `DefaultWorkerType: "Beaver"` 且 `DisallowOtherWorkerTypes: false`，**就業理論上不會壞（高度可能，需實機）**。
6. **最可能的實際失敗鏈（依序）**：
   - H1（最可能）：**建造選單沒有 HDBF 建築**（template collection 未註冊）→ 玩家只能說「doesn't work」。
   - H2：若建築出現（Emberpelts clone `Buildings.Folktails` 或玩家用支援 mod/手改檔）：
     a. **材質 crash**（若 Emberpelts 的 `MaterialCollectionIds` 不含 `Folktails`）→ 整個建造選單失敗（P0）；
     b. 陣營解析成 Folktails → 面板標「葉尾/Folktails」、日誌 `faction=Folktails`；
     c. 作物表錯：`BuildTable` 先用 **Folktails 內建 fallback 表**（Carrot/Potato/Wheat/Sunflower）打底再把 live 作物附在後面（`GameCropCatalog.SpecReader.cs:91-121`）→ 輪作會先種 Emberpelts 可能不存在的 vanilla 作物；
     d. 固定作物下拉/警告用 `FallbackCropCatalog` 而非 live catalog（`HdbfIndoorPanelWarnings.cs:164,180`）→ 顯示錯作物清單；
     e. 水/就業：水貨物在 Common（已證實），但 Emberpelts 是「恨水」陣營、可能沒有水泵/管路可接，HDBF 的人工補水探測可能找不到水源（**推測，需實機**）。
7. **修復分層**：最小可行＝新增「中立變體」資料＋`.optional` 陣營 patch（僅 Emberpelts 可見可用）＋ 4 個小程式修正（動態陣營、live 作物表、UI 用 snapshot 作物、存檔保留未知陣營）；完整＝用遊戲現成的 `ITemplateCollectionIdProvider` 多綁定（IL 已證實是官方擴充點）讓 HDBF 對「任何非官方陣營」自動注入專用 collection，搭配中立 Common 材質模型與資料驅動陣營。詳見 §6。
8. **本輪無法證明的事**：Emberpelts 主模組的 `FactionSpec.TemplateCollectionIds / MaterialCollectionIds / GoodCollectionIds` 實際值、它是否 clone `Buildings.Folktails`、其作物 `PlantableSpec.ResourceGroup` 是否為 `"Farmhouse"`、其水源建築是否存在。這些全部列在 §9，需 Lead 安裝 Emberpelts 後實機或至少讀它的 `data/Factions/Faction.Emberpelts.blueprint.json` 驗證。

---

## 1. 證據等級與邊界（誠實聲明）

| 標記 | 意思 |
|---|---|
| **【已證實】** | 本機 1.1.2.8 IL／官方 `Blueprints.zip`／本機工作坊檔案／HDBF 程式碼可直接驗證；本檔附出處 |
| **【高度可能】** | 遊戲機制已證實、生態系有先例，但缺 Emberpelts 主模組或實機日誌 |
| **【推測】** | 只能靠 Emberpelts 實機驗證（本機沒有主模組，無法讀它的資料） |

邊界：

* 未安裝 Emberpelts 主模組（3346318229 不在本機 `steamapps/workshop/content/1062090`；只有前置 3416879061 = Bobingabout Script Pack），所以**不臆造** Emberpelts 內部的 blueprint 內容；凡涉及它自己的資料一律標「需實機」。
* 未啟動 Timberborn；「玩家看到什麼」由遊戲 IL、官方藍圖、HDBF 程式碼＋既有實機日誌（`docs/VERIFICATION.md` §9、§26–§35）推導。
* 未跑 `build.ps1`；IL dump 用臨時 Mono.Cecil 工具（`%TEMP%`），未寫 repo。可重現指令見附錄 A。

---

## 2. 遊戲側事實（已證實；IL／官方藍圖／本機工作坊）

### 2.1 建造選單的唯一入口是 template collection（IL 鏈）

1. `Timberborn.BlockObjectTools.PlaceableBlockObjectSpecService.GetBlockObjects(groupSpec)`：
   `IL_0013 callvirt TemplateService::GetAll<Timberborn.BlockSystem.PlaceableBlockObjectSpec>()` → 依 `ToolGroupId` 篩選、排序後回傳。
2. `Timberborn.TemplateSystem.TemplateService.GetAll<T>()`：
   `IL_0007 ldfld _templateCollectionService` → `IL_000C callvirt TemplateCollectionService::get_AllTemplates()` → 對每個 blueprint `IL_0024 callvirt Blueprint::GetSpecs<T>(List<T>)`。
3. `Timberborn.TemplateCollectionSystem.TemplateCollectionService.Load()`：
   對 `_templateCollectionIdProviders` 逐個 `IL_001E callvirt ITemplateCollectionIdProvider::GetTemplateCollectionIds()` → 對每個 collection id，`IL_0044 ISpecService::GetSpecs<TemplateCollectionSpec>()` → `Where(CollectionId == id)` → `IL_0079 SelectMany(spec.Blueprints)` → `GetBlueprint(path)` → `AllTemplates`。
4. 兩個 provider 的實作（IL）：
   * `CommonTemplateCollectionIdProvider`：`.cctor` `ldstr "Common"`，`GetTemplateCollectionIds()` 是編譯器 iterator，**無論哪個陣營都回傳 `"Common"`**。
   * `GameFactionSystem.FactionTemplateCollectionIdProvider.GetTemplateCollectionIds()`：
     `IL_0001 ldfld _factionService` → `IL_0006 callvirt FactionService::get_Current()` → `IL_000B FactionSpec::get_TemplateCollectionIds()`。
   * 綁定：`GameFactionSystemConfigurator.Configure()` `IL_003D–004C` `MultiBind<ITemplateCollectionIdProvider>().To<FactionTemplateCollectionIdProvider>().AsSingleton()`；`TemplateCollectionSystemConfigurator` 綁 `CommonTemplateCollectionIdProvider`。
5. **推論【已證實】**：一個 settlement 的 `AllTemplates` ＝ CollectionId `"Common"` 的 blueprints ＋ 當前 `FactionSpec.TemplateCollectionIds` 所列各 collection 的 blueprints。**沒有其他來源**；`AllTemplates` 以外的 blueprint 仍可作為 spec 載入，但不會變成 template、不會生成建造按鈕。
6. HDBF 自己的資料只註冊到兩個官方 collection：
   * `data/TemplateCollections/TemplateCollection.Buildings.Folktails.blueprint.json:3` `"CollectionId": "Buildings.Folktails"`，`:4-9` append 4 份 `BoxFarm.<Tier>.Folktails.blueprint`。
   * `data/TemplateCollections/TemplateCollection.Buildings.IronTeeth.blueprint.json:3` 同理。
   * `data/Buildings/Food/BoxFarm/` 只有 8 份（4 tier × 2 官方陣營），每份 `TemplateSpec.TemplateName = "BoxFarm.<Tier>.<Faction>"`（如 `BoxFarm.Basic.Folktails.blueprint.json:25-26`）。

> 補充：HDBF 用「與原版同名路徑」的方式修改原版 collection（檔案路徑 `TemplateCollections/TemplateCollection.Buildings.Folktails.blueprint.json` 正是官方 `Blueprints.zip` 內的同名路徑），所以 `Blueprints#append` 生效。這個「跨 mod 同路徑修改」機制是官方文件明列的（`mechanistry/timberborn-modding` wiki: Blueprints#modifying-blueprints）；`Factions/Faction.Folktails.blueprint.json` 也被 More Mines 用同樣方式 `TemplateCollectionIds#append`（本機 `3723216060/version-1.1.0.2/Factions/Faction.Folktails.blueprint.json:6-9`）。

### 2.2 官方兩個陣營的 FactionSpec（官方 `Blueprints.zip` 原文）

`Factions/Faction.Folktails.blueprint.json`：

* `Id: "Folktails"`、`Order: 0`
* `MaterialCollectionIds: ["Folktails"]`
* `TemplateCollectionIds: ["Buildings.Folktails","Characters.Folktails","ModularShaftParts.Folktails","NaturalResources.Folktails"]`
* `GoodCollectionIds: ["Folktails"]`、`NeedCollectionIds: ["Folktails"]`
* `StartingBuildingId: "DistrictCenter.Folktails"`

`Factions/Faction.IronTeeth.blueprint.json`：同結構，`TemplateCollectionIds` 再加上 `"Planes.IronTeeth"`。

`TemplateCollections/TemplateCollection.Buildings.Common.blueprint.json`：`CollectionId: "Common"`，內容只有 `"Buildings/Paths/Path/Path.blueprint"`（**陣營中立建築的官方範例**；該 blueprint 用 `BackwardCompatibleTemplateNames: ["Path.Folktails","Path.IronTeeth"]` 承接舊模板名）。

### 2.3 `Common` 永遠被載入（三個系統都有 Common provider，IL 已證實）

| 系統 | provider | 證據 |
|---|---|---|
| Template collection | `CommonTemplateCollectionIdProvider` → `"Common"` | `.cctor ldstr "Common"`；`GetTemplateCollectionIds` iterator |
| Material collection | `CommonMaterialCollectionIdsProvider` → `"Common"` | `.cctor ldstr "Common"`；綁定於 `TimbermeshMaterialsConfigurator`（同型別模式） |
| Good collection | `CommonGoodCollectionIdsProvider` → `"Common"` | `.cctor ldstr "Common"`；`GoodCollectionSystemConfigurator.Configure` `Bind<CommonGoodCollectionIdsProvider>().ToExisting<IGoodCollectionIdsProvider>()`（IL 已抓） |

官方 `MaterialCollections/MaterialCollection.Common.blueprint.json` 內容（節錄）：`Materials/Common/Wood/BaseWoodCommon`、`Materials/Common/Wood/BaseWoodCommon_Amber`、`Materials/Common/Paper/PaperCommon`、`Materials/Common/Dirt/Dirt`、`Materials/Common/Mud`、`Materials/Goods/Water`、`Materials/Beavers/Common/BeaverAdult/Child`……→ **存在一組「所有陣營都拿得到」的中立材質名**。

官方 `GoodCollections/GoodCollection.Common.blueprint.json`：`Badwater, Berries, Dirt, Explosives, Extract, Fireworks, Gear, Log, MetalBlock, PineResin, Plank, ScrapMetal, Water` → **HDBF 建造成本（Log/Plank/MetalBlock）與水（Water）在任何陣營都存在**（Common 永遠載入）。**【已證實】**

### 2.4 材質是 faction-scoped；跨陣營材質會讓「整個建造選單」載入失敗

* `Timberborn.TimbermeshMaterials.MaterialRepository.Load()` 透過 `IMaterialCollectionIdsProvider` 收集材質並放進 `_materials`（IL）。
* `MaterialRepository.GetMaterial(name)`：`IL_0018 TryGetValue`，找不到時 `IL_0021 ldstr "Material "` + name + `" not found in repository."` → `IL_0031 newobj ArgumentException` → `IL_0036 throw`。**【已證實】**
* 既有實機證據：`docs/API_FACTS.md` §8（143-160）與 `docs/VERIFICATION.md` §9（164-199）已證實：藍圖模型指向別的陣營材質時，`PrefabOptimizationChain.ProcessPrefab` 在**建造選單建立 tool preview** 時丟 `ArgumentException: Material <name> not found in repository`，**整個建造選單載入中止**（不只那一棟建築）。
* HDBF 自己的模型也確實使用 faction-scoped 原版材質（本次以 zlib 解壓 `.timbermesh` 後抽字串，可重現，見附錄 A）：
  * `data/Buildings/Food/BoxFarm/BoxFarm.Basic.Folktails.Model.timbermesh` → `BaseWood_Brown.Folktails`、`Moss.Folktails`
  * `data/Buildings/Food/BoxFarm/BoxFarm.Basic.IronTeeth.Model.timbermesh` → `BaseWood_DarkBrown.IronTeeth`、`RoofPlanks.IronTeeth`
  * `BoxFarm.Advanced.Folktails.Model.timbermesh` → `BaseMetal.Folktails`、`Details.Folktails`、`BaseWood_White.Folktails`、`WindowsAtlas.Folktails`
  再加上 `docs/workstreams/art-04-flat-roof-own-material.md:294` 的已驗證規則：「材質名必須是**當前陣營**的原版 MaterialCollection 資產名；跨陣營名會在 prefab 建立時拋 `Material ... not found in repository`」。**【已證實】**

### 2.5 作物目錄怎麼來（HDBF 這部分其實做對了「以 live template 為準」）

`GameCropCatalog.ReadActiveSpecs()`（`GameCropCatalog.cs:178-237`）：

* 取 `_specService.GetSpecs<PlantableSpec>()`，只收 `ResourceGroup == "Farmhouse"`（`:205-209`）；
* 再用 `_templates.AllTemplates` 的 template 名集合過濾（`GameCropCatalog.SpecReader.cs:22-57`，`:219-222`）→ **只有當前 template collection 真的有的作物才會被讀到**。
* 這是正確方向：只要 template collection 正確，自訂陣營的 Farmhouse 作物就會被讀到；問題在後面的表組裝與陣營鍵（§3.2、§5）。

### 2.6 WorkerType 是全域 spec，不是陣營資料

* `WorkerTypes/WorkerType.Beaver.blueprint.json`、`WorkerType.Bot.blueprint.json` 在官方 `Blueprints.zip`；`WorkerTypeSpec` 欄位只有 `Id/BackwardCompatibleIds/DisplayName/WorkerOnlyText/IgnoresWorkingHours`（IL type dump），**沒有 FactionId**。
* `WorkerTypeService.Load()` 把「所有」`WorkerTypeSpec` 註冊進字典（IL）；`WorkplaceWorkerType.Awake()` 直接 `WorkerType = _workplaceSpec.DefaultWorkerType`（IL）。
* HDBF 藍圖 `WorkplaceSpec.DefaultWorkerType: "Beaver"`、`DisallowOtherWorkerTypes: false`（8 份皆同）→ `IsWorkerTypeAllowed` 直接回 true（IL：`DisallowOtherWorkerTypes == false → IL_001F ldc.i4.1`）。因此**就業與 WorkerType 不會因自訂陣營而壞（高度可能；除非該陣營 mod 自己改了 WorkerType 資料）**。
* 風險：`WorkplaceWorkerType.Load()` 用 `WorkerTypeService.GetWorkerType(存檔值)` → 字典索引器，若存檔的 worker type 不存在會 KeyNotFound（IL）。HDBF 一律用 `Beaver`，屬全域，正常。

### 2.7 存檔載入時陣營怎麼還原（含「移除 Emberpelts」的風險）

* `FactionService.Load()`：有存檔 singleton `Id` → `SetCurrentFaction(id)`；新局則是 `NewGameConfiguration.FactionId`（IL）。
* `SetCurrentFaction` → `FactionSpecService.GetFaction(id)` → `SingleOrDefault(...)`，找不到就 `throw new ArgumentException("Faction with id " + id + " not found.")`（IL）。**【已證實】**
* 意涵：**玩家把 Emberpelts 從一個 Emberpelts 存檔的 mod 清單移除後，該存檔會在載入時直接失敗（遊戲層級）**；這不是 HDBF 能修的部分，但寫玩家回覆時要提醒（§7）。

### 2.8 `.optional.blueprint.json` 的語意（IL；建議修法會用到）

* `BlueprintAsset..cctor`：`Extension = ".blueprint"`、`FullExtension = ".blueprint.json"`、`OptionalExtension = ".optional.blueprint"`。
* `BlueprintFileBundleLoader.GetBundles(rootPath, paths)`：把所有 asset 依 `Path.Replace(OptionalExtension, Extension)` 分組，`Where(group => group.Any(a => !a.Path.EndsWith(OptionalExtension)))` → **只含 `.optional` 的群組會被丟棄**。
* `GetBundle(path)`：同時載入 `path` 與 `path.Replace(".blueprint", ".optional.blueprint")`，分組後 `All(EndsWith optional)` → 回傳 null（不載入）。
* 結論：`.optional.blueprint.json` 是「**同一個 normalized path 的原版 blueprint 存在時才套用的修改檔**」。**新檔案單獨加 `.optional` 不會被載入**（所以不能把新 collection 檔命名成 `.optional` 來當「有裝才生效」）。這個機制正好可以用在「只在 Emberpelts 存在時，把 HDBF 的 collection id append 到它的 FactionSpec」。

### 2.9 生態系既有的「自訂陣營支援」做法（本機工作坊檔案，均已實地讀取）

1. **per-faction collection append**（本機至少 6 個獨立 mod 這樣做 Emberpelts）：
   * `3721718127/version-1.1.0.0/TemplateCollections/others/TemplateCollection.Buildings.Emberpelts.TWTradeOffice.blueprint.json`：`CollectionId: "Buildings.Emberpelts"`，`Blueprints: [".../TWTradeOffice.Folktails.blueprint"]`。**直接拿 Folktails 藍圖給 Emberpelts 用的先例**（該模型的材質字串為 `BaseWood_Brown.Folktails`、`Details.Folktails` 等，本次已解壓確認）。
   * `3431421822/version-1.0.0/TemplateCollections/TemplateCollection.Buildings.Emberpelts.blueprint.json`（append `*.Emberpelts.blueprint` 變體）
   * `3345889146/version-1.1.0/TemplateCollection/TemplateCollection.Buildings.Emberpelts.blueprint.json`（同上）
   * `3813587159/version-1.1/TemplateCollections/TemplateCollection.Buildings.Emberpelts.blueprint.json`（`Blueprints#Append`，注意大小寫）
   * `3765935439/version-1.1/TemplateCollections/TemplateCollection.ChooChoo.Emberpelts.blueprint.json`（由 `tools/gen_faction_skins.py` 產生的「per-faction skin」collection；證明大 mod 會為每個自訂陣營生成變體）
   * `3282276626/version-1.1.0/manifest.json`：`OptionalMods: [{Id: "Emberpelts"}, {Id: "BobPlatforms"}]`，同時附 `TemplateCollections/TemplateCollection.Buildings.Emberpelts.BobPlatforms.optional.blueprint.json`（**OptionalMods 物件陣列＋per-faction 檔案的正式先例**）
   * **共通結論【已證實】**：生態系的標準做法是「**自訂陣營有自己的 collection id（Emberpelts = `Buildings.Emberpelts`），mod 要另外 append**」。這反過來支持 H1：HDBF 沒 append，就不會出現在 Emberpelts。
2. **`ModdedFactionsBuildingSupport` 慣例**：
   * `3815376360/version-1.1/manifest.json:10-14`：`OptionalMods: [{Id: "ModdedFactionsBuildingSupport"}]`。
   * `3815376360/version-1.1/Buildings/Conveyors/Collections/Conveyors.ModdedFactions.blueprint.json:3`：`CollectionId: "Buildings.Support.ModdedFactions"`（Timber Pipes 同名慣例：`3801726769/version-1.1/Buildings/Water/Pipes/Collections/Pipes.ModdedFactions.blueprint.json:3`）。
   * 該支援 mod **本機未安裝**，其實作未取證；由 id 與用法推斷：它會讓「非官方陣營」也載入 `Buildings.Support.ModdedFactions` 這個 collection。**【推測；但 HDBF 沒註冊此 collection 是已證實的】**。
3. **自訂陣營 FactionSpec 實例**（More Mines 的 `TWDirtclaws`，本機 `3723216060/version-1.1.0.2/Factions/Faction.TWDirtClaws.blueprint.json`）：
   * 自己宣告 `TemplateCollectionIds: ["TWDirtclaws.Template.Default","Buildings.TWDirtclaws.Base","Buildings.TWDirtclaws.TW","NaturalResources.TWDirtclaws","TWMoreMines.MoreTunnels.Dirtclaws"]`；
   * `MaterialCollectionIds: ["Folktails","IronTeeth"]`（**為了重用 vanilla 模型，自訂陣營可以同時掛兩個官方材質 collection**）；
   * 用 mod 自己的 `TWFactionCollectionCloneSpec`（clone `Buildings.Folktails` 並 blacklist/filter）達到「重用 vanilla 建築」。
   * 意涵：**自訂陣營不是自動繼承官方 collection**；重用要靠 faction mod 自己的 clone 或每一棟逐一 append。**【已證實（該陣營的做法）】** 但 Emberpelts 是否也 clone 了 `Buildings.Folktails` 屬**需實機**（若 clone，HDBF 反而「會出現」→ 走 H2）。

### 2.10 Emberpelts 事實（外部＋本機可證）

* 主模組：Steam Workshop `3346318229`，作者 Bobingabout，**目前 28,790 訂閱**；必要前置＝`3416879061` Bobingabout Script Pack（本機有：`version-1.1/manifest.json` Id `BobingaboutScriptPack`、Version 1.1.2.4、MinimumGameVersion 1.1.0.0）。**【已證實：本機前置 + 外部頁面】**
* 作者描述（Steam 頁面，外部資料，2026-11-02 讀取）：「adds a new faction, The Emberpelts… 維持 vanilla 手感，多數 Folktails/IronTeeth 的東西都會有，只是換成紅色系/新外觀；**Most of their crops are new, but a few are reused such as Wheat, Corn and Canola**；新資源 Clay→Bricks 作為建材；Emberpelts 恨水；用果汁取代水；解鎖條件 Folktails Wellbeing 1；仍是 WIP」。→ 對本調查最重要的一點：**Emberpelts 的作物集合與官方兩個陣營不同（多數是新作物）**，所以 HDBF 內建 fallback 作物表一定不對。
* 本機能證的 collection id：`Buildings.Emberpelts`（6 個 mod 的檔案，§2.9）。**Emberpelts 自己的 FactionSpec 檔案內容（Template/Material/Good collection ids）本機沒有，需實機或訂閱後讀 `data/Factions/Faction.Emberpelts.blueprint.json`。**

---

## 3. HDBF 現況：陣營相關硬編碼與語意（程式碼側，全部【已證實】）

### 3.1 核心與資料

| # | 位置 | 現況 | 對自訂陣營的後果 |
|---|---|---|---|
| 1 | `src/HDBF.Core/Indoor/HdbfFaction.cs:13-20` | `enum HdbfFaction { Folktails = 0, IronTeeth = 1 }`（存檔 schema 值，不能重編號） | 無法表示任何自訂陣營；只能塞進兩個桶之一 |
| 2 | `src/HDBF.Gameplay/Components/GameCropCatalog.cs:120-129` | `ResolveFaction(string)`：名稱含 `IronTeeth` → IronTeeth，**其餘一律 Folktails** | Emberpelts → Folktails |
| 3 | `GameCropCatalog.cs:165-176` | `ResolveActiveFaction()` 用 `FactionService.Current.Id` 再走 `ResolveFaction`；例外時 Folktails | Emberpelts 的 active faction 被當 Folktails |
| 4 | `GameCropCatalog.cs:153-163` + `GameCropCatalog.SpecReader.cs:91-121` | `Rebuild`：只有「active faction == Folktails」時才用 live specs 建 `_folktails`；`BuildTable` **先用 `FallbackCropCatalog` 的官方表打底**（`:94-110`），再把 live 作物**附在後面**（`:112-118`） | Emberpelts 的新作物會被塞進「Folktails 表」尾端；輪作會先種官方 fallback 作物（Emberpelts 可能沒有） |
| 5 | `GameCropCatalog.cs:65-74` | `ForFaction` 對未知 enum 回空表 | 安全降級（不崩），但也代表「未知陣營」沒有自己的表 |
| 6 | `GameCropCatalog.SpecReader.cs:22-57`、`GameCropCatalog.cs:178-237` | 作物以 `PlantableSpec.ResourceGroup == "Farmhouse"` 且 template 在 `_templates.AllTemplates` 內為準 | 方向正確；只要 collection 正確，Emberpelts 作物可被讀到（仍需確認其 ResourceGroup） |
| 7 | `data/Buildings/Food/BoxFarm/*.blueprint.json` ×8 | `TemplateSpec.TemplateName = "BoxFarm.<Tier>.<Faction>"`（`:25-26`）；模型為 faction-scoped 材質（§2.4） | 沒有中立變體可用於自訂陣營 |
| 8 | `data/TemplateCollections/TemplateCollection.Buildings.{Folktails,IronTeeth}.blueprint.json:3-9` | 只把 8 份藍圖註冊進兩個官方 collection；沒有 `Common`、沒有 `Buildings.Support.ModdedFactions`、沒有自訂陣營 id | **H1 的直接原因**（§4） |
| 9 | `data/manifest.json:7-8` | `RequiredMods: []`、`OptionalMods: []` | 未宣告對 Emberpelts/支援 mod 的軟依賴（不是功能必要，但影響載入順序與玩家理解） |

### 3.2 執行期語意

| # | 位置 | 現況 | 對自訂陣營的後果 |
|---|---|---|---|
| 10 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.cs:163-164` | `_faction = GameCropCatalog.ResolveFaction(BlueprintId())`；`IndoorFarmRecord.Create(tier, _faction)` | 建築一生成就記成 Folktails；面板、日誌、存檔全文錯 |
| 11 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Activation.cs:87-89` | 啟動日誌 `"indoor farm active, ... faction=" + _faction + ", blueprint=" + BlueprintId()` | 診斷時會看到 `faction=Folktails, blueprint=BoxFarm.Basic.Folktails`（若建築有出現） |
| 12 | `src/HDBF.Gameplay/Components/HdbfBoxFarmController.Commands.cs:153-181` + `FallbackCropCatalog.TryResolve`（`FallbackCropCatalog.cs:96-120`） | 固定作物驗證**先查 live catalog**、再查 fallback | 這條是對的；但 UI 下拉給的清單是錯的（#15），玩家選不到新作物 |
| 13 | `src/HDBF.Core/Indoor/IndoorSaveEnums.cs:47-66` | 存檔 faction 只認 `"Folktails"`/`"IronTeeth"`，其他退回數值/Folktails | 自訂陣營只能寫成 Folktails |
| 14 | `src/HDBF.Core/Indoor/IndoorSaveValidator.cs:98-102`、`IndoorSaveMigrator.cs:60-65`、`IndoorSaveMigrationSupport.cs:21-28` | 未知 enum 一律修成 Folktails | 未知陣營資訊不可逆丟失 |
| 15 | `src/HDBF.Persistence/Indoor/IndoorEntitySaveBridge.cs:180-185`、`IndoorEntitySaveBridge.Keyed.cs:239-243` | 載入後若 `record.Faction != 藍圖解析 faction` → **用藍圖值覆寫**（並標 degraded） | 就算存檔寫對了自訂陣營，載入也會被改回藍圖解析值（目前＝Folktails） |
| 16 | `src/HDBF.Core/Indoor/IndoorFarmSnapshot.cs:52`、`HdbfBoxFarmController.IndoorSnapshot.cs:39-46,107` | snapshot 帶 `HdbfFaction Faction` | UI 只能顯示兩個陣營 |
| 17 | `src/HDBF.UI/Panels/HdbfIndoorPanelFormat.cs:104-109` | `FactionKey`：IronTeeth ? `PanelFactionIronTeeth` : `PanelFactionFolktails`（只有兩個 loc key） | 自訂陣營顯示「葉尾/Folktails」 |
| 18 | `src/HDBF.UI/Panels/HdbfIndoorPanelFactory.cs:54-56` | 副標＝Tier + FactionKey | 同上 |
| 19 | `src/HDBF.UI/Panels/HdbfIndoorPanelWarnings.cs:159-171,177-206` | `BuildCropWarnings`/`BuildCropChoices` 用 **`FallbackCropCatalog.Default`**（`:164`、`:180`），不是 live catalog | 固定作物下拉只列 Carrot/Potato/Wheat/Sunflower（Emberpelts 多數作物看不到）；選了也會被判 invalid |
| 20 | `src/HDBF.UI/Panels/HdbfIndoorPanelFormat.cs:157-183` | `CropNameKey` 白名單只有 10 個官方作物 id；`CropName` 未知 id 顯示原始 id | Emberpelts 新作物顯示英文 id（可接受，但不在地化） |
| 21 | `src/HDBF.UI/Panels/HdbfFarmPanelFragment.cs:236-244` | 面板 fingerprint 含 `(int)snapshot.Faction` | 陣營若改成動態字串需同步更新 fingerprint |
| 22 | `src/HDBF.Core/Indoor/IndoorCropReport.cs:19-90,147-180`、`IndoorLaborMath.Scheduling.cs:129-144` | 作物列/輪作都以 `record.Faction` 取表 | 只要表被正確建在 record 桶裡就可用（見 §6 最小修法） |
| 23 | `data/Localizations/{enUS,zhCN,zhTW}.csv` | `HDBF.Panel.Faction`、`HDBF.Panel.FactionFolktails`、`HDBF.Panel.FactionIronTeeth` | 沒有自訂陣營名稱（動態名稱可免新增 key，見 §6） |

### 3.3 測試/驗證器把「8 份藍圖、2 陣營」寫死（擴充前必須先改）

| 位置 | 內容 |
|---|---|
| `tests/HDBF.Tests/V2BlueprintContractTests.cs:44-46` | `Tiers` × `Factions = { "Folktails","IronTeeth" }` |
| 同檔 `:134-198` | 「必須剛好 8 份藍圖」 |
| 同檔 `:650-665` | 兩陣營 mirror images（只允許 TemplateName/兩個 model path 不同） |
| 同檔 `:497-541` | model path 必須是 `BoxFarm.<Tier>.<Faction>.Model` |
| `tests/HDBF.Tests/V2LocalizationTests.cs:72-76` | 三語言 key 數必須一致（新增 key 要三份 CSV 同步） |
| `tools/validate/DataChecks.cs:24,29` | `BlueprintSuffixes` 4 檔、`Factions` 2 陣營 |
| 同檔 `:173-226` | 「data/Buildings/Food/BoxFarm/ 必須剛好 8 份」 |
| 同檔 `:1419-1489` | 兩個 collection 必須各列出該陣營 4 份、CollectionId 固定 |
| 同檔 `:306-307` | `RequiredMods`/`OptionalMods` 必須是 **string array**（與遊戲的物件陣列 schema 不符；`docs/workstreams/compat-01-adapters.md` §9.8 已指出） |
| `docs/v2/INTERFACES.md:15`、`docs/REQ_V2_SECTION_19.md:61,219` | 文件把 enum 與存檔 faction 寫死成 Folktails/IronTeeth |

---

## 4. 失敗鏈（現象 → 原因，依信心度排序）

### 4.1 H1：建築完全不出現在建造選單（最可能；機制已證實，實際發生=高度可能）

**玩家現象（推測語氣）**：「我玩 Emberpelts，建造選單的食物分頁找不到盒子農場。」

**原因鏈（每步都有證據）**：

1. 玩家開 Emberpelts 新局 → `FactionService.Current = FactionSpec(Id="Emberpelts")`。
2. `TemplateCollectionService.Load()` 收集 `"Common"` + `Current.TemplateCollectionIds`（§2.1）。官方 `Buildings.Common` 只有 Path；HDBF 的 collection 不在其中（§2.1-6）。
3. HDBF 的 8 份 blueprint 只被 append 到 `Buildings.Folktails` / `Buildings.IronTeeth`（`data/TemplateCollections/...:4-9`）→ 除非 Emberpelts 的 `TemplateCollectionIds` 內含這兩個 id，`AllTemplates` 不會有 HDBF 建築。
4. 沒有 template → `TemplateService.GetAll<PlaceableBlockObjectSpec>()` 沒有它 → `PlaceableBlockObjectSpecService` 不生成 tool → 建造選單沒有 HDBF（§2.1）。
5. 玩家無法建造 → 不會有實體、不會有面板、HDBF 日誌除了 load 外完全沒有 `indoor.entity active` → 「doesn't work」。

**兩個補強**：

* **即使玩家裝 `ModdedFactionsBuildingSupport` 也一樣**：HDBF 沒有註冊 `Buildings.Support.ModdedFactions`（本機 `data/TemplateCollections/` 只有兩份官方 collection；全 repo 搜尋無此字串）。**【已證實】**
* **除非 Emberpelts 自己 clone 了 `Buildings.Folktails`**：More Mines 的 `TWDirtclaws` 就是用 `TWFactionCollectionCloneSpec` clone `Buildings.Folktails`（本機檔案可證）。若 Emberpelts 也這樣做，HDBF 的 Folktails 版會「順便」出現 → 走 H2。**需實機確認（§9）**。

### 4.2 H2：建築出現但壞掉（條件：Emberpelts clone 官方 collection／玩家用支援 mod 或手改檔）

#### H2-a 材質 crash（最嚴重；機制已證實，條件需實機）

* HDBF 模型全部引用 faction-scoped 原版材質（§2.4）。若 Emberpelts 的 `MaterialCollectionIds` **不含** `"Folktails"`（也沒有提供同名材質），則在 Emberpelts 局載入建造選單時，`MaterialRepository.GetMaterial("BaseWood_Brown.Folktails")` 丟 `ArgumentException`，**整個建造選單載入失敗**（既有實機證據：`docs/VERIFICATION.md` §9 正是「Folktails 局載 IronTeeth 材質」造成整個選單爆炸）。
* 若 Emberpelts 的 `MaterialCollectionIds` **含** `"Folktails"`（像 TWDirtclaws 那樣同時掛兩個官方材質 collection，或像 TWTradeOffice 直接 append Folktails 藍圖進 Emberpelts collection 的先例），則可正常建立，只是外觀不會是 Emberpelts 紅色系（除非它的 MaterialPatcher 有 patch 這些材質名——Bobingabout Script Pack 的 MaterialPatcher 正是用 `FactionID` 過濾 patch，README 有多個 Emberpelts 例子，**高度可能它會把官方材質名換成紅色貼圖**）。
* **判定依據**：安裝 Emberpelts 後讀 `Factions/Faction.Emberpelts.blueprint.json` 的 `MaterialCollectionIds`，或直接看 Player.log 是否有 `Material ... not found in repository.` / `Processing prefab BoxFarm`。

#### H2-b 陣營解析錯誤（程式碼已證實）

* `ResolveFaction("BoxFarm.Basic.Folktails")` → Folktails（`GameCropCatalog.cs:120-129`）。即使 active faction 是 Emberpelts，record 記 Folktails、面板顯示 Folktails、日誌寫 Folktails（§3.2 #10/#11）。
* 因為 HDBF 的 `Rebuild` 對「未知 active faction」仍把 live specs 建進 `_folktails` 表（§3.1 #4），所以**作物資料本身可能是 Emberpelts 的**，只是被貼上 Folktails 標籤——這也代表「面板顯示 Folktails」的誤導性很高。

#### H2-c 作物表錯（程式碼已證實；影響=高度可能）

* `BuildTable` 先用 Folktails fallback 4 作物打底（`GameCropCatalog.SpecReader.cs:94-110`），live 作物附在後面（`:112-118`）→ Emberpelts 局輪作順序會是 `Carrot, Potato, Wheat, Sunflower, <Emberpelts 作物…>`。官方 fallback 定義（`FallbackCropCatalog.cs`）會讓輪作種下 Emberpelts 可能根本不存在的作物，並產出 fallback 定義的貨物 id（例如 Carrot/Potato/Wheat/Sunflower 系列）；Emberpelts 作者已明說「多數作物是新的」，所以 **F-FACTION-06 是實質 bug**。
* 固定作物下拉/警告用 fallback 表（§3.2 #19）→ 玩家看不到 Emberpelts 新作物可選；若存檔帶著新作物 id，UI 反而顯示 invalid crop 警告（`:164`）。

#### H2-d 收穫產物與庫存（無直接壞掉證據）

* 收穫產物＝live 作物的 `CuttableSpec.Yielder.Yield.Id`（`GameCropCatalog.SpecReader.cs:59-89`），寫入 `SimpleOutputInventory`（`SimpleOutputInventorySpec`，容量 240/600/1200/2400）。貨物是否存在於該陣營：Common 有 Log/Plank/MetalBlock/Water，但新作物的收穫貨物是否在 Emberpelts 的 GoodCollectionIds/需求裡**未知**（需實機）。若貨物在遊戲 spec 中存在但不在該陣營 good collection，通常仍可堆在庫存，但玩家可能看不到用途/需求（**推測**）。

#### H2-e 水（推測，需實機）

* HDBF 的水是 `Water` 貨物（Common 已證實可用）＋內部 tank＋可選 pipe/manual。Emberpelts「恨水」且以果汁為主，**可能沒有水泵/水箱/水管鏈**可接，也可能沒有可採水的水體存貨；HDBF 的人工補水探測（`HdbfManualWaterSource`）在該陣營能否找到水源需實機。若找不到 → 面板 `NoWater`/`No Connection`、農場停擺（不是 crash）。

#### H2-f 存檔（現況無損，但語意錯；未來風險）

* 現在自訂陣營一律 Folktails，存檔/載入兩邊一致，實際資料不會丟；但 `IndoorEntitySaveBridge.cs:180-185` 的「藍圖為準強制覆寫」意味著**一旦藍圖解析與實際陣營不一致（例如以後加了中立藍圖），存檔的陣營欄位會被默默改寫**。**F-FACTION-05 是潛伏 bug**。
* 遊戲層級風險：把 Emberpelts 從存檔的 mod 清單移除後，`FactionSpecService.GetFaction` 會 throw（§2.7）→ 存檔不能載入；不是 HDBF 的 bug，但玩家回覆要寫（§7）。

### 4.3 明確區分「未支援」與「bug」

| 類別 | 條目 | 說明 |
|---|---|---|
| **未支援（功能缺口）** | F-FACTION-01、F-FACTION-02、F-FACTION-07、F-FACTION-08、F-FACTION-09 | 不是「支援過的自訂陣營壞掉」，而是 HDBF 從設計上只認兩個官方陣營：collection 不註冊、沒有中立藍圖/材質、UI/本地化/測試/驗證器都寫死兩個。玩家「不能用」的主因屬這一類。 |
| **Bug（一旦建築出現就會踩）** | F-FACTION-03、F-FACTION-04、F-FACTION-05、F-FACTION-06 | 陣營由名稱硬解析、固定作物下拉用錯 catalog、fallback 表污染自訂陣營作物、存檔覆寫語意。這些在官方兩陣營下「剛好正確」，在自訂陣營下錯。 |
| **設計不相容（需實機判）** | F-FACTION-10 | 水系統/作物產物/玩法前提（Emberpelts 恨水、新作物）可能與 HDBF 的 tank+pipe 模型不合。 |
| **非 HDBF 的遊戲層風險** | F-FACTION-11（風險說明，不是 bug 編號） | 移除自訂陣營 mod 會讓該存檔載入失敗（§2.7）。 |

---

## 5. Bug / Gap 清單（F-FACTION-01 … 10）

### F-FACTION-01（P0，未支援／功能缺口）Emberpelts 建造選單沒有 HDBF 建築
* **原因**：§4.1；`data/TemplateCollections/*:3-9` 只註冊兩個官方 collection；`TemplateCollectionService` 只載入 `Common`+當前 `FactionSpec.TemplateCollectionIds`（IL）。
* **信心度**：機制【已證實】；玩家實際症狀【高度可能】（唯一替代路徑是 Emberpelts clone 官方 collection，需實機）。
* **修法**：§6 M0/M1。
* **驗證**：安裝 Emberpelts 開局看建造選單；或讀其 FactionSpec。

### F-FACTION-02（P0，未支援／設計限制）沒有可跨陣營重用的中立建築變體
* **原因**：8 份藍圖的 model/template name 都綁官方陣營；模型材質 faction-scoped（§2.4）。直接把 Folktails 藍圖塞進自訂陣營 collection，只有在該陣營有 `Folktails` 材質時才安全，否則會升級成「整個建造選單 crash」（§4.2 H2-a）。
* **信心度**：【已證實：機制與資產內容】；Emberpelts 的材質 collection 條件【需實機】。
* **修法**：§6 M0/M1 的「中立藍圖＋Common 材質模型」。

### F-FACTION-03（P1，bug）固定作物下拉與作物警告用 `FallbackCropCatalog`，不是 live catalog
* **位置**：`src/HDBF.UI/Panels/HdbfIndoorPanelWarnings.cs:159-171`（`:164`）、`:177-206`（`:180`）；對照 `HdbfIndoorPanelFactory.cs:78`。
* **影響**：任何「live 作物表 ≠ 內建表」的情況都錯（自訂陣營必錯；官方兩陣營目前剛好一致，所以沒被發現）。Emberpelts 玩家看不到新作物可選、帶著新作物 id 反而顯示 invalid。
* **修法**：把 live 作物表帶進 `IndoorFarmSnapshot`（新增 `IReadOnlyList<CropDefinition> Crops` 或最少 `crop ids+顯示名`），controller 在 `_cropCatalog.ForFaction(_faction)` 填入，UI 用 snapshot；空表才退回 fallback。注意 UI 不得引用 Gameplay（目前 UI 只依賴 Core）→ 透過 snapshot 傳遞是正確分層。
* **信心度**：【已證實（程式碼）】。

### F-FACTION-04（P1，bug/語意）陣營由藍圖名稱硬解析，且 active faction 未知時一律 Folktails
* **位置**：`GameCropCatalog.cs:120-129`、`:165-176`；`HdbfBoxFarmController.cs:163`。
* **影響**：面板副標/日誌/存檔語意錯；作物表被貼錯標籤；未來任何中立藍圖都會被解析成 Folktails。
* **修法**：新增「active faction 解析」純函式（建議放 Core，方便 L1 測試）：輸入 `(blueprintName, currentFactionId, currentFactionTemplateCollectionIds)`，輸出 `HdbfFaction + string factionId`；官方 id → enum；其他 → 以「active faction」為準（enum 用 sentinel，例如 `HdbfFaction.Modded=2` 或保留 Folktails 但另存 factionId 字串，見 §6）。藍圖名稱只作 fallback（給沒有 FactionService 的離線工具）。
* **信心度**：【已證實（程式碼）】。

### F-FACTION-05（P1/P2，bug）載入時由藍圖解析值強制覆寫存檔 Faction
* **位置**：`src/HDBF.Persistence/Indoor/IndoorEntitySaveBridge.cs:180-185`、`IndoorEntitySaveBridge.Keyed.cs:239-243`；未知值的修復：`IndoorSaveValidator.cs:98-102`、`IndoorSaveMigrator.cs:60-65`、`IndoorSaveMigrationSupport.cs:21-28`。
* **影響**：目前對自訂陣營「一致地錯」所以資料沒丟；但這是「藍圖為準」的假設，一旦支援中立藍圖/自訂陣營，會默默把存檔改成錯的陣營；未知 enum 也會被洗成 Folktails（不可逆）。跨陣營分享存檔時（雖然結算本身只有一個 faction）語意仍錯。
* **修法（最小）**：record 增加選填 `FactionId`（string）保存真實 id；載入時**不再**用藍圖解析值覆寫「非官方」值；官方 id 仍以藍圖為準（保持既有行為）；validator/migrator 對未知 `FactionId` 保留字串、enum 用 sentinel。需要存檔 schema 決策（見 §6）。
* **信心度**：【已證實（程式碼）】。

### F-FACTION-06（P2，bug）自訂陣營的 live 作物被塞進 Folktails fallback 表（輪作/固定作物表污染）
* **位置**：`GameCropCatalog.cs:153-163`、`GameCropCatalog.SpecReader.cs:91-121`。
* **影響**：輪作先種 4 個官方 fallback 作物（Emberpelts 可能不存在/不是其作物），之後才是它真正的作物；收穫產物可能是 fallback 定義的貨物。
* **修法**：`BuildTable` 對「未知/自訂 active faction」改成 **live-only**（不 seed fallback；找不到就用 live specs 建表；表為空才退回 fallback 並記一筆 log）。官方兩陣營行為不變。
* **信心度**：【已證實（程式碼）】；實際種錯作物【高度可能】。

### F-FACTION-07（P2，未支援）UI/本地化只有兩個官方陣營名稱
* **位置**：`HdbfIndoorPanelFormat.cs:104-109`；`HdbfIndoorPanelFactory.cs:54-56`；`data/Localizations/*.csv`；`HdbfUiLocKeys.cs:32-43`；fingerprint `HdbfFarmPanelFragment.cs:236-244`。
* **修法**：snapshot 帶 `FactionDisplayName`（由 controller 讀 `FactionService.Current.DisplayName` / `Id`；`LocalizedText` 由遊戲本地化）；UI 優先用它，沒有才退回兩個 loc key；fingerprint 加入字串 hash。作物名維持「未知 id 顯示原始 id」的降級（已存在，見 `CropName`）。
* **信心度**：【已證實（程式碼）】。

### F-FACTION-08（P2，未支援）合約測試/驗證器把 2 陣營 8 藍圖寫死
* **位置**：§3.3 表。
* **修法**：把「官方陣營 = 2」改成「每個 collection 的預期清單 + 中立 collection 的預期清單」；`CheckBlueprintInventory` 改成「官方 8 份 + 中立 N 份」；mirror tests 只套用官方對；新增自訂陣營中立藍圖契約（材質白名單必須是 `MaterialCollection.Common` 的材質名、TemplateName 必須是 `BoxFarm.<Tier>`、不可含 faction 後綴）。
* **信心度**：【已證實】。

### F-FACTION-09（P2，發佈風險）manifest 的 OptionalMods 目前是空陣列；validator 格式與遊戲不符
* **位置**：`data/manifest.json:7-8`；`tools/validate/DataChecks.cs:306-307`；先例：`3282276626/version-1.1.0/manifest.json`（`OptionalMods: [{Id:"Emberpelts"}]`）、`3815376360/version-1.1/manifest.json`（`OptionalMods: [{Id:"ModdedFactionsBuildingSupport"}]`）。
* **修法**：要列軟依賴時，`OptionalMods` 用物件陣列 `[{"Id":"Emberpelts"}]`（可加 `MinimumVersion`），並同步改 validator 接受物件陣列（保留舊 string array 相容）。不要讓它變成 RequiredMods——HDBF 不應強迫玩家裝任何陣營 mod。
* **信心度**：【已證實：格式與先例】。

### F-FACTION-10（P1，設計不相容，需實機）Emberpelts 的「恨水」玩法與 HDBF 的 tank+pipe/manual 模型可能不合
* **分析**：HDBF 水面是 `Water` 貨物（Common 保證存在）＋內部 tank；補水有三條路：pipe（Timber Pipes，第三方）、人工搬水（找附近水體/水貨物 inventory）、一鍵補水。Emberpelts 作者明說「恨水、以果汁取代水」，其建築組可能沒有水泵/水箱/水管；若無水體存貨可搬、玩家又沒裝 Timber Pipes，農場會卡 `NoWater`（不會 crash）。
* **信心度**：【推測】；需實機跑 1–2 個遊戲日並看 `indoor.water.*` / 面板。
* **修法（若成立）**：不強改平衡，改為「明確降級＋面板說明」：在自訂陣營且找不到任何水源時，面板顯示可理解的「此陣營沒有 HDBF 可用的水源」而不是只說 No workers/No water；文件建議玩家在 Emberpelts 局使用 pipe 或接受無水降產。

---

## 6. 修法（最小可行 → 完整支援）

> 共通前提：**「可見」與「安全」是兩件事**。把 HDBF 藍圖塞進自訂陣營 collection 之前，必須先解決「跨陣營材質」；否則會從「看不到」升級成「整個建造選單 crash」。本節方案排序以「先安全可見」為原則。

### 6.1 M0（最小、資料為主）：給 Emberpelts 一個專屬 HDBF collection（需搭配中立模型或已驗證的材質前提）

**做法**：

1. `data/TemplateCollections/TemplateCollection.Buildings.HDBF.blueprint.json`（新檔名，避免與其他 mod 同名路徑互改）：
   ```json
   {
     "TemplateCollectionSpec": {
       "CollectionId": "Buildings.HDBF",
       "Blueprints": [
         "Buildings/Food/BoxFarm/BoxFarm.Basic.blueprint",
         "Buildings/Food/BoxFarm/BoxFarm.Intermediate.blueprint",
         "Buildings/Food/BoxFarm/BoxFarm.Advanced.blueprint",
         "Buildings/Food/BoxFarm/BoxFarm.Top.blueprint"
       ]
     }
   }
   ```
2. `data/Factions/Faction.Emberpelts.optional.blueprint.json`（**`.optional` 語意**：只有 Emberpelts 主模組的同名 faction blueprint 存在時才套用；不存在則整個群組被丟棄，安全無副作用）：
   ```json
   {
     "FactionSpec": {
       "TemplateCollectionIds#append": ["Buildings.HDBF"]
     }
   }
   ```
   * 需要確認 Emberpelts 的 faction blueprint 路徑就是 `Factions/Faction.Emberpelts.blueprint.json`（官方慣例；若不對，optional 會靜默不生效——安全但無效，需看它 mod 內實際路徑）。
   * 未來每個自訂陣營＝多加一個 5 行 optional 檔（Emberpelts/Whitepaws/LeafCoats/GreedyBuilders/…）；也可用腳本從已知 id 清單產生。
3. **中立藍圖 4 份**（`data/Buildings/Food/BoxFarm/BoxFarm.<Tier>.blueprint.json`，TemplateName `BoxFarm.<Tier>`，其餘 spec 與現有 8 份相同；`TierCatalog.TryGetByBlueprintId` 已支援這個名稱，`TierCatalog.cs:235-285`）。
4. **中立模型**：材質只能用 `MaterialCollection.Common` 的材質名（例如 `BaseWoodCommon`、`BaseWoodCommon_Amber`、`PaperCommon`），需重新產生 4 個 `.timbermesh`（art 管線；`art/` + `docs/workstreams/art-04-flat-roof-own-material.md` 的 UV 規則）。**這一項是 M0 的真正工作量。**
   * **快速替代（風險高、僅供驗證）**：先 append 現有 `BoxFarm.*.Folktails.blueprint` 到 `Buildings.Emberpelts`（TWTradeOffice 先例）。若 Emberpelts 的 `MaterialCollectionIds` 沒有 `Folktails`，會造成整個建造選單 crash → **不要直接出貨**；先做 §9 的實機確認。
5. 搭配 §6.3 的程式修正（M0 只解決「可見」）。

### 6.2 M1（推薦的「最小可靠」）：用官方 `ITemplateCollectionIdProvider` 自動服務所有自訂陣營

**為什麼可行**：1.1.2.8 的 `TemplateCollectionService` 建構子收 `IEnumerable<ITemplateCollectionIdProvider>`（IL），而 `GameFactionSystemConfigurator` 正是用 `MultiBind<ITemplateCollectionIdProvider>()` 綁官方 faction provider（IL）。官方介面是 public，Bindito 的 MultiBind 是公開 API（HDBF 已用 `MultiBind<TemplateModule>()`、`MultiBind<EntityPanelModule>()`）。**【已證實】**

**做法**：

1. 新增 `src/HDBF.Gameplay/Components/HdbfTemplateCollectionIdProvider.cs`（或放 Gameplay/Compat）：

   ```csharp
   // 概念草稿，不是最終碼
   public sealed class HdbfTemplateCollectionIdProvider : ITemplateCollectionIdProvider
   {
       private readonly FactionService _factions;
       public IEnumerable<string> GetTemplateCollectionIds()
       {
           var current = _factions?.Current;
           if (current is null) yield break;
           // 官方陣營維持現狀（8 份 faction 藍圖；不重複）
           if (current.Id == "Folktails" || current.Id == "IronTeeth") yield break;
           // 若該自訂陣營已重用官方 collection（clone 類 mod），HDBF 已可見 → 不重複
           if (current.TemplateCollectionIds.Contains("Buildings.Folktails")
               || current.TemplateCollectionIds.Contains("Buildings.IronTeeth")) yield break;
           yield return "Buildings.HDBF";
       }
   }
   ```
   * 在 `HdbfGameConfigurator.Configure()` 加一行（與現有 `MultiBind<TemplateModule>()` 同風格）：
     `MultiBind<ITemplateCollectionIdProvider>().To<HdbfTemplateCollectionIdProvider>().AsSingleton();`
   * 優點：**不依賴 `ModdedFactionsBuildingSupport`、不修改別人的 FactionSpec、未來任何自訂陣營自動支援**。
   * 風險：provider 在 `TemplateCollectionService.Load()` 期間被呼叫，需要 `FactionService.Current` 已載入；官方自己的 `FactionTemplateCollectionIdProvider` 有一樣的依賴（IL 證實），所以排序已有前例，但仍應在實機看 `AllTemplates` 是否在自訂陣營生效（可加一次性 log）。
   * 若不想寫 C#：替代方案是依賴 `ModdedFactionsBuildingSupport` 的 `Buildings.Support.ModdedFactions` collection（把中立藍圖註冊到該 id＋manifest `OptionalMods` 列它）。這是資料-only，但需要玩家額外安裝支援 mod，且該 mod 行為本機未取證（**推測**）。
2. 中立藍圖/模型同 M0 第 3、4 點。
3. 需要搭配 §6.3 的程式修正。

### 6.3 最小程式修正（M0/M1 共用；也是「若建築出現」時修 bug 的必要項）

| 修正 | 檔案（建議） | 內容 |
|---|---|---|
| C1 動態陣營解析 | Core 新增純函式（建議 `src/HDBF.Core/Indoor/HdbfFactionResolver.cs`）＋ `GameCropCatalog.cs:120-176`、`HdbfBoxFarmController.cs:163` | 由 `FactionService.Current` 決定 active faction/id；藍圖名稱只當 fallback（離線工具/舊存檔）。官方 id 行為不變。 |
| C2 live-only 作物表 | `GameCropCatalog.cs:153-163`、`GameCropCatalog.SpecReader.cs:91-121` | 未知/自訂 active faction → 只用 `activeSpecs` 建表（不 seed 官方 fallback）；官方維持現行。 |
| C3 UI 用 snapshot 作物表 | `IndoorFarmSnapshot.cs`（加 `Crops`）、`HdbfBoxFarmController.IndoorSnapshot.cs`（填 `_cropCatalog.ForFaction(_faction)`）、`HdbfIndoorPanelWarnings.cs:159-206`、`HdbfIndoorPanelFactory.cs:78` | 固定作物下拉/警告改吃 snapshot 的 live 表；空表才 fallback；UI 仍不引用 Gameplay。 |
| C4 UI 顯示動態陣營名 | `IndoorFarmSnapshot.cs` 加 `FactionDisplayName/FactionId`；`HdbfBoxFarmController` 讀 `FactionService.Current`（需注入 FactionService，或由 GameCropCatalog 暴露 active faction id）；`HdbfIndoorPanelFactory.cs:54-56`、`HdbfIndoorPanelFormat.cs:104-109`、`HdbfFarmPanelFragment.cs:236-244` | 官方兩陣營仍用現有 loc key；自訂陣營用遊戲的 `DisplayName`/Id 字串；fingerprint 加字串 hash。 |
| C5 存檔保留未知陣營 | `IndoorFarmRecord.cs`（加 `FactionId` string）、`IndoorSaveEnums.cs`、`IndoorSaveCodec.cs:60,178`、`IndoorSaveKeys.cs:56-57`、`IndoorEntitySaveBridge*.cs`、`IndoorSaveValidator.cs:98-102`、`IndoorSaveMigrator.cs:60-65`、`IndoorSaveMigrationSupport.cs:21-28`、`IndoorSaveFieldRepair.cs` | 存檔同時寫真實 `factionId`；載入時「官方 id 以藍圖為準」、非官方保留存檔值；格式採向後相容（無新欄位時用舊邏輯）；是否升 schemaVersion 由 Lead 決定（見 §9 未解問題）。 |
| C6 著陸 log | `GameCropCatalog` / `HdbfBoxFarmController.Activation.cs:87-89` | 自訂陣營時把 `activeFactionId`、`active template collection ids`、`crop table size` 寫一次 Info，方便玩家 log 診斷（目前只有 faction enum）。 |

### 6.4 F1（完整支援：任意/未來自訂陣營）

1. **資料驅動陣營**：把「陣營列舉」從 `HdbfFaction` enum 擴充為「enum（官方 0/1 ＋ sentinel 2）＋`FactionId` 字串」，所有表都以 factionId 為鍵（`ICropCatalog` 可加 `ForFaction(string factionId)` 或維持 enum 桶 + 動態表）。
2. **中立模型全套**：4 個中立 .timbermesh（Common 材質）、可選的 Emberpelts 紅色系變體（若 MaterialPatcher 可重用官方材質名，可用 blueprint modifier 做換色，避免再出 4 個模型）。
3. **模板策略**：M1 的 provider ＋中立 collection；可選「若自訂陣營已 reuse 官方 collection，就不注入」的守門（草稿已含）。
4. **UI/本地化**：動態陣營名、作物名對未知 id 顯示 raw id；如要本地化新作物，需在 `HdbfUiLocKeys`＋3 份 CSV 加 key（可選）。
5. **相容性矩陣**：
   * 官方 Folktails/IronTeeth（現有 8 藍圖，回歸測試）。
   * Emberpelts（本調查主目標；需安裝）。
   * 至少一個「clone 官方 collection」的自訂陣營（TWDirtclaws 類）→ 驗證守門不重複。
   * 至少一個「自帶作物/水」的自訂陣營（Emberpelts 即符合）。
   * 無任何自訂陣營（純官方）→ HDBF 行為零變化。
6. **發佈注意**：HDBF 的外觀/本地化文件、Workshop 說明要寫「官方兩陣營完整支援；其他自訂陣營＝可用但外觀中性（Common 材質）、作物依該陣營 live 表、水需玩家自備 pipe 或手動」。
7. **驗證器/測試**：§3.3 全部改為「官方 8 + 中立 N」契約；新增中立藍圖的材質白名單檢查（只允許 `MaterialCollection.Common` 名稱）；新增 provider 政策的 L1 純函式測試（見 §7）。

### 6.5 受影響檔案清單（供 Lead 拆卡）

| 層 | 檔案 | 變更 |
|---|---|---|
| Core | `Indoor/HdbfFaction.cs` | 保留 0/1；新增 sentinel（2=Modded/Unknown）或文件註明 sentinel 策略 |
| Core | `HdbfFactionResolver.cs`（新）、`Balance/TierCatalog.cs`（可能不需改） | 純函式：blueprintName + activeFactionId → (HdbfFaction, factionId) |
| Core | `Indoor/IndoorFarmRecord.cs`、`IndoorSaveEnums.cs`、`IndoorSaveCodec.cs`、`IndoorSaveValidator.cs`、`IndoorSaveMigrator.cs`、`IndoorSaveMigrationSupport.cs`、`IndoorSaveFieldRepair.cs` | `FactionId` 欄位、序列化/修復/遷移 |
| Core | `Indoor/IndoorFarmSnapshot.cs`、`Indoor/IndoorCropReport.cs`、`Indoor/IndoorLaborMath*.cs` | snapshot 帶 live 作物表/陣營名；取表以 factionId 為準 |
| Persistence | `Indoor/IndoorEntitySaveBridge.cs`、`...Keyed*.cs`、`IndoorSaveKeys.cs` | 覆寫規則、`FactionId` 讀寫 |
| Gameplay | `Components/GameCropCatalog.cs`、`GameCropCatalog.SpecReader.cs` | 動態 active faction、live-only 表、診斷 log |
| Gameplay | `Components/HdbfBoxFarmController*.cs`、`HdbfGameConfigurator.cs`、`Components/HdbfTemplateCollectionIdProvider.cs`（新）、`Specs/HdbfBoxFarmSpec.cs`（如需新欄位） | 動態陣營注入、snapshot、DI MultiBind |
| UI | `Panels/HdbfIndoorPanelFormat.cs`、`HdbfIndoorPanelWarnings.cs`、`HdbfIndoorPanelFactory*.cs`、`HdbfIndoorPanelData.cs`、`HdbfFarmPanelFragment.cs`、`Localization/HdbfUiLocKeys.cs` | 動態名稱、snapshot 作物、fingerprint；（可選）新 loc key × 3 CSV |
| data | `manifest.json`（OptionalMods 物件陣列）、`TemplateCollections/TemplateCollection.Buildings.HDBF.blueprint.json`（新）、`Factions/Faction.<Custom>.optional.blueprint.json`（新，每個已知陣營一份或 M1 免）、`Buildings/Food/BoxFarm/BoxFarm.<Tier>.blueprint.json`（新，中立）＋中立 `.timbermesh` ×4、`Localizations/*.csv`（若新 key） | 見 §6.1/§6.2 |
| art | `art/`（模型產生器/匯出腳本）、`docs/workstreams/art-04-flat-roof-own-material.md` | 中立 Common 材質模型之 UV/材質對映 |
| tests | `V2BlueprintContractTests.cs`、`V2LocalizationTests.cs`、`V2CropCatalogTests.cs`、`V2PanelContractTests.cs`、`V2SaveCodecTests.cs`、`V2SaveValidatorTests.cs`、`V2PersistenceBridgeContractTests.cs`、`V2GameplayContractTests.cs`、新增 `FactionResolver*Tests.cs` / `ModdedFaction*Tests.cs` | §3.3 契約改寫＋新陣營測試 |
| tools | `validate/DataChecks.cs`（`Factions`、8/8 inventory、collection 清單、OptionalMods 物件陣列、中立藍圖材質白名單） | 見 §3.3 |
| docs | `docs/v2/INTERFACES.md`（enum、snapshot）、`docs/REQ_V2_SECTION_19.md:61,219`、`docs/API_FACTS.md`（新增 faction/template collection IL 事實）、`docs/VERIFICATION.md`、本檔 | 規格與驗證文件 |

### 6.6 離線可做的契約測試（L1，不載入遊戲）

現有測試工程只 reference `HDBF.Core`（＋ link UI 的 game-free 檔，見 `tests/HDBF.Tests/HDBF.Tests.csproj:29-51`），所以下列「純邏輯」要先搬進 Core 或放進 test-link 清單：

1. **陣營解析**（新純函式，放 Core）：`TestResolveFactionOfficialIds`、`TestResolveFactionUnknownIdUsesActiveFaction`、`TestResolveFactionBlueprintNameOnlyFallsBack`。
2. **Modded collection 政策**（純函式）：`TestHdbfCollectionNotAddedForOfficialFactions`、`TestHdbfCollectionAddedForUnknownFaction`、`TestHdbfCollectionNotAddedWhenFactionAlreadyReusesVanillaCollections`（對應 M1 守門）。
3. **live-only 作物表**：把 `BuildTable` 抽出 Core 純函式（輸入 fallback + activeSpecs + `includeFallbackSeed`）→ `TestUnknownFactionTableUsesLiveSpecsOnly`、`TestOfficialFactionStillSeedsFallback`、`TestEmptyLiveSpecsFallsBack`。
4. **存檔 round-trip**：`TestFactionIdRoundTripsForUnknownFaction`、`TestOfficialFactionBlueprintWinsOnLoad`（沿用現有 bridge 契約測試的做法，把新的判斷抽成 Core 函式）、`TestUnknownEnumValuePreservedAsFactionId`。
5. **UI 讀模型**：`TestPanelCropChoicesUseSnapshotCrops`、`TestPanelCropWarningUsesSnapshotCrops`（`HdbfIndoor*.cs` 已被 link 進測試工程，可直接測 `HdbfIndoorPanelFactory.Create`）。
6. **資料契約**：`TestShippedBlueprintInventoryIsOfficial8PlusNeutralN`；`TestNeutralBlueprintsUseOnlyCommonMaterials`（讀 `.timbermesh` 解壓後材質字串，比對 `MaterialCollection.Common` 白名單——沿用 `ArtModelContractTests` 的解壓能力）；`TestNeutralBlueprintTemplateNamesHaveNoFactionSuffix`；`TestOfficialFactionsKeepTheirEightVariants`；`TestEmberpeltsOptionalPatchIsOptional`（檢查檔名 `.optional` 與 JSON 只含 `TemplateCollectionIds#append`）。
7. **驗證器契約**：`tools/validate` 的 self-check（`DataChecks`）要能先紅後綠；`docs/workstreams/tech-data.md` 的「故意改壞」模式可沿用（C 系列）。

> 注意：`GameCropCatalog` 在 Gameplay（引用 Timberborn）→ L1 不能直接 new；把「表組裝/陣營決策」抽成 Core 純函式，Gameplay 只做轉接，是唯一符合 REQ-TEST-03 的作法。

### 6.7 需要 Emberpelts 實機（或至少讀其資料）才能驗證的項目

| # | 問題 | 怎麼驗 |
|---|---|---|
| V1 | Emberpelts 的 `FactionSpec.TemplateCollectionIds` 實際值（是否含 `Buildings.Folktails`/`Buildings.IronTeeth`） | 安裝後開它的存檔，或讀 `data/Factions/Faction.Emberpelts.blueprint.json` |
| V2 | 其 `MaterialCollectionIds`（是否含 `Folktails`）→ 決定「直接 append Folktails 藍圖」是否安全 | 同上；或看 Player.log 是否 `Material ... not found in repository` |
| V3 | 其 `GoodCollectionIds`（Log/Plank/MetalBlock 已有 Common 保證；確認新作物收穫貨物是否可用/可見） | 種 1 天、看收穫貨物是否入庫/可搬 |
| V4 | 其作物 `PlantableSpec.ResourceGroup` 是否為 `"Farmhouse"`；作物 id/收穫 id | 在遊戲內開 FarmHouse 作物清單，或看 HDBF `indoor.crop-spec-missing` 是否出現 |
| V5 | 建造選單是否真的沒有 HDBF（H1）→ 對照 `ModdedFactionsBuildingSupport` 安裝前後 | 開局截圖；若 H1 成立，兩次都不出現 |
| V6 | 若出現（H2）：是否 material crash、面板陣營顯示、作物下拉內容、就業、水 | Player.log 搜 `Material `、`Processing prefab BoxFarm`、`indoor.entity active`、`indoor.day`、`indoor.water` |
| V7 | 移除 Emberpelts 後存檔能否載入（遊戲層 `Faction not found`） | 刻意移除 mod 讀檔（在存檔副本上做） |
| V8 | `ModdedFactionsBuildingSupport` 的實際行為（collection id 注入方式） | 安裝該 mod + 任一自訂陣營，看 `Buildings.Support.ModdedFactions` 是否生效（本機未安裝） |

### 6.8 建議實機驗證回合（Lead 用；HDBF 部署後）

1. **基線（不改任何東西）**：安裝 Emberpelts 3346318229 + 3416879061（前置），開 Emberpelts 新局 → 開建造選單 → 預期：**沒有 HDBF**（H1）。只截圖，不改檔。
2. **資料驗證**：讀 Emberpelts 的 `Factions/Faction.Emberpelts.blueprint.json`，記錄 §6.7 V1/V2。
3. **套 M0（若 V2 == 含 Folktails）**：先只加 `TemplateCollection.Buildings.Emberpelts.HDBF.blueprint.json`（append 現有 Folktails 藍圖）+ optional faction patch → 開局看建築是否出現；place 一座 Basic → 看是否 material crash、是否能建造完成、面板顯示什麼。
4. **套 M0＋C1..C6**：重跑 3，確認陣營顯示、作物下拉、輪作（1 天）、存檔/讀檔、就業、水。
5. **M1 驗證**（可選）：裝中立藍圖/模型後，再跑一次；另找一個 clone 官方 collection 的自訂陣營（例如 TWDirtclaws 若可安裝）確認不重複。
6. **回歸**：官方 Folktails/IronTeeth 各跑 1 天，確認 8 份藍圖/面板/存檔/作物表完全沒變。
7. **卸載測試**（在存檔副本）：移除 Emberpelts 讀檔，確認 §2.7 的遊戲層失敗樣態，寫進玩家 FAQ。

---

## 7. 給玩家的回覆草稿與暫時繞過建議

**現況（HDBF 2.5.3）**：

* HDBF 只支援官方兩個陣營（Folktails / IronTeeth）。**Ember-pelts 是工作坊自訂陣營，目前不支援**：它的建造選單不會出現 HDBF 的四檔盒子農場（即使另裝 `ModdedFactionsBuildingSupport` 也一樣，因為 HDBF 沒有把自己的建築註冊到那個支援 collection）。
* 這是功能缺口（不是「支援了但壞掉」）；玩家回報的「doesn't work」與程式碼證據一致。
* **暫時沒有安全的繞過方式**：不要把手動複製 blueprint/collection 當解法——若 Emberpelts 沒有對應材質，可能會讓整個建造選單載入失敗；也不建議為此移除 HDBF（HDBF 在官方陣營仍正常）。
* 若要繼續玩 Emberpelts：HDBF 建築暫時無法使用；等支援版本。若曾把 Emberpelts 從存檔的 mod 清單移除，**該存檔會載入失敗（`Faction with id Emberpelts not found`）**，請保留 Emberpelts 安裝。
* 願意協助：請提供 Emberpelts 局開局後的 `Player.log`（尤其搜尋 `BoxFarm`/`Material`）＋建造選單截圖，可加速確認。

（英文版可直接用上方句子翻譯；重點：only Folktails/IronTeeth supported, custom factions not yet supported, no safe workaround, don't remove Emberpelts from a save that used it.）

---

## 8. 未來「所有自訂陣營 mod」的風險（設計面，必讀）

1. **每個自訂陣營都有自己的 collection id**（Emberpelts=`Buildings.Emberpelts`、ChooChoo=`ChooChoo.*`、TWDirtclaws=`Buildings.TWDirtclaws.*`…），而且**不保證繼承官方 collection**。任何只註冊在 `Buildings.Folktails/IronTeeth` 的建築，對所有自訂陣營都不可見（H1 會對**每一個**自訂陣營重演）。目前已知至少 6 個 mod 要為 Emberpelts 單獨出一份 collection 檔，就是這個結構性問題的證據。
2. **材質是第二道門檻**：跨陣營模型/材質會讓整個建造選單 crash（§2.4）；中立模型必須用 `MaterialCollection.Common` 的材質名。這代表「只用資料 append 現有藍圖」不是可規模化的通用解，只適合已驗證材質 collection 的特定陣營。
3. **支援 mod 依賴不可控**：`ModdedFactionsBuildingSupport` 的 `Buildings.Support.ModdedFactions` 是社群慣例（Conveyor Belts v2、Timber Pipes 使用），但它是第三方；HDBF 要嘛自己用 `ITemplateCollectionIdProvider`（官方擴充點，已證實），要嘛接受「玩家要裝另一個 mod 才可能看到」的低可見度。
4. **玩家會持續回報**：Workshop 上自訂陣營 mod 熱門（Emberpelts 28,790 訂閱），HDBF 是通用建築 mod；未來每個新自訂陣營（與每次陣營 mod 改版）都可能重開同一個 bug。建議在 Workshop 說明明列「支援官方兩陣營；自訂陣營未支援（未來可能以中性外觀支援）」，並在 UI/日誌加一次性診斷（§6.3 C6），降低誤報。
5. **測試/驗證器是自家 gate**：任何擴充都會被「剛好 8 份藍圖／2 陣營」的契約擋下（§3.3）；擴充前先改契約測試，否則 Lead 的 `build.ps1`/L1/validate 會紅。
6. **`FactionSpec` 是 record（ComponentSpec）**：不能直接改陣營資料；跨 mod 修改要用「同路徑 blueprint」或遊戲的 `#append`/`.optional` 機制（§2.8）。不要用 Harmony 改（HDBF 的 0-Harmony 約束）。
7. **存檔相容**：一旦引入中立 blueprint 名稱（`BoxFarm.<Tier>`）取代/並存舊的 `BoxFarm.<Tier>.<Faction>`，舊存檔的建築識別（template/blueprint path，本輪未驗證儲存細節）需要遷移策略；官方 `Path.blueprint` 用 `BackwardCompatibleTemplateNames: ["Path.Folktails","Path.IronTeeth"]` 承接舊名，是可參考的機制（需確認 Timberborn 對建築 template 名稱的存檔行為）。
8. **不要出貨「材質未驗證」的 per-faction append**：那是把「看不到」變成「整個建造選單 crash」的 P0 升級；寧可晚一版，先做中立模型或先驗證該陣營材質 collection。

---

## 9. 未解問題（需 Emberpelts 主模組／Lead 決策）

1. Emberpelts `FactionSpec` 的 `TemplateCollectionIds / MaterialCollectionIds / GoodCollectionIds` 實際值（§6.7 V1/V2/V3）。
2. Emberpelts 是否 clone/reuse 官方 collection（決定 H1 vs H2，以及 M1 守門是否需要）。
3. Emberpelts 作物的 `PlantableSpec.ResourceGroup` 是否 `"Farmhouse"`、作物/收穫 id 清單（§6.7 V4）。
4. Emberpelts 局的水來源：是否有水泵/水箱/水管鏈；Timber Pipes 是否為玩家常見組合（§6.7 V6/F-FACTION-10）。
5. `ModdedFactionsBuildingSupport` 的實作（本機未安裝）：是否真的把 `Buildings.Support.ModdedFactions` 注入所有自訂陣營（§2.9 推測）。
6. 存檔 schema 決策：`FactionId` 要不要升 `CurrentSchemaVersion`（`IndoorFarmRecord.CurrentSchemaVersion`）？升＝要寫遷移與舊檔測試；不升＝靠選填欄位與容忍讀取（需 Lead 決定；本檔建議「不升 schema、加選填欄位＋嚴格驗證」作為最小方案，完整支援再升）。
7. 官方藍圖 zip 是否包含 Emberpelts 時代新增的 spec 欄位？本機 1.1.2.8 的 `Blueprint.zip` 是 1,067 檔；Emberpelts 需要的 faction 欄位都已在（本輪已列）。
8. `TemplateSpec.BackwardCompatibleTemplateNames` 對「存檔中建築識別」的實際效果未驗證（若 M0 保留 8 份官方藍圖＋新增中立，可能不需；若未來替換，需實機）。
9. 玩家 saqlain141 的實際症狀（選單沒有 vs 有但壞）與 Player.log 尚未取得；建議 Lead 回覆時索取（§7）。

---

## 附錄 A：證據可重現指令（皆不寫 repo、不跑 build.ps1）

```powershell
# 0) 準備臨時 Mono.Cecil dumper（與 tools/ApiIndex 同一個本地 NuGet 快取；輸出到 %TEMP%）
$probe = Join-Path $env:TEMP 'hdbf-probe'; New-Item -ItemType Directory -Force $probe | Out-Null
# （本次使用自製 Probe.csproj + Program.cs：types/il/refs/strings 四種模式，Mono.Cecil 0.11.5）

$m = 'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed'
dotnet "$probe\bin\Release\net9.0\Probe.dll" il $m "$env:TEMP\out.txt" `
  '^Timberborn\.TemplateCollectionSystem\.TemplateCollectionService$' 'Load'
dotnet "$probe\bin\Release\net9.0\Probe.dll" il $m "$env:TEMP\out2.txt" `
  '^Timberborn\.GameFactionSystem\.FactionTemplateCollectionIdProvider$' '.*'
dotnet "$probe\bin\Release\net9.0\Probe.dll" il $m "$env:TEMP\out3.txt" `
  '^Timberborn\.TimbermeshMaterials\.MaterialRepository$' 'GetMaterial'
dotnet "$probe\bin\Release\net9.0\Probe.dll" il $m "$env:TEMP\out4.txt" `
  '^Timberborn\.FactionSystem\.FactionSpecService$' 'GetFaction'
# 也可用 repo 內既有工具（需自行 build 到 %TEMP%）：tools/ApiIndex il <managed> <out> <typePattern> <memberPattern>
```

```powershell
# 1) 官方藍圖（1.1.2.8）— 唯讀 zip
python - <<'PY'
import zipfile
p = r'C:\Program Files (x86)\Steam\steamapps\common\Timberborn\Timberborn_Data\StreamingAssets\Modding\Blueprints.zip'
z = zipfile.ZipFile(p)
for n in ['Factions/Faction.Folktails.blueprint.json',
          'TemplateCollections/TemplateCollection.Buildings.Common.blueprint.json',
          'MaterialCollections/MaterialCollection.Common.blueprint.json',
          'GoodCollections/GoodCollection.Common.blueprint.json']:
    print('='*8, n); print(z.read(n).decode('utf-8-sig'))
PY
```

```powershell
# 2) HDBF 模型材質名（解壓 .timbermesh 抽 UTF-8 字串）
python - <<'PY'
import zlib, re
for p in [r'data\Buildings\Food\BoxFarm\BoxFarm.Basic.Folktails.Model.timbermesh',
          r'data\Buildings\Food\BoxFarm\BoxFarm.Basic.IronTeeth.Model.timbermesh',
          r'data\Buildings\Food\BoxFarm\BoxFarm.Advanced.Folktails.Model.timbermesh']:
    raw = zlib.decompress(open(p,'rb').read())
    names = [m.group().decode('ascii') for m in re.finditer(rb'[A-Za-z_][A-Za-z0-9_.]{3,}', raw)]
    print(p, sorted({n for n in names if any(k in n for k in ('.Folktails','.IronTeeth','Wood','Metal','Roof','Details','Windows','Moss'))}))
PY
```

```powershell
# 3) 本機工作坊檔案索引（唯讀）
Get-ChildItem 'C:\Program Files (x86)\Steam\steamapps\workshop\content\1062090' -Recurse -Filter '*Emberpelts*' |
  Select-Object -ExpandProperty FullName
# 重點：3282276626（Path Extention，OptionalMods: Emberpelts）、3721718127（TWTradeOffice，append Folktails 藍圖到 Buildings.Emberpelts）、
#       3431421822 / 3345889146 / 3813587159 / 3765935439（per-faction collection）、
#       3723216060（More Mines：自訂陣營 FactionSpec + clone spec 實例）、
#       3815376360 / 3801726769（Buildings.Support.ModdedFactions 慣例）
```

## 附錄 B：本機工作坊證據檔案索引（節錄）

| 檔案 | 證明了什麼 |
|---|---|
| `3416879061/version-1.1/manifest.json` + `Readme.txt` | 本機的 3416879061 是 **Bobingabout Script Pack**（前置），不是 Emberpelts 主模組（Lead 線索 #1 正確） |
| `3723216060/version-1.1.0.2/Factions/Faction.TWDirtClaws.blueprint.json` | 自訂陣營 `TWDirtclaws` 的 FactionSpec 實例：自有 TemplateCollectionIds、`MaterialCollectionIds:["Folktails","IronTeeth"]`、clone spec |
| `3723216060/version-1.1.0.2/Factions/Faction.Folktails.blueprint.json` | `TemplateCollectionIds#append` 跨 mod 修改官方陣營的先例 |
| `3721718127/version-1.1.0.0/TemplateCollections/others/TemplateCollection.Buildings.Emberpelts.TWTradeOffice.blueprint.json` | `Buildings.Emberpelts` collection id；把 Folktails 材質模型藍圖 append 給 Emberpelts 的先例 |
| `3431421822/.../TemplateCollection.Buildings.Emberpelts.blueprint.json`、`3345889146/...`、`3813587159/...`、`3765935439/...` | 多個 mod 需要 per-faction collection 才能支援 Emberpelts（H1 的結構性證據） |
| `3282276626/version-1.1.0/manifest.json` | `OptionalMods: [{Id:"Emberpelts"}, {Id:"BobPlatforms"}]` 的官方格式先例 |
| `3815376360/version-1.1/manifest.json` + `.../Conveyors.ModdedFactions.blueprint.json`、`3801726769/.../Pipes.ModdedFactions.blueprint.json` | `ModdedFactionsBuildingSupport`／`Buildings.Support.ModdedFactions` 慣例（該 mod 本機未安裝） |
| `3277416566/version-0.6/Specifications/Factions/FactionSpecification.Emberpelts.optional.json` | 舊版（U0.6）mod 曾用 `optional` faction patch 服務 Emberpelts（歷史先例） |

## 附錄 C：與其他回報的交叉引用

* `docs/workstreams/feedback-20261102/latent-future.md:29` 已註明「Ember-pelts 自訂陣營 → 屬另一調查」；本檔補上。
* 同檔 `:307` 已指出 `BuildCropChoices` 用 `FallbackCropCatalog` 的問題——本檔把它升級為自訂陣營的 P1 bug（F-FACTION-03），並補「UI 只能拿 snapshot」的分層修法。
* `docs/workstreams/compat-01-adapters.md` §9.8 已指出 manifest `OptionalMods` 格式議題（F-FACTION-09 引用）。
* `docs/VERIFICATION.md` §9、`docs/API_FACTS.md` §8 是材質 crash 的既有實機證據（F-FACTION-02/H2-a 引用）。
* 主模組缺失與前置 3416879061 的判定，與 Lead 2026-11-02 補充線索 #1 一致。
