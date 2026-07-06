# VisitAPI

[English](README.md)

给 SPT 里**任意商人**加一套**「对话」**系统。用一个脚本文件写完整剧情——分支选项、旁白、背景图/视频、任务接取 / 上交 / 完成、首次见面剧情，以及可放在**突袭里**和**藏身处里**的交互点。界面复用**原版商人对话界面**，看起来就是原生的。VisitAPI 自身文本自动跟随游戏语言（中 / EN）。

可选：装上单独的**场景资源包**后，还能**走进 7 个原版商人各自的 3D 房间去拜访**——带他们的语音和动作（见下文）。

## 环境要求

| 组件 | 版本 |
|---|---|
| SPT | 4.0.13 |
| BepInEx | 5.4.x（SPT 自带） |

## 安装

把发布包解压到你的 SPT 根目录：

```
SPT/
├─ BepInEx/
│  ├─ plugins/VisitAPI/VisitAPI.dll        ← 客户端插件
│  └─ config/VisitAPI/
│     ├─ <商人id>.dlg                       ← 你的对话脚本（一个商人一个）
│     └─ backgrounds/                       ← 背景图片和视频
└─ user/mods/VisitAPI-Server/              ← 服务端 mod（仅做任务时需要）
   ├─ VisitAPI-Server.dll
   ├─ db/quests/  db/locales/  db/assort/   ← 任务、本地化文本、商人货架
   └─ images/quest/                         ← 自定义任务图标
```

只要客户端插件就能用对话 + 世界内触发器。服务端 mod 仅在你要注册任务、或出售任务解锁的物品时才需要。

## 3D 商人拜访（可选附加包）

**VisitAPI 场景资源包**是单独的下载，装上后会给 7 个原版商人加一个**拜访**按钮，突袭外打开他们各自的 **3D 房间**（语音 + 动作 + 时间轴演出）。这是可选的——上面的对话框架不装它也能用。

安装：把场景包解压进 SPT 根目录，让它落到：

```
SPT/BepInEx/plugins/VisitAPI/scenes/
├─ tradermod.shared.dll
└─ bundles/vendors/            ← 商人场景包 + 对话数据
```

VisitAPI 会自动探测这个位置（也可在配置里把 `Scene / AssetsRoot` 指向自定义路径）。首次打开某个房间会慢——共享包 + 该商人的房间按需加载。

场景资产来自 **[bmpq / spt-tradermod](https://github.com/)**，依据 **MIT 许可**使用——完全归功于原作者。本包只含他的资产 + VisitAPI 反射用到的 `tradermod.shared` 类型；**不含**他的 `tradermod.eft` 插件（VisitAPI 自己驱动场景）。

## 快速上手

创建 `BepInEx/config/VisitAPI/<商人id>.dlg`（UTF-8，**文件名 = 24 位十六进制商人 id**）：

```
trader: 5ac3b934156ae10c4430e83c "Ragman"
start: root

<root>
需要点什么？随便看看。
- 看看你的货。 -> @trade
- 就打个招呼。
- 回头见。
```

游戏里（突袭外）打开该商人 → 交易界面顶部出现**对话**按钮 → 点击打开你的对话。改了 `.dlg` 会**热重载**——重新打开对话即可，无需重启游戏。

## 功能

- **任意商人的对话按钮**，用原版对话 UI 绘制（看起来就是原生的）。
- **分支对话**：旁白、`{player}` 占位符、背景**图片或循环视频**。
- **突袭内触发器** —— 走到地图某个点位即可交互。
- **藏身处触发器** —— 在藏身处某区域（如情报中心）交互，合并进原生菜单。
- **任务** —— 直接在对话里接取 / 上交 / 完成，支持任务连锁推进、任务解锁的商人物品。
- **忠诚度 / 好感度 & 首次见面门控** —— 按等级或商人好感度决定玩家看到哪段开场。
- **`@trade` / `@tasks` / `@services`** —— 从选项跳到该商人的交易 / 任务 / 服务界面。
- **中英双语** —— VisitAPI 自身文本自动跟随游戏语言（中 / EN），也可在配置里强制指定。
- **`.dlg` 热重载** —— 改完重新打开对话即可，无需重启游戏。
- **3D 商人拜访（可选）** —— 走进 7 个原版商人各自的 3D 房间拜访，带语音 + 动作（需单独的场景资源包）。

## 示例

发布包**默认不带任何对话**——由你自己添加。[examples/minimal.dlg](examples/minimal.dlg) 里有一个开箱即跑的 **Ragman** 示例。想试：把它复制到 `BepInEx/config/VisitAPI/5ac3b934156ae10c4430e83c.dlg`，重启，突袭外打开 Ragman → 点**对话**。

## 文档

- **`.dlg` 脚本参考** —— [docs/DLG_FORMAT.zh-CN.md](docs/DLG_FORMAT.zh-CN.md)（[English](docs/DLG_FORMAT.md)）

## 参与贡献

发现 bug 或想参与？Discord 私信我：**@tricoloursky**

## 鸣谢

- **VisitAPI** —— [MIT](LICENSE)，随意使用、fork、做你自己的商人对话。
- **场景附加包** —— 商人房间资产来自 **bmpq / spt-tradermod**（MIT），随包附带并署名。

## 许可

[MIT](LICENSE)
