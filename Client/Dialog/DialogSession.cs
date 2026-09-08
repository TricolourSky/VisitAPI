using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using UnityEngine;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>
/// 一场自定义 `.dlg` 对话的统一生命周期：行副作用分发 + 失控熔断 + 任务状态变了重建当前节点。
/// 旧版是 8 个服务各自 Watch 一遍（TabRouter/QuestRefresh/Standing/Variable/SetStatus/Handover/Once/Fuse），
/// 生命周期各自为政 —— 触发器和对话互相出 BUG 的头号接缝，现在收成这一个对象。
/// </summary>
public sealed class DialogSession
{
    readonly ClientDialogController _dc;
    readonly DialogTree _tree;
    readonly Profile _profile;
    readonly QuestController _quests;
    readonly InventoryController _inventory;
    readonly TraderScreensGroup _screen;
    int _fuseCount;
    float _fuseWindow;

    public static void Begin(ClientDialogController dc, DialogTree tree, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup screen)
    {
        var s = new DialogSession(dc, tree, profile, quests, inventory, screen);
        dc.OnDialogChanged += s.OnDialog;
        QuestRefresh.Watch(dc, tree, quests);
    }

    DialogSession(ClientDialogController dc, DialogTree tree, Profile profile, QuestController quests, InventoryController inventory, TraderScreensGroup screen)
    { _dc = dc; _tree = tree; _profile = profile; _quests = quests; _inventory = inventory; _screen = screen; }

    void OnDialog(BaseTraderDialog dialog)
    {
        // 验收证据行（坑 #98 规矩：要看的必须 LogInfo）：每一拍的去向 + 每条执行的行，一场对话十几行
        Plugin.Log.LogInfo(dialog == null ? "[dlg] 对话结束（当前对话置空）"
            : $"[dlg] 切到 {(DialogTemplateBuilder.NodeByDialog.TryGetValue(dialog.Id, out var n) ? n : "旁白拍")} {dialog.Id} side={dialog.DialogSide} lines={dialog.Lines?.Count() ?? 0}");
        if (dialog == null) return;
        // 每个 dialog 对象只经过这里一次（OnDialogChanged 每拍发一个新对象），不会重复订阅
        dialog.OnExecuteLine += line =>
        {
            var acts = line?.Template?.Actions?.Select(a => a.GetType().Name.Replace("Dialog", "").Replace("Action", "")) ?? Enumerable.Empty<string>();
            Plugin.Log.LogInfo($"[dlg] 执行行 {line?.Template?.Id} 动作=[{string.Join(",", acts)}]");
            Fuse();
            if (line?.Template == null || !LineEffects.TryGet(line.Template.Id, out var e)) return;
            if (e.Tab != null) RouteTab(e.Tab.Value);
            if (e.Standing != null) EffectActions.Standing(_profile, _screen, e.Standing.Value.trader, e.Standing.Value.delta);
            if (e.SyncVar != null) Vars.Sync(e.SyncVar.Value.id, e.SyncVar.Value.value);
            if (e.SetStatus != null) EffectActions.SetStatus(_quests, e.SetStatus.Value.quest, e.SetStatus.Value.status);
            if (e.HandoverQuest != null) EffectActions.Handover(_quests, _profile, _inventory, e.HandoverQuest);
            if (e.Once != null) OnceService.Mark(e.Once.Value);
        };
    }

    void RouteTab(TraderScreensGroup.ETraderMode mode)
    {
        if (Raid.Now) return;
        var node = _dc.CurrentDialog != null && DialogTemplateBuilder.NodeByDialog.TryGetValue(_dc.CurrentDialog.Id, out var n) ? n : null;
        DialogBackground.KeepAlive = true;
        Plugin.Instance.StartCoroutine(TabRouter.OpenTradeWindow(_tree, node, _screen, _profile, _quests, _inventory, mode));
    }

    // 1 秒内执行 25 行 = 剧本死循环，熔断保护；熔断后压 4 秒冷静期（期间不重置计数窗口）
    void Fuse()
    {
        if (Time.unscaledTime - _fuseWindow > 1f) { _fuseWindow = Time.unscaledTime; _fuseCount = 0; }
        if (++_fuseCount < 25) return;
        Plugin.Log.LogError("[fuse] dialog line runaway detected - stopping dialog controller");
        _fuseCount = 0;
        _fuseWindow = Time.unscaledTime + 4f;
        _dc.StopDialog();
        DialogScreenTracker.Live?.method_8();
    }
}
