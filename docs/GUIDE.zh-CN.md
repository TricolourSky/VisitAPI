# VisitAPI — 完整指南

[English](GUIDE.md)

> **一句话：给游戏里的任意商人加一个「拜访」对话系统，用一个剧本文件写剧情。**

放一个 `{traderId}.dlg` 剧本文件，商人页面就多出「拜访」页签，地图和藏身处可以放置拜访触发点。对话支持分支选项、旁白演出、背景图/视频、任务接取/递交/完成、任务链联动、首次见面专属剧情、战局后随机对话。界面复用原版 TraderDialogScreen，与游戏风格完全一致。

---

## 要求

| 组件 | 版本 |
|---|---|
| SPT | 4.0.13 |
| BepInEx | 5.4.23.2（随 SPT 附带） |

## 安装

```
SPT/
├── BepInEx/
│   ├── plugins/VisitAPI/VisitAPI.dll        ← 客户端插件
│   └── config/VisitAPI/                     ← 对话剧本与媒体（见下）
│       ├── {traderId}.dlg                   ← 对话剧本（主格式）
│       ├── {traderId}.json                  ← 旧版 JSON 格式（兼容，.dlg 优先）
│       ├── {traderId}.seen.json             ← 运行时生成：已拜访/一次性选项记录
│       └── backgrounds/                     ← 背景图与视频
└── user/mods/VisitAPI-Server/               ← 服务端模组（任务功能需要）
    ├── VisitAPI-Server.dll
    ├── package.json
    ├── db/
    │   ├── quests/*.json                    ← 任务定义（SPT 4.0.13 扁平格式）
    │   ├── locales/{en,ch,zh-cn}.json       ← 任务文本
    │   └── quest_transitions.json           ← 任务联动（A 完成 → B 变状态）
    ├── quest_state/                         ← 运行时生成：已接取记录（按档案）
    └── quest_state_completed/               ← 运行时生成：已完成记录（按档案）
```

> 只用对话/背景/`@trade`/`@tasks` 时服务端可不装；用任务动作（accept/handover/complete）时必须装。

---

## 快速上手：一个最小剧本

`BepInEx/config/VisitAPI/{traderId}.dlg`（UTF-8 编码）：

```
trader: 90726f6a656374536f726132 "SORA"
start: root

<root> bg: SORA.png
你来了，随便看。
- 看看你有什么好东西。 -> @trade
- 没事，先走了。
```

保存后重新打开「拜访」页签即可生效（热加载，无需重启游戏）。

## .dlg 剧本语法

完整参考见 **[docs/DLG_FORMAT.md](docs/DLG_FORMAT.md)**，速查：

```
trader: <商人ID> "显示名"
start: <起始节点>
first: <首次拜访节点>
trigger: raid <地图> (x, y, z) door 宽x高[x转角] dist 3 radius 1.2 "提示"
trigger: hideout <区域名> level 2 dist 4 node <节点> if 任务=状态 "提示"
when: level>=4 -> <节点>                  ← 按商人忠诚等级/好感选起始节点
random: 10% <节点1> <节点2>               ← 战局后随机对话
quest <别名> = <24位任务ID>
tab: if 任务=状态                          ← "拜访"页签仅在任务处于该状态时显示

<节点名> bg: 背景文件
> 旁白行（黑屏字幕，逐段点击推进）
NPC 台词（普通行，多行=多段）
- 选项文本 -> 目标 | 指令, 指令...
```

- 目标：节点名 / `@start`（重选起始节点）/ `@trade` / `@tasks`；省略 = 关闭对话
- 指令：`accept:任务` `handover:任务` `complete:任务`（自动按任务状态门控显隐）、
  `if:任务=状态[/状态]`、`ifnot:...`、`once`（一次性）、`always`（取消自动门控）、
  组合 `complete:A, accept:B`（完成 A 同时接取 B）
- 文本支持 `{player}` / `{playerName}` 占位符（替换为角色昵称）
- 旧版 JSON 格式继续兼容（字段一一对应，含新增的 `hideoutTriggers` / `tabQuestId` / `tabShowWhenStatus`）

## 触发器

| 类型 | 写法 | 行为 |
|---|---|---|
| 战局（raid） | `trigger: raid 地图 (x,y,z) ...` | 玩家靠近并注视位置出现交互提示，仅**首次拜访**生效一次 |
| 藏身处（hideout） | `trigger: hideout 区域 level N ...` | 区域达到等级后出现交互提示；带 `if 任务=状态` 时由**任务状态门控**（状态匹配期间可重复出现）；不带则仅首次拜访一次 |

- 区域名 = EAreaType 枚举：IntelligenceCenter / Generator / MedStation / Workbench …
- 坐标获取：进战局按 **F8**，BepInEx 日志输出可直接粘贴的坐标
- 区域等级在藏身处内升级后，触发器每 10 秒自动补检，无需重进

## 任务系统（服务端）

**任务定义**：`db/quests/*.json`，SPT 4.0.13 扁平格式（`conditionType` 顶层），结构参考 `sora_storage_device.json`。任务 ID 为 24 位小写十六进制，惯例用 ASCII 转 hex（`soradevice01` → `736f72616465766963653031`）。

**文本**：`db/locales/{en,ch,zh-cn}.json`，键格式 `"<任务ID> name/description/..."`，条件目标文本键 = 条件 ID。`zh-cn` 会自动同时注册为 SPT 内部中文代码 `ch`。`startedMessageText`/`successMessageText` 置空可禁用商人邮件。

**任务联动**：`db/quest_transitions.json`——`triggerQuestId` 完成时把 `dependentQuestId` 置为 `targetStatus`（如 3=可完成），用于隐藏任务驱动可见任务。

**完成流程**：客户端优先走原生 `FinishQuest`（任务日志/奖励/邮件/音效全由游戏处理，服务端只记录与联动）；任务不在任务书（隐藏任务）时静默完成；其余失败情形回退为本地呈现 + 手动音效。

**HTTP 端点**（`127.0.0.1:6970/visitapi/`，SPT 主服务在 6969）：

| 端点 | 请求体 | 说明 |
|---|---|---|
| `/quest/accept` | `{ProfileId, QuestId}` | 记录接取（原生接取由客户端并行执行） |
| `/quest/handover` | `{ProfileId, QuestId}` | 扣除背包物品，推进至可完成 |
| `/quest/complete` | `{ProfileId, QuestId, Native}` | `Native=true` 只记录+联动；否则写档+发奖 |
| `/quest/status` | `{ProfileId, QuestIds[]}` | 批量查询状态（档案优先，状态文件回填） |
| `/quest/sync` | `{ProfileId}` | 重放任务联动 |

**任务状态值**：`Locked=0, AvailableForStart=1, Started=2, AvailableForFinish=3, Success=4, Fail=5`（.dlg 中状态名与数字皆可）。

## 背景媒体

`bg:` 纯文件名默认到 `config/VisitAPI/backgrounds/` 下找，带路径则原样解析（相对 `config/VisitAPI/`）。静态：png/jpg/jpeg/bmp；视频：mp4/webm/mov/avi/mkv/ogv（自动循环，推荐 H.264 mp4）。商人页签初始背景：`config/VisitAPI/{traderId}.png`。

---

## 工具开发参考（DiaglogDesigner 等）

### 数据流

```
作者编辑                客户端 (VisitAPI.dll)                服务端 (VisitAPI-Server.dll)
─────────              ─────────────────────                ───────────────────────────
{traderId}.dlg  ──►  DialogScriptParser ──► DialogTree ─┐
{traderId}.json ──►  Newtonsoft 反序列化 ───────────────┤（同一运行时模型）
                                                        ▼
                                      商人页签 / 战局触发器 / 藏身处触发器
                                                        │ 任务动作（HTTP :6970）
db/quests/*.json ────────────────────────────────────►  CustomQuestService 注册
db/locales/*.json ───────────────────────────────────►  AddQuestLocales 注册
db/quest_transitions.json ───────────────────────────►  完成时联动
```

### 工具必须知道的约定

| 项 | 约定 |
|---|---|
| 文件编码 | UTF-8（.dlg 用 `File.ReadAllLines(UTF8)` 读取） |
| 加载优先级 | `.dlg` 优先；解析失败自动回退同名 `.json`；解析错误带行号写 BepInEx 日志 |
| 热加载 | 对话树按文件时间戳缓存，保存文件后重开对话即生效 |
| 节点名字符集 | `[A-Za-z0-9_.\-]+`（ASCII），台词中的 `[动作]`/`<富文本>` 不会被误判 |
| 文本禁区 | 选项文本中不能出现 ` -> ` 和 ` \| `（前后带空格，会被当作分隔符） |
| 商人/任务 ID | 24 位小写 hex（惯例 ASCII→hex），或字母数字 `_`/`-` 的模组自定义 ID |
| 运行时模型 | `DialogModels.cs` 的 `DialogTree`（.dlg 与 .json 的共同目标），生成 JSON 的工具以此为 schema |
| 状态存档 | `{traderId}.seen.json`（首次拜访 + once 记录，按档案分键）；删除即重置 |

### 源码地图

| 文件 | 职责 |
|---|---|
| `DialogScriptParser.cs` | .dlg → DialogTree 编译器 |
| `DialogModels.cs` | 对话树运行时模型（JSON schema） |
| `DialogStateStore.cs` | 对话树加载（缓存/回退）+ 拜访/一次性状态存档 |
| `InteractTriggerBase.cs` | 双触发器共用基类（GPO 反射、交互注入、档案提取、FireVisit） |
| `RaidInteractTrigger.cs` / `HideoutInteractTrigger.cs` | 战局/藏身处触发器 |
| `HideoutTriggerConfig.cs` | 藏身处触发器配置模型（兼容旧 hideout_triggers.json） |
| `TraderDealScreenVisitButton.cs` | 「拜访」页签注入、原生对话驱动、节点执行 |
| `TraderDealScreenHook.cs` / `TraderDialogScreenPatch.cs` | Harmony 补丁（商人界面/原生对话放行） |
| `NativeQuestController.cs` | 任务动作：原生接取/完成 + 服务端同步 + 回退呈现 |
| `QuestStatusCache.cs` | 任务状态缓存与可见性判断 |
| `VisitPlugin.cs` | 入口：补丁安装、战局/藏身处生命周期、触发器生成 |
| `Server/VisitApiQuestLoader.cs` | 任务/文本注册（CustomQuestService） |
| `Server/VisitApiQuestHelper.cs` | 任务状态读写、奖励、联动 |
| `Server/VisitApiQuestServer.cs` | 6970 端口 HTTP 路由 |

### 构建与部署

```
dotnet build VisitAPI.csproj -c Release      # 客户端 → 自动部署 BepInEx/plugins/VisitAPI/
dotnet build Server -c Release               # 服务端 → 自动部署 user/mods/VisitAPI-Server/（含 db/ xcopy 同步）
```

注意：xcopy 不删除已移除的文件，删除任务/文本后需手动清理部署目录；`plugins` 目录树内全局只能有一份 VisitAPI.dll。

---

## 调试与常见问题

- **F8**：战局内输出当前坐标到 BepInEx 日志。
- **拜访页签没出现**：文件名是否与 traderId 一致；是否配置了 `tab:` 门控且任务状态不满足。
- **.dlg 改了不生效**：看 BepInEx 日志有无 `sora.dlg:行号: 错误`（解析失败会回退 .json）。
- **任务相关不生效**：任务/文本/联动在服务端启动时加载，改动后**必须重启 SPT 服务端**；对话剧本无需重启。
- **任务没进任务列表**：服务端是否安装并重启；端口 6970 是否被占用；日志搜 `[NativeQuest]`。
- **once 误点重置**：删 `config/VisitAPI/{traderId}.seen.json`。
- **视频警告 "Unexpected timestamp"**：用 Baseline Profile 的 H.264 重编码可消除。

## 版本

`0.2.1` · 适用于 SPT 4.0.13
