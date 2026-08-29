using System.Collections.Generic;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI.ChapterUI
{
    /// <summary>「相关物品」区（1.1 的 ChapterLinks / 日记下面的 LinksList）：一行物品图标。图标用游戏自己的物品图生成器
    /// （ItemViewFactory.LoadItemIcon，和仓库格子同源），悬停出原生提示框显示物品名；每个物品也是一个"可读"项（新物品挂绿 `!`，悬停即读）。DEV_NOTES #71。</summary>
    public static class ChapterLinks
    {
        static readonly Dictionary<string, Item> _items = new();   // 只为取图标和名字造的展示用物品，一个模板造一次

        public static void Fill(MainQuestLinkedItemsListView v, IEnumerable<string> tpls, List<GameObject> spawned, System.Action onRead)
        {
            if (v == null || v._itemsContainer == null) return;
            var template = v._itemViewTemplate != null ? v._itemViewTemplate.gameObject : ChapterBundle.Prefab("LinkedItemView");
            foreach (Transform old in v._itemsContainer) if (template == null || old.gameObject != template) Object.Destroy(old.gameObject);
            var list = tpls?.ToList() ?? new List<string>();
            (v._parentPanel != null ? v._parentPanel : v.gameObject).SetActive(list.Count > 0);
            if (template == null || list.Count == 0) return;
            foreach (var tpl in list)
            {
                var item = ItemFor(tpl); if (item == null) continue;
                var view = Object.Instantiate(template, v._itemsContainer, false); view.SetActive(true); spawned.Add(view);
                var link = view.GetComponent<MainQuestLinkedItemView>();
                var image = NewImage(link != null && link._itemIconContainer != null ? link._itemIconContainer : (RectTransform)view.transform);
                var icon = ItemViewFactory.LoadItemIcon(item);
                if (icon.Sprite != null) image.sprite = icon.Sprite;
                else { System.Action unsub = null; unsub = icon.Changed.Bind(() => { if (image != null) image.sprite = icon.Sprite; unsub?.Invoke(); }); }   // ItemIcon 是全局缓存件，Bind 返回的是退订委托，用完就退
                if (ItemUiContext.Instance != null) view.AddComponent<HoverTooltipArea>().Init(ItemUiContext.Instance.Tooltip, item.LocalizedName(), true);
                var marker = link != null ? link._unreadMarker : null; var key = "item:" + tpl;
                if (marker != null) marker.SetActive(!ReadState.IsRead(key));
                ReadState.OnHover(view, () => { ReadState.MarkRead(new[] { key }); if (marker != null) marker.SetActive(false); onRead?.Invoke(); });
            }
        }

        static Item ItemFor(string tpl)
        {
            if (_items.TryGetValue(tpl, out var cached)) return cached;
            try { return _items[tpl] = Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate().ToString(), tpl, null); }
            catch (System.Exception e) { Plugin.Log.LogWarning("[chapter/items] bad template " + tpl + ": " + e.Message); return _items[tpl] = null; }
        }

        static Image NewImage(RectTransform parent)
        {
            var rt = (RectTransform)new GameObject("ItemIcon", typeof(RectTransform), typeof(Image)).transform;
            rt.SetParent(parent, false); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = rt.GetComponent<Image>(); img.preserveAspect = true; img.raycastTarget = false; return img;
        }
    }
}
