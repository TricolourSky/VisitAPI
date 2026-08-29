using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using EFT;
using EFT.Dialogs;
using EFT.UI;
using SPT.Common.Http;

namespace VisitAPI.Native;

public static class StandingService
{
    static readonly Dictionary<MongoID, (string trader, double delta)> _byLine = new();

    public static void Register(MongoID lineId, string traderId, double delta) => _byLine[lineId] = (traderId, delta);

    public static void Watch(BaseTraderDialogController dc, Profile profile, TraderScreensGroup screen)
    {
        dc.OnDialogChanged += dialog =>
        {
            if (dialog == null) return;
            dialog.OnExecuteLine += line =>
            {
                if (line?.Template != null && _byLine.TryGetValue(line.Template.Id, out var s)) Apply(profile, screen, s.trader, s.delta);
            };
        };
    }

    static void Apply(Profile profile, TraderScreensGroup screen, string traderId, double delta)
    {
        profile.TradersInfo.TryGetValue(new MongoID(traderId), out var info);
        var session = screen != null ? screen.TradersList?.FirstOrDefault(t => t.Id == traderId)?.Info : null;
        if (info == null && session == null) { Plugin.Log.LogWarning("[standing] trader not found: " + traderId); return; }
        var value = System.Math.Max(0.0, (session?.Standing ?? info.Standing) + delta);
        if (info != null) info.SetStanding(value);
        if (session != null && !ReferenceEquals(session, info)) session.SetStanding(value);
        Plugin.Log.LogDebug($"[standing] {traderId} {(delta >= 0 ? "+" : "")}{delta} -> {value:0.###}");
        VisitHttp.Post("/visitapi/standing/add", "{\"traderId\":\"" + traderId + "\",\"delta\":" + delta.ToString(CultureInfo.InvariantCulture) + "}", "[standing]");
    }
}
