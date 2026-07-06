# VisitAPI

[中文说明](README.zh-CN.md)

Add a **"Talk"** dialogue system to any trader in SPT. Write a whole storyline in a single script file — branching choices, narration, background images / video, quest accept / hand-over / complete, first-meeting scenes, and interaction points you can place **in raid** and **in the hideout**. The UI reuses the **vanilla trader dialogue screen**, so it looks native. VisitAPI's own text auto-follows the game language (中 / EN).

Optionally, **visit the retail traders inside their own 3D rooms** — with their voice lines and gestures — by installing the separate **Scenes** add-on pack (see below).

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

## 3D vendor visits (optional add-on)

The **VisitAPI Scenes** pack is a separate download that adds a **Visit** button to the 7 retail traders, opening their **3D room** (voice + gestures + timeline), replayed out of raid. It is optional — the dialogue framework above works without it.

Install: extract the Scenes pack into your SPT folder so it lands at:

```
SPT/BepInEx/plugins/VisitAPI/scenes/
├─ tradermod.shared.dll
└─ bundles/vendors/            ← the vendor scene bundles + dialogue data
```

VisitAPI auto-detects it there (or set `Scene / AssetsRoot` in the config to a custom path). First open of a room is slow — the shared bundle + the trader's room load on demand.

The scene assets come from **[bmpq / spt-tradermod](https://github.com/)** and are used under the **MIT license** — full credit to the original author. This pack ships only his assets + the `tradermod.shared` types VisitAPI reflects; it does **not** include his `tradermod.eft` plugin (VisitAPI drives the scenes itself).

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
- **3D vendor visits (optional)** — visit the 7 retail traders in their own 3D rooms with voice + gestures, via the separate Scenes add-on pack.

## Example

The release ships **no dialogue by default** — you add your own. A ready-to-run **Ragman** example is in [examples/minimal.dlg](examples/minimal.dlg). To try it: copy that file to `BepInEx/config/VisitAPI/5ac3b934156ae10c4430e83c.dlg`, restart, open Ragman out of raid → click **Talk**.

## Documentation

- **`.dlg` script reference** — [docs/DLG_FORMAT.md](docs/DLG_FORMAT.md) ([中文](docs/DLG_FORMAT.zh-CN.md))

## Contribute

Found a bug or want to contribute? DM me on Discord: **@tricoloursky**

## Credits

- **VisitAPI** — [MIT](LICENSE), free to use, fork, and ship your own trader dialogues.
- **Scenes add-on** — vendor room assets from **bmpq / spt-tradermod** (MIT), bundled with credit.

## License

[MIT](LICENSE)
