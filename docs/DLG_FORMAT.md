# `.dlg` dialogue script — format reference

[中文](DLG_FORMAT.zh-CN.md)

One trader = one `BepInEx/config/VisitAPI/<traderId>.dlg` file (UTF-8). **The file name must equal the 24-hex trader id.** VisitAPI auto-discovers every `.dlg` in that folder on startup, registers the trader, and gives it an out-of-raid **Talk** button (see below). Parse errors are written to the BepInEx log with line numbers (bilingual, following the game language). `.dlg` is edited at runtime — no rebuild; just restart the game.

---

## Header (before the first node)

```
trader: <24-hex traderId> "Display Name"
start: <repeat-visit root node>                  # default: root
first: <first-meeting node>                       # optional
actor: <npc model name>                           # the 3D-scene dialogue actor (see "3D scene dialogue")
scene: <bundle file or 24-hex trader id>          # 3D visit room (see "3D visit scene")
when: level>=4 -> <node>                          # pick the root by level / standing (see below)
when: level>=3, standing>=0.05 -> <node>          # conditions can be comma-joined (all must hold)
trigger: raid <map> (x, y, z) [door WxH[xRot]] [dist 3] [radius 1.2] [once|repeat] "prompt"
trigger: hideout <area> (x, y, z) [node <node>] [dist 3] [if quest=status[/status]] [free] "prompt"
trigger: npc <map> <name> [node <node>] [dist 3.5] [radius 0.8] [if quest=status[/status]] "prompt"   # experimental
trigger: item <map> (x, y, z) tpl <templateId> accept <quest> [rot <deg>] "pickup notification"        # experimental
quest <alias> = <24-hex questId>                  # quest alias; use <alias> anywhere a quest id is expected
```

- `trader:` must be the trader id (matching the file name); the quoted name is optional/cosmetic.
- **`start:`** is the normal repeat-visit root (default node name `root`). **`first:`** is the one-time first-meeting node — played once in raid/hideout (tracked per trader + profile); **the out-of-raid Talk button always skips `first:` and enters the root family directly.**
- **`when:`** selects which root the player lands on, by `level>=N` / `level<=N` / `standing>=N` / `standing<=N` (comma-join several — all must hold). The **first matching line in file order wins**; if none match, `start:` is used. `level` = player level, `standing` = your rep with this trader. This applies to the Talk button **and** the raid/hideout entries.
- **`quest <alias> = <id>`** defines a short alias; you can then write the alias wherever a quest id is expected (`accept:`, `if:`, trigger `if`, …). Can appear on any header line.

### Triggers

A `trigger:` line places an in-world "Visit" interaction. Only the **type** and the **map / area** are required; the rest is order-free.

**`trigger: raid <map> (x,y,z) …`**
- `<map>` is matched (substring, case-insensitive) against the raid's location id. Common ids: `factory4_day` / `factory4_night`, `bigmap` (Customs), `Woods`, `Shoreline`, `Interchange`, `RezervBase` (Reserve), `Lighthouse`, `TarkovStreets` (Streets), `Sandbox` / `Sandbox_high` (Ground Zero), `laboratory` (Labs).
- `(x,y,z)` = required world coordinates. Stand on the spot and press **F11** — the BepInEx log prints a ready value.
- `door WxH[xRot]` = door-style hit box (width × height × optional Y-rotation). `dist N` = max distance (default 3). `radius N` = look-cone hit radius (default 1.2). `once` (default — disappears after the first visit) / `repeat` (every raid).
- **Multiple `trigger: raid` lines = multiple points** (different maps, or several spots on one map), tracked independently.

**`trigger: hideout <area> (x,y,z) …`** (spawns when the location is the hideout)
- `(x,y,z)` = absolute coordinates (F11 to grab them). `<area>` is informational in this version (positioning is by coordinates).
- `node <node>` = open the dialogue straight at that node (e.g. resume a questline mid-story). `dist N` = max distance. `if quest=status[/status]` = show **only** while the quest is in one of those states (and hide otherwise).
- `free` (or `door`) = free-standing look-to-interact: for an open spot with no native area menu (the trigger requires you to *look* at the point). Without `free`, the Visit action is **merged into** the native area menu (e.g. the Intelligence Center menu).
- Two `trigger: hideout` lines at the **same coordinates with different `if` gates** = the "accept the next task here / come back to hand it in here" pattern (the gates are mutually exclusive, so only one shows at a time).

**`trigger: npc <map> <name> …`** (experimental — crosshair-on-NPC interaction)
- The interaction follows a **live NPC model** instead of a fixed point: `<name>` is matched (case-insensitive substring) against spawned bots' **nicknames** first, then looked up as an exact scene **GameObject name** (a model placed by your trader mod). The name must contain **no spaces**.
- Aiming the **crosshair** at the model (default hit radius 0.8, distance 3.5) pops the interaction prompt; `node` / `dist` / `radius` / `if` mean the same as on hideout triggers.
- Spawning/placing the NPC model itself is your mod's job — VisitAPI only adds the look-to-interact.

**`trigger: item <map> (x,y,z) tpl <templateId> accept <quest> …`** (experimental — collectible intel item, Tarkov-1.0 style)
- Spawns a **real, natively lootable item** at the coordinates (via the game's own `GameWorld.SetupItem` — pickup, inventory and found-in-raid are all vanilla behaviour). `tpl` = an item template id (a server-side cloned custom item works too).
- The moment the player **picks it up**: the `accept` quest is accepted + a toast notification shows (the quoted text; omit it for the built-in bilingual default).
- The item only spawns while the quest is **not yet accepted** (status unknown/Locked/AvailableForStart) — once collected and the quest is running, it stops appearing. `rot <deg>` = optional Y-axis facing.

---

## Nodes

```
<nodeName> bg: backgroundFile | anim: animState
> narration line [| bg: backgroundFile] [| anim: animState]   # subtitle box, click to advance; trailing suffixes swap the bg / play an animation
NPC spoken line
- option text -> target
- option text -> target | directive, directive...
-> target                                          # node-level auto-jump (for pure-narration nodes)
```

- **Node name** allows only letters / digits / `_` / `.` / `-` (so `[action]` / `<rich text>` inside a line is never mistaken for a node header).
- **`bg:`** a plain file name is looked up under `config/VisitAPI/backgrounds/`; a value containing `/` or `\` is used as a path relative to `config/VisitAPI/`. Omit `bg:` to keep the previous background. Supported: **images** `.png .jpg .jpeg` and **looping video** `.mp4 .webm .m4v .mov` (muted by default).
- **`>` narration** plays in the native subtitle box, one line at a time, click to advance; then the NPC line + options render. A node with only `>` lines and a `-> target` auto-continues to that node when narration ends.
- **Per-line background**: append `| bg: file` to a `>` line (e.g. `> She pushed the door open and stepped in. | bg: Standoff.png`) to switch the background when that line is shown; lines without `| bg:` keep the previous background. The node-header `bg:` is the initial background on entry, and per-line `| bg:` swaps it as narration advances. File-name resolution is the same as `bg:`.
- **Per-line animation (3D scene dialogue)**: see the next section. `| bg:` and `| anim:` can both appear on one line, in any order.

---

## 3D scene dialogue (Lightkeeper-style, one animation per line)

The native Lightkeeper visit = no CG background (you see him in 3D through the dialogue box) and one model animation per spoken line. VisitAPI reproduces the same mechanism:

```
actor: SORA_Model                 # header: the in-world NPC model (a bot nickname or a scene GameObject name)
<C1> anim: Greeting               # play an animation when the node renders
> She looks up at you. | anim: LookUp    # each narration line can carry its own animation — one line, one animation
> "You came." | anim: Nod
```

- **Omit `bg:` and you have a 3D scene** — the dialogue box floats over the game world, with your NPC model in view (exactly how the native Lightkeeper dialogue looks).
- `actor:` resolves the same way as a `trigger: npc` name: bot nickname first, then a scene GameObject.
- `anim: <state>` plays a **state of the model's Animator** (`CrossFadeInFixedTime`, 0.25 s blend). The state name = a state in your AnimatorController. The model and its AnimatorController ship in your trader mod's asset bundle — any standard Unity Animator works; no EFT SequenceReader assets needed.
- Pairs naturally with `trigger: npc`: aim at the model to start the dialogue, and every line drives its animation.
- **NPC spoken line** = any plain line. If a node has several plain lines, the **last** one is shown as the NPC's text (use `>` narration for multi-line lead-ins).
- **`- option text -> target`** = a clickable option. **target**: a node name / `@start` (the root) / `@trade` / `@tasks` / `@services` (open that tab of the out-of-raid trade screen) / `@close` (or `@leave`). **Omitting `-> target` closes the dialogue** after running the option's directives.
- Text supports `{player}` / `{playerName}` (replaced with the character nickname).
- Lines starting with `#` or `//` are comments; blank lines are free.

> `@trade` / `@tasks` / `@services` only work **out of raid** (the dialogue opened from the Talk button sits on top of the trade screen). In raid they no-op (the option stays on the node).

---

## 3D visit scene (`scene:`, retail-1.0-visit style)

`scene:` makes the trade-screen entry button open the dialogue inside a **standalone 3D room** (the retail vendor-visit look): your scene bundle loads, the camera cuts to the room's anchor, the menu backdrop hides, and the dialogue box floats over the scene; closing the dialogue tears it all back down.

```
scene: my_room.bundle            # a bundle file under config/VisitAPI/scenes/
scene: 54cb50c76803fa8b248b4571  # or: borrow a retail vendor's visit room (needs the scene asset pack)
```

- **Trade-screen entry only**: dialogues opened by raid / hideout triggers keep their current presentation (flat background / in-world).
- `scene:` and `bg:` are **mutually exclusive** — while the scene is up every `bg:` is ignored (the scene IS the backdrop).
- `anim:` directives automatically drive the **trader model inside the scene** (no `actor:` needed); see below for how the model is found.
- **Custom-scene convention**: pack one scene in the bundle (bring your own geometry + lights), add an empty node named **`Position_Camera`** as the camera anchor, and give the trader-model node an **Animator** (a name containing `Trader`/`Vendor`/`_Model` is safest) with a SkinnedMeshRenderer underneath. Drop the bundle into `BepInEx/config/VisitAPI/scenes/`.
- A scene that fails to load never blocks the dialogue — it just falls back to the flat-background mode.

---

## Option directives (after ` | `, separated by `,` or `|`)

| Directive | Effect | Auto-shows only when |
|---|---|---|
| `accept: <quest>` | accept the quest | AvailableForStart |
| `handover: <quest> [label]` | open the native item hand-over window | Started |
| `complete: <quest>` | complete the quest + grant rewards | AvailableForFinish |
| `setstatus: <quest>[=status]` | set quest status without accepting/completing (bare = AvailableForFinish) | — |
| `if: <quest>=status[/status]` | explicit show condition (overrides the auto-show above) | — |
| `ifnot: <quest>=status[/status]` | hide while the quest is in these states | — |
| `standing: <delta>` / `standing: <traderId>=<delta>` | adjust trader rep (好感度), e.g. `+0.03` / `-0.08` — **no quest involved** | — |
| `once` | never show this option again after it is picked once | — |
| `always` | cancel the auto-show gate (for hidden-quest flows) | — |

- **Auto-show gating**: `accept:` / `handover:` / `complete:` options show only while the quest is in the matching state, unless `always` is set. If *every* option of a node gates out, **all** options are shown instead (dead-end safety — a 0-option node would soft-lock the player, since the dialogue can't be closed with ESC).
- **`complete:`** is refused while the quest still owes a `handover` item (so a partial hand-over can't force-complete it); the option just stays on the node.
- **Combined action**: one main action per option (`accept` / `handover` / `complete`). After a main action you may add `accept:` as a *modifier* to accept another quest at the same time, e.g. `| complete: usb, accept: mats, always`.
- **`standing:`** is a modifier, not a main action — it coexists with `accept:` / `complete:` on the same option. It defaults to the dialogue's own trader; `standing: <24-hex-traderId>=+0.01` targets another trader (a raw trader id, **not** a quest alias). Standing is not clamped; pair a positive delta with `once` so it can't be farmed.
- A `<quest>` is a header alias (`quest xx = …`) or a full 24-hex id. **Status** names: `Locked / AvailableForStart / Started / AvailableForFinish / Success / Fail`, or numbers `0`–`5`.

---

## The out-of-raid Talk button

Every trader that has a `<id>.dlg` automatically gets a **Talk** button on its out-of-raid trade screen (anchored top-centre, configurable via the `TalkButton.*` config). Clicking it opens the dialogue at the `when:`-selected root (it skips `first:`). From there, `@trade` / `@tasks` / `@services` switch the trade screen's tabs.

---

## Quest status values

| Value | Name |
|---|---|
| 0 | Locked |
| 1 | AvailableForStart |
| 2 | Started |
| 3 | AvailableForFinish |
| 4 | Success |
| 5 | Fail |

---

## Notes

- **Coordinates**: press **F11** in raid/hideout to print `Camera.main`'s position to the BepInEx log, ready to paste into a `trigger:` line.
- **Reserved keys**: `tab:` and `random:` parse without error but are **not active** in this version (leftovers from the old build). Don't rely on them.
- **Separators**: ` -> ` and ` | ` (with surrounding spaces) are read as separators — avoid them inside dialogue text.
- **Language**: VisitAPI's own generated text (the `(End)` / `Continue…` rows, the Talk button default label, the default Visit prompt, parse errors) follows the game language automatically. Your `.dlg` content itself is whatever you write — author both languages if you want, or keep one.
