# .dlg dialogue script — format reference

[中文](DLG_FORMAT.md)

One trader = one `BepInEx/config/VisitAPI/<traderId>.dlg` file (UTF-8).
A `.dlg` takes priority over a `.json` of the same name; on a parse error it falls back to the `.json`, and errors are written to the BepInEx log with line numbers.
Full example: [sora.dlg](sora.dlg).

## Header (before the first node)

```
trader: <24-hex traderId> "Display Name"
start: <default start node>
first: <first-visit node>                        # optional
trigger: raid <map> (x, y, z) door WxH[xRot] dist 3 radius 1.2 "prompt"
trigger: hideout <AreaEnum> level 2 dist 4 (x, y, z) node <node> if quest=status "prompt"
when: level>=4 -> <node>                          # pick the start node by loyalty/standing
when: level>=3, standing>=0.05 -> <node>          # conditions can be comma-joined
random: 10% <node1> <node2> ...                   # random dialogue after a raid
quest <alias> = <24-hex questId>                  # quest alias, multiple lines, anywhere
tab: always                                       # the "Visit" tab always shows (ignores unlock/quest gates)
tab: if quest=status[/status]                     # show only while the quest is in these states (replaces the default unlock gate)
```

- In a `trigger` line only the **type** and the **map / area** are required; everything else is in any order and optional (defaults: `dist 3` / `radius 1.2` / `level 1` / prompt `"Visit"`; door collider height defaults to `2.2`).
- `trigger: hideout` replaces the old `hideout_triggers.json` (the old file still works; for the same trader + area the `.dlg` wins).
- Area enum names: `IntelligenceCenter` / `Generator` / `MedStation` / `Workbench` … (`EAreaType`).
- `trigger: hideout ... if quest=status` — a trigger with a quest condition is **gated by quest status**: while the status matches it appears every time you enter the hideout (not limited to "already visited"), and disappears once the status no longer matches. A trigger without `if` keeps the old behaviour (appears once, on first visit only).
- **By default the Visit tab only shows once the trader is unlocked** (applies to every trader that has a `.dlg`). Use `tab: always` to force it to always show (even while locked), or `tab: if quest=status` to gate on quest status instead.

## Nodes

```
<nodeName> bg: backgroundFile
> narration text (black-screen subtitle, can be multiple lines)
NPC line (plain line; multiple lines = play one segment at a time)
- option text -> target
- option text -> target | command, command...
```

- **Node names** allow only letters / digits / `_` / `.` / `-`, so `[action]` and `<rich text>` inside lines are never mistaken for a node name.
- **bg:** a plain file name is looked up under `backgrounds/`; a path is used as-is. Omit it to keep the previous background.
- **target**: a node name / `@start` (back to the start node) / `@trade` (open the trade screen) / `@tasks` (open the tasks screen). **Omitting `-> target` closes the dialogue.**
- Lines starting with `#` or `//` are comments; blank lines are free.

## Option commands (after `|`, comma-separated)

| Command | Effect | Auto-show condition |
|---|---|---|
| `accept: quest` | accept the quest | only when "available to start" |
| `handover: quest` | open the item hand-over screen | only when "in progress" |
| `complete: quest` | complete the quest and grant rewards | only when "ready to finish" |
| `if: quest=status[/status]` | explicit show condition (overrides auto) | — |
| `ifnot: quest=status[/status]` | hide while in these states | — |
| `once` | never show again after it is picked once | — |
| `always` | cancel the auto-show condition (for hidden quest flows) | — |

- A quest may be an alias (header `quest xx = ...`) or a full 24-hex ID.
- Status names: `Locked / AvailableForStart / Started / AvailableForFinish / Success / Fail`, or numbers `0`–`5`.
- Combined action: main action (`complete` / `handover`) first, then `accept:` = perform it **and** accept another quest at the same time, e.g.
  `| complete: usb, accept: mats, always`

## Relationship with JSON

- A `.dlg` compiles to exactly the same `DialogTree` model as a `.json` at load time; there is no runtime difference.
- `.json` stays supported forever, and also supports the newer `"hideoutTriggers": [...]` embedded field.
- If a line contains ` -> ` or ` | ` (with surrounding spaces) it is read as a separator — almost impossible in normal dialogue; if it really happens, write that node in `.json` instead.

Sora is the Best~
