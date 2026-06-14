# VisitAPI — Ragman example

A self-contained demo that adds a **Visit** tab + an **in-raid trigger** + a **hideout trigger** + a **quest** to Ragman (`5ac3b934156ae10c4430e83c`).

## What it shows

| File | Demonstrates |
|---|---|
| `BepInEx/config/VisitAPI/5ac3b934156ae10c4430e83c.dlg` | dialogue script, `trigger: raid`, `trigger: hideout`, quest `accept` / `complete` |
| `user/mods/VisitAPI-Server/db/quests/ragman_visit_demo.json` | trader quest JSON (SPT 4.0.13 flat format) |
| `user/mods/VisitAPI-Server/db/locales/en.json`, `zh-cn.json` | quest text (key = `<questId> field`) |
| `user/mods/VisitAPI-Server/db/quest_transitions.json` | quest-chain links (empty for this demo) |

## Install

Copy the two folders (`BepInEx/` and `user/`) into your SPT root, merging with the
existing VisitAPI install. Restart the **SPT server** once (quests load at server start);
the `.dlg` itself hot-reloads.

## Try it

1. In game, open **Ragman** → a **Visit** tab appears (`root` node).
2. Drop into a raid; walk to the trigger spot to meet Ragman and **accept** the quest
   (edit the coordinates in the `.dlg` to a real spot — press **F8** in raid to log coords).
3. Back in the hideout, interact at the **Intelligence Center** (level 1+) to **complete**
   the quest and get the reward.

Delete these files to remove the demo. Full `.dlg` syntax: `docs/DLG_FORMAT.md`.
