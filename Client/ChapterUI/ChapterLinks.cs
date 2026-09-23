using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using JsonType;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    public partial class MainQuestTabView
    {
        static readonly Dictionary<string, Item> _linkItems = new();
        readonly Dictionary<GameObject, string> _linkMarkers = new();

        void FillLinks(MainQuestLinkedItemsListView v, IEnumerable<(ChapterModel.ELink type, string tpl, string raw)> items)
        {
            if (v == null || v._itemsContainer == null) return;
            var template = v._itemViewTemplate != null ? v._itemViewTemplate.gameObject : ChapterBundle.Prefab("LinkedItemView");
            var list = items?.ToList() ?? new List<(ChapterModel.ELink, string, string)>();
            (v._parentPanel != null ? v._parentPanel : v.gameObject).SetActive(list.Count > 0);
            if (template == null) return;
            var pool = ViewPool.For(v._itemsContainer, template);
            pool.ReleaseAll();
            foreach (var (type, tpl, raw) in list)
            {
                var item = LinkItem(tpl); if (item == null) continue;
                var view = pool.Acquire();
                var link = view.GetComponent<MainQuestLinkedItemView>();
                var cell = link != null && link._itemIconContainer != null ? link._itemIconContainer : (RectTransform)view.transform;
                CellBackground(cell, item);
                var image = IconImage(cell);
                var icon = ItemViewFactory.LoadItemIcon(item);
                if (icon.Sprite != null) image.sprite = icon.Sprite;
                else
                {
                    System.Action unsub = null; unsub = icon.Changed.Bind(() => { if (image != null) image.sprite = icon.Sprite; unsub?.Invoke(); });
                    if (icon.Sprite != null) { image.sprite = icon.Sprite; unsub?.Invoke(); }
                }
                if (ItemUiContext.Instance != null)
                    (view.GetComponent<HoverTooltipArea>() ?? view.AddComponent<HoverTooltipArea>()).Init(ItemUiContext.Instance.Tooltip, item.LocalizedName(), true);
                TypeBadge(link, type);
                var marker = link != null ? link._unreadMarker : null; var key = "item:" + raw;
                if (marker != null) { marker.SetActive(!ReadState.IsRead(key)); _linkMarkers[marker] = key; }
                ReadState.OnHover(view, () => { ReadState.MarkRead(new[] { key }); RefreshUnread(); });
                var btn = view.GetComponent<Button>() ?? view.AddComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => Inspect(item));
            }
        }

        static void TypeBadge(MainQuestLinkedItemView link, ChapterModel.ELink type)
        {
            if (link == null || link._typeIcon == null) return;
            Sprite s = null;
            try
            {
                if (type == ChapterModel.ELink.Craft) s = EFTHardSettings.Instance.StaticIcons.NotificationSprites[ENotificationIconType.Hideout];
                else if (type == ChapterModel.ELink.Offer) s = EFTHardSettings.Instance.StaticIcons.NotificationSprites[ENotificationIconType.RagFair];
            }
            catch { }
            link._typeIcon.sprite = s;
            link._typeIcon.enabled = s != null;
        }

        static void Inspect(Item item)
        {
            if (ItemUiContext.Instance == null) return;
            try { ItemUiContext.Instance.Inspect(new DefaultItemContext(item, EItemViewType.Handbook), null); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/items] inspect failed: " + e.Message); }
        }

        static Item LinkItem(string tpl)
        {
            if (_linkItems.TryGetValue(tpl, out var cached)) return cached;
            try { return _linkItems[tpl] = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate().ToString(), tpl, null); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/items] bad template " + tpl + ": " + e.Message); return _linkItems[tpl] = null; }
        }

        static readonly Color CellFrame = new Color32(70, 70, 70, 220);
        static readonly Color CellBase = new Color32(12, 12, 12, 210);
        static void CellBackground(RectTransform cell, Item item)
        {
            var frame = Layer(cell, "CellFrame", 0, CellFrame);
            var back = Layer(cell, "CellBase", 1, CellBase);
            var tint = Layer(cell, "CellTint", 2, Color.clear);
            try
            {
                var c = item.BackgroundColor.ToColor(); c.a = 0.3019608f;
                tint.color = c;
            }
            catch (System.Exception e) { Plugin.Log.LogDebug("[chapter/items] 背景色取不到，按默认: " + e.Message); tint.color = new Color(0.4f, 0.4f, 0.4f, 0.3f); }
            frame.transform.SetAsFirstSibling(); back.transform.SetSiblingIndex(1); tint.transform.SetSiblingIndex(2);
        }

        static Image Layer(RectTransform parent, string name, int inset, Color color)
        {
            var old = parent.Find(name);
            var img = old != null ? old.GetComponent<Image>() : null;
            if (img == null)
            {
                var rt = (RectTransform)new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
                rt.SetParent(parent, false);
                rt.Stretch();
                rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
                img = rt.GetComponent<Image>(); img.raycastTarget = false;
            }
            img.color = color;
            return img;
        }

        static Image IconImage(RectTransform parent)
        {
            var old = parent.Find("ItemIcon");
            if (old != null) { old.SetAsLastSibling(); return old.GetComponent<Image>(); }
            var rt = (RectTransform)new GameObject("ItemIcon", typeof(RectTransform), typeof(Image)).transform;
            rt.SetParent(parent, false);
            rt.Stretch();
            rt.offsetMin = new Vector2(4f, 4f); rt.offsetMax = new Vector2(-4f, -4f);
            var img = rt.GetComponent<Image>(); img.preserveAspect = true; img.raycastTarget = false; return img;
        }
    }
}
