# VisitAPI

[English](README.md) | **简体中文**

给 SPT 商人（含自定义商人）加上 EFT 1.0 正式版风格「拜访」对话系统的开源框架——用纯文本 `.dlg` 剧本，让任何商人拥有 3D 场景对话、剧情任务、好感度与战局内对话触发点。

> 目标版本：**SPT 4.1.1**

## 功能

- **访问按钮** —— 商人界面出现 1.0 同款「访问」页签，点击进入 3D 场景对话
- **.dlg 剧本** —— 节点、选项、条件门、一次性选项（once/always/first）、图片/视频/3D 场景背景、语音+BGM、好感度（`standing:`）、任务状态推进（`setstatus:`）
- **原生 Narrate 管线** —— 原版商人走 EFT 内置的休眠拜访系统 + 零售对话数据回放（口型/字幕/分支变量全原生）
- **任务系统** —— 自定义任务 JSON，接取/上交/完成全部走原生网络事务，附任务图片路由
- **战局内/藏身处触发点** —— `trigger:` 语法在地图坐标放置对话点（距离+视角锥+任务门控）
- **进度持久化** —— 对话变量经服务端回放写入档案，跨会话不丢

## 剧本编辑器

**[VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor)** 是官方配套工具——本地网页端的 `.dlg` 剧本可视化编辑器。想给商人写自己的对话剧情，从它开始比手写脚本舒服得多。

## 安装

1. 从 [Releases](https://github.com/TricolourSky/VisitAPI/releases) 下载两个包：
   - `VisitAPI-x.y.z-spt4.1.1.zip` —— 客户端插件 + 服务端模组，解压到 SPT 根目录
   - `VisitAPI-scenes-tarkin.zip` —— 3D 商人房间，解压到 SPT 根目录
     场景资源源自 [bmpq/spt-tradermod](https://github.com/bmpq/spt-tradermod)（MIT），体积原因作为 Release 附件分发
2. `.dlg` 剧本放 `<SPT>\BepInEx\config\VisitAPI\<商人id>.dlg`

## 从源码构建

需要本机 SPT 4.1.1 安装（取引用 DLL），以及与本仓库并列 checkout 的 [VisitAPI Editor](https://github.com/TricolourSky/VisitAPI-Editor) 仓库（`.dlg` 解析器源码链编自它）：

```
dotnet build Client\VisitAPI.csproj  -c Release -p:EftDir=<你的SPT目录> [-p:DlgSrc=<VisitAPI.Dlg源码目录>]
dotnet build Server\VisitAPI-Server.csproj -c Release -p:SptDir=<你的SPT服务端目录>
```

游戏目录存在时构建会自动部署（Deploy target），不存在则静默跳过。

## .dlg 快速上手

```
@start hello

[hello]
npc: 你来了。今天要点什么？
opt: 看看货 -> openTrade
opt: 有活给我吗？ -> openTasks
opt: 没事，走了 -> @close
```

完整语法（条件门/触发点/好感度/多媒体）见 `docs/` 与 [`examples/`](examples/)。

## 免责声明

本仓库**不包含任何 BSG 游戏资产**。零售对话数据与场景包经 Releases 分发。本项目与 Battlestate Games、SPT 官方无关。

## 许可

MIT（见 [LICENSE](LICENSE)）。场景包版权归 bmpq/tarkin（MIT），再分发须保留其署名。
