# VisitAPI

[中文说明](README.zh-CN.md)

Add a **"Visit"** dialogue system to any trader in SPT. Write a whole storyline in a single script file — branching choices, narration, background images/video, quest accept / hand‑over / complete, first‑meeting scenes, and interaction points you can place **in raid** and **in the hideout**. The UI reuses the vanilla trader dialogue screen, so it looks native.

## Requirements

| Component | Version |
|---|---|
| SPT | 4.0.13 |
| BepInEx | 5.4.23.2 (bundled with SPT) |

## Install

Extract the release into your SPT folder:

```
SPT/
├─ BepInEx/
│  ├─ plugins/VisitAPI/VisitAPI.dll        ← client plugin
│  └─ config/VisitAPI/                      ← your dialogue scripts + media
│     ├─ <traderId>.dlg                     ← one script per trader
│     └─ backgrounds/                       ← images & videos
└─ user/mods/VisitAPI-Server/              ← server mod (needed for quests)
   ├─ VisitAPI-Server.dll, package.json
   └─ db/quests, db/locales, quest_transitions.json
```

The **server mod is only needed for quest actions** (accept / hand‑over / complete). Pure dialogue, triggers and `@trade` / `@tasks` work client‑only.

## Quick start

Create `BepInEx/config/VisitAPI/<traderId>.dlg` (UTF‑8):

```
trader: 5ac3b934156ae10c4430e83c "Ragman"
start: root

<root>
Need anything? Take a look.
- Show me your goods. -> @trade
- Just saying hi.
- See you.
```

Open the trader in game → a **Visit** tab appears. Editing the `.dlg` hot‑reloads — just reopen the dialogue, no game restart.

## Features

- **Visit tab** on any trader, using the vanilla dialogue UI
- **Branching dialogue** with narration, `{player}` placeholders, background image / looping video
- **In‑raid trigger** — walk up to a spot on a map and interact
- **Hideout trigger** — interact at a hideout area (e.g. Intelligence Center)
- **Quests** — accept / hand‑over / complete from dialogue, quest‑chain transitions, hidden trigger quests
- **Loyalty / first‑meeting gating** and post‑raid random dialogue
- Hot‑reloadable `.dlg` script (legacy `.json` still supported)

## Examples (Ragman)

This release ships a ready‑to‑run **Ragman** example under `examples/` (also placed in the package):

| File | Shows |
|---|---|
| `BepInEx/config/VisitAPI/5ac3b934156ae10c4430e83c.dlg` | in‑raid trigger + hideout trigger + dialogue + quest accept/complete |
| `user/mods/VisitAPI-Server/db/quests/ragman_visit_demo.json` | trader quest JSON |
| `user/mods/VisitAPI-Server/db/locales/en.json`, `zh-cn.json` | quest text |

Copy those into your SPT folder to try it on Ragman; delete them to remove the demo.

## Documentation

- `.dlg` script reference — [docs/DLG_FORMAT.md](docs/DLG_FORMAT.md)
- Full guide (Chinese) — [docs/GUIDE.zh-CN.md](docs/GUIDE.zh-CN.md)

## Contribute to the Project.
If you want to contribute to this project, Feel free to DM me on Discord: @tricoloursky
