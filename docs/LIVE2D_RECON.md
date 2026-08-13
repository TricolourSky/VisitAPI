# 1.9.5 Live2D 集成 — 侦察档案（2026-08-05）

> 三路网络侦察结论汇总（SDK 技术 / 授权 / 前人实践），供探讨与后续 PoC 参考。

## 一、技术可行性：**可行，路径明确**

- **SDK 版本必须锁 5-r.4.x**（如 5-r.4.2）：官方支持 Unity 2022.3 LTS + Built-in 渲染管线。**5-r.5+ 已转向 URP 专用，EFT 是 Built-in，不可用。**
- **运行时加载是官方正门**：`CubismModel3Json.LoadAtPath(path, loader)` → `.ToModel()`，loader 委托处理三类型（byte[]=moc3 / string=json / Texture2D=贴图 LoadImage）。VTube Studio 就是这条路的量产实证（作者公开过 gist）。
- **Framework 是纯 C# 源码**（GitHub 公开）：去掉 Editor 目录、开 unsafe，可自编成引用 UnityEngine 的独立 DLL 打进模组。
- **原生库 Live2DCubismCore.dll**（闭源，官网 SDK 包才有）：P/Invoke 名 `Live2DCubismCore`；放游戏根目录或插件 Awake 里 LoadLibrary 绝对路径预载。
- **⚠️ 最大技术风险点——着色器**：Cubism 材质靠 `Resources.Load` 取自带 shader，目标游戏里没有 → 裸调 ToModel 拿不到材质。**解法已探明**：`ToModel()` 有材质拾取器委托重载，把 Cubism 自带 shader/材质打进我们自己的 AssetBundle，运行时喂进去即可（零补丁）。**打 AssetBundle 需要一次性用 Unity 2022.3 编辑器。**
- **驱动组件运行时全可用**：口型 `CubismAudioMouthInput`（从 AudioSource 实时采样→直接接我们 1.5 的语音源！）、眨眼 `CubismAutoEyeBlinkInput`、呼吸谐波组件——纯 AddComponent 零 Editor 资产。动作播放绕开 Fade 体系走 legacy Animation 路线（`clip.legacy=true` + Animation 组件，官方论坛实证）。
- **渲染进对话界面**：模型是 MeshRenderer 体系 → 独立 Layer + 正交相机 → RenderTexture → RawImage（我们 1.4 视频背景已有同款管线）。
- **Mono 后端 = 简单难度**（IL2CPP 才是地狱线，与我们无关）。
- 已知验证项：moc3 与 Core 版本兼容（Core 版本低于模型导出版本会静默返回 null）。
- 旧项目全库检索 live2d/cubism **零命中**——无历史包袱。

## 二、授权：**风险不在技术，在"可扩展应用"条款**

- Framework 源码 = Live2D Open Software License（可用可改，许可声明必须保留）。
- **Core 专有**：官方明确不上 GitHub → **我们的开源仓库绝不能放它**；随成品分发须满足专有许可 §5.2 四条件（未核实 RedistributableFiles.txt 实际清单）。
- 个人/年销售额 <1000 万日元 = General User：使用免费、个人使用免签约。
- **⚠️ 最大合规风险——Expandable Application 条款**：官方定义"通过添加文件/数据使用与生成不定数量模型的作品"须**发布前审查 + 特别出版合同，个人也不豁免，且"完全免费原则上不批"**。"让社区任意接入自己 Live2D 模型的通用框架"大概率落入此定义。
- **出路**：(a) Live2D 支持做成**用户侧自装的可选伴生层**——框架本体零 Live2D 代码/运行时，SDK 由最终用户自行下载放入（个人使用无需出版合同）；(b) 邮件问询 Live2D 拿书面答复（个案可谈，周期不可控）。
- **演示模型**：官方免费示例（Hiyori 等 26 个）**不可随包再分发**裸文件；截图/视频宣传可用（按下载页标注版权）；随包发行要么委托原创模型、要么引导用户自行下载。

## 三、前人实践

- 无"BepInEx 注入外部 Live2D"完整开源先例，但每块拼图都有实证：VTube Studio gist（运行时加载）、官方教程（loader 三类型）、UniversalUnityDemosaics（BepInEx 插件操作 Cubism 类型无障碍）、鬼谷八荒动态立绘生态（Live2D 立绘 mod 需求真实存在）。
- 示例模型目录结构：`runtime/` 下 model3.json（入口）+ moc3 + physics3.json + 贴图目录 + motion/*.motion3.json。

## 四、与 VisitAPI 的契合点（架构草案）

- 独立可选插件 `VisitAPI.Live2D.dll`：核心框架运行时探测它（软依赖），不装不影响任何现有功能——同时满足合规出路 (a)。
- .dlg 语法自然延伸：头部 `live2d: <模型目录>`（对标 `scene:`）；节点/逐句 `motion:` / `expr:`；口型自动吃 `audio:` 语音；待机=眨眼+呼吸组件。
- 视觉栈（从底到顶）：bg: 图片/视频背景 → Live2D 立绘层（RT+RawImage）→ 原生对话窗 = 完整 galgame 式演出。`live2d:` 与 `scene:` 互斥（作者二选一）。

## 五、PoC 范围建议（实验阶段）

1. 环境验证：Framework 5-r.4.2 编译成 DLL；Core 预载；shader AssetBundle（一次性 Unity 2022.3 编辑器活）
2. F10 调试：加载本地示例模型（用户自行下载）→ 对话屏显示 → 播一段 motion + 语音口型
3. 通过后再做 .dlg 语法接入

关键来源：docs.live2d.com（运行时加载/口型/眨眼/平台支持）、github.com/Live2D/CubismUnityComponents（5-r.4.x releases/LICENSE）、live2d.com 许可页（Proprietary/Open/Free Material/Expandable）、DenchiSoft gist。
