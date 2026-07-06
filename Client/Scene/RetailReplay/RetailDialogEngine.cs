using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Dialogs;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VisitAPI.Native;

namespace VisitAPI.Scene.RetailReplay
{
    // The retail-1.0 trader dialog payload (dialogue.json), readable by the NATIVE DTO. Registering its
    // templates into the native repository (GClass3624) + merging its localization into the game's locale
    // tables is ALL the native dialog machinery needs: TraderDialogWindow then renders the real retail
    // dialog trees, and every line/option/subtitle localizes through GClass2348.Localized.
    // 0.16.9 lacks a few 1.0 enum values, so unknown members are skipped instead of failing the parse.
    internal static class RetailDialogEngine
    {
        private static TraderDialogsDTO? _dto;
        private static bool _dialogDataRegistered;
        private static Dictionary<string, GClass3666>? _dialogLines;
        private static HashSet<MongoID>? _templateIds;
        private static Dictionary<string, MongoID>? _entryByTrader;
        private static List<KeyValuePair<MongoID, int>>? _seedVariables;

        private static GClass3619? _dialogController;
        private static TraderDialogScreen.BTRDialogClass? _dialogScreen;
        private static bool _screenClosedNatively;

        // The retail dialog box: register the extracted dialog templates + locales, then push the NATIVE
        // TraderDialogScreen with the menu's own controllers and a GClass3668 animation controller wrapping
        // the scene's trader model — the same construction the retail visit flow uses (GClass2302.method_2).
        // Falls back (return false) to a plain greeting animation when any piece is missing.
        internal static bool TryOpenNativeDialog(string traderId, Animator? traderAnimator)
        {
            try
            {
                if (!EnsureDialogData()) return false;
                MongoID? entry = FindEntryDialogId(traderId);
                if (entry == null)
                {
                    Plugin.Log.LogWarning("[RetailDialog] no entry dialog (CanBeFirstDialog) for " + traderId);
                    return false;
                }
                MainMenuControllerClass? menu = ResolveMainMenuController();
                if (menu == null)
                {
                    Plugin.Log.LogWarning("[RetailDialog] MainMenuControllerClass not found");
                    return false;
                }

                NPCObject? npc = null;
                if (traderAnimator != null)
                    npc = traderAnimator.GetComponent<NPCObject>() ?? traderAnimator.gameObject.AddComponent<NPCObject>();

                GClass3619 dialogController = menu.DialogController;
                ApplySeedVariables(dialogController);
                GInterface462 animationController = new GClass3668(npc, dialogController);

                _dialogController = dialogController;
                dialogController.OnActionFinished -= OnDialogActionFinished;
                dialogController.OnActionFinished += OnDialogActionFinished;

                Profile profile = ItemUiContext.Instance.Session.Profile;
                _dialogScreen = new TraderDialogScreen.BTRDialogClass(profile, traderId, menu.QuestController, menu.InventoryController,
                    animationController, dialogController, entry);
                _screenClosedNatively = false;
                _dialogScreen.ShowScreen(EScreenState.Queued);

                if (Array.IndexOf(WhitelistPatch.VanillaTraders, traderId) < 0)
                    dialogController.StartDialog(traderId, entry, animationController);

                LogDialogState(dialogController);
                Plugin.Log.LogInfo("[RetailDialog] native dialog opened for " + traderId + " (entry " + entry + ")");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[RetailDialog] open native dialog: " + ex);
                return false;
            }
        }

        // The dialog's own "I have to go" line raises a QuitAction; the screen closes itself natively and we
        // tear the scene down after it — same wiring as the retail flow's method_3. The trade/quests/service
        // rows raise their own action types whose native handlers need backend contexts SPT never provides
        // (decompiled: ExecuteDialogAction treats Trading/QuestsScreenAction exactly like Quit), so route
        // them to SPT's own trade screen instead — close the visit, land on the matching tab, the same move
        // the .dlg @trade directive makes.
        private static void OnDialogActionFinished(GClass3629 action)
        {
            if (action == null) return;
            string? tab = null;
            switch (action.Type)
            {
                case GClass3629.EActionType.TradingScreenAction: tab = "Trade"; break;
                case GClass3629.EActionType.QuestsScreenAction: tab = "Tasks"; break;
                case GClass3629.EActionType.SelectSubService:
                case GClass3629.EActionType.PurchaseService: tab = "Services"; break;
            }
            if (tab != null)
            {
                Plugin.Log.LogInfo("[RetailDialog] " + action.Type + " — routing to the SPT " + tab + " tab");
                if (SceneStage.IsBusy) { SceneStage.RequestDeferredClose(); return; }
                Plugin.Instance.StartCoroutine(CloseVisitThenSwitchTab(tab));
                return;
            }
            if (action.Type != GClass3629.EActionType.QuitAction) return;
            if (SceneStage.IsBusy)
            {
                Plugin.Log.LogInfo("[RetailDialog] dialog quit during open — deferring close");
                SceneStage.RequestDeferredClose();
                return;
            }
            Plugin.Log.LogInfo("[RetailDialog] dialog quit — closing scene");
            _screenClosedNatively = true;
            SceneStage.Close();
        }

        // Tear the visit down first (screen + scene), then switch the trade screen's tab once it is the
        // active screen again — method_3 re-shows the tab content, which needs the screen live.
        private static IEnumerator CloseVisitThenSwitchTab(string tab)
        {
            SceneStage.Close();
            for (int i = 0; i < 600 && SceneStage.IsOpen; i++) yield return null;
            yield return null;
            yield return null;
            if (!NativeBinder.SwitchTradeTab(tab))
                Plugin.Log.LogWarning("[RetailDialog] tab switch failed: " + tab + " (no live trade screen behind the visit — F9 open?)");
        }

        // Scene teardown calls this first. The controller is SHARED (the menu's). Its line-execution chain
        // is async: each executed NPC line awaits its animation, then executes the next one. Closing only
        // the screen leaves that chain alive, and its next iteration reads Trader/CurrentDialog — by then
        // the NEXT vendor's — and keeps advancing states in the background (farewell-on-open, instant
        // quits, scrambled lines across traders). StopDialog() nulls CurrentDialog so the zombie chain
        // dies on its next step.
        internal static void CloseDialog()
        {
            if (_dialogController != null)
            {
                _dialogController.OnActionFinished -= OnDialogActionFinished;
                try { _dialogController.StopDialog(); }
                catch (Exception ex) { Plugin.Log.LogWarning("[RetailDialog] stop dialog: " + ex.Message); }
                _dialogController = null;
            }
            // Cycling to another vendor must pop the still-open dialog screen; the quit line already
            // closed it natively, so only close it ourselves when it didn't.
            if (_dialogScreen != null)
            {
                if (!_screenClosedNatively)
                {
                    try { _dialogScreen.CloseScreen(); }
                    catch (Exception ex) { Plugin.Log.LogWarning("[RetailDialog] close dialog screen: " + ex.Message); }
                }
                _dialogScreen = null;
            }
            // Farewell subtitles run on their own timers over a GLOBAL channel the next vendor's subtitle
            // view also listens to — fire the native skip event (what SkipAnimation raises) so the previous
            // trader's line doesn't bleed into the next window.
            try { GlobalEventHandlerClass.Instance.CreateCommonEvent<GClass3551>().Invoke(ESubtitlesSource.Common); } catch { }
        }

        // One forensic line per open: how many rows the window has and every Session/Profile variable on
        // the shared controller — dead opens (0 actives) and cross-vendor variable pollution show up here.
        // For the whitelisted traders StartDialog runs later inside the queued screen's Show, so their
        // count reads 0/0 at this point; the manual-start traders (the ones that break) log real state.
        private static void LogDialogState(GClass3619 controller)
        {
            try
            {
                int active = 0, total = 0;
                GClass3620? dialog = controller.CurrentDialog;
                if (dialog != null)
                {
                    foreach (GClass3625 line in dialog.Lines)
                    {
                        total++;
                        if (line.IsActiveAndInteractive) active++;
                    }
                }
                Plugin.Log.LogInfo("[RetailDialog] dialog lines active " + active + "/" + total + "; "
                    + controller.GetSaveStatesInfo().Replace("\n", " "));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[RetailDialog] dialog state log: " + ex.Message);
            }
        }

        private static MainMenuControllerClass? ResolveMainMenuController()
        {
            TarkovApplication app = UnityEngine.Object.FindObjectOfType<TarkovApplication>();
            if (app == null) return null;
            FieldInfo? field = typeof(TarkovApplication).GetField("mainMenuControllerClass", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return field?.GetValue(app) as MainMenuControllerClass;
        }

        internal static bool EnsureDialogData()
        {
            if (_dialogDataRegistered) return true;
            string path = Path.Combine(SceneAssets.VendorsDir, "dialogue.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[RetailDialog] dialogue.json not found: " + path);
                return false;
            }
            try
            {
                string text = File.ReadAllText(path);
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    Error = (sender, args) => { args.ErrorContext.Handled = true; },
                    Converters = { new NestedLocaleDictionaryConverter() },
                };
                _dto = JsonConvert.DeserializeObject<TraderDialogsDTO>(text, settings);
                if (_dto?.Elements == null || _dto.Elements.Length == 0)
                {
                    Plugin.Log.LogWarning("[RetailDialog] dialogue.json parsed empty");
                    return false;
                }

                _templateIds = new HashSet<MongoID>();
                foreach (GClass3665 element in _dto.Elements)
                    if (element != null) _templateIds.Add(element.Id);

                int stripped = SanitizeDanglingJumps(_dto.Elements);
                if (stripped > 0)
                    Plugin.Log.LogInfo("[RetailDialog] stripped " + stripped + " action(s) jumping to dialogs missing from the extract");

                GClass3624.Instance.AddTemplates(_dto.Elements);

                _dialogLines = new Dictionary<string, GClass3666>();
                Dictionary<string, Dictionary<string, string>> byLocale = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (GClass3665 element in _dto.Elements)
                {
                    if (element == null) continue;
                    if (element.Lines != null)
                        foreach (GClass3666 line in element.Lines)
                            if (line != null) _dialogLines[line.Id] = line;
                    if (element.LocalizationDictionary == null) continue;
                    foreach (KeyValuePair<string, Dictionary<string, string>> locale in element.LocalizationDictionary)
                    {
                        if (locale.Value == null) continue;
                        if (!byLocale.TryGetValue(locale.Key, out Dictionary<string, string> merged))
                            merged = byLocale[locale.Key] = new Dictionary<string, string>();
                        foreach (KeyValuePair<string, string> kv in locale.Value)
                            merged[kv.Key] = kv.Value;
                    }
                }

                LocaleManagerClass locales = LocaleManagerClass.LocaleManagerClass;
                if (locales != null)
                {
                    foreach (KeyValuePair<string, Dictionary<string, string>> locale in byLocale)
                        locales.UpdateLocales(locale.Key, locale.Value);
                }
                else
                {
                    Plugin.Log.LogWarning("[RetailDialog] LocaleManager not ready — dialog text will show raw keys");
                }

                // The retail payload marks each trader's MAIN dialog with "IsStart": true. The 0.16.9
                // GClass3665 has no such member (the field is silently dropped on deserialize), so read it
                // from the raw JSON. This is THE entry selector — every heuristic over CanBeFirstDialogue
                // failed, because that flag DEFAULTS to true and quest-reaction dialogs carry it too.
                _entryByTrader = new Dictionary<string, MongoID>(StringComparer.OrdinalIgnoreCase);
                _seedVariables = new List<KeyValuePair<MongoID, int>>();
                try
                {
                    JToken? elements = JObject.Parse(text)["elements"];
                    if (elements != null)
                    {
                        Dictionary<string, List<KeyValuePair<string, int>>> usages = new Dictionary<string, List<KeyValuePair<string, int>>>();
                        HashSet<string> written = new HashSet<string>();
                        foreach (JToken element in elements)
                        {
                            if (element.Value<bool?>("IsStart") == true)
                            {
                                string? trader = element.Value<string>("Trader");
                                string? id = element.Value<string>("Id");
                                if (!string.IsNullOrEmpty(trader) && !string.IsNullOrEmpty(id) && !_entryByTrader.ContainsKey(trader!))
                                    _entryByTrader[trader!] = id!;
                            }
                            JToken? lines = element["Lines"];
                            if (lines == null) continue;
                            foreach (JToken line in lines)
                            {
                                CollectVariableUsages(line["Trigger"], usages);
                                JToken? actions = line["Actions"];
                                if (actions == null) continue;
                                foreach (JToken action in actions)
                                {
                                    if (action.Value<string>("type") != "SetVariable") continue;
                                    string? target = action.Value<string>("variableId");
                                    if (target != null) written.Add(target);
                                }
                            }
                        }

                        // Variables the conditions READ but no dialog action ever WRITES are server-owned in
                        // retail (acquaintance level, unlock flags — e.g. Therapist's whole trade/chat hub is
                        // behind one of them at >=1). SPT never sets them, so those options stay dead. Seed
                        // each with the smallest >= threshold, but only when that value contradicts none of
                        // its other usages.
                        foreach (KeyValuePair<string, List<KeyValuePair<string, int>>> use in usages)
                        {
                            if (written.Contains(use.Key)) continue;
                            int seed = 0;
                            foreach (KeyValuePair<string, int> u in use.Value)
                                if (u.Key == ">=" && u.Value > seed) seed = u.Value;
                            if (seed == 0) continue;
                            bool consistent = true;
                            foreach (KeyValuePair<string, int> u in use.Value)
                            {
                                switch (u.Key)
                                {
                                    case ">=": consistent &= seed >= u.Value; break;
                                    case "<=": consistent &= seed <= u.Value; break;
                                    case ">": consistent &= seed > u.Value; break;
                                    case "<": consistent &= seed < u.Value; break;
                                    case "!=": consistent &= seed != u.Value; break;
                                    // ==0 gates first-meeting intro lines — Skier's jumps into a template
                                    // missing from the extract. Seeding past them lands on the regular
                                    // "already acquainted" branches, which are the ones that work.
                                    case "==": if (u.Value != 0) consistent &= seed == u.Value; break;
                                }
                                if (!consistent) break;
                            }
                            if (consistent) _seedVariables.Add(new KeyValuePair<MongoID, int>(use.Key, seed));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[RetailDialog] raw payload scan: " + ex.Message);
                }
                Plugin.Log.LogInfo("[RetailDialog] IsStart entries for " + _entryByTrader.Count + " trader(s), "
                    + _seedVariables.Count + " server-owned variable(s) to seed");

                _dialogDataRegistered = true;
                Plugin.Log.LogInfo("[RetailDialog] registered " + _dto.Elements.Length + " dialog template(s), "
                    + _dialogLines.Count + " line(s), locales: " + string.Join(", ", byLocale.Keys.ToArray()));
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[RetailDialog] dialogue.json: " + ex.Message);
                return false;
            }
        }

        // An action that switches/embeds a template absent from the extract throws KeyNotFound the moment
        // its line executes, killing the whole visit. Strip just those actions — the line still plays and
        // its remaining state changes still run, so the conversation continues past the data gap.
        private static int SanitizeDanglingJumps(GClass3665[] elements)
        {
            int stripped = 0;
            for (int i = 0; i < elements.Length; i++)
            {
                GClass3665 element = elements[i];
                if (element?.Lines == null) continue;
                List<GClass3666>? newLines = null;
                for (int j = 0; j < element.Lines.Count; j++)
                {
                    GClass3666 line = element.Lines[j];
                    if (line?.Actions == null || !line.Actions.Any(IsDanglingJump)) continue;
                    List<GClass3629> kept = new List<GClass3629>();
                    foreach (GClass3629 action in line.Actions)
                    {
                        if (IsDanglingJump(action)) stripped++;
                        else kept.Add(action);
                    }
                    newLines ??= new List<GClass3666>(element.Lines);
                    newLines[j] = new GClass3666(line.Id, line.DialogSide, line.IconType, line.Trigger, kept, line.AnimationData);
                }
                if (newLines == null) continue;
                GClass3665 replacement = new GClass3665(element.Id, element.MainTrader, element.SubTraders, newLines,
                    element.LocalizationDictionary, element.StartPoints);
                replacement.CanBeFirstDialog = element.CanBeFirstDialog;
                elements[i] = replacement;
            }
            return stripped;
        }

        // ONLY GClass3645 (SwitchDialog) is a data-driven jump. GClass3641 — json type "SwitchQuestDialog",
        // the quest-list option on every trader's hub — has NO [JsonProperty] on TargetDialogId (OptIn
        // serialization drops it); the id is assigned at runtime when the player picks a quest. Testing its
        // permanently-default id against the template set flagged all 11 quest-list actions as dangling and
        // turned every hub's quest option into a dead blank row (the v0.2.6 regression).
        private static bool IsDanglingJump(GClass3629 action)
        {
            if (_templateIds == null) return false;
            return action is GClass3645 switchAction && !_templateIds.Contains(switchAction.DialogId);
        }

        private static void CollectVariableUsages(JToken? condition, Dictionary<string, List<KeyValuePair<string, int>>> usages)
        {
            if (condition == null || condition.Type != JTokenType.Object) return;
            if (condition.Value<string>("type") == "VariableValue")
            {
                string? variable = condition.Value<string>("variableId");
                string? op = condition.Value<string>("operator");
                if (variable != null && op != null)
                {
                    if (!usages.TryGetValue(variable, out List<KeyValuePair<string, int>> list))
                        usages[variable] = list = new List<KeyValuePair<string, int>>();
                    list.Add(new KeyValuePair<string, int>(op, condition.Value<int?>("value") ?? 0));
                }
            }
            JToken? subConditions = condition["Conditions"];
            if (subConditions == null) return;
            foreach (JToken sub in subConditions)
                CollectVariableUsages(sub, usages);
        }

        // Session scope lands in the dictionary that survives the per-StartDialog reset (method_1 clears
        // only the Dialogue-scope one) without touching the profile. Idempotent — applied on every open.
        internal static void ApplySeedVariables(GClass3619 controller)
        {
            if (_seedVariables == null) return;
            foreach (KeyValuePair<MongoID, int> seed in _seedVariables)
                controller.SetVariableValue(new GClass3647.GClass3650(seed.Key, seed.Value, GClass3666.ESaveStateType.Session));
        }

        // Whether the payload has ANY entry dialog for this trader — gates the trade-screen 拜访 button so
        // a vendor whose scene exists but whose dialog tree was never captured (Peacekeeper: the tree only
        // lives server-side) doesn't offer a visit into an empty room. Quiet: runs on every trader select.
        internal static bool HasDialogFor(string traderId)
        {
            if (string.IsNullOrEmpty(traderId) || !EnsureDialogData()) return false;
            if (_entryByTrader != null && _entryByTrader.ContainsKey(traderId)) return true;
            return FindEntryFallback(traderId) != null;
        }

        // The dialog a visit ENTERS through: the payload's IsStart marker (exactly one per trader — verified
        // offline for all 7). The scoring fallback is only for payloads without the marker.
        internal static MongoID? FindEntryDialogId(string traderId)
        {
            if (_entryByTrader != null && _entryByTrader.TryGetValue(traderId, out MongoID marked))
            {
                Plugin.Log.LogInfo("[RetailDialog] entry for " + traderId + ": " + marked + " (IsStart)");
                return marked;
            }
            GClass3665? best = FindEntryFallback(traderId);
            if (best == null) return null;
            Plugin.Log.LogInfo("[RetailDialog] entry for " + traderId + ": " + best.Id + " (fallback scoring)");
            return best.Id;
        }

        private static GClass3665? FindEntryFallback(string traderId)
        {
            if (_dto?.Elements == null) return null;
            MongoID tid = traderId;
            GClass3665? best = null;
            int bestScore = -1;
            foreach (GClass3665 element in _dto.Elements)
            {
                if (element == null || !element.CanBeFirstDialog) continue;
                bool ownTrader = element.MainTrader == tid;
                if (!ownTrader && !element.HasTraderId(tid)) continue;
                if (element.Lines == null || element.Lines.Count == 0) continue;
                int score = (ownTrader ? 2 : 0) + (ReferencedDialogsPresent(element) ? 1 : 0);
                if (score > bestScore || (score == bestScore && best != null && element.Id > best.Id))
                {
                    best = element;
                    bestScore = score;
                }
            }
            return best;
        }

        // True when every sub-dialog this template's lines jump to exists in the extracted payload — a line
        // that switches to a missing template throws KeyNotFound mid-dialog and dead-ends the conversation.
        private static bool ReferencedDialogsPresent(GClass3665 element)
        {
            if (_templateIds == null || element.Lines == null) return true;
            foreach (GClass3666 line in element.Lines)
            {
                if (line?.Actions == null) continue;
                foreach (GClass3629 action in line.Actions)
                {
                    if (action is GClass3645 switchAction && !_templateIds.Contains(switchAction.DialogId)) return false;
                    if (action is GClass3641 embedAction && !_templateIds.Contains(embedAction.TargetDialogId)) return false;
                }
            }
            return true;
        }

        internal static GClass3666? GetLine(string id)
        {
            if (_dialogLines == null) EnsureDialogData();
            return _dialogLines != null && _dialogLines.TryGetValue(id, out GClass3666 found) ? found : null;
        }
    }

    // GClass3665.LocalizationDictionary is typed IReadOnlyDictionary<string, Dictionary<string, string>>,
    // which Json.NET can't materialize on its own (bmpq ships the same converter).
    internal sealed class NestedLocaleDictionaryConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
            => objectType == typeof(IReadOnlyDictionary<string, Dictionary<string, string>>);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            return serializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(reader);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
            => serializer.Serialize(writer, value);
    }
}
