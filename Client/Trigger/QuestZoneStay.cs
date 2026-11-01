using System;
using System.Collections.Generic;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.UI;
using UnityEngine;

namespace VisitAPI.Native;

/// <summary>10-17 SORA「看门狗」：zones 里 type "stay" 的计时区。主玩家在盒子里连续待够 Stay.Seconds 秒，
/// 才对这个区域的原生触发器手动 TriggerEnter——VisitPlace 目标照原生的路打勾（之后 autoFinish / 对话交任务都照旧）。
/// 出了盒子计时清零，回来重新计；只在 Stay.Quest 处于 Stay.Statuses（默认进行中）时计时，任务没接 / 已经交了就不动。
/// 计时到 Events[i].At 秒时触发一次事件（每局一次，计时清零后不再重复），现在的事件只有刷兵（StorySpawns.Fire）。
/// 倒计时显示（SORA 10-17：「跟时间有关的任务都会出现一个绿色方框的倒计时，例如埋藏、守护标记器」）：用原生屏幕上方那个绿色框
/// （GameUI.BattleUiPanelExtraction——撤离倒计时就是它，Fika 的流血 / 被救倒计时也借它），在区域里就一直显示「警戒中 01:59」，
/// 出范围 / 完成 / 死亡 / 任务不在进行中就收起；出范围另在右上角提示一句「计时重新开始」。Fika：每台机子计自己的主玩家，刷兵只有主机会刷。</summary>
public class QuestZoneStay : MonoBehaviour
{
    public QuestZones.Zone Zone;
    public TriggerWithId Trigger;

    float _since = -1f, _next;
    bool _done, _boxShown;
    int _shownSecs = -1;
    string _shownText, _boxText;   // 框上的字每秒才变一次，0.25 秒一拍不白拼、不白写（10-29）
    BattleUIPanelExtraction _box;
    readonly HashSet<int> _fired = new();

    void Update()
    {
        if (_done || Time.time < _next) return;
        _next = Time.time + 0.25f;
        try
        {
            var rule = Zone?.Stay;
            if (rule == null || Trigger == null) { _done = true; return; }
            var player = Raid.MainPlayer;
            if (!Raid.Alive(player) || !QuestOps.StatusIn(rule.Quest, rule.Statuses)) { _since = -1f; HideBox(); return; }
            if (!Inside(player.Position))
            {
                if (_since >= 0f) Notice(Loc.Pick("离开了警戒范围，计时重新开始", "You left the area. The timer restarts."));
                _since = -1f;
                HideBox();
                return;
            }
            if (_since < 0f) _since = Time.time;
            var t = Time.time - _since;
            for (var i = 0; i < rule.Events.Count; i++)
            {
                if (t < rule.Events[i].At || !_fired.Add(i)) continue;
                StorySpawns.Fire(rule.Events[i].Spawn, transform.position, player);
            }
            if (t >= rule.Seconds)
            {
                _done = true;
                HideBox();
                Trigger.TriggerEnter(player);
                return;
            }
            var secs = Mathf.CeilToInt(rule.Seconds - t);
            if (secs != _shownSecs || _shownText == null) { _shownSecs = secs; _shownText = $"{Caption(rule)} {Clock(secs)}"; }
            ShowBox(_shownText);
        }
        catch (Exception e) { Plugin.WarnOnce("zone-stay-" + Zone?.Id, $"[zones] {Zone?.Id} stay timer failed: " + e.Message); }
    }

    void OnDestroy() => HideBox();

    /// 第一次（或被别的东西收起之后）走原生 Show（带淡入），之后只改字，免得每次都重播淡入
    void ShowBox(string text)
    {
        try
        {
            var box = MonoBehaviourSingleton<GameUI>.Instance?.BattleUiPanelExtraction;
            if (box == null) return;
            if (!_boxShown || _box != box || !box.gameObject.activeSelf) { box.Show(text); _box = box; _boxShown = true; _boxText = text; }
            else if (text != _boxText && box._extractionLabel != null) { box._extractionLabel.text = text; _boxText = text; }
        }
        catch (Exception e) { Plugin.WarnOnce("zone-stay-box", "[zones] could not show the stay countdown box: " + e.Message); }
    }

    void HideBox()
    {
        if (!_boxShown) return;
        _boxShown = false;
        try { if (_box != null && _box.gameObject.activeSelf) _box.Close(); }
        catch (Exception e) { Plugin.WarnOnce("zone-stay-box-hide", "[zones] could not hide the stay countdown box: " + e.Message); }
    }

    static string Caption(QuestZones.StayRule rule) => Loc.Text(rule.Caption, "警戒中", "Guarding");

    bool Inside(Vector3 p) => QuestZones.Contains(transform, Zone?.Size ?? Vector3.one, p);

    static string Clock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";

    static void Notice(string text)
    {
        try { NotificationManager.DisplayMessageNotification(text); }
        catch (Exception e) { Plugin.WarnOnce("zone-stay-notice", "[zones] could not show the stay timer notice: " + e.Message); }
    }
}
