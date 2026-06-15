using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace VisitAPI;

internal static class NativeQuestController
{
	private const string BaseUrl = "http://127.0.0.1:6970/visitapi";

	internal static readonly HashSet<string> CompletedHiddenQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	internal static string? LastKnownProfileId { get; private set; }

	public static bool AcceptQuest(string profileId, string questId)
	{
		VisitPlugin.Log.LogInfo((object)("[NativeQuest] AcceptQuest: profile=" + profileId + " quest=" + questId));
		if (!string.IsNullOrEmpty(profileId))
		{
			LastKnownProfileId = profileId;
		}
		// 先尝试游戏原生接取：任务在原生任务书里（WTT/eft 真任务）时走完整原生流程、任务面板同步；
		// 找不到（VisitAPI 隐藏触发任务，如 SORA 影子任务）则回到纯服务端旁路写档。
		// Native=true 时服务端只留旁路状态文件、不再自己写档，避免与原生重复冲突。
		bool nativeAccepted = TryNativeAcceptQuest(questId, out bool _);
		return Post("/quest/accept", new
		{
			ProfileId = profileId,
			QuestId = questId,
			Native = nativeAccepted
		});
	}

	// 原生接取：在游戏任务书里找到任务对象，调 AbstractQuestControllerClass.AcceptQuest(quest, true) 走完整原生流程。
	// 返回 true 表示已发起原生接取；questInBook 表示任务是否在原生任务书里。
	private static bool TryNativeAcceptQuest(string questId, out bool questInBook)
	{
		string questId2 = questId;
		questInBook = false;
		try
		{
			object questCtrl = TraderDealScreenVisitButton._cachedQuestCtrl;
			if (questCtrl == null && VisitPlugin.TryGetInRaidControllers(out object _, out object inRaidCtrl, out object _))
			{
				questCtrl = inRaidCtrl;
			}
			if (questCtrl == null)
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] No quest controller (native accept)");
				return false;
			}
			object quest = FindQuestInBook(questCtrl, questId2);
			if (quest == null)
			{
				VisitPlugin.Log.LogInfo((object)("[NativeQuest] Quest " + questId2 + " not in QuestBook; bypass accept"));
				return false;
			}
			questInBook = true;
			MethodInfo accept = questCtrl.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.FirstOrDefault((MethodInfo m) => m.Name == "AcceptQuest" && m.GetParameters().Length == 2);
			if (accept == null)
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] AcceptQuest(quest,bool) not found");
				return false;
			}
			if (accept.Invoke(questCtrl, new object[2] { quest, true }) is Task task)
			{
				task.ContinueWith(delegate(Task t)
				{
					if (t.IsFaulted)
					{
						VisitPlugin.Log.LogWarning((object)("[NativeQuest] Native AcceptQuest faulted: " + t.Exception?.InnerException?.Message));
					}
					else
					{
						VisitPlugin.Log.LogInfo((object)("[NativeQuest] Native AcceptQuest completed: " + questId2));
					}
				});
			}
			VisitPlugin.Log.LogInfo((object)("[NativeQuest] Native AcceptQuest triggered: " + questId2));
			return true;
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] Native accept failed: " + (ex.InnerException?.Message ?? ex.Message)));
			return false;
		}
	}

	public static void ShowNativeHandoverScreen(string questId, Action<bool> onResult)
	{
		VisitPlugin.Log.LogInfo((object)("[NativeQuest] ShowNativeHandoverScreen: quest=" + questId));
		try
		{
			TryNativeHandoverScreen(questId, onResult);
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] ShowNativeHandoverScreen failed: " + (ex.InnerException?.Message ?? ex.Message)));
			onResult(obj: false);
		}
	}

	public static bool CompleteQuest(string profileId, string questId)
	{
		VisitPlugin.Log.LogInfo((object)("[NativeQuest] CompleteQuest: profile=" + profileId + " quest=" + questId));
		if (!string.IsNullOrEmpty(profileId))
		{
			LastKnownProfileId = profileId;
		}
		// 优先走原生完成流程（任务日志/奖励/完成邮件/音效全由游戏处理）；
		// Native=true 时服务端只记录完成状态与联动，不再写档发奖（避免双倍奖励）
		bool nativeCompletion = TryNativeCompleteQuest(questId, out bool questInBook);
		string jsonBody = JsonConvert.SerializeObject((object)new
		{
			ProfileId = profileId,
			QuestId = questId,
			Native = nativeCompletion
		});
		string text = PostRaw("/quest/complete", jsonBody);
		bool flag = false;
		if (text != null)
		{
			try
			{
				JObject val = JObject.Parse(text);
				JToken obj = val["success"];
				flag = obj != null && Extensions.Value<bool>((IEnumerable<JToken>)obj);
				if (!flag)
				{
					VisitPlugin.Log.LogWarning((object)("[NativeQuest] /quest/complete: " + (((object)val["error"])?.ToString() ?? "unknown")));
				}
				else
				{
					CompletedHiddenQuestIds.Add(questId);
					VisitPlugin.Log.LogInfo((object)("[NativeQuest] Marked hidden quest completed: " + questId));
					JToken obj2 = val["updatedQuests"];
					JArray val2 = (JArray)(object)((obj2 is JArray) ? obj2 : null);
					if (val2 != null)
					{
						foreach (JToken item in val2)
						{
							JToken obj3 = item[(object)"questId"];
							string text2 = ((obj3 != null) ? Extensions.Value<string>((IEnumerable<JToken>)obj3) : null);
							JToken obj4 = item[(object)"status"];
							int num = ((obj4 != null) ? Extensions.Value<int>((IEnumerable<JToken>)obj4) : (-1));
							if (!string.IsNullOrEmpty(text2) && num >= 0)
							{
								TryNativeSetQuestStatus(text2, num);
							}
						}
					}
					if (!nativeCompletion && questInBook)
					{
						// 可见任务但原生路径失败时回退：服务端已写档发奖，客户端做本地呈现
						//（条件强制达成 + 状态置 Success + 刷新事件 + 手动音效）。
						// 隐藏任务（不在任务书）保持完全静默——玩家不应感知它的存在
						TryNativeSetQuestStatus(questId, 4);
						TryPlayQuestCompletedSound();
					}
				}
			}
			catch (Exception ex)
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] /quest/complete parse: " + ex.Message));
			}
		}
		if (!flag)
		{
			return text != null;
		}
		return true;
	}

	// 原生完成：先把客户端任务推进到"可完成"（条件强制达成 + 状态置 3，
	// 即"调查不明狙击手"联动所用机制——否则 FinishQuest 会静默拒绝），
	// 再调用 AbstractQuestControllerClass.FinishQuest(quest, true) 走完整原生流程。
	// 返回 true 表示已成功发起原生完成。
	private static bool TryNativeCompleteQuest(string questId, out bool questInBook)
	{
		string questId2 = questId;
		questInBook = false;
		try
		{
			object questCtrl = TraderDealScreenVisitButton._cachedQuestCtrl;
			if (questCtrl == null)
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] No cached quest controller (native complete)");
				return false;
			}
			object quest = FindQuestInBook(questCtrl, questId2);
			if (quest == null)
			{
				VisitPlugin.Log.LogInfo((object)("[NativeQuest] Quest " + questId2 + " not in QuestBook; silent completion"));
				return false;
			}
			questInBook = true;
			MethodInfo finish = questCtrl.GetType().GetMethod("FinishQuest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (finish == null)
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] FinishQuest method not found; using local completion");
				return false;
			}
			// 静默推进到"可完成"——只为满足 FinishQuest 的前置检查；
			// 通知/弹窗交给紧随其后的原生完成流程，避免一次完成弹两个提示
			TryNativeSetQuestStatus(questId2, 3, notify: false);
			if (finish.Invoke(questCtrl, new object[2] { quest, true }) is Task task)
			{
				task.ContinueWith(delegate(Task t)
				{
					if (t.IsFaulted)
					{
						VisitPlugin.Log.LogWarning((object)("[NativeQuest] Native FinishQuest faulted: " + t.Exception?.InnerException?.Message));
					}
					else
					{
						VisitPlugin.Log.LogInfo((object)("[NativeQuest] Native FinishQuest completed: " + questId2));
					}
				});
			}
			VisitPlugin.Log.LogInfo((object)("[NativeQuest] Native FinishQuest triggered: " + questId2));
			return true;
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] Native complete failed: " + (ex.InnerException?.Message ?? ex.Message)));
			return false;
		}
	}

	// 通过反射调用 GUISounds.PlayUISound 播放任务完成音效（按候选名逐个尝试）
	private static void TryPlayQuestCompletedSound()
	{
		try
		{
			Type guiSoundsType = TraderDealScreenVisitButton.FindType("EFT.UI.GUISounds") ?? TraderDealScreenVisitButton.FindType("GUISounds");
			if (guiSoundsType == null)
			{
				return;
			}
			UnityEngine.Object instance = UnityEngine.Object.FindObjectOfType(guiSoundsType);
			if (instance == (UnityEngine.Object)null)
			{
				return;
			}
			MethodInfo play = null;
			foreach (MethodInfo m in guiSoundsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				ParameterInfo[] ps = m.GetParameters();
				if (m.Name == "PlayUISound" && ps.Length == 1 && ps[0].ParameterType.IsEnum)
				{
					play = m;
					break;
				}
			}
			if (play == null)
			{
				return;
			}
			Type soundType = play.GetParameters()[0].ParameterType;
			foreach (string name in new[] { "QuestCompleted", "QuestFinished", "QuestComplete", "QuestSubTrackComplete", "TradeOperationComplete" })
			{
				if (Enum.IsDefined(soundType, name))
				{
					play.Invoke(instance, new object[1] { Enum.Parse(soundType, name) });
					VisitPlugin.Log.LogInfo((object)("[NativeQuest] Quest-complete sound: " + name));
					return;
				}
			}
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] No matching quest-complete sound; available: " + string.Join(", ", Enum.GetNames(soundType))));
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] TryPlayQuestCompletedSound: " + (ex.InnerException?.Message ?? ex.Message)));
		}
	}

	internal static void Sync(string? profileId)
	{
		if (!string.IsNullOrEmpty(profileId))
		{
			VisitPlugin.Log.LogInfo((object)("[NativeQuest] Sync: profile=" + profileId));
			Post("/quest/sync", new
			{
				ProfileId = profileId
			});
		}
	}

	// 枚举当前能拿到的原生 QuestController 里的任务，对每个回调 (questId, status)。
	// 返回数据来源标签（trader-cache / in-raid / none / *:no-quests）供日志用。
	private static string ForEachNativeQuest(Action<string, int> onQuest)
	{
		object questCtrl = TraderDealScreenVisitButton._cachedQuestCtrl;
		string source = "trader-cache";
		if (questCtrl == null && VisitPlugin.TryGetInRaidControllers(out object _, out object inRaidCtrl, out object _))
		{
			questCtrl = inRaidCtrl;
			source = "in-raid";
		}
		if (questCtrl == null)
		{
			return "none";
		}
		BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		if (!(questCtrl.GetType().GetProperty("Quests", all)?.GetValue(questCtrl) is IEnumerable quests))
		{
			return source + ":no-quests";
		}
		foreach (object item in quests)
		{
			if (item == null)
			{
				continue;
			}
			object tmpl = item.GetType().GetProperty("Template", all)?.GetValue(item);
			string id = tmpl?.GetType().GetProperty("Id", all)?.GetValue(tmpl) as string;
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}
			object statusObj = item.GetType().GetProperty("QuestStatus", all)?.GetValue(item)
				?? item.GetType().GetField("QuestStatus", all)?.GetValue(item)
				?? item.GetType().GetProperty("Status", all)?.GetValue(item);
			if (statusObj != null)
			{
				onQuest(id!, Convert.ToInt32(statusObj));
			}
		}
		return source;
	}

	// 读取原生任务状态（最准，含外部 WTT 任务）。拿不到返回 null。门控查询前用它刷新缓存。
	internal static Dictionary<string, int>? ReadNativeStatuses()
	{
		Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		ForEachNativeQuest(delegate(string id, int status) { map[id] = status; });
		return map.Count > 0 ? map : null;
	}

	// 调试探针（F7）：把原生任务的 id→状态打到日志。日志用英文避免 Player.log 乱码。
	internal static void DumpAllQuests()
	{
		int total = 0;
		Dictionary<int, int> byStatus = new Dictionary<int, int>();
		VisitPlugin.Log.LogInfo((object)"[QuestDump] ===== dump start =====");
		string source = ForEachNativeQuest(delegate(string id, int status)
		{
			total++;
			byStatus[status] = (byStatus.TryGetValue(status, out int c) ? c : 0) + 1;
			VisitPlugin.Log.LogInfo((object)$"[QuestDump]   {id}  status={status}({StatusName(status)})");
		});
		VisitPlugin.Log.LogInfo((object)$"[QuestDump] ===== source={source}, total={total}, {string.Join(", ", byStatus.Select(kv => StatusName(kv.Key) + "=" + kv.Value))} =====");
	}

	private static string StatusName(int s)
	{
		switch (s)
		{
		case 0: return "Locked";
		case 1: return "AvailableForStart";
		case 2: return "Started";
		case 3: return "AvailableForFinish";
		case 4: return "Success";
		case 5: return "Fail";
		default: return "?";
		}
	}

	private static void TryNativeHandoverScreen(string questId, Action<bool> onResult)
	{
		string questId2 = questId;
		Action<bool> onResult2 = onResult;
		// 统一退路：仅当原生上交窗口确实无法呈现时才转服务端 /quest/handover
		void Fallback(string reason)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] " + reason + "; falling back to server handover"));
			FallbackHandover(questId2, onResult2);
		}
		object questCtrl = TraderDealScreenVisitButton._cachedQuestCtrl;
		if (questCtrl == null)
		{
			Fallback("No questCtrl");
			return;
		}
		object questObj = FindQuestInBook(questCtrl, questId2);
		if (questObj == null)
		{
			Fallback("Quest " + questId2 + " not in QuestBook");
			return;
		}
		object handoverCond = FindHandoverCondition(questObj);
		if (handoverCond == null)
		{
			Fallback("No ConditionHandoverItem in " + questId2);
			return;
		}
		MethodInfo getItems = questCtrl.GetType().GetMethod("GetItemsForCondition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (getItems == null)
		{
			Fallback("GetItemsForCondition not found");
			return;
		}
		object eligibleItems = getItems.Invoke(questCtrl, new object[1] { handoverCond });
		if (eligibleItems is Array { Length: 0 })
		{
			Fallback("No eligible items for handover");
			return;
		}
		object profile = questCtrl.GetType().GetField("Profile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(questCtrl);
		if (profile == null)
		{
			Fallback("No profile on questCtrl");
			return;
		}
		object inventoryCtrl = TraderDealScreenVisitButton._cachedInvCtrl;
		if (inventoryCtrl == null && VisitPlugin.TryGetInRaidControllers(out object _, out object _, out object raidInvCtrl))
		{
			inventoryCtrl = raidInvCtrl;
		}
		if (inventoryCtrl == null)
		{
			Fallback("No inventory controller");
			return;
		}
		Type windowType = TraderDealScreenVisitButton.FindType("EFT.UI.HandoverQuestItemsWindow");
		if (windowType == null)
		{
			Fallback("HandoverQuestItemsWindow type not found");
			return;
		}
		// 在商人 Deal 界面的对话里，原生上交窗口通常尚未实例化到场景中。旧逻辑直接拿
		// FindObjectsOfTypeAll 的首个匹配（往往是 prefab 资产）调 Show，prefab 不在任何
		// Canvas 层级里 → 窗口不可见。这里优先用场景活实例，没有则从 prefab 实例化一个。
		MonoBehaviour window = ResolveHandoverWindow(windowType);
		if ((UnityEngine.Object)(object)window == (UnityEngine.Object)null)
		{
			Fallback("HandoverQuestItemsWindow unavailable (no scene instance or prefab)");
			return;
		}
		MethodInfo showMethod = FindHandoverShowMethod(windowType);
		if (showMethod == null)
		{
			Fallback("HandoverQuestItemsWindow.Show(Condition...) not found");
			return;
		}
		// Show 的第 6 个参数是 Action<TSelected>：玩家在窗口里确认上交后回调，携带勾选的物品集合
		Type acceptDelegateType = showMethod.GetParameters()[5].ParameterType;
		Type selectedItemsType = acceptDelegateType.GetGenericArguments()[0];
		MethodInfo handoverItemMethod = questCtrl.GetType().GetMethod("HandoverItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		Action<object> onAccept = delegate(object selectedItemsObj)
		{
			// onAccept 由原生窗口的“上交”按钮在 Unity 主线程触发。
			try
			{
				if (handoverItemMethod == null)
				{
					VisitPlugin.Log.LogWarning((object)"[NativeQuest] HandoverItem method not found; cannot submit");
					onResult2(false);
					return;
				}
				// HandoverItem 返回的 Task 跑原生网络事务，其 ContinueWith 在后台线程执行——
				// 里面只能记日志，绝不能回调 onResult2（它会驱动对话刷新=Unity UI，跨线程会崩溃）。
				// onResult2 在此（主线程）同步回调，与原生 accept/complete 一致。
				if (handoverItemMethod.Invoke(questCtrl, new object[4] { questObj, handoverCond, selectedItemsObj, true }) is Task task)
				{
					task.ContinueWith(delegate(Task t3)
					{
						if (t3.IsFaulted)
						{
							VisitPlugin.Log.LogWarning((object)("[NativeQuest] HandoverItem faulted: " + t3.Exception?.InnerException?.Message));
						}
						else
						{
							VisitPlugin.Log.LogInfo((object)("[NativeQuest] HandoverItem completed: " + questId2));
						}
					});
				}
				onResult2(true);
			}
			catch (Exception ex)
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] onAccept error: " + (ex.InnerException?.Message ?? ex.Message)));
				onResult2(false);
			}
		};
		// 把 Action<object> 适配成 Show 需要的强类型 Action<TSelected>
		ParameterExpression selectedParam = Expression.Parameter(selectedItemsType, "selectedItems");
		ConstantExpression callbackConst = Expression.Constant(onAccept, typeof(Action<object>));
		InvocationExpression invokeBody = Expression.Invoke(callbackConst, Expression.Convert(selectedParam, typeof(object)));
		Delegate typedAccept = Expression.Lambda(acceptDelegateType, invokeBody, selectedParam).Compile();
		((Component)window).gameObject.SetActive(true);
		showMethod.Invoke(window, new object[7] { handoverCond, 0.0, eligibleItems, profile, inventoryCtrl, typedAccept, true });
		// 让窗口渲染在对话之上且能接收点击
		Canvas canvas = ((Component)window).GetComponent<Canvas>() ?? ((Component)window).gameObject.AddComponent<Canvas>();
		canvas.overrideSorting = true;
		canvas.sortingOrder = 2000;
		if ((UnityEngine.Object)(object)((Component)window).GetComponent<GraphicRaycaster>() == (UnityEngine.Object)null)
		{
			((Component)window).gameObject.AddComponent<GraphicRaycaster>();
		}
		VisitPlugin.Log.LogInfo((object)("[NativeQuest] HandoverQuestItemsWindow shown for quest " + questId2 + " (sortingOrder=2000)"));
	}

	// 任务的 NecessaryConditions 里找 ConditionHandoverItem（上交条件）
	private static object? FindHandoverCondition(object questObj)
	{
		if (questObj.GetType().GetProperty("NecessaryConditions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(questObj) is IEnumerable conditions)
		{
			foreach (object cond in conditions)
			{
				if (cond?.GetType().Name == "ConditionHandoverItem")
				{
					return cond;
				}
			}
		}
		return null;
	}

	// 找 HandoverQuestItemsWindow.Show(Condition, double, items, profile, invCtrl, Action<TSelected>, bool) 这个 7 参重载
	private static MethodInfo? FindHandoverShowMethod(Type windowType)
	{
		foreach (MethodInfo m in windowType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			if (m.Name == "Show")
			{
				ParameterInfo[] parameters = m.GetParameters();
				if (parameters.Length == 7 && parameters[0].ParameterType.Name == "Condition")
				{
					return m;
				}
			}
		}
		return null;
	}

	// 优先返回场景里的活实例；没有就从 prefab 资产实例化一个并挂到当前界面 Canvas 下，
	// 这样在对话上下文（上交窗口尚未被原生任务界面创建）时也能把窗口真正显示出来。
	private static MonoBehaviour? ResolveHandoverWindow(Type windowType)
	{
		MonoBehaviour? sceneInstance = null;
		MonoBehaviour? prefab = null;
		foreach (MonoBehaviour mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
		{
			if ((UnityEngine.Object)(object)mb == (UnityEngine.Object)null || ((object)mb).GetType() != windowType)
			{
				continue;
			}
			if (((Component)mb).gameObject.scene.IsValid())
			{
				sceneInstance = mb;
				break;
			}
			prefab = mb;
		}
		if ((UnityEngine.Object)(object)sceneInstance != (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogInfo((object)"[NativeQuest] Using existing HandoverQuestItemsWindow scene instance");
			return sceneInstance;
		}
		if ((UnityEngine.Object)(object)prefab == (UnityEngine.Object)null)
		{
			return null;
		}
		Transform? parent = GetActiveUiParent();
		if ((UnityEngine.Object)(object)parent == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] No active UI parent to host HandoverQuestItemsWindow");
			return null;
		}
		GameObject go = UnityEngine.Object.Instantiate<GameObject>(((Component)prefab).gameObject, parent, false);
		VisitPlugin.Log.LogInfo((object)"[NativeQuest] Instantiated HandoverQuestItemsWindow from prefab");
		return go.GetComponent(windowType) as MonoBehaviour;
	}

	// 拿一个当前场景里活动的 UI 父节点来挂载实例化出来的窗口
	private static Transform? GetActiveUiParent()
	{
		Component screensGroup = TraderDealScreenHook.ScreensGroup;
		if ((UnityEngine.Object)(object)screensGroup != (UnityEngine.Object)null)
		{
			return ((Component)screensGroup).transform;
		}
		foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>())
		{
			if ((UnityEngine.Object)(object)canvas != (UnityEngine.Object)null && ((Component)canvas).gameObject.scene.IsValid() && ((Behaviour)canvas).isActiveAndEnabled)
			{
				return ((Component)canvas).transform;
			}
		}
		return null;
	}

	private static object? FindQuestInBook(object questCtrl, string questId)
	{
		if (!(questCtrl.GetType().GetProperty("Quests", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(questCtrl) is IEnumerable enumerable))
		{
			return null;
		}
		foreach (object item in enumerable)
		{
			if (item != null)
			{
				object obj = item.GetType().GetProperty("Template", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
				if (obj != null && string.Equals(obj.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) as string, questId, StringComparison.OrdinalIgnoreCase))
				{
					return item;
				}
			}
		}
		return null;
	}

	private static void FallbackHandover(string questId, Action<bool> onResult)
	{
		string profileId;
		string playerName;
		string profileId2 = (TraderDealScreenVisitButton.TryGetCachedProfile(out profileId, out playerName) ? profileId : "");
		bool obj = Post("/quest/handover", new
		{
			ProfileId = profileId2,
			QuestId = questId
		});
		onResult(obj);
	}

	// notify=false：照常标记条件达成（FinishQuest 前置需要），但不触发玩家可见的状态变更通知
	private static void TryNativeSetQuestStatus(string questId, int status, bool notify = true)
	{
		try
		{
			object obj = TraderDealScreenVisitButton._cachedQuestCtrl;
			if (obj == null && VisitPlugin.TryGetInRaidControllers(out object _, out object questCtrl, out object _))
			{
				obj = questCtrl;
			}
			if (obj == null)
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] No quest controller for SetStatus " + questId));
				return;
			}
			object obj2 = FindQuestInBook(obj, questId);
			if (obj2 == null)
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] Quest " + questId + " not found for SetStatus"));
				return;
			}
			MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "TryExecuteTransition" && m.GetParameters().Length == 2);
			if (methodInfo != null)
			{
				object obj3 = Enum.ToObject(methodInfo.GetParameters()[1].ParameterType, status);
				object obj4 = methodInfo.Invoke(obj, new object[2] { obj2, obj3 });
				bool flag = default(bool);
				int num;
				if (obj4 is bool)
				{
					flag = (bool)obj4;
					num = 1;
				}
				else
				{
					num = 0;
				}
				int num2 = num & (flag ? 1 : 0);
				VisitPlugin.Log.LogInfo((object)$"[NativeQuest] TryExecuteTransition {questId} → {status}, result={obj4}");
				if (num2 != 0)
				{
					return;
				}
			}
			MethodInfo methodInfo2 = obj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "SetConditionalStatus" && m.GetParameters().Length == 2);
			if (methodInfo2 != null)
			{
				object obj5 = Enum.ToObject(methodInfo2.GetParameters()[1].ParameterType, status);
				methodInfo2.Invoke(obj, new object[2] { obj2, obj5 });
				VisitPlugin.Log.LogInfo((object)$"[NativeQuest] SetConditionalStatus {questId} → {status} (notify={notify})");
				if (notify)
				{
					TryFireQuestNotification(obj, obj2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				else
				{
					TryAdvanceQuestConditions(obj, obj2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				return;
			}
			MethodInfo methodInfo3 = obj2.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo m) => m.Name == "TransitionStatus" && m.GetParameters().Length == 2);
			if (methodInfo3 != null)
			{
				object obj6 = Enum.ToObject(methodInfo3.GetParameters()[0].ParameterType, status);
				methodInfo3.Invoke(obj2, new object[2] { obj6, true });
				VisitPlugin.Log.LogInfo((object)$"[NativeQuest] TransitionStatus {questId} → {status} (fromServer=true, notify={notify})");
				if (notify)
				{
					TryFireQuestNotification(obj, obj2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
				else
				{
					TryAdvanceQuestConditions(obj, obj2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				}
			}
			else
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] No transition method found for " + questId));
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] TryNativeSetQuestStatus: " + (ex.InnerException?.Message ?? ex.Message)));
		}
	}

	private static void TryFireQuestNotification(object questCtrl, object questObj, BindingFlags all)
	{
		try
		{
			TryAdvanceQuestConditions(questCtrl, questObj, all);
			MethodInfo methodInfo = questCtrl.GetType().GetMethods(all).FirstOrDefault((MethodInfo m) => m.Name == "OnConditionalStatusChangedEvent" && m.GetParameters().Length == 2);
			if (methodInfo != null)
			{
				methodInfo.Invoke(questCtrl, new object[2] { questObj, true });
				VisitPlugin.Log.LogInfo((object)"[NativeQuest] OnConditionalStatusChangedEvent called");
			}
			else
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] OnConditionalStatusChangedEvent not found");
			}
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] TryFireQuestNotification: " + (ex.InnerException?.Message ?? ex.Message)));
		}
	}

	private static void TryAdvanceQuestConditions(object questCtrl, object questObj, BindingFlags all)
	{
		try
		{
			PropertyInfo property = questObj.GetType().GetProperty("NecessaryConditions", all);
			if (property == null)
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] NecessaryConditions property not found");
				return;
			}
			if (!(property.GetValue(questObj) is IEnumerable enumerable))
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] NecessaryConditions is null");
				return;
			}
			MethodInfo methodInfo = questCtrl.GetType().GetMethods(all).FirstOrDefault((MethodInfo m) => m.Name == "TryExecuteTransition" && m.GetParameters().Length == 2);
			if (methodInfo == null)
			{
				return;
			}
			object obj = Enum.ToObject(methodInfo.GetParameters()[1].ParameterType, 3);
			MethodInfo methodInfo2 = questCtrl.GetType().GetMethods(all).FirstOrDefault((MethodInfo m) => m.Name == "OnConditionValueChanged" && m.GetParameters().Length == 4 && !m.IsGenericMethodDefinition);
			if (methodInfo2 == null)
			{
				VisitPlugin.Log.LogWarning((object)"[NativeQuest] OnConditionValueChanged(4-param non-generic) not found");
				return;
			}
			int num = 0;
			foreach (object item in enumerable)
			{
				if (item != null)
				{
					methodInfo2.Invoke(questCtrl, new object[4] { questObj, obj, item, true });
					num++;
				}
			}
			VisitPlugin.Log.LogInfo((object)$"[NativeQuest] OnConditionValueChanged called for {num} condition(s)");
		}
		catch (Exception ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] TryAdvanceQuestConditions: " + (ex.InnerException?.Message ?? ex.Message)));
		}
	}

	private static string? PostRaw(string endpoint, string jsonBody)
	{
		try
		{
			using WebClient webClient = new WebClient
			{
				Encoding = Encoding.UTF8
			};
			webClient.Headers["Content-Type"] = "application/json; charset=utf-8";
			return webClient.UploadString("http://127.0.0.1:6970/visitapi" + endpoint, jsonBody);
		}
		catch (WebException ex)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] " + endpoint + " network error: " + ex.Message));
			return null;
		}
		catch (Exception ex2)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] " + endpoint + " failed: " + ex2.Message));
			return null;
		}
	}

	private static bool Post(string endpoint, object payload)
	{
		string text = PostRaw(endpoint, JsonConvert.SerializeObject(payload));
		if (text == null)
		{
			return false;
		}
		try
		{
			JObject val = JObject.Parse(text);
			JToken successToken = val["success"];
			bool success = successToken != null && Extensions.Value<bool>((IEnumerable<JToken>)successToken);
			if (!success)
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] " + endpoint + ": " + (((object)val["error"])?.ToString() ?? "unknown error")));
			}
			return success;
		}
		catch
		{
			return false;
		}
	}

}
