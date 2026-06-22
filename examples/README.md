# Examples / 示例

### `minimal.dlg`

A minimal, **client-only** trader dialogue — no server, no quests, no media required. It shows the core pieces:

- **header** — `trader:` / `start:` / `first:`
- **`when:`** — a level-gated root (`root_regular` for level 15+, else `root`)
- **`trigger: raid`** — an in-world "Talk" point in a Ground Zero raid
- **`>` narration** — subtitle lines, click to advance
- **`{player}`** — replaced with the character nickname
- **`@trade`** — option that jumps to the trade screen
- **branching** + node jumps back to `root`

**Try it:** the file name is Ragman's id, so just drop it in `BepInEx/config/VisitAPI/` and restart. Open Ragman out of raid → a **Talk** button appears. (Rename it to your own trader's 24-hex id to use it elsewhere.) Backgrounds are optional — add `bg: yourfile.png` to a node and put the file in `BepInEx/config/VisitAPI/backgrounds/`.

For quests (`accept:` / `handover:` / `complete:`), trader standing (`standing:`), hideout triggers, and everything else, see **[../docs/DLG_FORMAT.md](../docs/DLG_FORMAT.md)** ([中文](../docs/DLG_FORMAT.zh-CN.md)). Quest directives also need a quest registered by the server mod (`db/quests/*.json`).

---

### `minimal.dlg`（中文）

一个最小的、**纯客户端**商人对话——不需要服务端、不需要任务、不需要素材。它演示了核心要素：

- **头部** —— `trader:` / `start:` / `first:`
- **`when:`** —— 按等级选根节点（15 级以上进 `root_regular`，否则 `root`）
- **`trigger: raid`** —— 新手地带突袭里的一个世界内「拜访」点
- **`>` 旁白** —— 字幕行，点击推进
- **`{player}`** —— 替换成角色昵称
- **`@trade`** —— 跳到交易界面的选项
- **分支** + 跳回 `root`

**试用：** 文件名就是 Ragman 的 id，直接丢进 `BepInEx/config/VisitAPI/` 重启即可。突袭外打开 Ragman → 出现**对话**按钮。（改成你自己商人的 24 位 id 就能用在别处。）背景可选——给节点加 `bg: 你的文件.png`，文件放进 `BepInEx/config/VisitAPI/backgrounds/`。

任务（`accept:` / `handover:` / `complete:`）、好感度（`standing:`）、藏身处触发器等见 **[../docs/DLG_FORMAT.zh-CN.md](../docs/DLG_FORMAT.zh-CN.md)**（[English](../docs/DLG_FORMAT.md)）。任务指令还需要服务端 mod 注册任务（`db/quests/*.json`）。
