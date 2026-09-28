using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

[HarmonyPatch]
public static class DecalGuard
{
    static readonly FieldInfo CountField = AccessTools.Field(typeof(StaticDeferredDecalRenderer), "_allDecalsCount");
    static readonly FieldInfo DirtyField = AccessTools.Field(typeof(StaticDeferredDecalRenderer), "_decalBuffersDirty");

    [HarmonyPatch(typeof(StaticDeferredDecalRenderer), nameof(StaticDeferredDecalRenderer.RegisterDecal))]
    [HarmonyPrefix]
    static bool RegisterPrefix(StaticDeferredDecalRenderer __instance, StaticDeferredDecal __0)
    {
        if (Dead(__instance)) return false;
        if (__0 != null) SceneShaders.FixDecal(__0.DecalMaterial);
        return Valid(__0) || Skip();
    }

    [HarmonyPatch(typeof(StaticDeferredDecalRenderer), nameof(StaticDeferredDecalRenderer.UnregisterDecal))]
    [HarmonyPrefix]
    static bool UnregisterPrefix(StaticDeferredDecalRenderer __instance, StaticDeferredDecal __0) => !Dead(__instance) && (Valid(__0) || Skip());

    static bool _deadLogged;

    /// 09-26（SP-Mods「撤离 / 转移后黑屏」，1.3.3 的 M7）：已销毁的渲染器如果还挂在静态贴花事件上（ReInitialize 多订的那次没退掉），
    /// 进战局时全图贴花都登记进它，卸战局场景时逐个做平方级的移除、碰到已销毁的贴花还会抛异常截断事件链。
    /// ClearVisit 已经退订；这里兜底：死渲染器收到事件什么也不做，事件链上其余渲染器照常处理
    static bool Dead(StaticDeferredDecalRenderer renderer)
    {
        if (renderer != null) return false;
        if (!_deadLogged) { _deadLogged = true; Plugin.Log.LogWarning("[narrate] A destroyed static decal renderer is still subscribed to decal events; ignoring its events (logged once)"); }
        return true;
    }

    /// 访问收尾（场景卸载前）：只清访问场景里的渲染器。
    /// 09-24 审查 M7：以前清的是 StaticDeferredDecalRenderer.Instance——没有访问时它是藏身处的渲染器，会把藏身处的贴花一起清掉。
    /// 另外引擎 NarrateGame.Move 调 ReInitialize，里面的 Awake 把贴花事件又订了一遍，而 OnDestroy 只退一次，
    /// 场景卸掉后这个已销毁的渲染器还挂在静态事件上；这里先退一次，OnDestroy 再退掉剩下那次（没有重复订阅时第二次退订是空操作）
    internal static void ClearVisit()
    {
        foreach (var renderer in Object.FindObjectsOfType<StaticDeferredDecalRenderer>(true))
        {
            if (renderer == null || !NarrateEntry.IsVisitScene(renderer.gameObject.scene)) continue;
            Clear(renderer);
            renderer.UnsubscribeFromDecalEvents();
        }
    }

    /// 清空一个渲染器的贴花表，准备重建（访问中 DecalDraw 重注册前也用它）
    internal static void Clear(StaticDeferredDecalRenderer renderer)
    {
        if (renderer == null) return;
        renderer.ClearDecals();
        CountField?.SetValue(renderer, 0);
        DirtyField?.SetValue(renderer, true);
    }

    /// 访问场景卸载完之后：贴花的 ComputeBuffer 是所有渲染器共用的静态缓冲，访问渲染器 ReInitialize / 释放实例时把它们重建或释放了，
    /// 藏身处渲染器的材质还绑着已释放的缓冲。让剩下的（藏身处的）渲染器重新建一遍缓冲、重新绑定，并把 Instance 指回它
    /// （否则 Instance 一直指着已销毁的访问渲染器）。从主菜单访问、没有藏身处时这里什么也不做。
    internal static void RebuildRemaining()
    {
        StaticDeferredDecalRenderer live = null;
        var count = 0;
        foreach (var renderer in Object.FindObjectsOfType<StaticDeferredDecalRenderer>(true))
        {
            if (renderer == null || !renderer.gameObject.scene.isLoaded || NarrateEntry.IsVisitScene(renderer.gameObject.scene)) continue;
            live ??= renderer;
            count++;
        }
        if (live == null)
        {
            if (!ReferenceEquals(StaticDeferredDecalRenderer.Instance, null) && StaticDeferredDecalRenderer.Instance == null) StaticDeferredDecalRenderer.Instance = null;
            return;
        }
        if (count > 1) Plugin.Log.LogWarning($"[narrate] {count} static decal renderers left in scene after visit, rebuilding only the first ({live.gameObject.scene.name})");
        StaticDeferredDecalRenderer.Instance = live;
        live.UpdateInstancesBuffers();
    }

    static bool Valid(StaticDeferredDecal decal)
    {
        if (decal == null) return false;
        var material = decal.DecalMaterial;
        return material != null && material.shader != null && material.mainTexture != null;
    }

    static int _outsideLogged;

    static bool Skip()
    {
        if (!Narrating.Now && _outsideLogged < 5) { _outsideLogged++; Plugin.Log.LogWarning($"[narrate] Rejected a decal with missing material/shader/main texture outside a visit (log {_outsideLogged}; stops logging after 5)"); }
        return false;
    }
}
