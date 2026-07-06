# Building a 3D Scene Bundle in Unity

> VisitAPI · authoring a custom vendor room that a `.dlg` opens with `scene: your_bundle`

This is the **asset-creation** tutorial: how to build your own 3D room — geometry, model, lighting, camera, gestures — as a Unity AssetBundle that VisitAPI stages when a dialogue uses the `scene:` header.

> If you only want to **reuse a retail vendor's room**, you don't need any of this — `scene: <24-hex traderId>` borrows it (needs the Scenes add-on pack). This tutorial is for a **fully custom** scene. For the `.dlg` side (`scene:` / `anim:` syntax), see [DLG_FORMAT.md](DLG_FORMAT.md).

---

## The contract — what VisitAPI needs from your scene

VisitAPI will stage **any** streamed Scene AssetBundle that provides two things, found by name:

1. **A camera anchor** — an empty GameObject whose name **starts with** `Position_Camera`. VisitAPI copies its **position + rotation** to the visit camera (FOV 60). Aim it at your trader.
2. **A trader model** — a GameObject that has an **`Animator`** with a **`SkinnedMeshRenderer`** somewhere under it. Give it a name containing `Vendor`, `Trader`, `_Model`, or `_Holder` so it is picked deterministically; otherwise the first skinned Animator in the scene is used. (Objects named `weapon_/launcher_/mod_/item_` or containing `.generated` are treated as props and skipped.)

The Animator's **state names** become the `anim: <state>` values you can call from your `.dlg`.

Everything else — geometry, lights, materials, skybox — is **yours and left untouched**. VisitAPI does **not** apply its raw-pack lighting/shader recovery to custom scenes; what you build is what renders.

---

## Requirements

- **Unity `2022.3.43f1`** via Unity Hub. This **must match EFT's engine version** — an AssetBundle built with a different Unity version will silently fail to load. This is the #1 gotcha.
- A **Built-in Render Pipeline** project (**not** URP/HDRP — EFT uses Built-in).
- Basic familiarity with Unity scenes and AssetBundles.
- VisitAPI installed, to test.

---

## Step 1 — Project setup

1. Unity Hub → install **2022.3.43f1**.
2. New project → **3D (Built-in Render Pipeline)**.
3. (Recommended) `Edit ▸ Project Settings ▸ Player ▸ Color Space = Linear` to match EFT.

## Step 2 — Build the room scene

1. Create a scene, e.g. `Assets/MyRoom.unity`.
2. Add your geometry (floor, walls, props) and **lights**.
3. Add your **trader model**: a skinned/humanoid model with an **`Animator`** component and a **`SkinnedMeshRenderer`**. Name the root, e.g. `Vendor_MyTrader`. Assign an **AnimatorController**.
4. In the AnimatorController, add **states** for the gestures you want — e.g. `Idle`, `Greeting`, `Nod`, `LookAround`. Those state names are exactly what `anim:` will call from the `.dlg`.
5. Add an empty GameObject named **`Position_Camera`**; move + rotate it to where the camera should sit, framing the trader.
   - *Tip:* temporarily parent a `Camera` under it to preview the framing in Game view, then delete that Camera — VisitAPI brings its own.

## Step 3 — Materials & shaders

- Use **standard Built-in shaders** (`Standard`, `Unlit/*`, etc.). VisitAPI rebinds shaders to the game's own by name and, for custom scenes, **keeps** anything it can't match — so your look survives.
- If a material shows up **pink / wrong** in game, the shader didn't get packed into the build → add it under **`Project Settings ▸ Graphics ▸ Always Included Shaders`** (or ship a ShaderVariantCollection) and rebuild.

## Step 4 — Lighting

VisitAPI leaves a custom scene's lighting alone, so the scene's **own** lighting travels in the bundle and is used as authored:

- **Realtime** lights just work, **or**
- **Bake** it: `Window ▸ Rendering ▸ Lighting ▸ Generate Lighting` — lightmaps + light probes ship inside the scene bundle and load with it (better GI/soft shadows).
- Set ambient / skybox in the scene's Lighting settings to taste.

## Step 5 — Assign the AssetBundle

1. Select `Assets/MyRoom.unity` in the Project window.
2. At the bottom of the Inspector, use the **AssetBundle** dropdown → *New…* → name it, e.g. `my_room`.

Put **one scene per bundle** (VisitAPI stages the first scene in the bundle).

## Step 6 — Build the bundle

Add `Assets/Editor/BuildBundles.cs`:

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
            BuildTarget.StandaloneWindows64);   // EFT is Win64
    }
}
```

Run the menu item **`VisitAPI ▸ Build Scene Bundles`**. Output: `AssetBundles/my_room` (plus manifest files you can ignore).

## Step 7 — Install & wire up

1. Copy the bundle file `AssetBundles/my_room` into `SPT/BepInEx/config/VisitAPI/scenes/`. You may rename it (e.g. `my_room.bundle`) — the **file name is what you reference**.
2. In a trader's out-of-raid `.dlg`, add the `scene:` header and use your gesture states:

   ```
   trader: 5ac3b934156ae10c4430e83c "Ragman"
   scene: my_room.bundle
   start: root

   <root> anim: Greeting
   > "Welcome to my shop." | anim: Nod
   Take a look around.
   - Show me the goods. -> @trade
   - Leaving.
   ```

3. Open that trader out of raid → **Talk** → your room stages, camera cuts to `Position_Camera`, the trader plays `Greeting`.

> Iterating? A loaded bundle is **cached for the session** — after you rebuild the bundle, **restart the game** so the new one is picked up. (First use needs no restart; the `.dlg` itself hot-reloads.)

## Step 8 — Verify (read the log)

`BepInEx/LogOutput.log`:

- `[SceneStage] scene 'MyRoom' up for trader …` — staged OK.
- `[VendorScene] source=name convention camera=Position_Camera trader=Vendor_MyTrader` — camera + model found. If either shows **`MISSING`**, fix the naming (Step 2 / the contract).
- `[NpcActor] 'Vendor_MyTrader' plays 'Greeting'` — an `anim:` fired.
- `[SceneStage] no native shader for: …` — that shader wasn't matched; if it renders wrong, include it (Step 3).

---

## Shared dependencies (optional)

If your scene references assets packed in a **separate** bundle, drop that bundle as `vendors_shared.bundle` next to your scene bundle in `scenes/` — VisitAPI preloads it first. A self-contained scene doesn't need this.

## Common pitfalls

| Symptom | Cause / fix |
|---|---|
| Bundle won't load / nothing stages | Unity version mismatch — build with **2022.3.43f1**, target **StandaloneWindows64**. |
| Log says camera/trader **MISSING** | Rename to the contract: anchor `Position_Camera*`; model name with `Vendor/Trader/_Model` + an `Animator` + a `SkinnedMeshRenderer`. |
| Pink materials | Shader not packed → **Always Included Shaders**, rebuild. |
| `anim:` does nothing | The state name isn't in your AnimatorController (a wrong name is harmless — it just doesn't play). Match the exact state name. |
| `scene:` ignored | `scene:` only stages on the **out-of-raid Talk** entry, and `scene:` + `bg:` are mutually exclusive. |

## See also

- `.dlg` syntax reference: [DLG_FORMAT.md](DLG_FORMAT.md)
- Install / overview: [README.md](../README.md)
