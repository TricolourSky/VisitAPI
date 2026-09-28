using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Quests;

namespace VisitAPI.Native;

/// <summary>09-24：1.1 数据里任务带的 GlobalVariable 奖励（进入 Started / Success / Fail 时给档案变量赋值）0.16.9 客户端不认这种奖励类型，
/// 移植时被剥掉了，门票里 4 条因此断掉（692d64d1 失败要把 692d6e97 清零，不清那段对话只能触发一次；68e91563 失败要把 68e8dce2 置 1，Kerman 那段 28 处条件在读）。
/// 包数据改写成 visitapi.setVariables: { "Fail": { "&lt;变量id&gt;": 1 } }，服务端 /visitapi/quest/flags 原样下发，这里看到任务进入对应状态就赋值——
/// 本地 ProfileVariables 立刻生效（任务引擎像对话赋值一样触发条件），再 Vars.Sync 落到服务端档案，和控制台 visit_setvar 是同一条路。
///
/// 什么时候赋（同日审查后重写，原来是「状态时间戳在 30 分钟内才赋」，30 分钟内重开游戏会再赋一次、客户端没赶上那次迁移就漏赋）：
///   ① 本会话亲眼看到的状态迁移（OnConditionalStatusChangedEvent，live=true，且映射后的状态键变了）一定赋——可重开任务第二次失败时
///      引擎不更新失败时间戳（StatusStartTimestamps 只在键不存在时写），靠时间戳分不出来；
///   ② 其余情况（登录时任务刚进任务书、重扫）按档案里的记号去重：每条（任务, 状态键）记一个档案变量 = 上次赋值对应的事件时间戳，
///      事件时间戳比记号新才赋。记号跟着档案走（Vars.Sync），重登不会重复赋，离线期间发生的也能补上。</summary>
public static class QuestVars
{
    static readonly Dictionary<string, string> _seen = new();   // 本会话里每条任务最近一次的状态键（null = 不关心的状态）
    static string _profileId;   // _seen 属于哪个档案；换档案（登出再登另一个）就清空，免得拿上一个档案的状态判「迁移」

    public static void Apply(Quest quest, bool live)
    {
        try { ApplyCore(quest, live); }
        catch (System.Exception e) { Plugin.Log.LogWarning($"[chain] Failed to apply setVariables for {quest?.Id}: {e.Message}"); }
    }

    static void ApplyCore(Quest quest, bool live)
    {
        if (quest?.Template == null) return;
        var profile = CurrentProfile();
        if (profile != null && profile.Id != _profileId) { _seen.Clear(); _profileId = profile.Id; }
        var key = StatusKey(quest.QuestStatus);
        var known = _seen.TryGetValue(quest.Id, out var before);
        _seen[quest.Id] = key;
        if (key == null) return;
        var vars = QuestFlags.VariablesOn(quest.Id, key);
        if (vars == null) return;
        var storage = profile?.ProfileVariables;
        if (storage == null) { Plugin.Log.LogWarning($"[chain] {quest.Id} entered {key}, but the profile variable storage is unavailable, skipping assignment this time"); return; }
        var stamp = EventStamp(quest, key);
        var mark = MarkId(quest.Id, key);
        var applied = storage.GetVariableValue(mark);
        var transition = live && known && before != key;
        if (!transition && applied >= stamp) return;   // 这次事件（或更晚的）已经赋过
        foreach (var kv in vars)
        {
            var id = new MongoID(kv.Key);
            storage.SetVariableValue(id, kv.Value);
            Vars.Sync(id, kv.Value);
        }
        var value = System.Math.Max(applied, stamp);
        storage.SetVariableValue(mark, value);
        Vars.Sync(mark, value);
    }

    /// 这次状态事件的时间戳（Unix 秒）：映射到同一个键的几种引擎状态取最早的那个——大厅里先 MarkedAsFailed、服务端确认后再 Fail，
    /// 两个时间戳不同但是同一次失败，取最早的就不会赋两次。没有时间戳时记 1（仍然只赋一次）
    static int EventStamp(Quest quest, string key)
    {
        var first = double.MaxValue;
        if (quest.StatusStartTimestamps != null)
            foreach (var kv in quest.StatusStartTimestamps)
                if (StatusKey(kv.Key) == key && kv.Value > 0d && kv.Value < first) first = kv.Value;
        if (first == double.MaxValue) return 1;
        return first >= int.MaxValue ? int.MaxValue : System.Math.Max(1, (int)first);
    }

    static MongoID MarkId(string questId, string key) => DialogTemplateBuilder.Id("visitapi.qv", questId + "|" + key);

    static string StatusKey(EQuestStatus st) => st switch
    {
        EQuestStatus.Started => "Started",
        EQuestStatus.Success => "Success",
        EQuestStatus.Fail or EQuestStatus.MarkedAsFailed or EQuestStatus.FailRestartable => "Fail",
        _ => null
    };

    static Profile CurrentProfile()
    {
        try { return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession()?.Profile; }
        catch { return null; }
    }
}
