# Brief: Workshop growth for *High-Density Box Farm* (item 3828809784)

Data read **2026-10-28** on app **1062090** (Timberborn; Update 1.1 live on main since 2026-09-03). Claims are tagged
**VERIFIED** (URL given, I read that page) or **INFERENCE** (my judgement). §10 lists what I could not verify from this
machine — nothing is invented to fill a gap.

## 0. Where the listing stands (VERIFIED — <https://steamcommunity.com/sharedfiles/filedetails/?id=3828809784>)

| field | value |
|---|---|
| Title | `High-Density Box Farm` (20 chars) |
| Unique visitors / subscribers / favorites | **16 / 5 / 0** |
| Ratings | **"Not enough ratings"** (no stars, no count) |
| Comments / discussions / change notes | **0 / 0 / 1** (publish only) |
| File size, posted | 2.020 MB, 28 Oct @ 4:33am |
| Tags | Mod compatibility `Update 1.1` · Mod type `New content` · Mod content `Buildings` |
| Description | one plain paragraph, **no images, headings, links or how-to** |

5 subs ÷ 16 visitors = **31% conversion**, normal-to-good (§3) — the problem is volume and social proof (zero ratings,
zero comments, one image), not the pitch.

## 1. How Workshop discovery works for this app (VERIFIED)

### 1.1 Surfaces and sorts
- **Workshop hub** (<https://steamcommunity.com/app/1062090/workshop/>): rails *Items "In the past week"*, then tabs
  **Most Popular · Most Subscribed · Last Updated · New · Best rated in the past three months**, plus a
  *"Most Required by Other Items"* rail.
- **Browse** (<https://steamcommunity.com/workshop/browse/?appid=1062090>): default sort label is **"Most Popular
  (One Week)"** — a *trend* window, not recency. Confirmed labels: `browsesort=trend` → *Most Popular (One Week)*,
  `mostrecent` → *Most Recent*, `toprated` → *Top Rated All Time*. Pool: **8,646 items**; **Map 7,723 · Mod 891**.
- **Consequence (VERIFIED + INFERENCE):** publishing earns no front-page slot; a new item leads only *New* /
  *Last Updated* and unfiltered *Most Recent*. Everything else needs subs, favorites, ratings and comments — exactly
  what HDBF lacks. First lever: the page people land on, not exposure.

### 1.2 Exhaustive tag taxonomy for app 1062090 (VERIFIED — read off the browse filter panel)
- Categories: `Map` · `Mod`
- **Mod compatibility:** `Update 1.1` · `Update 1.0` · `Update 0.7` · `Update 0.6`
- **Mod type:** `New content` · `Quality of life` · `Balance` · `Cheats` · `Visuals` · `Modding tools` · `Other`
- **Mod content:** `Buildings` · `Plants` · `Goods` · `Factions` · `Outfits` · `Decals` · `Translations` · `Miscellaneous`

Hub counts: Mod 891; Update 1.1 **410**, Update 1.0 581 (tags overlap, so they exceed 891); New content 271,
Quality of life 266, Balance 76, Cheats 35, Visuals 111, Modding tools 59, Other 98; Buildings **261**, Plants 33,
Goods 68, Factions 36, Outfits 9, Decals 44, Translations 22, Miscellaneous 209. Our exact niche — `Mod`+`Update
1.1`+`New content`+`Buildings` — is **121 items**
(<https://steamcommunity.com/workshop/browse/?appid=1062090&browsesort=mostrecent&section=readytouseitems&requiredtags%5B%5D=Mod&requiredtags%5B%5D=Update+1.1&requiredtags%5B%5D=New+content&requiredtags%5B%5D=Buildings>),
and HDBF is **#1 there under Most Recent** — the only free placement we own, and it decays on the next publish.

### 1.3 Signals you can influence
- Subscribers, favorites and unique visitors are printed on every item page and feed *Most Subscribed* / *Most
  Popular* (VERIFIED, §0 page).
- **Ratings** gate *Top Rated All Time* and *Best rated in the past three months*; below a threshold the page says
  "Not enough ratings" (VERIFIED). Threshold and the act of rating are undocumented (§10); empirically ratings are
  not automatic — *Growth Visuals* has 31 subscribers and 0 ratings
  (<https://steamcommunity.com/sharedfiles/filedetails/?id=3828086979>).
- **Trend is time-windowed**, not lifetime: the Workshop API exposes `SetRankedByTrendDays` for ranking "over a
  number of days" (<https://partner.steamgames.com/doc/features/workshop/implementation>). A burst of subs in days
  1–7 is worth more than the same total spread over a month.
- **Search matches title *or* description** — `ISteamUGC::SetSearchText` "sets a string that items need to match in
  either the title or the description" (same Steamworks page). Most actionable technical fact here. Comments are not
  a ranking input I could verify, but they are what a visitor reads before subscribing (INFERENCE; every successful
  item has 17–378, VERIFIED in §3).

### 1.4 Steam's own guidance
There is **no Steam-authored guide for mod authors on writing an item page** — I read
<https://partner.steamgames.com/doc/features/workshop>, `/implementation` and `/tags`; all three are developer
integration docs (VERIFIED negative). Usable rules: title, description, tags, visibility and preview image are all
editable after publish (`SetItemTitle`, `SetItemDescription`, `SetItemTags`, `SetItemPreview`); tags "cannot be
deleted once they have been added", so a wrong tag is permanent (<https://partner.steamgames.com/doc/features/workshop/tags>);
items stay hidden until the author accepts the Workshop Legal Agreement (HDBF is visible, so done); community text
supports BBCode and inline images (VERIFIED empirically — *New Building Styles* renders imgur images inline).

## 2. Tag audit — are `[Update 1.1, New content, Buildings]` optimal? (VERIFIED against §1.2)

**Verdict: correct, close to optimal; add nothing speculative.**
- `Update 1.1` is the filter 410 of 891 mods carry and the one current players use. `New content`+`Buildings` is
  literally the taxonomy for a new building set. Right.
- Do **not** add `Update 1.0` (581 items) unless the mod is tested on 1.0.13.1: mis-tagged mods generate "doesn't
  work on my version" comments (INFERENCE) and tags are permanent (VERIFIED).
- There is **no `QoL`, `Map`, `Mod`, `Factions` or `Update 1.1 only` tag value** beyond §1.2 — `Mod`/`Map` are
  categories, and `Factions` (36) would be wrong because HDBF supports both factions rather than being a faction mod.
- Only defensible additions, `Miscellaneous` (209) or `Goods` (68), both mis-describe the item.
  **Recommendation: keep the current three.**

## 3. What top Timberborn items do (VERIFIED, 6 item pages read 2026-10-28)

| item | visitors | subs | fav | ratings | comments | conv. |
|---|---|---|---|---|---|---|
| [Ladder](https://steamcommunity.com/sharedfiles/filedetails/?id=3286476486) (Tobbert) | 160,123 | 125,398 | 4,076 | 1,638 (5★) | 378 | 78% |
| [Riverborne: Raft Logistics](https://steamcommunity.com/sharedfiles/filedetails/?id=3669575969) (eMka) | 53,083 | 13,393 | 951 | 301 (5★) | 112 | 25% |
| [New Building Styles](https://steamcommunity.com/sharedfiles/filedetails/?id=3723883597) (REDIC) | 27,605 | 11,040 | 611 | 346 (5★) | 76 | 40% |
| [Mod Manager](https://steamcommunity.com/sharedfiles/filedetails/?id=3286370025) (Tobbert/normanr) | 19,244 | 6,091 | 393 | 96 (4★) | 17 | 32% |
| [Growth Visuals](https://steamcommunity.com/sharedfiles/filedetails/?id=3828086979) | 84 | 31 | 2 | 0 | 0 | 37% |
| **High-Density Box Farm** | **16** | **5** | **0** | **—** | **0** | **31%** |

Conversion = subs ÷ visitors; **healthy items run 25–40%**, so 31% already says the pitch is acceptable. Ten times
the traffic at this ratio is only ~50 subs — the page must improve *before* the push.

Repeatable conventions:

| # | convention | evidence |
|---|---|---|
| 1 | title = feature name, 1–4 words | `Ladder`, `Mod Manager`, `Flywheels`; bilingual e.g. `机器人不会损坏 / The robot is indestructible` |
| 2 | first line = one-sentence promise, then a list | *Riverborne* names 3 buildings; *Ladder* uses `How to use` / `Problems?` / `Links` |
| 3 | long descriptions answer objections | *Mod Manager* = troubleshooting FAQ first, features second; 1,500–2,500 chars typical |
| 4 | caveats up front | "Technical Stuff" block (*New Building Styles*); employment disclaimer (*Riverborne*) |
| 5 | inline BBCode images + one 16:9 preview | preview renders 637×358 on page, **322×181 in browse grids** (VERIFIED) |
| 6 | support/compat signals are part of the pitch | source link, mod.io mirror, "Requires X" (*Growth Visuals*) |
| 7 | favorites ≈ 3–7% of subs, ratings ≈ 1–3% | ask inside the mod's own UI, not only on the Workshop page |

### Our page: rewrite shortlist (INFERENCE, grounded above)
- Title: keep the name, add a searchable qualifier — `High-Density Box Farm — 3x3x1 Indoor Stackable Farm`.
- First two lines: "A 3×3×1 farm your beavers work *inside*: 60→600 crop slots per building, flat stackable roof,
  water from a pipe or a manual button. **No Harmony dependency — survives game updates.**" Dependency-free is a
  real differentiator: Harmony and Mod Settings lead the hub's "Most Required" rail (VERIFIED).
- Then four blocks: **What you get** (4 tiers, slots, workers, 150 power on tier 4) → **How to use** (unlock path,
  pipe vs water button, plank/metal maintenance) → **Compatibility** (Update 1.1, Folktails + IronTeeth, no
  dependencies, save-safe) → **Feedback** (item discussions + Discord thread).
- 3–5 in-game 16:9 screenshots; first one showing a *stack* of farms in a real colony with the roof legible; add a
  10–20 s WebM/GIF if cheap. Mention Chinese support if in-game text is localised (README claims ~147 entries — §4).

## 4. Real venues (VERIFIED where a URL is given)

- **Official Discord** — <https://discord.gg/timberborn>, server 558398674389172225
  (<https://discord.com/servers/timberborn-558398674389172225>). Mod channels verified from the official wiki
  (<https://timberborn.wiki.gg/wiki/Mods>): `#mod-announcements` (1379436290662993920), `#mod-users`
  (1064824959697944666), `#mod-creators` (888491376143134760), `#individual-mods` (1064825134348763209).
  `#individual-mods` is the per-mod thread channel: one thread, screenshots, answer there, no daily reposts.
  Channel permissions are INFERENCE — I could not read them without joining.
- **r/Timberborn** — <https://www.reddit.com/r/Timberborn/>, the official subreddit (linked from the devs' pinned
  "Useful links" post and from Mechanistry news). ≈**46k members**; flairs include `Settlement showcase`, `Modding`,
  `Custom map`, `News` (third-party stats <https://gummysearch.com/r/Timberborn/>, synced Jan 2025 — stale).
  **Rules unreadable from this machine** (Reddit returns 403, VERIFIED) — read the sidebar first; one post, not a
  campaign (INFERENCE).
- **Steam forums** — General Discussions (5,535 threads) and subforums
  (<https://steamcommunity.com/app/1062090/discussions/>), with the site-wide *Discussions Rules and Guidelines*.
  **Workshop Discussions** is the right place for feedback/show-up questions
  (`https://steamcommunity.com/workshop/discussions/18446744073709551615/?appid=1062090`). Devs' pinned **"Useful
  links"** thread: <https://steamcommunity.com/app/1062090/discussions/0/6252725698192055669/> (partly stale — it
  still says enabling Steam Workshop is "on our wishlist").
- **Official mod competition / showcases (highest leverage, zero cost).** Mod Competition announced 2026-04-23,
  **77 submissions**, winners 2026-06-05: 750 EUR + figurines + in-game credits + a Discord rank, 250 EUR runners-up,
  7 honourable mentions. Key quote: *"The number of mods published in the contest period was higher but sadly, not
  everyone submitted their work via the entry form"* — **the entry form is mandatory**
  (<https://store.steampowered.com/news/app/1062090/view/706647944382644992>). Calendar:
  <https://mechanistry.com/contests> (latest: 1.1 Build-a-Map Contest 2026-10-08, EUR 3000, **maps**; Golden Log
  Awards 2025). No mod contest scheduled as of 2026-10-28 — watch that page and Discord. Devs showcase mods on their
  own channels (a top-item comment: "the devs shared this on the timberborn YouTube", VERIFIED): YouTube
  <https://www.youtube.com/@Timberborn>, Twitch <https://www.twitch.tv/mechanistry>, X `@Timberborn`; press/creator
  route <https://mechanistry.com/press>.
- **mod.io mirror** — <https://mod.io/g/timberborn>, the official second platform (linked from mechanistry.com and
  the wiki); the in-game mod manager reads it through the *Mod Manager* mod. Top items mirror there and link it from
  the Workshop description (VERIFIED on *Ladder*, *Mod Manager*).
- **Chinese-language communities.** The Workshop itself carries Chinese items in the newest 30 (`机器人不会损坏 /
  The robot is indestructible`, `砍伐时掉落副产物/Tree Harvest Byproduct & Banner Control`, `Choo! Choo! Passenger
  Carriages 客车追加版`) — VERIFIED, so bilingual titles are normal. Elsewhere: 百度贴吧 `timberborn吧`
  <https://tieba.baidu.com/f?kw=timberborn> (exists, search-index evidence); bilibili has a standing 创意工坊模组
  介绍/详解 series, e.g. <https://www.bilibili.com/video/BV1SrenzyEJg/> and
  <https://www.bilibili.com/video/BV1HaxRzgEjU/> (captcha-blocked from here); 巴哈姆特 mod thread
  <https://forum.gamer.com.tw/C.php?bsn=70529&snA=57> (search-index evidence). **QQ groups: NOT VERIFIED** — no
  public checkable URL found; never claim one in copy.
- **Content creators.** Mechanistry runs a "Press & Content Creators" page
  (<https://mechanistry.com/press>); a third-party creator index exists at
  <https://www.useairhorn.com/games/timberborn> (JS-rendered, unreadable here). Practical route: find Timberborn
  videos/streams from the last 60 days that already feature mods, pitch **one specific feature**.

## 5. What NOT to do

- **Do not ask for ratings/favorites as a favour.** It sits squarely against Steam's conduct rules and the Workshop
  Legal Agreement; enforcement risk is asymmetric (INFERENCE on enforcement, certain on downside).
- **Do not blast the same announcement across Discord + Reddit + Steam within minutes.** One post per venue per
  release, written for that venue (INFERENCE; moderation is active — a "Timberborn Discord Ban" thread sits on the
  Steam front page today, VERIFIED).
- **Do not add tags you cannot defend** — tags are permanent (§2).
- **Do not publish a second item to "relaunch"** — that splits the only signal accumulated.
- **Do not promise a roadmap you will not ship, or cite unverified Chinese venues (QQ)** — both live forever (§4).

## 6. First 72 hours — ordered by impact ÷ effort

| # | action | effort | why (evidence) |
|---|---|---|---|
| 1 | Rewrite the item page: title suffix, 4-block description, 3–5 16:9 screenshots, screenshot-first preview | 2–3 h | the only asset we control; 25–40% conversion is achievable vs 31% today (§3) |
| 2 | Put "no Harmony / no Mod Settings dependency" in sentence 1, add a Compatibility block | 20 min | Harmony + Mod Settings lead the "Most Required" rail (VERIFIED) |
| 3 | Open the item's Discussion tab with "known issues / what's next", reply to every comment same day | 30 min | 0 comments today; every successful item has them (§3) |
| 4 | Discord `#individual-mods` thread: 2 screenshots, one paragraph, link; stay to answer | 45 min | largest live community, channel exists for this (§4) |
| 5 | Mirror the same build + copy to mod.io, link it from the description | 1 h | official second platform; top items all mirror (§4) |
| 6 | One r/Timberborn post, `Modding` flair, in-game screenshot + concrete numbers | 30 min | 46k-member official subreddit with a Modding flair (§4) |
| 7 | Chinese mirror post (bilingual title, 贴吧/B站-friendly) — only with a native speaker to answer replies | 2–4 h | bilingual titles already normal on this Workshop (§4) |
| 8 | Creator outreach: 5–10 targeted messages, one feature each, via press page / comments | 1–2 h | devs and creators do showcase mods (§4) |
| 9 | Check the contest page + Discord weekly; pre-write the entry blurb now; publish a changelog within 72 h | 30 min | 77 entries last time, entry form mandatory (§4); changelog bumps *Last Updated* |

## 7. 14-day cadence

- **D0:** checklist #1–#4. Write down the §0 numbers *before* editing anything.
- **D1–D2:** #5, #6, answer comments hourly; #7 (only if a native helper exists); #8 first creator wave (5).
- **D3:** measure 72 h KPIs (§8). If subs/visitor < 15%, the page is the problem — iterate first two lines and the
  preview image, do not add promotion.
- **D4–D7:** ship one player-requested visible improvement with a changelog + one Discord follow-up; second creator
  wave; reply everywhere; **no new Reddit/Discord threads**.
- **D8–D10:** one deeper artefact (layout guide image, save file, "optimal box farm" screenshot) posted to the item
  description + Discord thread — that is the shareable unit.
- **D11–D14:** retro per venue; add the most-asked question as an FAQ block; check the contest calendar; plan the
  next update.

## 8. KPIs (baseline 16 visitors / 5 subs / 0 fav / 0 ratings, 2026-10-28)

Read the item page directly (it prints unique visitors, current subscribers, current favorites, ratings count,
comments) plus Change Notes and the Discussion tab. **Steam exposes no referrer data**, so attribute by timestamping
each post and reading daily deltas (INFERENCE on method only).

| metric | 72 h | 7 d | 14 d |
|---|---|---|---|
| Unique visitors | ≥ 300 | ≥ 1,200 | ≥ 2,500 |
| Current subscribers | ≥ 60 | ≥ 250 | ≥ 500 |
| subs ÷ visitors | ≥ 20% | ≥ 20% | ≥ 20% |
| Favorites | ≥ 10 | ≥ 40 | ≥ 80 |
| Ratings | ≥ 3 | ≥ 10 | ≥ 15 |
| Comments | ≥ 3 | ≥ 8 | ≥ 15 |
| External threads (Discord/Reddit/mod.io) | ≥ 3 | ≥ 4 | ≥ 5 |

Secondary: favorites ÷ subs 3–7% (benchmark, §3); comments per 100 subs ≥ 1; every comment answered < 24 h; ≥ 1
changelog per week. Flat 20% conversion with rising visitors = keep pushing traffic; falling conversion = fix the page;
visitors flat after a venue post = that venue did not work, drop it and say so.

## 9. Keyword list (search covers title **or** description, §1.3)

**English** — for title, first paragraph, and a short "Keywords" tail: high-density farm · box farm · indoor farm ·
vertical farm · stackable farm · 3x3x1 farm · compact farm · crop farm · efficient farm · 600 crops · auto planting ·
auto harvesting · water tank · irrigation · maintenance · drought food · Folktails · Iron Teeth · IronTeeth · no
Harmony · no dependencies · Update 1.1 · high density agriculture

**Chinese** — for a bilingual title/description: 高密度农场 · 盒子农场 · 箱式农场 · 室内农场 · 立体农场 · 堆叠农场 ·
3x3x1 · 紧凑农场 · 高效农业 · 自动播种 · 自动收割 · 水箱 · 灌溉 · 维护 · 旱季粮食 · 海狸浮生记 · 铁牙 · 民间派系 ·
无需前置 · 无依赖 · 版本1.1

Do not keyword-stuff: one natural sentence with 5–6 of these beats a dump (INFERENCE; the description converts, §3).

## 10. What I could not verify — do not build copy on these

1. **How a Workshop rating is cast and the "enough ratings" threshold** — no Valve documentation found; only the
   empirical fact that ratings are not automatic (§1.3). Never ask for ratings regardless (§5).
2. **r/Timberborn posting rules** (Reddit 403s here), **mod.io upload rules** (JS page returned only its title) and
   **Discord channel permissions** (wiki lists channels/IDs, not who may post) — none readable from this machine.
3. **QQ groups or other Chinese chat communities** — no public verifiable URL; do not claim one.
4. **The current preview image's legibility** — no image-reading capability here, so "make it read at 322×181" is a
   convention argument (§3), not a review of our art.
5. **HDBF's real `Update 1.0` compatibility** — unknown, the only reason not to add that tag (§2); an `old-1.0` test
   would settle it.

*Workshop counts change daily; re-read §0 before quoting any number externally.*
