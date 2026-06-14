# VisitAPI — Full guide

[中文](GUIDE.zh-CN.md)

> **In one line: add a "Visit" dialogue system to any trader in the game, and write the whole story in a single script file.**

Drop a `{traderId}.dlg` script file and the trader page gains a **Visit** tab; you can also place visit interaction points on raid maps and in the hideout. Dialogue supports branching options, narration cut-ins, background images/video, quest accept / hand-over / complete, quest-chain links, first-meeting scenes, and post-raid random dialogue. The UI reuses the vanilla `TraderDialogScreen`, so it matches the game perfectly.

---

## Requirements

| Component | Version |
|---|---|
| SPT | 4.0.13 |
| BepInEx | 5.4.23.2 (bundled with SPT) |

## Install

```
SPT/
├── BepInEx/
│   ├── plugins/VisitAPI/VisitAPI.dll        ← client plugin
│   └── config/VisitAPI/                      ← dialogue scripts and media (below)
│       ├── {traderId}.dlg                    ← dialogue script (primary format)
│       ├── {traderId}.json                   ← legacy JSON format (still works, .dlg wins)
│       ├── {traderId}.seen.json              ← generated at runtime: visited / one-shot records
│       └── backgrounds/                      ← background images and videos
└── user/mods/VisitAPI-Server/               ← server mod (needed for quests)
    ├── VisitAPI-Server.dll
    ├── package.json
    ├── db/
    │   ├── quests/*.json                     ← quest definitions (SPT 4.0.13 flat format)
    │   ├── locales/{en,ch,zh-cn}.json        ← quest text
    │   └── quest_transitions.json            ← quest links (A completes → B changes status)
    ├── quest_state/                          ← generated at runtime: accepted records (per profile)
    └── quest_state_completed/                ← generated at runtime: completed records (per profile)
```

> The server mod can be skipped when you only use dialogue / backgrounds / `@trade` / `@tasks`; it is required when you use quest actions (accept / hand-over / complete).

---

## Quick start: a minimal script

`BepInEx/config/VisitAPI/{traderId}.dlg` (UTF-8):

```
trader: 90726f6a656374536f726132 "SORA"
start: root

<root> bg: SORA.png
You're here. Take a look.
- Show me what you've got. -> @trade
- Nothing, I'm off.
```

Reopen the **Visit** tab after saving and it takes effect (hot reload, no game restart).

## .dlg script syntax

Full reference: **[DLG_FORMAT.en.md](DLG_FORMAT.en.md)**. Cheat sheet:

```
trader: <traderId> "Display Name"
start: <start node>
first: <first-visit node>
trigger: raid <map> (x, y, z) door WxH[xRot] dist 3 radius 1.2 "prompt"
trigger: hideout <area> level 2 dist 4 node <node> if quest=status "prompt"
when: level>=4 -> <node>                  # pick the start node by loyalty / standing
random: 10% <node1> <node2>               # random dialogue after a raid
quest <alias> = <24-hex questId>
tab: if quest=status                       # the "Visit" tab only shows in this state

<nodeName> bg: backgroundFile
> narration line (black-screen subtitle, click to advance)
NPC line (plain line; multiple lines = multiple segments)
- option text -> target | command, command...
```

- target: a node name / `@start` (re-pick the start node) / `@trade` / `@tasks`; omit = close the dialogue
- commands: `accept:quest` `handover:quest` `complete:quest` (auto shown/hidden by quest status),
  `if:quest=status[/status]`, `ifnot:...`, `once` (one-shot), `always` (cancel auto gating),
  combined `complete:A, accept:B` (complete A and accept B at once)
- text supports `{player}` / `{playerName}` placeholders (replaced with the character nickname)
- the legacy JSON format still works (fields map one-to-one, including the new `hideoutTriggers` / `tabQuestId` / `tabShowWhenStatus`)

## Triggers

| Type | Syntax | Behaviour |
|---|---|---|
| Raid | `trigger: raid map (x,y,z) ...` | an interaction prompt appears when the player gets close and looks at the spot; **first visit only**, once |
| Hideout | `trigger: hideout area level N ...` | the prompt appears once the area reaches the level; with `if quest=status` it is **gated by quest status** (can reappear while the status matches); without it, first visit only |

- Area name = `EAreaType` enum: IntelligenceCenter / Generator / MedStation / Workbench …
- Getting coordinates: press **F8** in raid; the BepInEx log prints ready-to-paste coordinates
- After an area is upgraded in the hideout, triggers are re-checked automatically every 10 seconds — no need to re-enter

## Quest system (server)

**Quest definitions**: `db/quests/*.json`, SPT 4.0.13 flat format (`conditionType` at the top level), structure as in `ragman_visit_demo.json`. Quest IDs are 24-hex lowercase; by convention use ASCII→hex (`soradevice01` → `736f72616465766963653031`).

**Text**: `db/locales/{en,ch,zh-cn}.json`, key format `"<questId> name/description/..."`, condition target-text key = condition ID. `zh-cn` is automatically registered as SPT's internal Chinese code `ch` as well. Leaving `startedMessageText` / `successMessageText` empty disables the trader mail.

**Quest links**: `db/quest_transitions.json` — when `triggerQuestId` completes, set `dependentQuestId` to `targetStatus` (e.g. 3 = ready to finish); used to let a hidden quest drive a visible one. It is a **top-level JSON array** (no comments).

**Completion flow**: the client prefers the native `FinishQuest` (quest log / rewards / mail / sound are all handled by the game, the server only records and links); a quest that is not in the task book (hidden quest) completes silently; other failure cases fall back to local presentation + a manual sound.

**HTTP endpoints** (`127.0.0.1:6970/visitapi/`, the main SPT server is on 6969):

| Endpoint | Body | Notes |
|---|---|---|
| `/quest/accept` | `{ProfileId, QuestId}` | record the acceptance (persisted to the profile) |
| `/quest/handover` | `{ProfileId, QuestId}` | remove the inventory items, advance to ready-to-finish |
| `/quest/complete` | `{ProfileId, QuestId, Native}` | `Native=true` only records + links; otherwise writes the profile + grants rewards |
| `/quest/status` | `{ProfileId, QuestIds[]}` | batch status query (profile first, status file fills in) |
| `/quest/sync` | `{ProfileId}` | replay the quest links |

**Quest status values**: `Locked=0, AvailableForStart=1, Started=2, AvailableForFinish=3, Success=4, Fail=5` (both status names and numbers work in `.dlg`).

## Background media

A plain file name in `bg:` is looked up under `config/VisitAPI/backgrounds/`; a path is resolved as-is (relative to `config/VisitAPI/`). Static: png/jpg/jpeg/bmp; video: mp4/webm/mov/avi/mkv/ogv (auto-loop, H.264 mp4 recommended). The trader tab's initial background: `config/VisitAPI/{traderId}.png`.

---

## Tool-development reference (DialogDesigner, etc.)

### Data flow

```
Author edits           Client (VisitAPI.dll)                 Server (VisitAPI-Server.dll)
─────────              ─────────────────────                ───────────────────────────
{traderId}.dlg  ──►  DialogScriptParser ──► DialogTree ─┐
{traderId}.json ──►  Newtonsoft deserialize ────────────┤ (same runtime model)
                                                        ▼
                                      Visit tab / raid trigger / hideout trigger
                                                        │ quest actions (HTTP :6970)
db/quests/*.json ────────────────────────────────────►  CustomQuestService registration
db/locales/*.json ───────────────────────────────────►  AddQuestLocales registration
db/quest_transitions.json ───────────────────────────►  links on completion
```

### Conventions a tool must know

| Item | Convention |
|---|---|
| File encoding | UTF-8 (`.dlg` is read with `File.ReadAllLines(UTF8)`) |
| Load priority | `.dlg` first; falls back to the same-name `.json` on a parse error; parse errors are logged to BepInEx with line numbers |
| Hot reload | the dialogue tree is cached by file timestamp; saving the file then reopening the dialogue applies it |
| Node name charset | `[A-Za-z0-9_.\-]+` (ASCII); `[action]` / `<rich text>` in lines are never mistaken for it |
| Forbidden in text | option text must not contain ` -> ` or ` \| ` (with surrounding spaces — used as separators) |
| Trader / quest IDs | 24-hex lowercase (ASCII→hex by convention), or a mod's custom ID of letters/digits/`_`/`-` |
| Runtime model | `DialogTree` in `DialogModels.cs` (the shared target of `.dlg` and `.json`); a JSON-emitting tool uses it as the schema |
| State store | `{traderId}.seen.json` (first-visit + once records, keyed per profile); delete to reset |

### Source map

| File | Role |
|---|---|
| `DialogScriptParser.cs` | `.dlg` → `DialogTree` compiler |
| `DialogModels.cs` | dialogue-tree runtime model (JSON schema) |
| `DialogStateStore.cs` | dialogue-tree loading (cache / fallback) + visited / one-shot state store |
| `InteractTriggerBase.cs` | shared base for both triggers (GPO reflection, interaction injection, profile extraction, FireVisit) |
| `RaidInteractTrigger.cs` / `HideoutInteractTrigger.cs` | raid / hideout triggers |
| `HideoutTriggerConfig.cs` | hideout trigger config model (compatible with the old `hideout_triggers.json`) |
| `TraderDealScreenVisitButton.cs` | Visit tab injection, native dialogue driving, node execution |
| `TraderDealScreenHook.cs` / `TraderDialogScreenPatch.cs` | Harmony patches (trader screen / native dialogue pass-through) |
| `NativeQuestController.cs` | quest actions: native complete + server sync + fallback presentation |
| `QuestStatusCache.cs` | quest status cache and visibility logic |
| `VisitPlugin.cs` | entry point: patch install, raid / hideout lifecycle, trigger spawning, FavoriteScheme guard |
| `Server/VisitApiQuestLoader.cs` | quest / text registration (CustomQuestService) |
| `Server/VisitApiQuestHelper.cs` | quest status read/write, rewards, links |
| `Server/VisitApiQuestServer.cs` | HTTP routing on port 6970 |

### Build & deploy

```
dotnet build VisitAPI.csproj -c Release            # client → auto-deploys to BepInEx/plugins/VisitAPI/
dotnet build Server/VisitAPI-Server.csproj -c Release   # server → auto-deploys to user/mods/VisitAPI-Server/ (db/ xcopy sync)
```

Note: xcopy does not delete removed files; after deleting quests/text, clean the deploy folder manually. There must be exactly one `VisitAPI.dll` anywhere under the `plugins` tree.

---

## Debugging & FAQ

- **F8**: print the current coordinates to the BepInEx log in raid.
- **Visit tab missing**: is the file name the same as the traderId; is a `tab:` gate configured whose quest status is not met.
- **`.dlg` changes don't apply**: check the BepInEx log for `name.dlg:line: error` (a parse failure falls back to `.json`).
- **Quest stuff doesn't work**: quests / text / links load at server start — after changing them you **must restart the SPT server**; the dialogue script needs no restart.
- **Quest doesn't enter the task list**: is the server installed and restarted; is port 6970 free; search the log for `[NativeQuest]`. Note: a quest tied to a still-locked trader only appears after that trader is unlocked.
- **`once` mis-clicked, want to reset**: delete `config/VisitAPI/{traderId}.seen.json`.
- **Video warning "Unexpected timestamp"**: re-encode to H.264 with a Baseline Profile to remove it.

## Version

`0.2.2` · for SPT 4.0.13 · MIT

Sora is the Best~
