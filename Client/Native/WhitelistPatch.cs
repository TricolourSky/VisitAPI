using System;
using System.Collections.Generic;
using EFT;
using EFT.Dialogs;
using EFT.UI;
using HarmonyLib;

namespace VisitAPI.Native;

// method_5 = 原生对话屏按商人 id 放行的白名单 switch, 未列入的商人会抛异常
// finalizer 吞掉该异常并对 RegisteredTraders 里的商人直接 StartDialog 兜底放行
[HarmonyPatch(typeof(TraderDialogScreen), "method_5")]
public static class WhitelistPatch
{
    public static readonly HashSet<string> RegisteredTraders = new();

    static Exception Finalizer(Exception __exception, ClientDialogController ___dialogController,
        MongoID ____traderId, MongoID? ____dialogId, ITraderAnimationController ____animationController)
    {
        if (__exception == null || !RegisteredTraders.Contains(____traderId.ToString())) return __exception;
        ___dialogController.StartDialog(____traderId, ____dialogId, ____animationController);
        return null;
    }
}
