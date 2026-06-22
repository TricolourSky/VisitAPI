using System;
using System.Collections;
using System.Reflection;
using VisitAPI.Native;

namespace VisitAPI
{
    // Dialogue-driven trader standing (好感度) — applies a `standing:` delta to the LIVE profile. EFT keeps
    // per-trader rep in Profile.TradersInfo[traderId].Standing; nudging it updates the trader screen this session
    // (and clamps at 0). NOTE: persistence relies on the profile being saved later (after a raid / menu action) —
    // if it must persist immediately a server route can be added (same shape as the quest-complete route). All
    // reflection, fails soft: a missing member just logs and no-ops, it never throws into the dialog.
    internal static class StandingService
    {
        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static void Apply(string traderId, double delta)
        {
            if (string.IsNullOrEmpty(traderId) || delta == 0) return;
            try
            {
                // Raid/hideout: the local player. Menu (no MyPlayer): the profile stashed by the open path
                // (out-of-raid changes persist when the menu next saves the profile).
                EFT.Player player = EFT.GamePlayerOwner.MyPlayer;
                object? profile = (player != null ? NativeBinder.GetProfile(player) : null) ?? NativeBinder.ActiveProfile;
                if (profile == null) { Plugin.Log.LogWarning("[Standing] no profile (not in raid and no active dialog profile)"); return; }
                object? infos = profile?.GetType().GetProperty("TradersInfo", All)?.GetValue(profile)
                    ?? profile?.GetType().GetField("TradersInfo", All)?.GetValue(profile);
                if (!(infos is IDictionary dict)) { Plugin.Log.LogWarning("[Standing] TradersInfo not found / not a dictionary"); return; }

                object? ti = FindByKey(dict, traderId);
                if (ti == null) { Plugin.Log.LogWarning("[Standing] no TraderInfo for " + traderId); return; }

                PropertyInfo? standing = ti.GetType().GetProperty("Standing", All);
                if (standing == null || !standing.CanWrite) { Plugin.Log.LogWarning("[Standing] Standing not writable on " + ti.GetType().Name); return; }

                double cur = Convert.ToDouble(standing.GetValue(ti));
                double next = Math.Max(0.0, cur + delta);
                standing.SetValue(ti, Convert.ChangeType(next, standing.PropertyType));
                Plugin.Log.LogInfo("[Standing] " + traderId + ": " + cur.ToString("0.###") + " -> " + next.ToString("0.###") + " (" + (delta >= 0 ? "+" : "") + delta + ")");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Standing] apply failed: " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        // TradersInfo keys are MongoID structs, not strings — match on ToString().
        private static object? FindByKey(IDictionary dict, string key)
        {
            foreach (DictionaryEntry e in dict)
                if (string.Equals(e.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase)) return e.Value;
            return null;
        }
    }
}
