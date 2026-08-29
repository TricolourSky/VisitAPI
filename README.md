# VisitAPI

**English** | [简体中文](README.zh-CN.md)

An open-source framework that brings EFT 1.0-style trader **Visit** dialogues to SPT — write plain-text `.dlg` scripts to give any trader (including custom traders) a 3D scene conversation, story quests, standing rewards and in-raid dialogue triggers.

> Target: **SPT 4.1.3** (EFT 0.16.9.5) · Client: BepInEx 5.4.23 plugin (net472) · Server: SPT mod (net10.0)

## Features

- **Visit button** — a 1.0-style "Visit" tab on the trader screen that opens a 3D scene conversation
- **.dlg scripts** — nodes, options, condition gates (level / standing / quest state / `ifitems`, i.e. only show this line while the player is actually carrying the goods), branch memory (`set:` / `ifvar:`), one-shot options (once/always/first), image/video/3D-scene backgrounds, voice + BGM, standing rewards (`standing:`), quest state transitions (`setstatus:`)
- **Native Narrate pipeline** — vanilla traders run on EFT's dormant built-in visit system with retail dialogue playback (lip-sync, subtitles and branching variables are all native)
- **Quest system** — custom quest JSON with fully native network transactions (accept / handover / complete) plus quest image routing
- **In-raid & hideout triggers** — place dialogue points at map coordinates via `trigger:` syntax (distance + view cone + quest gating), or fire on a timer with `enter <seconds>` (no coordinates needed) to auto-accept a quest shortly after the player loads in
- **Native subtitle narration** — `>` narration lines play in the game's own subtitle bar (`SubtitlesView`) instead of the trader dialogue window; click or press Space to advance
- **Quest banner** — quest started / ready to hand in / completed / failed are announced on a retail-style banner that reuses the vanilla notification pipeline (slide-in animation, sound and queueing are all native), leaving SPT's own notifications untouched
- **Progress persistence** — dialogue variables are replayed server-side into the profile and survive across sessions
- **Chapter system** — a STORY tab on the tasks screen (the retail 1.1 story page itself) that groups quests into chapters: chapter icon column, banner, main/optional objectives, journal, related items, unread markers, retail chapter banners and sounds; story quests live on the STORY tab only and stay out of the side-quest list

## Script Editor

**[VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor)** is the official companion tool — a local web-based visual editor for writing `.dlg` dialogue scripts. If you plan to author your own trader dialogues, start there instead of writing scripts by hand.

## Install

1. Grab both packages from [Releases](https://github.com/TricolourSky/VisitAPI/releases):
   - `VisitAPI-x.y.z-spt4.1.3.zip` — client plugin + server mod **plus `VisitAPI.Editor.exe`**, the companion script editor. Extract into your SPT root; the editor lands there next to `SPT.Server.exe` — double-click it and it opens in your browser (nothing to install, binds to 127.0.0.1 only).
   - `VisitAPI-scenes-tarkin.zip` — 3D trader rooms. Extract into your SPT root.
     Scene assets are derived from [bmpq/spt-tradermod](https://github.com/bmpq/spt-tradermod) (MIT) and are distributed as a release asset due to size.
2. Put your `.dlg` scripts into `<SPT>\BepInEx\config\VisitAPI\<traderId>.dlg`
3. The package ships the framework plus the retail dialogue data it needs (`db/dialogues/dialogue.json`, used for vanilla-trader playback).
   It deliberately contains **no** dialogue scripts, quests or locale text — only the empty folders those go in.

## Build from source

You need a local SPT 4.1.3 install (for reference DLLs) and the [VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor) repository checked out next to this one (the `.dlg` parser sources are compiled in from there):

```
dotnet build Client\VisitAPI.csproj  -c Release -p:EftDir=<your SPT dir> [-p:DlgSrc=<VisitAPI.Dlg source dir>]
dotnet build Server\VisitAPI-Server.csproj -c Release -p:SptDir=<your SPT server dir>
```

When the game directory exists the build auto-deploys (Deploy target); otherwise deployment is silently skipped.

## .dlg quick start

```
trader: 5ac3b934156ae10c4430e83c "Example Trader"
start: root

<root> bg: room.png
> Narration - plays in the game's own subtitle bar.
You made it. What do you need?
- Show me your stock. -> @trade
- Got any work for me? -> @tasks
- Nothing, I'm off.
```

Name the file after the trader's 24-hex id and drop it in `BepInEx\config\VisitAPI\`.
See [`examples/minimal.dlg`](examples/minimal.dlg) for the full syntax (gates, triggers, quests, standing, media).

## Chapter system (STORY tab)

A chapter is an ordinary quest JSON with a few switches; its "complete quest" objectives are the list of sub-quests:

```json
"visitapi": { "chapter": true, "icon": "/files/quest/icon/ch1_icon.png" },
"image": "/files/quest/icon/ch1_banner.png",
"secretQuest": true,
"notes": { "Started": "<24-hex note id>", "Success": "<24-hex note id>" },
"conditions": { "AvailableForFinish": [
  { "conditionType": "Quest", "target": "<sub-quest A id>", "status": [4], "isFinisher": false, "id": "<24-hex>", "index": 0 },
  { "conditionType": "Quest", "target": "<sub-quest B id>", "status": [4], "isFinisher": true,  "id": "<24-hex>", "index": 1 }
] }
```

- The chapter starts when any sub-quest starts and is turned in automatically once all sub-quests are done (mail and rewards go through the native pipeline)
- `notes`: one journal entry unlocks when the quest reaches Started / Success / Fail; the text lives in the locale under the note id as key. Chapters and sub-quests can both carry notes
- Sub-quest switches: `autoStart` (accepted on its own once **the chapter has started** and the sub-quest's own prerequisites are met), `autoFinish` (turned in the moment its objectives are met — how retail 1.1 story tasks behave), `items` (template ids shown as related items; items from hand-over / find objectives are added automatically)
- To make a whole chapter run itself, put `autoStart` on the **chapter** — chapters are not held back by that gate. Putting it on the first sub-quest instead does nothing useful: the sub-quest would be handed out while the player is still sitting in the menu
- Story quests (chapter + sub-quests) never appear in the side-quest or trader task lists; they are driven by dialogue (`accept:` / `complete:`), trigger points or the switches above. Objective rows show "VISIT X" / "GO TO: map" / "HIDEOUT" buttons derived from your `.dlg`
- Quests outside a chapter can set `dialogOnly`: the Accept/Complete button in the task list turns into "VISIT X", so accepting and handing in go through the dialogue only
- **A failed sub-quest does not kill the chapter** — its objective row gets a red cross and a "(Failed)" prefix and the chapter carries on (retail 1.1 behaves the same way). To stop a written-off sub-quest from blocking the chapter's hand-in, give its "complete quest" condition `"status": [4, 5, 6]` (editor: sub-quest ⋮ → "failed counts as done")
- Besides `accept`, a trigger point can `finish` / `fail` a quest — walking into a spot, or simply being N seconds into the raid, marks a quest complete or failed with no dialogue window:
  ```
  trigger: raid Sandbox enter 10 accept <quest A>                  # 10 s after loading into Ground Zero, A is accepted
  trigger: raid Sandbox (x, y, z) dist 6 auto fail <A> accept <B>  # step here and A is written off, B begins
  ```
- Icons / banners go into the mod's `images/quest/icon/`; **every id must be 24 hex characters**
- A complete runnable example lives in [`examples/chapter/`](examples/chapter/); the editor (1.2.0) has a dedicated chapter module plus every switch on the quest properties page
- To put story quests back into the regular lists, turn off `HideStoryQuestsInLists` in `BepInEx/config/com.sora.visitapi.cfg`

## Disclaimer

This repository contains **no BSG game assets**. Retail dialogue data and scene bundles are distributed via Releases. Not affiliated with Battlestate Games or the SPT team.

## License

MIT (see [LICENSE](LICENSE)). Scene bundles are copyright bmpq/tarkin (MIT) — retain their attribution when redistributing.
