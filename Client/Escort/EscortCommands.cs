using System.Linq;
using EFT;
using EFT.Communications;
using HarmonyLib;

namespace VisitAPI.Native;

/// <summary>10-16 SORA：「有什么可以直接指挥他们的方式嘛」→ 借游戏自带的语音轮盘（Y 键那套），不加按键。
/// 只认本机玩家自己说的：跟我走 = 恢复跟随；原地待命 / 停 = 守住下令时的位置（有敌人照样交给兵自己的 AI 打）；集合 = 跑到身边再恢复跟随；
/// 冲（Go go go / 前进）= 奔向当前敌人（自己没发现敌人的跟着有目标的队友冲同一个，10-26），到跟前交给兵自己的 AI（SAIN 或原版），20 秒到期自动恢复。Fika 客机的话主机听不到，v1 只有主机能指挥。</summary>
public static class EscortCommands
{
    [HarmonyPatch(typeof(Player), nameof(Player.Say))]
    public static class Said
    {
        static void Postfix(Player __instance, EPhraseTrigger phrase)
        {
            if (__instance == null || !__instance.IsYourPlayer || EscortRegistry.Count == 0) return;
            EscortMode mode;
            switch (phrase)
            {
                case EPhraseTrigger.FollowMe: mode = EscortMode.Follow; break;
                case EPhraseTrigger.HoldPosition:
                case EPhraseTrigger.Stop: mode = EscortMode.Hold; break;
                case EPhraseTrigger.Regroup: mode = EscortMode.Come; break;
                case EPhraseTrigger.Gogogo:
                case EPhraseTrigger.GoForward: mode = EscortMode.Charge; break;
                default: return;
            }
            var n = EscortRegistry.SetMode(__instance, mode);
            if (n == 0) return;
            try
            {
                var what = mode switch
                {
                    EscortMode.Hold => Loc.Pick("原地待命", "hold position"),
                    EscortMode.Come => Loc.Pick("集合", "regroup"),
                    EscortMode.Charge => Loc.Pick("冲", "push"),
                    _ => Loc.Pick("跟我走", "follow me"),
                };
                NotificationManager.DisplayMessageNotification(Loc.Pick($"{Who(__instance, n)}：{what}", $"{Who(__instance, n)}: {what}"));
            }
            catch { }
        }

        /// 10-18：SORA 同伴也听这套指挥，提示按实际跟着的人写（只有她就写 SORA）。10-25：队友的称呼由剧情包写（Escort.Label，没写就是「队友」）
        static string Who(Player owner, int n)
        {
            var sora = EscortRegistry.AliveOf(owner).Count(Escort.IsSora);
            var label = Escort.Label;
            if (sora == 0) return $"{label} ×{n}";
            return sora == n ? "SORA" : Loc.Pick($"SORA 和{label} ×{n - sora}", $"SORA and {label} ×{n - sora}");
        }
    }
}
