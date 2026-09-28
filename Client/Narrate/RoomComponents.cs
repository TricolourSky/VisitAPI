using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.Hideout;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>1.3.4 B9：1.1 商人房间里的「眼睛跟着玩家转」组件（LightKeeperEyeTargetFollower，0.16.9 有同名类）。
/// 原生 Awake 第一行就读 _leftEye / _rightEye / _headMesh；Ragman、Skier、Mechanic、Therapist 房间每次访问都在这里报空引用——
/// 包里这几个引用在运行时是空的（字段没对上）。两条路，按运行时实际情况走：
///   引用齐全：放行原生 Awake；NPC 进场那一刻（和原生 NPCObject.GoIn 同一时刻）把注视目标设成 GoIn 传进来的访问玩家相机，
///             原生 Update 看到目标变了就自己开始转眼珠（原生灯塔商人是靠进区域事件设这个目标，访问里没有那个事件）；
///   引用缺失：在 Awake 之前把组件关掉，不再报错（眼睛不动，和现在一样，只是不再刷日志）。</summary>
[HarmonyPatch(typeof(LightKeeperEyeTargetFollower), nameof(LightKeeperEyeTargetFollower.Awake))]
public static class EyeFollowerGuard
{
    static bool Prefix(LightKeeperEyeTargetFollower __instance)
    {
        var missing = new List<string>();
        if (__instance._leftEye == null) missing.Add("_leftEye");
        if (__instance._rightEye == null) missing.Add("_rightEye");
        if (__instance._headMesh == null || __instance._headMesh.sharedMesh == null) missing.Add("_headMesh");
        if (missing.Count == 0) return true;
        __instance.enabled = false;
        return false;
    }
}

[HarmonyPatch(typeof(NPCObject), nameof(NPCObject.GoIn))]
public static class EyeFollowerTarget
{
    static void Postfix(NPCObject __instance, Transform cameraTransform)
    {
        if (__instance == null || cameraTransform == null) return;
        try
        {
            var scene = __instance.gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var eye in root.GetComponentsInChildren<LightKeeperEyeTargetFollower>(true))
                    if (eye != null && eye.enabled) eye._transformToLookAt = cameraTransform;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[narrate] eye follower target not set: " + e.Message); }
    }
}

/// <summary>1.3.4 B10：Mechanic 房间里藏身处的灯光氛围组件（MultiObjectAmbiance）靠藏身处供电状态驱动，灯光表 Patterns 是 Odin 序列化的，
/// 房间包里这份数据没带过来，Patterns 为空——离开访问时它的 OnDisable / OnDestroy 遍历这张表，每次 3 + 3 条空引用。
/// 给它一张空表：没有灯光表就什么也不做，和原生「这个供电状态没配灯光」一样。藏身处自己的组件表是有的，不受影响。</summary>
[HarmonyPatch]
public static class AmbianceGuard
{
    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MultiObjectAmbiance), nameof(MultiObjectAmbiance.OnDisable));
        yield return AccessTools.Method(typeof(MultiObjectAmbiance), nameof(MultiObjectAmbiance.OnDestroy));
    }

    static void Prefix(MultiObjectAmbiance __instance)
    {
        if (__instance == null || __instance.Patterns != null) return;
        __instance.Patterns = new Dictionary<ELightStatus, Pattern<MultiObjectAmbiance.AmbianceAffectedObjects>>();
    }
}

/// <summary>1.3.4 B11：离开访问时商人的告别动画（原生 NPCObject.PlayAction → SequencePlayer）还在播，我们的收尾就把房间卸了，
/// 动画序列找不到自己的 Animator，BSG 日志里报 [AnimSeqPlayer] Error。原生设计里 Hide 之后房间留着，所以原生碰不到。
/// 原生 SkipAnimation 只收字幕、停不下动画序列，所以走另一条：记下 NPC 正在播的动画，卸房间前等它们播完（最多 MaxWait 秒）。</summary>
[HarmonyPatch(typeof(NPCObject), nameof(NPCObject.PlayAction))]
public static class NpcAnimations
{
    public const float MaxWait = 8f;
    static readonly List<Task> _playing = new();

    static void Postfix(Task __result)
    {
        if (__result == null || __result.IsCompleted) return;
        _playing.RemoveAll(t => t == null || t.IsCompleted);
        _playing.Add(__result);
    }

    public static bool Playing
    {
        get
        {
            _playing.RemoveAll(t => t == null || t.IsCompleted);
            return _playing.Count > 0;
        }
    }

    public static void Forget() => _playing.Clear();
}
