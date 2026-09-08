using System.Linq;
using EFT;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>
/// 把触发点的提示挂进原生交互菜单。merge 模式（藏身处）要等原生菜单先出现一帧再并入——
/// 帧序状态由调用方（每个 VisitTrigger 实例）自己持有（T-4：旧版是共享静态字段，多点互扰）。
/// </summary>
public static class TriggerMenu
{
    public static bool Show(GamePlayerOwner owner, string name, System.Action fire, bool merge, ref int nativeSeenFrame)
    {
        var state = owner.AvailableInteractionState;
        if (!merge)
        {
            var shown = state.Value;
            if (shown?.Actions != null && shown.Actions.Count == 1 && shown.Actions[0].Name == name) return true;
            var menu = new AvailableInteractionState();
            menu.Actions.Add(new InteractionAction { Name = name, Action = fire });
            menu.InitSelected();
            state.Value = menu;
            return true;
        }
        var current = state.Value;
        if (current?.Actions == null || current.Actions.Count == 0) { nativeSeenFrame = -1; return false; }
        if (current.Actions.Any(a => a.Name == name)) return true;
        if (nativeSeenFrame < 0) { nativeSeenFrame = Time.frameCount; return false; }
        if (Time.frameCount <= nativeSeenFrame) return false;
        var merged = new AvailableInteractionState();
        merged.Actions.AddRange(current.Actions);
        merged.Actions.Add(new InteractionAction { Name = name, Action = fire });
        merged.DefaultSelected();
        state.Value = merged;
        return true;
    }

    public static void Hide(GamePlayerOwner owner, string name)
    {
        var current = owner.AvailableInteractionState.Value;
        if (current?.Actions == null || !current.Actions.Any(a => a.Name == name)) return;
        var rest = current.Actions.Where(a => a.Name != name).ToList();
        if (rest.Count == 0) { owner.AvailableInteractionState.Value = null; return; }
        var replaced = new AvailableInteractionState();
        replaced.Actions.AddRange(rest);
        replaced.DefaultSelected();
        owner.AvailableInteractionState.Value = replaced;
    }
}
