using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using EFT.AssetsManager;
using HarmonyLib;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>1.3.4 B2：原生 NarrateController.Show 建好访问玩家后调一次 GameWorld.OnGameStarted()（全程序只有它、进战局两处共三处调用，藏身处从来不调）。
/// 别的模组普遍挂在 OnGameStarted 上判断「进图了」，访问时就去按地图查东西：梯子模组每次访问都去找叫 "narrate" 的地图包，
/// 以前查实的「进对话就报 key 为 null」（梯子、HollywoodFX）也是在这一刻。
/// 这里把 Show 里那一次调用换成：只通知引擎自己的接收方（AfterGameStarted 上 Assembly-CSharp 里的订阅者，IL 核过是 3 个：空间音频、控制台、撤离倒计时音效）。
/// 用转译换掉调用点，而不是给 OnGameStarted 加前缀返回 false——Harmony 前缀返回 false 时别的模组的后缀照样会跑。</summary>
[HarmonyPatch]
public static class NarrateStartBroadcast
{
    static readonly FieldInfo AfterStarted = AccessTools.Field(typeof(GameWorld), "_afterGameStarted");
    static readonly MethodInfo Original = AccessTools.Method(typeof(GameWorld), nameof(GameWorld.OnGameStarted));
    static readonly MethodInfo Replacement = AccessTools.Method(typeof(NarrateStartBroadcast), nameof(Notify));

    /// Show 是 async 方法，调用点在编译器生成的状态机 MoveNext 里
    static MethodBase TargetMethod()
    {
        var show = AccessTools.Method(typeof(TarkovApplication.NarrateController), nameof(TarkovApplication.NarrateController.Show));
        var sm = show?.GetCustomAttribute<System.Runtime.CompilerServices.AsyncStateMachineAttribute>()?.StateMachineType;
        return sm == null ? null : AccessTools.Method(sm, "MoveNext");
    }

    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var replaced = 0;
        foreach (var ins in instructions)
        {
            if ((ins.opcode == OpCodes.Callvirt || ins.opcode == OpCodes.Call) && ins.operand is MethodInfo m && m == Original)
            {
                replaced++;
                yield return new CodeInstruction(OpCodes.Call, Replacement).MoveLabelsFrom(ins).MoveBlocksFrom(ins);
                continue;
            }
            yield return ins;
        }
        if (replaced != 1) Plugin.Log.LogWarning($"[narrate] expected 1 OnGameStarted call in NarrateController.Show, replaced {replaced} (game version changed?)");
    }

    static void Notify(GameWorld world)
    {
        if (world == null) return;
        NarrateLocationId.Fill(world, "before game start");
        var engine = typeof(GameWorld).Assembly;
        if (AfterStarted?.GetValue(world) is Action list)
            foreach (var d in list.GetInvocationList())
            {
                if (d.Method.DeclaringType?.Assembly != engine) continue;
                try { ((Action)d)(); }
                catch (Exception e) { Plugin.Log.LogWarning($"[narrate] engine game-start receiver {d.Method.DeclaringType?.Name}.{d.Method.Name} failed: {e.Message}"); }
            }
    }
}

/// <summary>1.3.4 B1：原生 Show 的 catch 走 TarkovApplication.HandleError("Narrate game ", e)：先弹带堆栈的错误框，关掉后再走一整套 ComebackToMainMenu，
/// 和我们自己的中止流程同时在跑。只拦这一个来源：记下异常，交给 NarrateEntry 收尾（清半成品、回主菜单、一行提示、打开 2D 对话兜底）。</summary>
[HarmonyPatch(typeof(TarkovApplication), nameof(TarkovApplication.HandleError))]
public static class NarrateShowError
{
    const string Source = "Narrate game ";
    public static Exception Last;

    static bool Prefix(string matchingName, Exception e)
    {
        if (matchingName != Source) return true;
        Last = e;
        Plugin.Log.LogError("[narrate] visit failed while building the visit player/world (native error box suppressed, falling back): " + e);
        return false;
    }
}

/// <summary>1.3.4 B1：玩家建到一半抛异常时 NarrateGame.PlayerOwner 还没赋值——原生 Stop 看到它为空就什么都不回收，
/// 半成品 NarratePlayer 留在场景里每帧报 Player.ComplexLateUpdate 空引用（prev 那局 15290 次）。
/// 访问收尾在 Stop 之后调这里：正常流程里访问玩家已经被 Stop 回收进对象池（物体关掉了），还开着的访问玩家就是孤儿，照原生 Stop 的做法 Dispose 再回池。</summary>
public static class NarrateOrphans
{
    public static void Collect()
    {
        foreach (var p in UnityEngine.Object.FindObjectsOfType<NarratePlayer>())
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            Plugin.Log.LogWarning($"[narrate] half-built visit player '{p.name}' left behind, disposing it like native Stop");
            try { p.Dispose(); }
            catch (Exception e) { Plugin.Log.LogWarning("[narrate] orphan player dispose failed: " + e.Message); }
            try { AssetPoolObject.ReturnToPool(p.gameObject); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[narrate] orphan player return-to-pool failed, destroying it: " + e.Message);
                try { UnityEngine.Object.Destroy(p.gameObject); } catch { }
            }
        }
    }
}
