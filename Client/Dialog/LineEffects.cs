using System.Collections.Generic;
using EFT;
using EFT.UI;

namespace VisitAPI.Native;

/// <summary>
/// 一条对话行（选项）执行时要发生的全部副作用，一行一条记录。
/// 旧版是 6 个服务各持一张静态 `_byLine` 字典、各订阅一遍 OnExecuteLine —— 现在只有这一张表，
/// 由 <see cref="DialogTemplateBuilder"/> 写入、<see cref="DialogSession"/> 统一分发。
/// 行 id 是按 商人|节点#序号 确定性生成的：重开对话重新 Register 会整条覆盖，不会残留旧值。
/// </summary>
public sealed class LineEffect
{
    public (string trader, double delta)? Standing;
    public (string quest, int status)? SetStatus;
    public (MongoID id, int value)? SyncVar;         // set: 落服务端那一笔（本地写入走引擎自己的 Action）
    public string HandoverQuest;
    public TraderScreensGroup.ETraderMode? Tab;      // @trade/@tasks/@services
    public (string trader, string profile, string node, int option)? Once;
}

public static class LineEffects
{
    static readonly Dictionary<MongoID, LineEffect> _byLine = new();

    /// <summary>取出（没有就建）某行的效果记录。TemplateBuilder 每次都全字段赋值，天然覆盖旧值。</summary>
    public static LineEffect For(MongoID lineId)
    {
        if (!_byLine.TryGetValue(lineId, out var e)) _byLine[lineId] = e = new LineEffect();
        return e;
    }

    public static bool TryGet(MongoID lineId, out LineEffect e) => _byLine.TryGetValue(lineId, out e);
}
