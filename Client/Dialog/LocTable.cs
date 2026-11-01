using System.Collections.Generic;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

/// <summary>
/// 一份 .dlg 的字幕表（多语言，09-23）：默认文字一张表，各语言的译文各一张（来自 DialogLangs 的译文行）。
/// 给引擎的 locByLang 按「默认文字，盖上英文译文，再盖上该语言的译文」合成（中文、英文那两张不叠英文）；引擎按 LocalizationManager.Culture 挑表，
/// 所以「当前文化」那张放的是玩家要的语言（Loc.Code）。
/// </summary>
sealed class LocTable
{
    readonly Dictionary<string, string> _default = new();
    readonly Dictionary<string, Dictionary<string, string>> _byLang = new();
    readonly string _want = Loc.Code, _nick;

    public LocTable(string nick) { _nick = nick; }

    string Fill(string s) => s.Replace("{playerName}", _nick).Replace("{player}", _nick);

    /// <summary>登记一句：默认文字 + 各语言译文；返回字幕键 visitapi_{trader}_{node}_{tag}（存档兼容契约，别改）。</summary>
    public string Put(string trader, string node, string tag, string text, Dictionary<string, string> tr)
    {
        var k = $"visitapi_{trader}_{node}_{tag}";
        _default[k] = Fill(text);
        foreach (var kv in DialogLangs.Ordered(tr))
        {
            if (!_byLang.TryGetValue(kv.Key, out var d)) _byLang[kv.Key] = d = new Dictionary<string, string>();
            d[k] = Fill(kv.Value);
        }
        return k;
    }

    /// <summary>这一键按玩家语言该显示的字（旁白面板自己画的那份 NarrationByDialog）：玩家的语言 → 英文 → 默认文字（同 Loc.Tr）。</summary>
    public string Shown(string k) => Find(_want, k) ?? (_want != "ch" ? Find("en", k) : null) ?? _default[k];

    string Find(string code, string k) => _byLang.TryGetValue(code, out var d) && d.TryGetValue(k, out var s) ? s : null;

    public Dictionary<string, Dictionary<string, string>> Tables(string culture)
    {
        var r = new Dictionary<string, Dictionary<string, string>>();
        foreach (var code in _byLang.Keys) r[code] = Table(code);
        r["ch"] = Table("ch"); r["en"] = Table("en");
        if (!string.IsNullOrEmpty(culture)) r[culture] = Table(_want);
        return r;
    }

    /// 默认文字 → 英文译文 → 这门语言的译文，后盖前（10-26：没有这门语言的译文就直接退回默认文字的话，俄语界面下 SORA 的对话全是中文）
    Dictionary<string, string> Table(string code)
    {
        var d = new Dictionary<string, string>(_default);
        if (code != "ch" && code != "en") Overlay(d, "en");
        Overlay(d, code);
        return d;
    }

    void Overlay(Dictionary<string, string> d, string code)
    {
        if (_byLang.TryGetValue(code, out var over)) foreach (var kv in over) d[kv.Key] = kv.Value;
    }
}
