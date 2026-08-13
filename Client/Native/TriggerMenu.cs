using System.Linq;
using EFT;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

public static class TriggerMenu
{
    static int _nativeSeenFrame = -1;

    public static bool Show(GamePlayerOwner owner, string name, System.Action fire, bool merge)
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
        if (current?.Actions == null || current.Actions.Count == 0) { _nativeSeenFrame = -1; return false; }
        if (current.Actions.Any(a => a.Name == name)) return true;
        if (_nativeSeenFrame < 0) { _nativeSeenFrame = Time.frameCount; return false; }
        if (Time.frameCount <= _nativeSeenFrame) return false;
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
