using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using UnityEngine;

namespace VisitAPI
{
    // Experimental "collectible intel" (Tarkov-1.0-style): a real, natively lootable item is placed in the map
    // (`trigger: item …`); picking it up accepts a quest line + shows a notification. The item is REAL native
    // loot — ItemFactoryClass.CreateItem + GameWorld.SetupItem (the same path the game uses to materialize
    // items in the world), so pickup, inventory and FiR are all vanilla behaviour. An item only spawns while
    // its quest is still un-accepted (null/Locked/AvailableForStart), so a collected intel never re-appears.
    internal static class QuestItemSpawner
    {
        private sealed class Pending
        {
            public string QuestId = "";
            public string Note = "";
        }

        private static readonly Dictionary<string, Pending> _watched = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        private static InventoryController? _subscribed;

        internal static int SpawnAll(DialogTree tree, string loc)
        {
            if (tree.ItemTriggers == null) return 0;
            int n = 0;
            foreach (ItemTrigger t in tree.ItemTriggers)
            {
                if (t.Position == null || t.Position.Length < 3 || string.IsNullOrEmpty(t.Tpl)) continue;
                if (!RaidTriggerManager.MapMatches(loc, t.Map)) continue;
                if (!string.IsNullOrEmpty(t.AcceptQuestId))
                {
                    int? status = QuestStatusCache.StatusOf(t.AcceptQuestId!);
                    if (status.HasValue && status.Value >= 2) continue;
                }
                if (SpawnOne(t)) n++;
            }
            return n;
        }

        private static bool SpawnOne(ItemTrigger t)
        {
            try
            {
                GameWorld gw = Singleton<GameWorld>.Instance;
                if (!Singleton<ItemFactoryClass>.Instantiated)
                {
                    Plugin.Log.LogWarning("[QuestItem] ItemFactoryClass singleton not ready — cannot spawn tpl " + t.Tpl);
                    return false;
                }
                ItemFactoryClass factory = Singleton<ItemFactoryClass>.Instance;
                string id = Guid.NewGuid().ToString("N").Substring(0, 24);
                Item item = factory.CreateItem(id, t.Tpl, null);
                if (item == null)
                {
                    Plugin.Log.LogWarning("[QuestItem] CreateItem failed for tpl " + t.Tpl + " (unknown template?)");
                    return false;
                }
                item.SpawnedInSession = true;
                Vector3 pos = new Vector3(t.Position![0], t.Position[1], t.Position[2]);
                gw.SetupItem(item, gw.MainPlayer, pos, Quaternion.Euler(0f, t.RotationY, 0f));
                if (!string.IsNullOrEmpty(t.AcceptQuestId))
                {
                    _watched[t.Tpl] = new Pending { QuestId = t.AcceptQuestId!, Note = t.Note ?? "" };
                    EnsureSubscription(gw);
                }
                Plugin.Log.LogInfo("[QuestItem] spawned tpl " + t.Tpl + " at " + pos + (t.AcceptQuestId != null ? " -> quest " + t.AcceptQuestId : ""));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[QuestItem] spawn tpl " + t.Tpl + " failed: " + (ex.InnerException ?? ex).Message);
                return false;
            }
        }

        private static void EnsureSubscription(GameWorld gw)
        {
            InventoryController? inv = gw.MainPlayer != null ? gw.MainPlayer.InventoryController : null;
            if (inv == null || ReferenceEquals(_subscribed, inv)) return;
            Unsubscribe();
            inv.AddItemEvent += OnItemAdded;
            _subscribed = inv;
        }

        // Pickup detection: the native AddItemEvent fires on the main thread when the item lands in the player's
        // inventory, so quest accept + notification are safe here (no ContinueWith — DEV_NOTES #2).
        private static void OnItemAdded(GEventArgs2 args)
        {
            if (args == null || args.Status != CommandStatus.Succeed || args.Item == null) return;
            string tpl = args.Item.TemplateId;
            if (!_watched.TryGetValue(tpl, out Pending pending)) return;
            _watched.Remove(tpl);
            Plugin.Log.LogInfo("[QuestItem] picked up tpl " + tpl + " -> accepting quest " + pending.QuestId);
            NativeQuestController.AcceptQuest(pending.QuestId);
            string note = string.IsNullOrEmpty(pending.Note) ? Loc.DefaultItemQuestNote : pending.Note;
            try
            {
                NotificationManagerClass.DisplayMessageNotification(note, ENotificationDurationType.Long, ENotificationIconType.Quest, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[QuestItem] notification: " + ex.Message);
            }
        }

        internal static void Reset()
        {
            _watched.Clear();
            Unsubscribe();
        }

        private static void Unsubscribe()
        {
            if (_subscribed == null) return;
            try { _subscribed.AddItemEvent -= OnItemAdded; } catch { }
            _subscribed = null;
        }
    }
}
