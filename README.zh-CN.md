# VisitAPI

[English](README.md) | **简体中文**

给 SPT 商人（含自定义商人）加上 EFT 1.0 正式版风格「拜访」对话系统的开源框架——用纯文本 `.dlg` 剧本，让任何商人拥有 3D 场景对话、剧情任务、好感度与战局内对话触发点。

> 目标版本：**SPT 4.1.3**（EFT 0.16.9.5）· 客户端：BepInEx 5.4.23 插件（net472）· 服务端：SPT 模组（net10.0）

## 功能

- **访问按钮** —— 商人界面出现 1.0 同款「访问」页签，点击进入 3D 场景对话
- **.dlg 剧本** —— 节点、选项、条件门（等级 / 好感 / 任务状态 / `ifitems`：玩家身上真有要交的东西时这句才显示）、分支记号（`set:` / `ifvar:`）、一次性选项（once/always/first）、图片/视频/3D 场景背景、语音+BGM、好感度（`standing:`）、任务状态推进（`setstatus:`）
- **原生 Narrate 管线** —— 原版商人走 EFT 内置的休眠拜访系统 + 零售对话数据回放（口型/字幕/分支变量全原生）
- **任务系统** —— 自定义任务 JSON，接取/上交/完成全部走原生网络事务，附任务图片路由
- **战局内/藏身处触发点** —— `trigger:` 语法在地图坐标放置对话点（距离+视角锥+任务门控）；也可以写 `enter <秒>` 按时间起爆（不用坐标），进图落地几秒后自动接取任务
- **原生字幕框旁白** —— `>` 旁白行走游戏自己的字幕条（`SubtitlesView`），不再挤在商人对话框里；点击或按空格推进
- **任务横幅** —— 任务开始 / 达成要求 / 完成 / 失败用正式版观感的横幅播报，借的是原生通知底盘（立起躺下动画、音效、排队全原生），SPT 自己的通知一根毛都不动
- **进度持久化** —— 对话变量经服务端回放写入档案，跨会话不丢
- **章节系统** —— 任务页多出「剧情」页签（1.1 正式版剧情页原件），把若干任务组成一章：章节图标列、横幅、主/可选目标、日记、相关物品、未读提示、1.1 同款章节横幅与音效；剧情任务只住剧情页，不挤进支线列表

## 剧本编辑器

**[VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor)** 是官方配套工具——本地网页端的 `.dlg` 剧本可视化编辑器。想给商人写自己的对话剧情，从它开始比手写脚本舒服得多。

## 安装

1. 从 [Releases](https://github.com/TricolourSky/VisitAPI/releases) 下载两个包：
   - `VisitAPI-x.y.z-spt4.1.3.zip` —— 客户端插件 + 服务端模组，**外加配套的剧本编辑器 `VisitAPI.Editor.exe`**，解压到 SPT 根目录
     （编辑器就落在 `SPT.Server.exe` 旁边，双击即可，会在浏览器里打开；不用安装，只监听本机 127.0.0.1）
   - `VisitAPI-scenes-tarkin.zip` —— 3D 商人房间，解压到 SPT 根目录
     场景资源源自 [bmpq/spt-tradermod](https://github.com/bmpq/spt-tradermod)（MIT），体积原因作为 Release 附件分发
2. `.dlg` 剧本放 `<SPT>\BepInEx\config\VisitAPI\<商人id>.dlg`
3. 发布包含框架本体和它所需的零售对话数据（`db/dialogues/dialogue.json`，用于原版商人的台词回放）。
   **不包含**任何剧本、任务和文案 —— 只有它们该放的空目录。

## 从源码构建

需要本机 SPT 4.1.3 安装（取引用 DLL），以及与本仓库并列 checkout 的 [VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor) 仓库（`.dlg` 解析器源码链编自它）：

```
dotnet build Client\VisitAPI.csproj  -c Release -p:EftDir=<你的SPT目录> [-p:DlgSrc=<VisitAPI.Dlg源码目录>]
dotnet build Server\VisitAPI-Server.csproj -c Release -p:SptDir=<你的SPT服务端目录>
```

游戏目录存在时构建会自动部署（Deploy target），不存在则静默跳过。

## .dlg 快速上手

```
trader: 5ac3b934156ae10c4430e83c "示例商人"
start: root

<root> bg: room.png
> 旁白 —— 走游戏自己的字幕条。
你来了。今天要点什么？
- 看看货。 -> @trade
- 有活给我吗？ -> @tasks
- 没事，走了。
```

文件名取商人的 24 位十六进制 id，放进 `BepInEx\config\VisitAPI\` 即可。
完整语法（条件门/触发点/任务/好感度/多媒体）见 [`examples/minimal.dlg`](examples/minimal.dlg)。

## 章节系统（剧情页）

章节 = 一条普通任务 JSON 加几个开关，它目标里的「完成任务」就是子任务清单：

```json
"visitapi": { "chapter": true, "icon": "/files/quest/icon/ch1_icon.png" },
"image": "/files/quest/icon/ch1_banner.png",
"secretQuest": true,
"notes": { "Started": "<24位hex日记id>", "Success": "<24位hex日记id>" },
"conditions": { "AvailableForFinish": [
  { "conditionType": "Quest", "target": "<子任务A的id>", "status": [4], "isFinisher": false, "id": "<24位hex>", "index": 0 },
  { "conditionType": "Quest", "target": "<子任务B的id>", "status": [4], "isFinisher": true,  "id": "<24位hex>", "index": 1 }
] }
```

- 任一子任务开始，章节自动开始；子任务全部完成，章节自动交（邮件、奖励照原生走）
- `notes`：任务到达 Started / Success / Fail 时解锁一条日记，正文放 locale，键就是日记 id；章节和子任务都可以带
- 子任务上的开关：`autoStart`（**所属章节已经开始**、且自己的前置也满足时自动接下）、`autoFinish`（一达成就自动交，1.1 的剧情任务都是这样）、`items`（相关物品的模板 id 列表，上交/找到类目标里的物品会自动带上）
- 想让整章自己跑起来，把 `autoStart` 标在**章节**上（章节不受上面那道闸）。标在第一条子任务上没用：人还在菜单里任务书刚建好，它就把那条子任务塞给玩家了
- 剧情任务（章节 + 子任务）不进支线 / 商人任务列表，接交靠对话（`accept:` / `complete:`）、触发点或上面的自动开关；目标行会按 `.dlg` 自动出「去找 X」「去现场：地图」「去藏身处」按钮
- 不在章节里的任务可以标 `dialogOnly`：任务列表的接受/完成按钮换成「去找 X」，接交只走对话
- **失败也能继续**：子任务失败时目标行画红叉并标「(已失败)」，章节照常进行（照 1.1 正式版）。想让被作废的子任务不挡住章节完成，把章节里那条「完成任务」条件的 `status` 写成 `[4, 5, 6]`（编辑器：子任务 ⋮ →「失败也算过」）
- 触发点除了 `accept` 还能 `finish` / `fail`：走到某处或进图 N 秒后，直接把某条任务判完成或判失败，不弹对话
  ```
  trigger: raid Sandbox enter 10 accept <任务A>          # 进中心区 10 秒后自动接下 A
  trigger: raid Sandbox (x, y, z) dist 6 auto fail <A> accept <B>   # 走到这里 A 作废、B 开始
  ```
- 图标 / 横幅放模组的 `images/quest/icon/`；**所有 id 必须 24 位十六进制**
- 完整可跑的例子见 [`examples/chapter/`](examples/chapter/)；编辑器 1.2.0 有独立的「章节编辑」模块，任务属性面也有全部开关
- 想让剧情任务回到普通列表：`BepInEx/config/com.sora.visitapi.cfg` 里关掉 `HideStoryQuestsInLists`

## 免责声明

本仓库**不包含任何 BSG 游戏资产**。零售对话数据与场景包经 Releases 分发。本项目与 Battlestate Games、SPT 官方无关。

## 许可

MIT（见 [LICENSE](LICENSE)）。场景包版权归 bmpq/tarkin（MIT），再分发须保留其署名。
