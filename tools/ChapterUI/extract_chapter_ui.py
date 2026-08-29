"""把 AssetRipper 导出的 1.1 章节屏子树抽成可进 SDK 工程的 prefab 包。

输入：tools/EFT111_Export（AssetRipper 导出的 Unity 工程）
输出：<SDK>/Assets/VisitAPI/ChapterUI/  ——  prefab ×5、Sprite+贴图、脚本桩（三组）、asmdef

引用改写规则（DEV_NOTES #69）：
  · UnityEngine.UI.dll / Unity.TextMeshPro.dll 的 dll 引用（fileID=类名 MD4）→ SDK 包里的脚本 GUID
  · 0.16.9.5 游戏里已有的 EFT 组件 → 同 GUID 的空壳桩（Assembly-CSharp），运行时绑回游戏真类
  · 1.1 独有的章节视图类 → `VisitAPI` 程序集桩（命名空间 VisitAPI.ChapterUI），运行时绑到插件里的同名类
  · Coffee.SoftMaskForUGUI.dll → 同名 asmdef 桩，运行时绑回游戏里那份 dll
  · TMP 字体/材质引用一律清空（运行时从游戏现成字体上抄）
"""
import os, re, sys, struct, hashlib, shutil, glob

E = r"E:\项目\VisitAPI Rework\tools\EFT111_Export\ExportedProject\Assets"
E2 = E.replace("EFT111_Export", "EFT111_Export2")   # PreloaderUIScene（通知栏，DEV_NOTES #72）
ROOTS = [E] + ([E2] if os.path.exists(os.path.join(E2, "Scenes", "UI", "PreloaderUIScene.unity")) else [])
U = r"E:\项目\Unity\EscapeFromTushonka-SDK"
OUT = os.path.join(U, "Assets", "VisitAPI", "ChapterUI")
SCRATCH = os.path.dirname(os.path.abspath(__file__))

DLL = {"d3e719b59ab71ba3f6b398058c866280": ("UnityEngine.UI", "ugui"),
       "67dfb1fdfb2b407222eda8e23ac8b724": ("TMPro", "tmp"),
       "728f81b5c0f536bb36df4becd774fc1a": ("Coffee.UISoftMask", "softmask")}
UGUI_CLASSES = ["Image","RawImage","Button","Toggle","ToggleGroup","ScrollRect","Scrollbar","Mask","RectMask2D",
    "HorizontalLayoutGroup","VerticalLayoutGroup","GridLayoutGroup","ContentSizeFitter","LayoutElement","AspectRatioFitter",
    "Outline","Shadow","Slider","GraphicRaycaster","CanvasScaler","Dropdown","InputField","Text"]
TMP_CLASSES = ["TextMeshProUGUI","TMP_FontAsset","TMP_SubMeshUI","TMP_SpriteAsset"]
SOFTMASK_CLASSES = ["SoftMask","SoftMaskable"]

# 0.16.9.5 里已有的 EFT 组件：命名空间, 基类, 保留字段（Unity 能认的类型）
GAME_STUBS = {
    "NonDrawingGraphic": ("EFT.UI", "Graphic", []),
    "LocalizedText": ("EFT.UI", "MonoBehaviour", ["string localizationKey", "List<TextMeshProUGUI> _labels"]),
    "ScrollRectNoDrag": ("EFT.UI", "ScrollRect", ["bool ControlSize", "float MaxWidth", "float MaxHeight", "bool AutoZeroing", "TextAnchor Alignment"]),
    "FlexibleGridLayoutGroup": ("", "GridLayoutGroup", []),
    "HoverTrigger": ("EFT.UI", "MonoBehaviour", []),
    "CustomTextMeshProUGUI": ("", "TextMeshProUGUI", []),
    "HoverTooltipArea": ("EFT.UI", "MonoBehaviour", ["string _message", "float _delay", "bool _limitTooltipWidth", "bool _customOffset", "Vector2 _offset"]),
    "DefaultUIButton": ("EFT.UI", "MonoBehaviour", ["Sprite _iconSprite", "Sprite _iconIdleSprite", "string _text", "int _fontSize", "float _minWidth", "bool _useEllipsis", "string _enabledTooltip", "string _disabledTooltip", "TextMeshProUGUI _headerLabel", "TextMeshProUGUI _sizeLabel", "Image _iconImage", "Image _iconIdleImage"]),
    "DefaultUIButtonAnimation": ("EFT.UI", "MonoBehaviour", []),
    "TweenAnimatedButton": ("EFT.UI", "MonoBehaviour", []),
    "QuestObjectiveView": ("EFT.UI", "MonoBehaviour", ["MonoBehaviour _handoverButton"]),
    # 1.1 页签栏（QuestTypeGroup）用到的三个 0.16 已有类；spawner 不桩（不要它的逻辑），页签模板由插件自己激活+接线
    "AnimatedToggle": ("EFT.UI", "Toggle", ["string _onTrigger", "string _offTrigger"]),
    "UISpawnableToggle": ("EFT.UI", "MonoBehaviour", ["TextMeshProUGUI _headerLabel", "TextMeshProUGUI _sizeLabel", "Image _iconSprite", "bool _isBoldOnHover", "GameObject HoverImage", "AnimatedToggle Toggle"]),
}
# SDK 里已经有桩的（GUID 不同，改指过去）
SDK_EXISTING = {"PixelPerfectSpriteScaler": "5309854654cae174ba23adbb4b29ab73"}
# 1.1 独有 → VisitAPI 程序集；跨程序集的引用字段一律声明成 MonoBehaviour（运行时按真类型赋值）
VISIT_STUBS = {
    "MaxSizeLayoutGroup": ("HorizontalOrVerticalLayoutGroup", []),
    "HoverReadTrigger": ("MonoBehaviour", []),
    "DialogButtonsContainer": ("MonoBehaviour", ["MonoBehaviour _visitTraderButton", "MonoBehaviour _radioButton", "MonoBehaviour _visitOnLocationButton"]),
    "MainQuestUnreadWarning": ("MonoBehaviour", ["List<GameObject> _hidableObjects", "TMP_Text _counterField"]),
    "MainQuestTaskView": ("MonoBehaviour", ["Graphic _checkMarkBorder", "Graphic _checkMark", "Graphic _crossMark", "Graphic _skipMark", "TMP_Text _descriptionField", "TMP_Text _titleField", "TMP_Text _counterField", "MonoBehaviour _conditionView", "MonoBehaviour _dialogButtonsContainer", "Color32 _activeColor", "Color32 _finishedColor", "Color32 _failedColor"]),
    "MainQuestTaskListView": ("MonoBehaviour", ["RectTransform _conditionsContainer", "MonoBehaviour _conditionsViewTemplate", "GameObject _unreadWarning", "MonoBehaviour _hoverReadTrigger"]),
    "MainQuestLinkedItemView": ("MonoBehaviour", ["RectTransform _itemIconContainer", "Image _typeIcon", "GameObject _unreadMarker"]),
    "MainQuestLinkedItemsListView": ("MonoBehaviour", ["RectTransform _itemsContainer", "MonoBehaviour _itemViewTemplate", "GameObject _parentPanel"]),
    "MainQuestNoteView": ("MonoBehaviour", ["CanvasGroup _mainCanvasGroup", "CanvasGroup _unreadWarning", "TMP_Text _text", "MonoBehaviour _itemsView"]),
    "MainQuestNotesListView": ("MonoBehaviour", ["RectTransform _container", "MonoBehaviour _noteViewTemplate", "MonoBehaviour _scroll"]),
    "MainQuestChapterTasksView": ("MonoBehaviour", ["MonoBehaviour _mainTasksList", "MonoBehaviour _optionalTasksList", "GameObject _expandTasksButton"]),
    "MainQuestChapterDescriptionView": ("MonoBehaviour", ["Image _image", "TMP_Text _nameField"]),
    "MainQuestChapterIconView": ("MonoBehaviour", ["Image _chapterIcon", "MonoBehaviour _unreadWarning", "GameObject _selectionObject", "Button _button", "MonoBehaviour _buttonHoverTrigger"]),
    "MainQuestChapterListView": ("MonoBehaviour", ["RectTransform _container", "MonoBehaviour _iconTemplate"]),
    # 1.1 章节横幅（PreloaderUIScene 通知栏里的模板，DEV_NOTES #72）：基类 BaseNotificationView 的字段也得列上，不列 Unity 导入时就丢；运行时绑到插件里 : BaseNotificationView 的同名类
    "MainQuestNotificationView": ("MonoBehaviour", ["Image _icon", "TMP_Text _text", "LayoutElement _layout", "CanvasGroup _canvasGroup", "RectTransform _container", "Image _background", "Color _defaultTextColor", "Color _defaultBackgroundColor", "Animator _animator", "TextMeshProUGUI _title", "Image _checkmarkIcon", "Sprite _chapterBackgroundSprite", "Sprite _subtaskBackgroundSprite", "Sprite _checkmarkStartedSprite", "Sprite _checkmarkSuccessSprite", "Sprite _checkmarkFailSprite"]),
    "MainQuestTabView": ("MonoBehaviour", ["MonoBehaviour _chaptersListView", "MonoBehaviour _chapterDescriptionView", "MonoBehaviour _linkedItemsView", "MonoBehaviour _historyView", "MonoBehaviour _shortHistoryView", "MonoBehaviour _unreadHistoryWarning", "Button _expandHistoryButton", "MonoBehaviour _tasksView", "GameObject _noTasksWarning", "Button _expandTasksButton", "List<GameObject> _objectsToActivate"]),
}
SUB_PREFABS = ["ChapterIcon", "LinkedItemView", "MainQuestTaskView", "QuestNoteView"]
# 任务屏页签栏用到的 1.1 Sprite（「剧情」页签图标 / 页签分割线 / 页签底图），不在 MainQuestPanel 子树里，手工点名带上
EXTRA_SPRITES = ["Sprite/MainQuestTabIcon.asset", "Sprite/QuestTypeSelectionSeparator.asset", "Sprite/QuestTypeSelectionBackground.asset",
                 "Sprite/QuestsTabQuestListBackground.asset", "Sprite/QuestsTabMainBackground.asset"]   # TasksPart/Background 与 Tasks Panel/OverallBackground 的深色底

def md4(msg):
    F = lambda x, y, z: (x & y) | (~x & z); G = lambda x, y, z: (x & y) | (x & z) | (y & z); H = lambda x, y, z: x ^ y ^ z
    rol = lambda x, n: ((x << n) | (x >> (32 - n))) & 0xffffffff
    ml = len(msg) * 8; msg += b"\x80"
    while len(msg) % 64 != 56: msg += b"\x00"
    msg += struct.pack("<Q", ml); A, B, C, D = 0x67452301, 0xefcdab89, 0x98badcfe, 0x10325476
    for i in range(0, len(msg), 64):
        X = list(struct.unpack("<16I", msg[i:i + 64])); a, b, c, d = A, B, C, D
        for r in range(16):
            t = (a + F(b, c, d) + X[r]) & 0xffffffff; a, b, c, d = d, rol(t, [3, 7, 11, 19][r % 4]), b, c
        for r in range(16):
            t = (a + G(b, c, d) + X[(r % 4) * 4 + r // 4] + 0x5a827999) & 0xffffffff; a, b, c, d = d, rol(t, [3, 5, 9, 13][r % 4]), b, c
        for r in range(16):
            t = (a + H(b, c, d) + X[[0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15][r]] + 0x6ed9eba1) & 0xffffffff; a, b, c, d = d, rol(t, [3, 9, 11, 15][r % 4]), b, c
        A, B, C, D = (A + a) & 0xffffffff, (B + b) & 0xffffffff, (C + c) & 0xffffffff, (D + d) & 0xffffffff
    return struct.pack("<4I", A, B, C, D)

def dll_fid(ns, cls): return struct.unpack("<i", md4(b"s\x00\x00\x00" + (ns + cls).encode())[:4])[0]
def stub_guid(name): return hashlib.md5(("visitapi.chapterui." + name).encode()).hexdigest()

def load_pkg_table(fname):
    t = {}
    for line in open(os.path.join(SCRATCH, fname), encoding="utf-8"):
        parts = line.split()
        if len(parts) == 2: t[parts[0]] = parts[1]
    return t

def build_meta_index():
    idx = {}
    for root in ROOTS:
        for sub in ("Sprite", "Texture2D", "GameObject", "Scripts", "Plugins", "Resources", "AnimatorController", "AnimationClip"):
            for mf in glob.glob(os.path.join(root, sub, "**", "*.meta"), recursive=True):
                try: g = re.search(r"guid: ([0-9a-f]{32})", open(mf, encoding="utf-8", errors="replace").read(2048))
                except Exception: continue
                if g: idx.setdefault(g.group(1), (root, os.path.relpath(mf[:-5], root).replace("\\", "/")))
    return idx

def extract_subtree(scene_text, root_name="MainQuestPanel", parent_name="TasksPart", prune=()):
    """prune：这些名字的节点只保留自身当槽位，子树整个剪掉（m_Children 清空）"""
    docs = re.split(r"\n--- !u!", scene_text); objs = {}
    for d in docs[1:]:
        m = re.match(r"(\d+) &(\d+)", d)
        if m: objs[m.group(2)] = (int(m.group(1)), d)
    go_name = {f: re.search(r"m_Name: (.*)", b).group(1).strip() for f, (c, b) in objs.items() if c == 1}
    tr_go, tr_father, go_tr, children = {}, {}, {}, {}
    for f, (c, b) in objs.items():
        if c in (4, 224):
            g = re.search(r"m_GameObject: \{fileID: (\d+)\}", b); fa = re.search(r"m_Father: \{fileID: (\d+)\}", b)
            if g: tr_go[f] = g.group(1); go_tr[g.group(1)] = f
            if fa: tr_father[f] = fa.group(1); children.setdefault(fa.group(1), []).append(f)
    roots = [go for go, n in go_name.items() if n == root_name and go in go_tr
             and go_name.get(tr_go.get(tr_father.get(go_tr[go], ""), ""), "") == parent_name]
    assert len(roots) == 1, roots
    root_tr = go_tr[roots[0]]; order = []; stack = [root_tr]
    pruned = set()
    while stack:
        t = stack.pop(); order.append(t)
        if go_name.get(tr_go[t]) in prune: pruned.add(t); continue
        stack += reversed(children.get(t, []))
    out = []
    for t in order:
        go = tr_go[t]; out.append(objs[go])
        for cid in re.findall(r"component: \{fileID: (\d+)\}", objs[go][1]):
            if cid in objs:
                c, b = objs[cid]
                if cid == root_tr: b = re.sub(r"m_Father: \{fileID: \d+\}", "m_Father: {fileID: 0}", b)
                if cid in pruned: b = re.sub(r"m_Children:\n(  - \{fileID: \d+\}\n)+", "m_Children: []\n", b)
                out.append((c, b))
    return "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n" + "".join("--- !u!%d &%s\n" % (c, b.split(" &")[1].split("\n")[0]) + b.split("\n", 1)[1].rstrip("\n") + "\n" for c, b in out)

class Rewriter:
    def __init__(self, idx):
        self.idx = idx; self.ugui = load_pkg_table("ugui_guids.txt"); self.tmp = load_pkg_table("tmp_guids.txt")
        self.fid = {}
        for dllguid, (ns, kind) in DLL.items():
            for cls in {"ugui": UGUI_CLASSES, "tmp": TMP_CLASSES, "softmask": SOFTMASK_CLASSES}[kind]:
                self.fid[(dllguid, dll_fid(ns, cls))] = (kind, cls)
        self.script_guid = {}   # export guid -> (new guid)
        canon = {}              # 类名 -> 第一次导出（E）里的脚本 guid：两次导出同一个脚本可能拿到不同 guid，桩只认第一份
        for g, (root, p) in sorted(idx.items(), key=lambda kv: ROOTS.index(kv[1][0])):
            if p.startswith("Scripts/"):
                name = os.path.splitext(os.path.basename(p))[0]
                canon.setdefault(name, g)
                if name in GAME_STUBS: self.script_guid[g] = canon[name]
                elif name in SDK_EXISTING: self.script_guid[g] = SDK_EXISTING[name]
                elif name in VISIT_STUBS: self.script_guid[g] = stub_guid(name)
        self.assets = set(); self.unknown = {}
    def ref(self, m):
        fid, guid, typ = int(m.group(1)), m.group(2), m.group(3)
        if guid in DLL:
            kind, cls = self.fid.get((guid, fid), (None, None))
            if kind == "ugui": return "{fileID: 11500000, guid: %s, type: 3}" % self.ugui[cls]
            if kind == "tmp": return "{fileID: 11500000, guid: %s, type: 3}" % self.tmp[cls]
            if kind == "softmask": return "{fileID: 11500000, guid: %s, type: 3}" % stub_guid(cls)
            self.unknown["%s#%d" % (DLL[guid][0], fid)] = self.unknown.get("%s#%d" % (DLL[guid][0], fid), 0) + 1; return m.group(0)
        ent = self.idx.get(guid)
        if ent is None: self.unknown[guid] = self.unknown.get(guid, 0) + 1; return m.group(0)
        root, p = ent
        if p.startswith("Scripts/"):
            if guid in self.script_guid: return "{fileID: 11500000, guid: %s, type: 3}" % self.script_guid[guid]
            self.unknown[p] = self.unknown.get(p, 0) + 1; return m.group(0)
        if p.startswith("Resources/ui/fonts/"): return "{fileID: 0}"
        if p.startswith(("Sprite/", "Texture2D/", "GameObject/", "AnimatorController/", "AnimationClip/")): self.assets.add(ent); return m.group(0)
        self.unknown[p] = self.unknown.get(p, 0) + 1; return m.group(0)
    def run(self, text): return re.sub(r"\{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: (\d)\}", self.ref, text)

def copy_asset(ent, idx, rw):
    root, rel = ent
    src = os.path.join(root, rel); dst = os.path.join(OUT, rel.split("/")[0], os.path.basename(rel))
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    if os.path.exists(dst + ".meta") and open(dst + ".meta").read() != open(src + ".meta").read():
        stem, ext = os.path.splitext(os.path.basename(rel)); dst = os.path.join(os.path.dirname(dst), "%s__%d%s" % (stem, ROOTS.index(root), ext))
    if rel.endswith(".controller"):   # 页签动画控制器：把它引用的 clip 一并带上
        text = open(src, encoding="utf-8", errors="replace").read()
        for g in re.findall(r"guid: ([0-9a-f]{32})", text):
            p = idx.get(g)
            if p and p[1].startswith("AnimationClip/") and p not in rw.assets: rw.assets.add(p); copy_asset(p, idx, rw)
        shutil.copyfile(src, dst)
    elif rel.endswith(".asset") and rel.startswith("Sprite/"):
        text = open(src, encoding="utf-8", errors="replace").read()
        for g in re.findall(r"guid: ([0-9a-f]{32})", text):
            p = idx.get(g)
            if p and p[1].startswith("Texture2D/") and p not in rw.assets: rw.assets.add(p); copy_asset(p, idx, rw)
        open(dst, "w", encoding="utf-8").write(text)
    elif rel.endswith(".anim"):   # 动画曲线按"组件脚本 GUID"绑定目标（Image/TMP 的 dll 引用），不改写就找不到目标、页签高亮动画哑火
        open(dst, "w", encoding="utf-8").write(rw.run(open(src, encoding="utf-8", errors="replace").read()))
    else:
        shutil.copyfile(src, dst)
    shutil.copyfile(src + ".meta", dst + ".meta")

def write_stub(folder, name, ns, base, fields, guid):
    os.makedirs(folder, exist_ok=True)
    body = "\n".join("    [SerializeField] private %s;" % f for f in fields)
    abstract = ""
    if base == "HorizontalOrVerticalLayoutGroup":
        abstract = "\n    public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); }\n    public override void CalculateLayoutInputVertical() { }\n    public override void SetLayoutHorizontal() { }\n    public override void SetLayoutVertical() { }"
    cls = "public class %s : %s\n{\n%s%s\n}\n" % (name, base, body, abstract)
    src = "using System.Collections.Generic;\nusing TMPro;\nusing UnityEngine;\nusing UnityEngine.UI;\n\n" + (("namespace %s\n{\n%s}\n" % (ns, cls)) if ns else cls)
    open(os.path.join(folder, name + ".cs"), "w", encoding="utf-8").write(src)
    open(os.path.join(folder, name + ".cs.meta"), "w").write("fileFormatVersion: 2\nguid: %s\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n" % guid)

def write_asmdef(folder, name, refs):
    os.makedirs(folder, exist_ok=True)
    open(os.path.join(folder, name + ".asmdef"), "w").write('{\n  "name": "%s",\n  "references": [%s],\n  "autoReferenced": true\n}\n' % (name, ", ".join('"%s"' % r for r in refs)))
    open(os.path.join(folder, name + ".asmdef.meta"), "w").write("fileFormatVersion: 2\nguid: %s\nAssemblyDefinitionImporter:\n  externalObjects: {}\n" % stub_guid("asmdef." + name))

def main():
    if os.path.exists(OUT): shutil.rmtree(OUT)
    os.makedirs(OUT)
    idx = build_meta_index(); rw = Rewriter(idx)
    scene = open(os.path.join(E, "Scenes", "UI", "CommonUIScene.unity"), encoding="utf-8", errors="replace").read()
    # 整块 1.1 TasksPart 当左半边宿主：页签栏 + 底图 + 章节屏；SideQuestsPanel 只留槽位（1.1 任务列表那一坨类不要，0.16 原生列表塞回这个位置）
    prefabs = {"TasksPart": extract_subtree(scene, "TasksPart", "Tasks Panel", prune=("SideQuestsPanel",))}
    for p in SUB_PREFABS: prefabs[p] = open(os.path.join(E, "GameObject", p + ".prefab"), encoding="utf-8", errors="replace").read()
    if E2 in ROOTS:   # 通知栏里的 1.1 章节横幅模板（PreloaderUIScene: …/Notifier/Content/MainQuestNotification，场景里默认关着）
        scene2 = open(os.path.join(E2, "Scenes", "UI", "PreloaderUIScene.unity"), encoding="utf-8", errors="replace").read()
        prefabs["MainQuestNotification"] = extract_subtree(scene2, "MainQuestNotification", "Content")
    pd = os.path.join(OUT, "Prefabs"); os.makedirs(pd)
    for name, text in prefabs.items():
        open(os.path.join(pd, name + ".prefab"), "w", encoding="utf-8").write(rw.run(text))
        meta = os.path.join(E, "GameObject", name + ".prefab.meta")
        if os.path.exists(meta): shutil.copyfile(meta, os.path.join(pd, name + ".prefab.meta"))
        else: open(os.path.join(pd, name + ".prefab.meta"), "w").write("fileFormatVersion: 2\nguid: %s\nPrefabImporter:\n  externalObjects: {}\n" % stub_guid("prefab." + name))
    rw.assets.update((E, s) for s in EXTRA_SPRITES)
    for ent in sorted(rw.assets):
        if not ent[1].startswith("GameObject/"): copy_asset(ent, idx, rw)
    # 1.1 剧情音效（tools/ChapterUI/audio：从 resources.resource 里解出来的 wav/ogg，DEV_NOTES #73）；Unity 导入即 AudioClip，运行时按文件名取
    ad = os.path.join(OUT, "AudioClip"); os.makedirs(ad, exist_ok=True)
    for f in glob.glob(os.path.join(SCRATCH, "audio", "*.wav")) + glob.glob(os.path.join(SCRATCH, "audio", "*.ogg")): shutil.copyfile(f, os.path.join(ad, os.path.basename(f)))
    # 脚本桩（guid 用第一次导出那份，和 Rewriter 的 canon 一致）
    export_guid = {}
    for g, (root, p) in sorted(idx.items(), key=lambda kv: ROOTS.index(kv[1][0])):
        if p.startswith("Scripts/"): export_guid.setdefault(os.path.splitext(os.path.basename(p))[0], g)
    for name, (ns, base, fields) in GAME_STUBS.items():
        write_stub(os.path.join(OUT, "Scripts", "GameStubs"), name, ns, base, fields, export_guid[name])
    for name, (base, fields) in VISIT_STUBS.items():
        write_stub(os.path.join(OUT, "Scripts", "VisitAPI"), name, "VisitAPI.ChapterUI", base, fields, stub_guid(name))
    write_asmdef(os.path.join(OUT, "Scripts", "VisitAPI"), "VisitAPI", ["Unity.TextMeshPro", "UnityEngine.UI"])
    for name in SOFTMASK_CLASSES:
        write_stub(os.path.join(OUT, "Scripts", "SoftMask"), name, "Coffee.UISoftMask", "Mask" if name == "SoftMask" else "MonoBehaviour", [], stub_guid(name))
    write_asmdef(os.path.join(OUT, "Scripts", "SoftMask"), "Coffee.SoftMaskForUGUI", ["UnityEngine.UI", "Unity.TextMeshPro"])
    print("prefabs:", list(prefabs)); print("assets copied:", len(rw.assets))
    print("unresolved refs:"); [print("   %4d %s" % (n, k)) for k, n in sorted(rw.unknown.items(), key=lambda x: -x[1])]

if __name__ == "__main__":
    main()
