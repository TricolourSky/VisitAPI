using System;
using System.Collections.Generic;
using System.Reflection;
using EFT.Dialogs;
using HarmonyLib;
using Newtonsoft.Json;

namespace VisitAPI.Native;

/// 访问期间，同一对话里条件相同、当下都成立的几句商人台词随机挑一句放到最前（引擎只取第一句成立的商人台词）。
/// 09-24 审查低项：以前直接改写共享模板的台词顺序且不还原，对所有对话（包括主菜单里的原版对话）永久生效。
/// 现在只在访问期间做，并在构造完成后换回原来的台词表——DynamicTraderDialog 的构造函数把 template.Lines 读一遍建好自己的台词列表，之后不再读它
[HarmonyPatch(typeof(DynamicTraderDialog), MethodType.Constructor, typeof(TraderDialogTemplate), typeof(IDialogContext))]
public static class NarrateNpcVariantShuffle
{
    static readonly System.Random Rng = new();
    static readonly FieldInfo LinesField = AccessTools.Field(typeof(TraderDialogTemplate), "Lines");
    // 换了顺序还没换回来的模板 → 原来的台词表。构造抛异常时 Postfix 不跑，下次再构造这个模板时先还原
    static readonly Dictionary<TraderDialogTemplate, object> _pending = new();

    static void Prefix(TraderDialogTemplate template, IDialogContext context, out object __state)
    {
        __state = null;
        try
        {
            if (template == null || LinesField == null) return;
            if (_pending.TryGetValue(template, out var stale)) { LinesField.SetValue(template, stale); _pending.Remove(template); }
            if (!Narrating.Now) return;
            var lines = template.Lines;
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
            var original = LinesField.GetValue(template);
            var list = new List<DialogLineTemplate>(lines);
            (list[first], list[pick]) = (list[pick], list[first]);
            LinesField.SetValue(template, list);
            _pending[template] = original;
            __state = original;
        }
        catch (Exception e) { Plugin.Log.LogWarning("[narrate] Random trader line pick failed, using original order: " + e.Message); }
    }

    static void Postfix(TraderDialogTemplate template, object __state)
    {
        if (__state == null || template == null) return;
        try
        {
            LinesField.SetValue(template, __state);
            _pending.Remove(template);
        }
        catch (Exception e) { Plugin.Log.LogWarning("[narrate] Failed to restore trader line order (will restore next time this dialogue is built): " + e.Message); }
    }

    static string Sig(DialogMainConditionGroup t)
    {
        if (t == null) return "";
        try { return JsonConvert.SerializeObject(t); }
        catch { return "#" + t.GetHashCode(); }
    }
}
