# VisitAPI

[中文说明](README.zh-CN.md)

Add a **"Talk"** dialogue system to any trader in SPT. Write a whole storyline in a single script file — branching choices, narration, background images / video, quest accept / hand-over / complete, first-meeting scenes, and interaction points you can place **in raid** and **in the hideout**. The UI reuses the **vanilla trader dialogue screen**, so it looks native. VisitAPI's own text auto-follows the game language (中 / EN).

## Requirements

| Component | Version |
|---|---|
| SPT | 4.0.13 |
| BepInEx | 5.4.x (bundled with SPT) |

## Install

Extract the release into your SPT folder:

```
SPT/
├─ BepInEx/
│  ├─ plugins/VisitAPI/VisitAPI.dll        ← client plugin
│  └─ config/VisitAPI/
│     ├─ <traderId>.dlg                     ← your dialogue scripts (one per trader)
│     └─ backgrounds/                       ← background images & videos
└─ user/mods/VisitAPI-Server/              ← server mod (only needed for quests)
   ├─ VisitAPI-Server.dll
   ├─ db/quests/  db/locales/  db/assort/   ← quests, locale text, trader assort
   └─ images/quest/                         ← custom quest icons
```

The client plugin alone is enough for dialogue + in-world triggers. The server mod is only needed if you register quests or sell quest-locked items.

## Quick start

Create `BepInEx/config/VisitAPI/<traderId>.dlg` (UTF-8, **file name = the 24-hex trader id**):

```
trader: 5ac3b934156ae10c4430e83c "Ragman"
start: root

<root>
Need anything? Take a look.
- Show me your goods. -> @trade
- Just saying hi.
- See you.
```

Open the trader in game (out of raid) → a **Talk** button appears at the top of the trade screen → it opens your dialogue. Editing the `.dlg` **hot-reloads** — just reopen the dialogue, no game restart.

## Features

- **Talk button on any trader**, drawn with the vanilla dialogue UI (looks native).
- **Branching dialogue** with narration, `{player}` placeholders, and a background **image or looping video**.
- **In-raid trigger** — walk up to a spot on a map and interact.
- **Hideout trigger** — interact at a hideout area (e.g. the Intelligence Center), merged into the native menu.
- **Quests** — accept / hand-over / complete straight from dialogue, with quest-chain transitions and quest-locked trader items.
- **Loyalty / standing & first-meeting gating** — pick which greeting the player gets by level or trader rep.
- **`@trade` / `@tasks` / `@services`** — jump to the trader's trade / tasks / services screen from an option.
- **Bilingual** — VisitAPI's own UI text follows the game language automatically (中 / EN), or force it in the config.
- **Hot-reloadable `.dlg`** — edit and reopen the dialogue, no game restart.

## Example

The release ships **no dialogue by default** — you add your own. A ready-to-run **Ragman** example is in [examples/minimal.dlg](examples/minimal.dlg). To try it: copy that file to `BepInEx/config/VisitAPI/5ac3b934156ae10c4430e83c.dlg`, restart, open Ragman out of raid → click **Talk**.

## Documentation

- **`.dlg` script reference** — [docs/DLG_FORMAT.md](docs/DLG_FORMAT.md) ([中文](docs/DLG_FORMAT.zh-CN.md))

## Contribute

Found a bug or want to contribute? DM me on Discord: **@tricoloursky**

## License

[MIT](LICENSE) — free to use, fork, and ship your own trader dialogues.
