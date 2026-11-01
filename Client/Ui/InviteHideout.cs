using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.UI.Chat;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>10-15（SORA 章节二「来自深渊的呼叫」，SORA：像 Kerman 一样寄信 + 金色「回复」，按下后跳到藏身处情报中心前开对话）：
/// 聊天里按「回复」的邀请，若对话商人有 .dlg、且剧本里有一条挂着这条任务的藏身处触发线（`trigger: hideout … if &lt;任务&gt;=AvailableForStart`），
/// 就不在当前界面盖对话屏，而是进藏身处（原生「藏身处」按钮走的同一个入口）、把玩家传到那条线的坐标前、直接触发它——等于自己走到电台前按下那一下，
/// 触发线的 once / 门 / 节点全按剧本原样生效。没有这种触发线的邀请（1.1 的 Kerman 等原生对话）照旧走 InviteDialog。</summary>
public static class InviteHideout
{
    static bool _running;
    static float _startedAt;

    public static bool TryStart(PendingInvite p, ChatScreen screen)
    {
        if (p?.Quest == null) return false;   // Mail 在建 PendingInvite 时就是非空的（InviteState.Pending），不用再判
        var trader = p.DialogueTrader;
        DialogTree tree;
        try { tree = DialogFiles.Loader.Load(trader); }
        catch (Exception e) { Plugin.Log.LogWarning($"[invite] hideout call: could not load the .dlg of {trader}: {e.Message}"); return false; }
        var trigger = tree?.Triggers?.FirstOrDefault(t => t.Kind == "hideout" && t.IfQuestId == p.QuestId);
        if (trigger == null) return false;
        // 门和藏身处里那条线同一口径（VisitTrigger.GatePasses：写了 if 就得写 statuses）：任务此刻的状态不在 statuses 里就不传过去，退回普通对话（10-29）
        if (trigger.IfStatuses.Count == 0 || !QuestOps.StatusIn(p.QuestId, trigger.IfStatuses))
        { Plugin.Log.LogWarning($"[invite] hideout call: quest {p.QuestId} is not in a status the trigger line allows, falling back to the plain dialog"); return false; }
        if (_running && Time.unscaledTime - _startedAt < 120f) { Plugin.Log.LogWarning("[invite] hideout call already in progress, ignoring the repeated click"); return true; }
        if (Narrating.Now || Raid.Now) { Plugin.Log.LogWarning("[invite] hideout call: not in the lobby, falling back to the plain dialog"); return false; }
        _running = true;
        _startedAt = Time.unscaledTime;
        InviteState.CloseChat(screen);
        Plugin.Instance.StartCoroutine(Run(trader, trigger));
        return true;
    }

    static IEnumerator Run(string trader, DialogTrigger trigger)
    {
        try
        {
            if (!InHideout() && !OpenHideout()) { Fail("could not open the hideout"); yield break; }
            // 藏身处要加载场景和全身模型包，等世界、玩家和我们那条触发线（TriggerHost 每秒扫一次）都就位
            var deadline = Time.unscaledTime + 90f;
            VisitTrigger vt = null;
            while (Time.unscaledTime < deadline)
            {
                if (InHideout() && GamePlayerOwner.MyPlayer != null) vt = TriggerHost.Find(trader, trigger);
                if (vt != null) break;
                yield return null;
            }
            if (vt == null) { Fail("the hideout did not come up within 90 s, or the trigger was not spawned there"); yield break; }
            yield return new WaitForSecondsRealtime(0.5f);
            if (DialogScreenTracker.Open) { Fail("a dialog screen is already open"); yield break; }
            var player = GamePlayerOwner.MyPlayer;
            if (player == null) { Fail("no hideout player"); yield break; }
            // 地点写的是区域名的线（Security / RestSpace…，坐标不看，#201）或坐标全 0 的：不传送，人站在哪就在哪开——传过去会落到场景原点一带（10-29）
            if (!Enum.TryParse(trigger.Place, true, out EAreaType _) && (trigger.X != 0f || trigger.Y != 0f || trigger.Z != 0f))
                Stand(player, new Vector3(trigger.X, trigger.Y, trigger.Z));
            yield return null;
            if (vt == null) { Fail("the trigger vanished while teleporting"); yield break; }
            vt.FireNow();
        }
        finally { _running = false; }
    }

    static bool InHideout() => Raid.InHideout && !Narrating.IsVisitWorld(Singleton<GameWorld>.Instance);

    /// 原生主菜单「藏身处」按钮走的入口：MainMenuShowOperation.method_24(area: null)——从主菜单进去，关掉所有盖着的界面（含聊天窗）；人已经在藏身处就只收起界面。
    /// 公开化的 dll 里它是 public，直接调；不做按名字反射的兜底，换客户端版本本来就要重编
    static bool OpenHideout()
    {
        try
        {
            if (!NarrateEntry.TryMenuOp(out _, out var op))
            { Plugin.Log.LogWarning("[invite] hideout call: main menu not ready"); return false; }
            op.method_24((EAreaType?)null)
                ?.ContinueWith(t => Plugin.Log.LogWarning("[invite] hideout call: opening the hideout failed: " + t.Exception?.GetBaseException()?.Message), TaskContinuationOptions.OnlyOnFaulted);
            return true;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] hideout call: could not open the hideout: " + e.Message); return false; }
    }

    /// 把玩家放到触发点前 1 米、面朝触发点。触发点是作者用 F11 取的相机高度，落脚高度向下探地面；探不到就按 1.45 米的站姿眼高往下减。
    /// 站的那一侧取玩家原来所在的方向（从入口走过来的那一侧），免得卡进电台后面的墙
    static void Stand(Player player, Vector3 point)
    {
        try
        {
            var side = player.Transform.position - point;
            side.y = 0f;
            if (side.sqrMagnitude < 0.01f) side = Vector3.back;
            side.Normalize();
            var stand = point + side * 1.0f;
            if (Physics.Raycast(stand + Vector3.up * 0.5f, Vector3.down, out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) stand.y = hit.point.y;
            else stand.y = point.y - 1.45f;
            player.Teleport(stand);
            var look = point - stand;
            player.MovementContext?.SetRotation(new Vector2(Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg, 0f));
        }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] hideout call: teleport failed, opening the dialog where the player stands: " + e.Message); }
    }

    static void Fail(string why)
    {
        Plugin.Log.LogWarning("[invite] hideout call: " + why);
        try { NotificationManager.DisplayMessageNotification(InviteState.CannotConnect); }
        catch (Exception e) { Plugin.Log.LogWarning("[invite] hideout call: could not show the notification: " + e.Message); }
    }
}
