# 在 Unity 里制作 3D 场景 bundle 教程

> VisitAPI · 做一个自定义商人房间，让 `.dlg` 用 `scene: 你的包` 打开它

这是**资产制作**教程：如何把你自己的 3D 房间——几何体、模型、灯光、相机、动作——做成一个 Unity AssetBundle，让 VisitAPI 在对话用 `scene:` 头时把它搭起来。

> 如果你只想**复用零售商人的房间**，不需要这套——`.dlg` 里写 `scene: <24位商人id>` 即可借用（需要场景资源包）。本教程针对**完全自制**的场景。`.dlg` 那一侧（`scene:` / `anim:` 语法）见 [DLG_FORMAT.zh-CN.md](DLG_FORMAT.zh-CN.md)。

---

## 契约 —— VisitAPI 对你的场景有什么要求

VisitAPI 会搭建**任何**满足两点的流式场景 AssetBundle（靠命名查找）：

1. **相机锚点** —— 一个名字**以 `Position_Camera` 开头**的空物体。VisitAPI 会把它的**位置 + 旋转**复制给拜访相机（FOV 60）。把它对准你的商人。
2. **商人模型** —— 一个带 **`Animator`**、且其下有 **`SkinnedMeshRenderer`** 的物体。名字里含 `Vendor`、`Trader`、`_Model` 或 `_Holder`，就能被**确定性地选中**；否则取场景里第一个带蒙皮的 Animator。（名字为 `weapon_/launcher_/mod_/item_` 或含 `.generated` 的会被当道具跳过。）

这个 Animator 的**状态名**，就是你在 `.dlg` 里 `anim: <状态名>` 能调用的动作名。

其余的一切——几何体、灯光、材质、天空盒——**都是你的，原样保留**。VisitAPI **不会**对自制场景施加它那套自制包的光影/shader 恢复处理；你做成什么样就是什么样。

---

## 环境要求

- 用 Unity Hub 装 **Unity `2022.3.43f1`**。这个版本**必须和 EFT 的引擎版本一致**——用别的 Unity 版本打的 AssetBundle 会**静默加载失败**。这是第一大坑。
- **内置渲染管线（Built-in Render Pipeline）**工程（**不要** URP/HDRP——EFT 用的是内置管线）。
- 会基本的 Unity 场景与 AssetBundle 操作。
- 装好 VisitAPI 用于测试。

---

## 第一步 · 建工程

1. Unity Hub → 装 **2022.3.43f1**。
2. 新建工程 → **3D（Built-in Render Pipeline）**。
3.（建议）`Edit ▸ Project Settings ▸ Player ▸ Color Space = Linear`，与 EFT 一致。

## 第二步 · 搭房间场景

1. 新建场景，例如 `Assets/MyRoom.unity`。
2. 摆好几何体（地面、墙、道具）和**灯光**。
3. 放**商人模型**：一个带 **`Animator`** 组件、且有 **`SkinnedMeshRenderer`** 的蒙皮/人形模型。根物体命名如 `Vendor_MyTrader`，挂一个 **AnimatorController**。
4. 在 AnimatorController 里为你想要的手势加**状态**——如 `Idle`、`Greeting`、`Nod`、`LookAround`。这些状态名就是 `.dlg` 里 `anim:` 要调用的动作名。
5. 加一个名为 **`Position_Camera`** 的空物体，移动 + 旋转到你想要的机位，框住商人。
   - *小技巧*：临时在它下面挂个 `Camera` 在 Game 视图里预览构图，调好后删掉这个 Camera——VisitAPI 会自带相机。

## 第三步 · 材质与 shader

- 用**内置管线的标准 shader**（`Standard`、`Unlit/*` 等）。VisitAPI 会按名把 shader 重绑到游戏自带的；对自制场景，**匹配不到的原样保留**——所以你的观感能保住。
- 如果某材质在游戏里显示**粉红/错误**，说明该 shader 没被打进包 → 在 **`Project Settings ▸ Graphics ▸ Always Included Shaders`** 里加上它（或打一个 ShaderVariantCollection），重打包。

## 第四步 · 光照

VisitAPI 不动自制场景的光照，所以场景**自己的**光照会随包走、按你做的呈现：

- **实时**灯光直接生效，**或**
- **烘焙**：`Window ▸ Rendering ▸ Lighting ▸ Generate Lighting`——lightmap + 光探针会打进场景包并随之加载（GI/软阴影更好）。
- 在场景的 Lighting 设置里调环境光/天空盒。

## 第五步 · 指派 AssetBundle

1. 在 Project 窗口选中 `Assets/MyRoom.unity`。
2. Inspector 底部的 **AssetBundle** 下拉 → *New…* → 起个名，例如 `my_room`。

**一个包一个场景**（VisitAPI 搭建包里的第一个场景）。

## 第六步 · 打包

新建 `Assets/Editor/BuildBundles.cs`：

```csharp
using UnityEditor;
using System.IO;

public static class BuildBundles
{
    [MenuItem("VisitAPI/Build Scene Bundles")]
    public static void Build()
    {
        const string outDir = "AssetBundles";
        Directory.CreateDirectory(outDir);
        BuildPipeline.BuildAssetBundles(
            outDir,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);   // EFT 是 Win64
    }
}
```

点菜单 **`VisitAPI ▸ Build Scene Bundles`**。输出：`AssetBundles/my_room`（外加几个 manifest 文件，忽略即可）。

## 第七步 · 安装并接线

1. 把包文件 `AssetBundles/my_room` 复制到 `SPT/BepInEx/config/VisitAPI/scenes/`。可以改名（如 `my_room.bundle`）——**文件名就是你要引用的名字**。
2. 在某商人突袭外的 `.dlg` 里加 `scene:` 头，并用你的手势状态：

   ```
   trader: 5ac3b934156ae10c4430e83c "Ragman"
   scene: my_room.bundle
   start: root

   <root> anim: Greeting
   > “欢迎光临。” | anim: Nod
   随便看看。
   - 看货。 -> @trade
   - 走了。
   ```

3. 突袭外打开该商人 → 点**对话** → 你的房间搭起来，镜头切到 `Position_Camera`，商人播 `Greeting`。

> **在迭代？** 已加载的包会**在本次会话内缓存**——重打包后要**重启游戏**才会用上新包。（首次使用不用重启；`.dlg` 本身是热重载的。）

## 第八步 · 验证（看日志）

`BepInEx/LogOutput.log`：

- `[SceneStage] scene 'MyRoom' up for trader …` —— 搭建成功。
- `[VendorScene] source=name convention camera=Position_Camera trader=Vendor_MyTrader` —— 相机 + 模型都找到了。若任一显示 **`MISSING`**，按契约改命名（第二步）。
- `[NpcActor] 'Vendor_MyTrader' plays 'Greeting'` —— 某个 `anim:` 播了。
- `[SceneStage] no native shader for: …` —— 该 shader 没匹配上；若显示错误就把它打进包（第三步）。

---

## 共享依赖（可选）

如果你的场景引用了打在**另一个**包里的资产，把那个包命名为 `vendors_shared.bundle` 放在 `scenes/` 里你的场景包旁边——VisitAPI 会先预载它。自包含的场景不需要这个。

## 常见坑

| 现象 | 原因 / 解决 |
|---|---|
| 包加载不了 / 什么都没发生 | Unity 版本不对——用 **2022.3.43f1** 打、目标 **StandaloneWindows64**。 |
| 日志里相机/模型 **MISSING** | 按契约改命名：锚点 `Position_Camera*`；模型名含 `Vendor/Trader/_Model` + 有 `Animator` + 有 `SkinnedMeshRenderer`。 |
| 材质粉红 | shader 没打进包 → **Always Included Shaders**，重打包。 |
| `anim:` 没反应 | AnimatorController 里没有这个状态名（名字错了不会崩，只是不播）。对准确的状态名。 |
| `scene:` 被忽略 | `scene:` 只在**突袭外的对话**入口生效，且 `scene:` 与 `bg:` 互斥。 |

## 相关文档

- `.dlg` 语法参考：[DLG_FORMAT.zh-CN.md](DLG_FORMAT.zh-CN.md)
- 安装与总览：[README.zh-CN.md](../README.zh-CN.md)
