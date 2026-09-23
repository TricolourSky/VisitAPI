# VisitAPI

[English](README.md) | **简体中文**

给 SPT 商人（含自定义商人）加上 EFT 1.x 正式版风格「访问」对话系统的开源框架——用纯文本 `.dlg` 剧本，让任何商人拥有 3D 房间对话、剧情章节、好感度、任务区域与战局内对话触发点。内容以**内容包**的形式丢进去就生效；EFT 1.1 主线连同商人房间就是这样一个包。

> 目标版本：**SPT 4.1.6+**（4.1.x，EFT 0.16.9）· 客户端：BepInEx 5.4.23 插件（net472）· 服务端：SPT 模组（net10.0）· 兼容 Fika · 当前版本 **1.3.3**

![Showcase](docs/showcase.png)

## 功能

- **访问按钮** —— 商人界面出现正式版同款「访问」页签，点击进入 3D 房间对话
- **商人房间** —— 七位原版商人（Prapor、Therapist、Fence、Skier、Mechanic、Ragman、Jaeger）的 1.1 房间：口型、动作、环境音、正式版加载屏；随 EFT11 附加包提供（见安装）
- **.dlg 剧本** —— 节点、选项、条件门（等级 / 好感 / 任务状态 / `ifitems`）、分支记号（`set:` / `ifvar:`）、一次性选项（`once` / `always` / `first`）、图片 / 视频 / 3D 场景背景、语音+BGM、好感度（`standing:`）、任务推进（`accept:` / `complete:` / `handover:` / `setstatus:`）
- **一份剧本多种语言** —— 任何一句显示文字的下一行写 `en: 译文`，游戏按自己的语言显示译文，没译的句子显示原文；SPT 的 17 种语言代码都行
- **原生 Narrate 管线** —— 原版商人走 EFT 内置的访问系统 + 零售对话数据回放（口型 / 字幕 / 分支变量全原生），含 1.1 的「关键抉择」确认窗
- **任务系统** —— 内容包里的任务 JSON，接取 / 上交 / 完成全部走原生网络事务；任务图片、带 3D 模型的任务物品和它们的战局刷新点
- **任务区域** —— 包里的 `zones\*.json` 在地图坐标放置到访 / 放置物品区域，进图时按游戏自己的触发器生成，可带战局内字幕、语音和互动提示
- **战局内 / 藏身处触发点** —— `trigger:` 在地图坐标放置对话点（距离 + 视角锥 + 任务门控），或 `enter <秒>` 按时间起爆；`once` 让触发点每个档案只触发一次
- **商人定时联系** —— 剧情任务的前置带 `availableAfter` 时任务会等定时走完，到点后商人卡片上和玩家名旁边亮起 1.1 的金色电话角标，去找商人对话接下任务
- **原生字幕框旁白** —— `>` 旁白行走游戏自己的字幕条；点击或按空格推进
- **任务横幅** —— 任务开始 / 达成 / 完成 / 失败用正式版观感的横幅播报，借原生通知底盘，一条一条排队弹出
- **章节系统（剧情页）** —— 1.1 正式版剧情页：章节图标列、横幅、主 / 可选目标、带相关物品的日记、未读提示、章节横幅与音效、多个结局；章节按解锁先后排；剧情任务不进支线和商人列表；已读状态跟档案走
- **剧情解锁商人与地图** —— 由剧情任务解锁的商人在新档案里一开始是锁着的；任务也可以解锁地图（实验性，只对新建角色）
- **进度持久化** —— 对话变量与 1.1 风格的变量组经服务端回放写入档案，跨会话不丢
- **内容包** —— 服务端加载的一切都住在 `packs\<包名>\` 里，带一份 `pack.json`；丢一个文件夹进去就生效，拿走就没了；包与包之间撞了东西启动时点名报出来

## 剧本编辑器

**[VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor)** 是官方配套工具——本地网页端的 `.dlg` 剧本 / 任务 / 章节可视化编辑器，带语言页签、实时预览，以及从这个框架踩过的坑里总结出来的校验规则。它随框架发布包一起给。写剧情从它开始，比手写脚本舒服得多。

## 安装

1. 从 [Releases](https://github.com/TricolourSky/VisitAPI/releases) 下载：
   - `VisitAPI-x.y.z.zip` —— 客户端插件 + 服务端模组 + 剧本编辑器 `VisitAPI.Editor.exe`。**必装。** 解压到 SPT 根目录。它**不包含**任何剧本、任务、房间和文案，只有它们该放的文件夹。编辑器落在 `EscapeFromTarkov.exe` 旁边，双击即在浏览器打开（只监听本机 127.0.0.1）。
   - `VisitAPI-EFT11-x.y.z-Part1/2/3.zip` —— **EFT11 附加包**：EFT 1.1 主线（塔科夫之旅 + 陨落星辰）连同七位商人的 3D 房间和零售商人对话。可选。每卷都是以 SPT 根目录为解压点的完整压缩包，三卷都要解压。不装的话，自定义 `.dlg` 照样能用图片 / 视频背景，但原版商人的访问按钮没有房间可进。
2. `.dlg` 剧本放 `<SPT>\BepInEx\config\VisitAPI\<商人id>.dlg`；背景和音频放旁边的 `backgrounds\`、`audio\`。
3. 你自己的任务、文案、区域、图片放进一个包：`<SPT>\SPT_Runtime\user\mods\VisitAPI-Server\packs\<随便起个名>\`（编辑器会替你建）。

**从 1.3.2 或更早升级：** 把新的框架 zip 覆盖解压到游戏目录，然后把 `VisitAPI-Server\db\`、`images\`、`bundles\` 搬进 `VisitAPI-Server\packs\<随便起个名>\`（布局见下），再把 `BepInEx\plugins\VisitAPI\bundles\` 改名 `ui\`、`scenes\bundles\vendors\` 改名 `rooms\`。老位置 1.3.3 照读，日志里会提示。以前装过实验版 1.1 主线数据（在 `db\` 里）的，删掉它、改装 EFT11 附加包。

## 内容包

一个包 = `SPT_Runtime\user\mods\VisitAPI-Server\packs\` 下的一个文件夹：

```
packs\<包名>\
  pack.json          name、version、requires（如 "~1.3.3"）、author、description {ch, en}
  LICENSE            可选，发布的包带上
  quests\*.json      SPT 任务文件（章节就是加了几个开关的任务，见下）
  locales\<语言>.json  任务文案、日记正文、目标提示
  dialogues\*.json   零售格式的对话，给原生 Narrate 管线用
  zones\*.json       任务区域（地图、坐标、尺寸，可带字幕）
  items\*.json       任务物品模板；loot\*.json 它们的战局刷新点
  bundles\ + bundles.json   这些物品的 Unity 资源包
  variables\groups.json     1.1 变量组（组的值 = 成员变量之和）
  images\banners\    任务横幅（任务的 image 字段，路由 /files/quest/icon/<名字>）
  images\icons\      章节图标（visitapi.icon，路由 /files/quest/chapters_icon/<名字>）
```

包按名字顺序加载。两个包带了同一条任务 / 区域 / 物品 / 图片 / 文案键，服务端日志里点名、先来的赢。`requires` 对照框架版本检查（`~1.3.3` = 1.3.3 起的任何 1.3.x），对不上只记日志、照样加载。包的客户端文件住在 `BepInEx\plugins\VisitAPI\rooms\`（商人房间），框架自己的界面包在旁边的 `ui\`。

## 从源码构建

需要本机 SPT 4.1.x 安装（取引用 DLL），以及与本仓库并列、文件夹名为 `VisitAPI Editor` 的 [VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor) 仓库。有两块共享源码是从那边链源码编进来、而不是引用 DLL：`.dlg` 解析器（`src/VisitAPI.Dlg`）和内容包布局（`src/VisitAPI.Packs`）。一份剧本、一个包是什么意思，两边必须逐字一致，所以改动放在那边、两边一起重编。

```
dotnet build Client\VisitAPI.csproj        -c Release -p:EftDir=<你的SPT目录> [-p:DlgSrc=<VisitAPI.Dlg源码目录>]
dotnet build Server\VisitAPI-Server.csproj -c Release -p:SptDir=<你的SPT目录>\SPT_Runtime [-p:PacksSrc=<VisitAPI.Packs源码目录>]
```

游戏目录存在时构建会自动部署 DLL（`-p:SkipDeploy=true` 可跳过；服务端在跑时拷不进去）。不在仓库里、要从发布包取的东西：两个界面包（`BepInEx\plugins\VisitAPI\ui\*.bundle`，客户端构建会从 `content\ui\` 顺手拷贝，有就拷），以及几样从游戏里提取、内嵌进 DLL 的图片和音频（效果贴图、1.1 角标、关键抉择窗的美术、对讲机语音）。没有它们插件照样能编能跑——每样缺失记一行日志，对应的画面退回默认或跳过。

## .dlg 快速上手

```
trader: 5ac3b934156ae10c4430e83c "示例商人"
  en: Example Trader
start: root

<root> bg: room.png
> 旁白 —— 走游戏自己的字幕条。
  en: Narration - plays in the game's own subtitle bar.
你来了。今天要点什么？
- 看看货。 -> @trade
- 有活给我吗？ -> @tasks
- 没事，走了。
```

文件名取商人的 24 位十六进制 id，放进 `BepInEx\config\VisitAPI\` 即可。译文行写在它翻译的那一行正下方，`<语言代码>: 文字`（缩进随意；代码：`ch cz en es es-mx fr ge hu it jp kr pl po ro ru sk tu`），没译文的句子显示原文。完整语法（条件门 / 触发点 / 任务 / 好感度 / 多媒体）见编辑器内文档。

## 章节系统（剧情页）

章节 = 一条普通任务 JSON 加几个开关，它目标里的「完成任务」就是子任务清单：

```json
"visitapi": { "chapter": true, "icon": "/files/quest/chapters_icon/ch1.png", "order": 1 },
"image": "/files/quest/icon/ch1_banner.png",
"notes": { "Started": "<24位hex日记id>", "Success": "<24位hex日记id>" },
"conditions": { "AvailableForFinish": [
  { "conditionType": "Quest", "target": "<子任务A的id>", "status": [4], "id": "<24位hex>", "index": 0 },
  { "conditionType": "Quest", "target": "<子任务B的id>", "status": [4], "id": "<24位hex>", "index": 1 }
] }
```

- 任一子任务开始，章节自动开始；子任务全部完成，章节自动交（邮件、奖励照原生走）；标为终章的子任务一完成章节就结束，终章可以有多个
- `notes`：任务到达 Started / Success / Fail 时解锁一条日记；目标条件上也可以带 `questNoteId`（目标打勾那一刻解锁）。日记正文放 locale，键就是日记 id
- 子任务的 `visitapi` 开关：`autoStart`（所属章节已开始、自己的前置也满足时自动接下）、`autoFinish`（一达成就自动交）、`startAfter`（额外前置任务 id）、`anyOf`（`true`，或一组目标 id：其中任一完成即算完成）、`items`（相关物品模板 id，`craft:` / `offer:` 前缀标类型）、`noteLinks`（每条日记各自挂的相关物品）、`unlockTraderOnReady`（任务达成即解锁商人）
- `unlockDialogue`：一组商人 id，这条任务完成后这些商人的访问按钮才出现；`unlockLocations`：这条任务解锁的地图（只对新建角色）
- `order`：同时解锁的章节之间的排序（小的在前）；`dialogOnly`：任务列表的按钮换成「去找 X」，接交只走对话
- 顶层 `isStoryQuest` 标记 1.1 剧情任务：没有物品奖励的完成信不寄；`notDisplayedQuest` 把任务藏出列表
- locale 里的目标文案：`<条件id> desc`（小字）、`<条件id> talk`（哪一行出「去找 X」按钮、写什么字）
- 子任务失败不影响章节；想让被作废的子任务不挡住章节完成，把章节里那条「完成任务」条件的 `status` 写成 `[4, 5, 6]`
- 横幅放包的 `images\banners\`，章节图标放 `images\icons\`；所有 id 必须 24 位十六进制
- 想让剧情任务回到普通列表：`BepInEx\config\com.sora.visitapi.cfg` 里关掉 `HideStoryQuestsInLists`

这些编辑器都会替你写，上面的清单是给手读文件的人看的。

## 配置

`BepInEx\config\com.sora.visitapi.cfg`：

| 段 | 键 |
|---|---|
| `General` | `Language` —— `auto`（跟游戏）、`zh` 或 `en`；也决定 `.dlg` 显示哪种译文 |
| `TalkButton` | `OffsetX` / `OffsetY` —— 访问按钮位置 |
| `Chapter` | `ShowUnstartedChapters`、`CustomChapterOrder`（自制章节在官方 1～10 章之间的位次）、`HideStoryQuestsInLists` |
| `Narrate` | 访问相机：`LevelCamera`、`Fov`、`PixelLights`、`ShaderSource`（`game` / `bundle`）、`DimReflection`、`AmbientReflection`、`DecalDirect` |
| `Badge` | `CallBadge`（商人有话说时的金色电话）、`HandoverBadge`（1.1.5 的商人卡片角标） |
| `Debug` | `CoordKey` 打印相机坐标供触发点填写，`AbortVisitKey` 强制退出卡住的访问；默认都不绑 |

## 参与开发

欢迎提 Bug、功能建议和 Pull Request，见 [CONTRIBUTING.md](CONTRIBUTING.md)。请不要在 issue 或 PR 里附游戏文件、反编译源码或提取出来的资源。

## 免责声明

本仓库**不包含任何 BSG 游戏资产**。商人房间、零售对话数据与 EFT 1.1 主线数据作为 EFT11 附加包经 Releases 单独分发。本项目与 Battlestate Games、SPT 官方无关。

## 许可

MIT（见 [LICENSE](LICENSE)）。
