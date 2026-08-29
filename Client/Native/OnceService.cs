using System.Collections.Generic;
using EFT;
using EFT.Dialogs;
using VisitAPI.Dialog;

namespace VisitAPI.Native;

public static class OnceService
{
    static readonly Dictionary<string, DialogStateStore> _stores = new();
    static readonly Dictionary<MongoID, (string trader, string profile, string node, int option)> _byLine = new();

    public static DialogStateStore Store(string traderId)
    {
        if (!_stores.TryGetValue(traderId, out var s)) _stores[traderId] = s = new DialogStateStore(DialogFiles.Loader.BaseDir, traderId);
        return s;
    }

    public static DialogCondition Register(MongoID lineId, string traderId, string profileId, string node, int option)
    {
        _byLine[lineId] = (traderId, profileId, node, option);
        return new OnceGate(Store(traderId), profileId, node, option);
    }

    public static void Watch(BaseTraderDialogController dc)
    {
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += line =>
            {
                if (line?.Template != null && _byLine.TryGetValue(line.Template.Id, out var s))
                { Store(s.trader).MarkOnce(s.profile, s.node, s.option); Plugin.Log.LogDebug($"[once] {s.node}#{s.option} marked"); }
            };
        };
    }
}
