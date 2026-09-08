using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace VisitAPI.ChapterUI
{
    /// <summary>「相关物品」区（1.1 的 ChapterLinks / 日记下面的 LinksList）：一行物品图标，图标用游戏自己的物品图生成器
    /// （ItemViewFactory.LoadItemIcon，和仓库格子同源），悬停出原生提示框；每个物品也是"可读"项（新物品挂绿 `!`）。
    /// G5：`craft:`/`offer:` 型物品加类型角标（借原生藏身处/跳蚤图标，风格不出戏）；G6：点击打开原生物品详情窗。DEV_NOTES #71。</summary>
    public partial class MainQuestTabView
    {
        static readonly Dictionary<string, Item> _linkItems = new();   // 展示用物品，一个模板造一次
        readonly Dictionary<GameObject, string> _linkMarkers = new();   // 屏上的物品绿标 → 已读 key（池化复用时同键覆盖，不膨胀）

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
                var image = IconImage(link != null && link._itemIconContainer != null ? link._itemIconContainer : (RectTransform)view.transform);
                var icon = ItemViewFactory.LoadItemIcon(item);
                if (icon.Sprite != null) image.sprite = icon.Sprite;
                else { System.Action unsub = null; unsub = icon.Changed.Bind(() => { if (image != null) image.sprite = icon.Sprite; unsub?.Invoke(); }); }   // ItemIcon 是全局缓存件，Bind 返回退订委托，用完就退
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

        /// G5：类型角标——Craft 借原生"藏身处"通知图标、Offer 借"跳蚤"图标，普通物品不占角
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

        /// G6：点物品开原生详情窗（和仓库右键"检视"同一扇窗）
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

        static Image IconImage(RectTransform parent)
        {
            var old = parent.Find("ItemIcon");
            if (old != null) return old.GetComponent<Image>();
            var rt = (RectTransform)new GameObject("ItemIcon", typeof(RectTransform), typeof(Image)).transform;
            rt.SetParent(parent, false);
            var img = rt.Stretch().GetComponent<Image>(); img.preserveAspect = true; img.raycastTarget = false; return img;
        }
    }
}
