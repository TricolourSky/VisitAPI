# `.dlg` dialogue script — format reference

[中文](DLG_FORMAT.zh-CN.md)

One trader = one `BepInEx/config/VisitAPI/<traderId>.dlg` file (UTF-8). **The file name must equal the 24-hex trader id.** VisitAPI auto-discovers every `.dlg` in that folder on startup, registers the trader, and gives it an out-of-raid **Talk** button (see below). Parse errors are written to the BepInEx log with line numbers (bilingual, following the game language). `.dlg` is a runtime file — no rebuild. Edits to an existing `.dlg` **hot-reload**: just reopen the dialogue. (Adding a *new* `.dlg` file, or changing the `tab:` header, still needs a game restart.)

---

## Header (before the first node)

```
trader: <24-hex traderId> "Display Name"
start: <repeat-visit root node>                  # default: root
first: <first-meeting node>                       # optional
actor: <npc model name>                           # the 3D-scene dialogue actor (see "3D scene dialogue")
when: level>=4 -> <node>                          # pick the root by level / standing (see below)
when: level>=3, standing>=0.05 -> <node>          # conditions can be comma-joined (all must hold)
tab: if <quest>=<status>[/status]                 # gate the out-of-raid Talk button on a quest state (default: always shown)
trigger: raid <map> (x, y, z) [dist 3] [radius 1.2] "prompt"
trigger: hideout <area> (x, y, z) [node <node>] [dist 3] [if quest=status[/status]] [free] "prompt"
quest <alias> = <24-hex questId>                  # quest alias; use <alias> anywhere a quest id is expected
```

- `trader:` must be the trader id (matching the file name); the quoted name is optional/cosmetic.
- **`start:`** is the normal repeat-visit root (default node name `root`). **`first:`** is the one-time first-meeting node — played once in raid/hideout (tracked per trader + profile); **the out-of-raid Talk button always skips `first:` and enters the root family directly.**
- **`when:`** selects which root the player lands on, by `level>=N` / `level<=N` / `standing>=N` / `standing<=N` (comma-join several — all must hold). The **first matching line in file order wins**; if none match, `start:` is used. `level` = player level, `standing` = your rep with this trader. This applies to the Talk button **and** the raid/hideout entries.
- **`tab:`** gates the out-of-raid **Talk** button by quest state: `tab: if <quest>=<status>[/status]` shows the button **only** while the quest is in one of those states; `tab: always` (or omitting `tab:` entirely) shows it always. Use it to reveal a trader's chat/visit only after a story beat — e.g. `tab: if <handoverQuest>=Success` keeps the button hidden until that quest is handed in and completed. Only the out-of-raid button is affected; raid/hideout triggers have their own `if` gates.
- **`quest <alias> = <id>`** defines a short alias; you can then write the alias wherever a quest id is expected (`accept:`, `if:`, trigger `if`, …). Can appear on any header line.

### Triggers

A `trigger:` line places an in-world "Visit" interaction. Only the **type** and the **map / area** are required; the rest is order-free.

**`trigger: raid <map> (x,y,z) …`**
- `<map>` is matched (substring, case-insensitive) against the raid's location id. Common ids: `factory4_day` / `factory4_night`, `bigmap` (Customs), `Woods`, `Shoreline`, `Interchange`, `RezervBase` (Reserve), `Lighthouse`, `TarkovStreets` (Streets), `Sandbox` / `Sandbox_high` (Ground Zero), `laboratory` (Labs).
- `(x,y,z)` = required world coordinates. Stand on the spot and press **F11** — the BepInEx log prints a ready value.
- `dist N` = max distance (default 3). `radius N` = look-cone hit radius (default 1.2). The prompt shows while you are within `dist` **and** looking at the point; it reappears on every raid (gate one-time story beats with quest state instead — e.g. an option with `accept:` auto-hides once the quest is running).
- **Multiple `trigger: raid` lines = multiple points** (different maps, or several spots on one map), tracked independently.

**`trigger: hideout <area> (x,y,z) …`** (spawns when the location is the hideout)
- `(x,y,z)` = absolute coordinates (F11 to grab them). `<area>` is informational in this version (positioning is by coordinates).
- `node <node>` = open the dialogue straight at that node (e.g. resume a questline mid-story). `dist N` = max distance. `if quest=status[/status]` = show **only** while the quest is in one of those states (and hide otherwise).
- `free` (or `door`) = free-standing look-to-interact: for an open spot with no native area menu (the trigger requires you to *look* at the point). Without `free`, the Visit action is **merged into** the native area menu (e.g. the Intelligence Center menu).
- Two `trigger: hideout` lines at the **same coordinates with different `if` gates** = the "accept the next task here / come back to hand it in here" pattern (the gates are mutually exclusive, so only one shows at a time).

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
- **`bg:`** a plain file name is looked up under `config/VisitAPI/backgrounds/`; a value containing `/` or `\` is used as a path relative to `config/VisitAPI/`. Omit `bg:` to keep the previous background. Supported: **images** `.png .jpg .jpeg` and **video** `.mp4 .webm .m4v .mov` (muted by default).
- **Video looping**: a video background **loops by default**. Append ` once` after the file name to play it a single time and hold on its last frame — `bg: intro.mp4 once` (an explicit `bg: intro.mp4 loop` also works). The flag works on the node-header `bg:` and on per-line `| bg:` alike.
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
- `actor:` name resolution: a spawned bot whose nickname contains the name (case-insensitive), else an exact-named scene GameObject (a model placed by your trader mod).
- `anim: <state>` plays a **state of the model's Animator** (`CrossFadeInFixedTime`, 0.25 s blend). The state name = a state in your AnimatorController. The model and its AnimatorController ship in your trader mod's asset bundle — any standard Unity Animator works; no EFT SequenceReader assets needed.
- Pairs naturally with raid/hideout triggers: place the interaction point next to the model, and every dialogue line drives its animation.
- **NPC spoken line** = any plain line. If a node has several plain lines, the **last** one is shown as the NPC's text (use `>` narration for multi-line lead-ins).
- **`- option text -> target`** = a clickable option. **target**: a node name / `@start` (the root) / `@trade` / `@tasks` / `@services` (open that tab of the out-of-raid trade screen) / `@close` (or `@leave`). **Omitting `-> target` closes the dialogue** after running the option's directives.
- Text supports `{player}` / `{playerName}` (replaced with the character nickname).
- Lines starting with `#` or `//` are comments; blank lines are free.

> `@trade` / `@tasks` / `@services` only work **out of raid** (the dialogue opened from the Talk button sits on top of the trade screen). In raid they no-op (the option stays on the node).

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

The button can be **quest-gated** with a header `tab:` line (see the header section). Without one it always shows; with `tab: if <quest>=<status>[/status]` it appears only while that quest is in one of the listed states — so a trader can start out chat-less and reveal the Talk button after a story beat completes.

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
- **Separators**: ` -> ` and ` | ` (with surrounding spaces) are read as separators — avoid them inside dialogue text.
- **Language**: VisitAPI's own generated text (the `(End)` / `Continue…` rows, the Talk button default label, the default Visit prompt, parse errors) follows the game language automatically. Your `.dlg` content itself is whatever you write — author both languages if you want, or keep one.
