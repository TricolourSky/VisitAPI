# VisitAPI

**English** | [简体中文](README.zh-CN.md)

An open-source framework that brings EFT 1.0-style trader **Visit** dialogues to SPT — write plain-text `.dlg` scripts to give any trader (including custom traders) a 3D scene conversation, story quests, standing rewards and in-raid dialogue triggers.

> Target: **SPT 4.1.1** (EFT 0.16.9.5) · Client: BepInEx 5.4.23 plugin (net472) · Server: SPT mod (net10.0)

## Features

- **Visit button** — a 1.0-style "Visit" tab on the trader screen that opens a 3D scene conversation
- **.dlg scripts** — nodes, options, condition gates, one-shot options (once/always/first), image/video/3D-scene backgrounds, voice + BGM, standing rewards (`standing:`), quest state transitions (`setstatus:`)
- **Native Narrate pipeline** — vanilla traders run on EFT's dormant built-in visit system with retail dialogue playback (lip-sync, subtitles and branching variables are all native)
- **Quest system** — custom quest JSON with fully native network transactions (accept / handover / complete) plus quest image routing
- **In-raid & hideout triggers** — place dialogue points at map coordinates via `trigger:` syntax (distance + view cone + quest gating)
- **Progress persistence** — dialogue variables are replayed server-side into the profile and survive across sessions

## Script Editor

**[VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor)** is the official companion tool — a local web-based visual editor for writing `.dlg` dialogue scripts. If you plan to author your own trader dialogues, start there instead of writing scripts by hand.

## Install

1. Grab both packages from [Releases](https://github.com/TricolourSky/VisitAPI/releases):
   - `VisitAPI-x.y.z-spt4.1.1.zip` — client plugin + server mod. Extract into your SPT root.
   - `VisitAPI-scenes-tarkin.zip` — 3D trader rooms. Extract into your SPT root.
     Scene assets are derived from [bmpq/spt-tradermod](https://github.com/bmpq/spt-tradermod) (MIT) and are distributed as a release asset due to size.
2. Put your `.dlg` scripts into `<SPT>\BepInEx\config\VisitAPI\<traderId>.dlg`

## Build from source

You need a local SPT 4.1.1 install (for reference DLLs) and the [VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor) repository checked out next to this one (the `.dlg` parser sources are compiled in from there):

```
dotnet build Client\VisitAPI.csproj  -c Release -p:EftDir=<your SPT dir> [-p:DlgSrc=<VisitAPI.Dlg source dir>]
dotnet build Server\VisitAPI-Server.csproj -c Release -p:SptDir=<your SPT server dir>
```

When the game directory exists the build auto-deploys (Deploy target); otherwise deployment is silently skipped.

## .dlg quick start

```
@start hello

[hello]
npc: You made it. What do you need?
opt: Show me your stock -> openTrade
opt: Got any work for me? -> openTasks
opt: Nothing, I'm off -> @close
```

See `docs/` and [`examples/`](examples/) for the full syntax (gates, triggers, standing, media).

## Disclaimer

This repository contains **no BSG game assets**. Retail dialogue data and scene bundles are distributed via Releases. Not affiliated with Battlestate Games or the SPT team.

## License

MIT (see [LICENSE](LICENSE)). Scene bundles are copyright bmpq/tarkin (MIT) — retain their attribution when redistributing.
