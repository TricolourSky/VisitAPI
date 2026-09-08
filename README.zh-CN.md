# VisitAPI

[English](README.md) | **简体中文**

给 SPT 商人（含自定义商人）加上 EFT 1.x 正式版风格「访问」对话系统的开源框架——用纯文本 `.dlg` 剧本，让任何商人拥有 3D 房间对话、剧情章节、好感度与战局内对话触发点。

> 目标版本：**SPT 4.1.x**（EFT 0.16.9）· 客户端：BepInEx 5.4.23 插件（net472）· 服务端：SPT 模组（net10.0）· 兼容 Fika

![Showcase](docs/showcase.png)

## 功能

- **访问按钮** —— 商人界面出现正式版同款「访问」页签，点击进入 3D 房间对话
- **商人房间** —— 七位原版商人（Prapor、Therapist、Fence、Skier、Mechanic、Ragman、Jaeger）各有自己的 3D 房间：口型、动作、环境音、正式版加载屏；房间包单独下载（见安装）
- **.dlg 剧本** —— 节点、选项、条件门（等级 / 好感 / 任务状态 / `ifitems`）、分支记号（`set:` / `ifvar:`）、一次性选项（`once` / `always` / `first`）、图片 / 视频 / 3D 场景背景、语音+BGM、好感度（`standing:`）、任务推进（`accept:` / `complete:` / `handover:` / `setstatus:`）
- **原生 Narrate 管线** —— 原版商人走 EFT 内置的访问系统 + 零售对话数据回放（口型 / 字幕 / 分支变量全原生）
- **任务系统** —— 自定义任务 JSON，接取 / 上交 / 完成全部走原生网络事务，附任务图片路由
- **战局内 / 藏身处触发点** —— `trigger:` 在地图坐标放置对话点（距离 + 视角锥 + 任务门控），或 `enter <秒>` 按时间起爆；`once` 让触发点每个档案只触发一次
- **原生字幕框旁白** —— `>` 旁白行走游戏自己的字幕条；点击或按空格推进
- **任务横幅** —— 任务开始 / 达成 / 完成 / 失败用正式版观感的横幅播报，借原生通知底盘
- **章节系统（剧情页）** —— 1.1 正式版剧情页：章节图标列、横幅、主 / 可选目标、带相关物品的日记、未读提示、章节横幅与音效；剧情任务不进支线和商人列表；已读状态跟档案走
- **进度持久化** —— 对话变量经服务端回放写入档案，跨会话不丢

## 剧本编辑器

**[VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor)** 是官方配套工具——本地网页端的 `.dlg` 剧本与章节任务可视化编辑器。写剧情从它开始，比手写脚本舒服得多。

## 安装

1. 从 [Releases](https://github.com/TricolourSky/VisitAPI/releases) 下载：
   - `VisitAPI-x.y.z.zip` —— 客户端插件 + 服务端模组 + 剧本编辑器 `VisitAPI.Editor.exe`，解压到 SPT 根目录。编辑器落在 `SPT.Server.exe` 旁边，双击即在浏览器打开（只监听本机 127.0.0.1）。
   - `VisitAPI-x.y.z-TraderRooms.zip` —— 3D 商人房间（约 4.3 GB），解压到 SPT 根目录。可选：不装的话只有写了 `.dlg` 的商人才有访问按钮。
2. `.dlg` 剧本放 `<SPT>\BepInEx\config\VisitAPI\<商人id>.dlg`。
3. 发布包含框架本体和原版商人台词回放所需的零售对话数据；**不包含**任何剧本、任务和文案，只有它们该放的空目录。

## 从源码构建

需要本机 SPT 4.1.x 安装（取引用 DLL），以及与本仓库并列 checkout 的 [VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor) 仓库（`.dlg` 解析器源码从它的 `src/VisitAPI.Dlg` 链编进来）：

```
dotnet build Client\VisitAPI.csproj        -c Release -p:EftDir=<你的SPT目录> [-p:DlgSrc=<VisitAPI.Dlg源码目录>]
dotnet build Server\VisitAPI-Server.csproj -c Release -p:SptDir=<你的SPT服务端目录>
```

游戏目录存在时构建会自动部署 DLL（`-p:SkipDeploy=true` 可跳过）。有两样东西故意不在仓库里，从发布包取：`Client/art/bundles/` 下的界面包，以及两张效果贴图 `Client/art/bayer_matrix.png` / `grayscale_ramp.png`（没有它们插件照样能编能跑，对应的相机效果跳过并记一条日志）。

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

文件名取商人的 24 位十六进制 id，放进 `BepInEx\config\VisitAPI\` 即可。完整语法（条件门 / 触发点 / 任务 / 好感度 / 多媒体）见编辑器内文档。

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

- 任一子任务开始，章节自动开始；子任务全部完成，章节自动交（邮件、奖励照原生走）
- `notes`：任务到达 Started / Success / Fail 时解锁一条日记；目标条件上也可以带 `questNoteId`（目标打勾那一刻解锁）。日记正文放 locale，键就是日记 id
- 子任务开关：`autoStart`（所属章节已开始、自己的前置也满足时自动接下）、`autoFinish`（一达成就自动交）、`startAfter`（额外前置任务 id）、`items`（相关物品模板 id，`craft:` / `offer:` 前缀标类型）、`noteLinks`（每条日记各自挂的相关物品）
- `unlockDialogue`：一组商人 id，这条任务完成后这些商人的访问按钮才出现（按剧情逐步开放商人）
- `order`：章节显示顺序（小的在前）；`dialogOnly`：任务列表的按钮换成「去找 X」，接交只走对话
- 子任务失败不影响章节；想让被作废的子任务不挡住章节完成，把章节里那条「完成任务」条件的 `status` 写成 `[4, 5, 6]`
- 章节图标放模组的 `images/quest/chapters_icon/`，横幅放 `images/quest/icon/`；所有 id 必须 24 位十六进制
- 想让剧情任务回到普通列表：`BepInEx/config/com.sora.visitapi.cfg` 里关掉 `HideStoryQuestsInLists`

## 配置

`BepInEx/config/com.sora.visitapi.cfg`：界面语言、访问按钮偏移、章节显示、访问相机（视野、像素光、反射），以及两个可选的调试热键（`Debug.CoordKey` 打印相机坐标供触发点填写，`Debug.AbortVisitKey` 强制退出卡住的访问；默认都不绑）。

## 参与开发

欢迎提 Bug、功能建议和 Pull Request，见 [CONTRIBUTING.md](CONTRIBUTING.md)。请不要在 issue 或 PR 里附游戏文件、反编译源码或提取出来的资源。

## 免责声明

本仓库**不包含任何 BSG 游戏资产**。零售对话数据与房间包经 Releases 分发。本项目与 Battlestate Games、SPT 官方无关。

## 许可

MIT（见 [LICENSE](LICENSE)）。
