using System;
using System.Collections.Generic;
using EFT.Dialogs;
using HarmonyLib;
using Newtonsoft.Json;

namespace VisitAPI.Native;

[HarmonyPatch(typeof(DynamicTraderDialog), MethodType.Constructor, typeof(TraderDialogTemplate), typeof(IDialogContext))]
public static class NarrateNpcVariantShuffle
{
    static readonly System.Random Rng = new();

    static void Prefix(TraderDialogTemplate template, IDialogContext context)
    {
        try
        {
            var lines = template?.Lines;
            if (lines == null || lines.Count < 2 || context == null) return;
            var first = -1; string sig = null;
            var group = new List<int>();
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l == null || l.DialogSide != EDialogSide.Npc) continue;
                bool pass;
                try { pass = l.Trigger == null || l.Trigger.Test(context); } catch { pass = false; }
                if (!pass) continue;
                var s = Sig(l.Trigger);
                if (first < 0) { first = i; sig = s; group.Add(i); continue; }
                if (s == sig) group.Add(i);
            }
            if (group.Count < 2) return;
            var pick = group[Rng.Next(group.Count)];
            if (pick == first) return;
            var list = new List<DialogLineTemplate>(lines);
            (list[first], list[pick]) = (list[pick], list[first]);
            AccessTools.Field(typeof(TraderDialogTemplate), "Lines").SetValue(template, list);
            Plugin.Log.LogDebug($"[narrate] 对话 {template.Id} 同条件的商人台词 {group.Count} 句，本次随机到 {lines[pick].Id}");
        }
        catch (Exception e) { Plugin.Log.LogWarning("[narrate] 商人台词随机挑选失败，按原顺序: " + e.Message); }
    }

    static string Sig(DialogMainConditionGroup t)
    {
        if (t == null) return "";
        try { return JsonConvert.SerializeObject(t); }
        catch { return "#" + t.GetHashCode(); }
    }
}
