using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace VisitAPI
{
    internal static class DialogTreeLoader
    {
        internal static string BaseDir
        {
            get
            {
                string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                string bepInEx = Path.GetFullPath(Path.Combine(pluginDir, "..", ".."));
                return Path.Combine(bepInEx, "config", "VisitAPI");
            }
        }

        internal static string ResolveAsset(string relative) => Path.Combine(BaseDir, relative);

        // True if this trader has an authored `<id>.dlg` — the out-of-raid 对话 button only shows for these.
        internal static bool Exists(string traderId) => File.Exists(Path.Combine(BaseDir, traderId + ".dlg"));

        // Every trader id with a `.dlg` in the config folder (filename = trader id by convention). Drives both the
        // whitelist bypass and the out-of-raid entry button, so any modder's trader gets a button just by dropping a .dlg.
        internal static IEnumerable<string> ListTraderIds()
        {
            if (!Directory.Exists(BaseDir)) yield break;
            foreach (string file in Directory.GetFiles(BaseDir, "*.dlg"))
                yield return Path.GetFileNameWithoutExtension(file);
        }

        internal static DialogTree? Load(string traderId)
        {
            string path = Path.Combine(BaseDir, traderId + ".dlg");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[Dialog] .dlg not found: " + path);
                return null;
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            List<string> errors = new List<string>();
            DialogTree? tree = DialogScriptParser.Parse(lines, traderId + ".dlg", errors);
            foreach (string e in errors)
                Plugin.Log.LogWarning("[Dialog] " + e);
            return tree;
        }

        // Parsed-tree cache for the out-of-raid tab gate only. The trade screen re-selects traders often and the
        // `tab:` gate is static per .dlg, so parse once here (also avoids re-logging parse warnings every select).
        // The live click path (Load) stays uncached so an author can edit a .dlg between visits.
        private static readonly Dictionary<string, DialogTree?> _tabTreeCache =
            new Dictionary<string, DialogTree?>(System.StringComparer.OrdinalIgnoreCase);

        // The out-of-raid 对话 button visibility gate (`tab:` header). No `tab:` or `tab: always` = always shown;
        // `tab: if <quest>=<status>[/…]` = shown only while that quest is currently in one of the listed statuses.
        internal static bool TabVisible(string traderId)
        {
            if (!_tabTreeCache.TryGetValue(traderId, out DialogTree? tree))
            {
                tree = Load(traderId);
                _tabTreeCache[traderId] = tree;
            }
            if (tree == null || tree.TabAlways || string.IsNullOrEmpty(tree.TabQuestId)
                || tree.TabShowWhenStatus == null || tree.TabShowWhenStatus.Count == 0)
            {
                return true;
            }
            return QuestStatusCache.InAny(QuestStatusCache.StatusOf(tree.TabQuestId!), tree.TabShowWhenStatus);
        }
    }
}
