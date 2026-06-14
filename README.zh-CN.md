# VisitAPI

[English](README.md)

> **一句话:给游戏里的任意商人加一个「拜访」对话系统,用一个剧本文件写剧情。**

放一个 `{traderId}.dlg` 剧本文件,商人界面就多出「拜访」页签;还能在**战局地图**和**藏身处**里放置拜访交互点。对话支持:分支选项、旁白演出、背景图/视频、任务接取/递交/完成、任务链联动、首次见面专属剧情、战局后随机对话。界面复用原版商人对话框,风格与游戏一致。

## 要求

| 组件 | 版本 |
|---|---|
| SPT | 4.0.13 |
| BepInEx | 5.4.23.2(随 SPT 附带) |

## 安装

把发布包解压进 SPT 根目录:

```
SPT/
├─ BepInEx/
│  ├─ plugins/VisitAPI/VisitAPI.dll        ← 客户端插件
│  └─ config/VisitAPI/                      ← 你的对话剧本 + 媒体
│     ├─ <商人ID>.dlg                       ← 每个商人一个剧本
│     └─ backgrounds/                       ← 背景图与视频
└─ user/mods/VisitAPI-Server/              ← 服务端模组(任务功能需要)
   ├─ VisitAPI-Server.dll、package.json
   └─ db/quests、db/locales、quest_transitions.json
```

**服务端模组仅在用到任务动作(接取/递交/完成)时才需要**。纯对话、触发器、`@trade` / `@tasks` 只装客户端即可。

## 快速上手

新建 `BepInEx/config/VisitAPI/<商人ID>.dlg`(UTF‑8 编码):

```
trader: 5ac3b934156ae10c4430e83c "Ragman"
start: root

<root>
要点什么?随便看。
- 看看你的货。 -> @trade
- 就打个招呼。
- 回头见。
```

进游戏打开该商人 → 出现「拜访」页签。改 `.dlg` 即时热加载——重开对话即可,无需重启游戏。

## 功能

- 任意商人的**「拜访」页签**,复用原版对话界面
- **分支对话**:旁白、`{player}` 占位符、背景图 / 循环视频
- **战局触发器**:走到地图某点注视并交互
- **藏身处触发器**:在藏身处区域(如情报中心)交互
- **任务**:对话里接取 / 递交 / 完成、任务链联动、隐藏触发任务
- **忠诚等级 / 首次见面**拜访选项、战局后随机对话
- `.dlg` 剧本热加载(旧版 `.json` 仍兼容)

## 示例(以 Ragman 为例)

发布包内 `examples/` 提供一份开箱即用的 **Ragman** 示例:

| 文件 | 演示内容 |
|---|---|
| `BepInEx/config/VisitAPI/5ac3b934156ae10c4430e83c.dlg` | 战局触发器 + 藏身处触发器 + 对话 + 任务接取/完成 |
| `user/mods/VisitAPI-Server/db/quests/ragman_visit_demo.json` | 商人任务 JSON |
| `user/mods/VisitAPI-Server/db/locales/en.json`、`zh-cn.json` | 任务文本 |

把这些文件拷进 SPT 对应目录即可在 Ragman 上试玩;删掉即移除示例。

## 文档

- `.dlg` 剧本完整语法 — [docs/DLG_FORMAT.zh-CN.md](docs/DLG_FORMAT.zh-CN.md)
- 详细指南(中文) — [docs/GUIDE.zh-CN.md](docs/GUIDE.zh-CN.md)

## 参与项目贡献
如果你想参与这个项目请在Discord联系我 @tricoloursky
