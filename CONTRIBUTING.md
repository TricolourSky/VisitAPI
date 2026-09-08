# Contributing / 参与开发

Thanks for helping with VisitAPI. This page is short on purpose — read it once before opening an issue or a pull request.

感谢你为 VisitAPI 出力。这页很短，提 issue 或 PR 之前读一遍即可。

## Ground rules / 基本规矩

- **No game files.** Never commit or attach anything taken from the game: decompiled sources, extracted assets, bundles, dialogue data, captured server responses. The repository holds original source code only.
  **不带游戏文件。** 反编译源码、提取的资源、bundle、对话数据、抓包响应，一律不能进仓库或附在 issue / PR 里。仓库只放原创源码。
- **The `.dlg` syntax is frozen.** Changes to the script grammar or to the dialogue id scheme break every existing script and save file; they need an issue and a maintainer's go-ahead first.
  **`.dlg` 语法是冻结契约。** 改语法或对话 id 算法会让现有剧本和存档全部失效，先开 issue 讨论并得到维护者同意。
- **Bundle contracts are frozen.** Class and field names under `Client/ChapterUI/Contract/` and `Client/Native111/` are serialization contracts with the asset bundles — add, never rename or delete.
  **bundle 契约冻结。** `Client/ChapterUI/Contract/` 与 `Client/Native111/` 里的类名 / 字段名是资源包的序列化契约——只许加，不许改名或删除。
- **One Harmony patch table.** New patch classes must be registered in `Client/Core/Patches.cs`, otherwise they never run.
  **补丁只有一张表。** 新补丁类必须登记进 `Client/Core/Patches.cs`，否则等于没写。
- **No diagnostic hotkeys.** Evidence comes from logs and screenshots; do not bind keyboard keys for probes.
  **不用热键做诊断。** 取证靠日志和截图，别绑键盘键。

## Reporting a bug / 报 Bug

Use the bug-report template. Always include: SPT version, whether Fika is installed, `BepInEx/LogOutput.log` lines tagged `[narrate]`, `[dlg]`, `[quest]`, `[chapter/...]` or `[trigger]`, and a screenshot when the problem is visual.

用 Bug 模板。务必附上：SPT 版本、是否装了 Fika、`BepInEx/LogOutput.log` 里带 `[narrate]`、`[dlg]`、`[quest]`、`[chapter/...]`、`[trigger]` 标签的行，画面问题附截图。

## Pull requests / 提 PR

1. Fork, branch from `main`, keep one topic per PR.
2. Build both projects with `-p:SkipDeploy=true` — 0 warnings, 0 errors.
3. Test in game and say what you tested; paste the relevant log lines in the PR.
4. Fill in the PR template. Small, reviewable diffs get merged fastest.

1. Fork 后从 `main` 开分支，一个 PR 只做一件事。
2. 两个工程都用 `-p:SkipDeploy=true` 编一遍——0 警告 0 错误。
3. 实机测过，写清楚测了什么，把相关日志行贴进 PR。
4. 按 PR 模板填写。小而清楚的改动合得最快。

## Building / 构建

See the "Build from source" section of the README. You need a local SPT 4.1.x install and the VisitAPI Editor repository next to this one.

见 README 的「从源码构建」。需要本机 SPT 4.1.x 安装，以及与本仓库并列的 VisitAPI Editor 仓库。
