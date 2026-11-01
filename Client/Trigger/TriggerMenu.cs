using System.Linq;
using EFT;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

public static class TriggerMenu
{
    public static bool Show(GamePlayerOwner owner, string name, System.Action fire, bool merge, ref int nativeSeenFrame)
    {
        var state = owner.AvailableInteractionState;
        var current = state.Value;
        if (!merge)
        {
            if (current?.Actions != null && current.Actions.Count == 1 && current.Actions[0].Name == name) return true;
            var menu = new AvailableInteractionState();
            menu.Actions.Add(new InteractionAction { Name = name, Action = fire });
            menu.InitSelected();
            state.Value = menu;
            return true;
        }
        if (current?.Actions == null || current.Actions.Count == 0) { nativeSeenFrame = -1; return false; }
        if (Has(current, name)) return true;
        if (nativeSeenFrame < 0) { nativeSeenFrame = Time.frameCount; return false; }
        if (Time.frameCount <= nativeSeenFrame) return false;
        var merged = new AvailableInteractionState();
        merged.Actions.AddRange(current.Actions);
        merged.Actions.Add(new InteractionAction { Name = name, Action = fire });
        merged.DefaultSelected();
        state.Value = merged;
        return true;
    }

    /// Show 每帧都来：for 循环，不走 LINQ（闭包 + 枚举器每帧分配）
    static bool Has(AvailableInteractionState state, string name)
    {
        var acts = state.Actions;
        for (var i = 0; i < acts.Count; i++) if (acts[i].Name == name) return true;
        return false;
    }

    public static void Hide(GamePlayerOwner owner, string name)
    {
        var current = owner.AvailableInteractionState.Value;
        if (current?.Actions == null || !Has(current, name)) return;
        var rest = current.Actions.Where(a => a.Name != name).ToList();
        if (rest.Count == 0) { owner.AvailableInteractionState.Value = null; return; }
        var replaced = new AvailableInteractionState();
        replaced.Actions.AddRange(rest);
        replaced.DefaultSelected();
        owner.AvailableInteractionState.Value = replaced;
    }
}
