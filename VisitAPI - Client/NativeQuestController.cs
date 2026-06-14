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
		// 仅服务端接取（/quest/accept 写档持久化）。不调用游戏原生 AcceptQuest：
		// 原生接取会触发任务列表 UI 整体重建，恰好把同一步里刚点亮的前置任务
		//（如 Ragman「调查不明第三方势力」）进度条盖回去；且此刻 SORA 多半尚未解锁，
		// device 作为 SORA 任务本就不该立刻显示，原生接取无可见收益、纯添乱。
		// 任务在 SORA 解锁后由档案呈现；完成仍走情报中心对话的 complete 指令。
		return Post("/quest/accept", new
		{
			ProfileId = profileId,
			QuestId = questId
		});
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

	private static void TryNativeHandoverScreen(string questId, Action<bool> onResult)
	{
		string questId2 = questId;
		Action<bool> onResult2 = onResult;
		object questCtrl = TraderDealScreenVisitButton._cachedQuestCtrl;
		if (questCtrl == null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] No questCtrl; falling back to server handover");
			FallbackHandover(questId2, onResult2);
			return;
		}
		object questObj = FindQuestInBook(questCtrl, questId2);
		if (questObj == null)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] Quest " + questId2 + " not found; falling back"));
			FallbackHandover(questId2, onResult2);
			return;
		}
		object handoverCond = null;
		if (questObj.GetType().GetProperty("NecessaryConditions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(questObj) is IEnumerable enumerable)
		{
			foreach (object item in enumerable)
			{
				if (item?.GetType().Name == "ConditionHandoverItem")
				{
					handoverCond = item;
					break;
				}
			}
		}
		if (handoverCond == null)
		{
			VisitPlugin.Log.LogWarning((object)("[NativeQuest] No ConditionHandoverItem in " + questId2 + "; falling back"));
			FallbackHandover(questId2, onResult2);
			return;
		}
		MethodInfo method = questCtrl.GetType().GetMethod("GetItemsForCondition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] GetItemsForCondition not found; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		object obj = method.Invoke(questCtrl, new object[1] { handoverCond });
		if (obj is Array { Length: 0 })
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] No eligible items for handover; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		object obj2 = questCtrl.GetType().GetField("Profile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(questCtrl);
		if (obj2 == null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] No profile on questCtrl; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		object obj3 = TraderDealScreenVisitButton._cachedInvCtrl;
		if (obj3 == null && VisitPlugin.TryGetInRaidControllers(out object _, out object _, out object invCtrl))
		{
			obj3 = invCtrl;
		}
		if (obj3 == null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] No inventory controller; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		Type type = TraderDealScreenVisitButton.FindType("EFT.UI.HandoverQuestItemsWindow");
		if (type == null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] HandoverQuestItemsWindow type not found; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		MonoBehaviour val = null;
		MonoBehaviour[] array2 = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
		foreach (MonoBehaviour val2 in array2)
		{
			if ((UnityEngine.Object)(object)val2 != (UnityEngine.Object)null && ((object)val2).GetType() == type)
			{
				val = val2;
				break;
			}
		}
		if ((UnityEngine.Object)(object)val == (UnityEngine.Object)null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] HandoverQuestItemsWindow not found in scene; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		MethodInfo methodInfo = null;
		MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (MethodInfo methodInfo2 in methods)
		{
			if (!(methodInfo2.Name != "Show"))
			{
				ParameterInfo[] parameters = methodInfo2.GetParameters();
				if (parameters.Length == 7 && parameters[0].ParameterType.Name == "Condition")
				{
					methodInfo = methodInfo2;
					break;
				}
			}
		}
		if (methodInfo == null)
		{
			VisitPlugin.Log.LogWarning((object)"[NativeQuest] HandoverQuestItemsWindow.Show(Condition...) not found; falling back");
			FallbackHandover(questId2, onResult2);
			return;
		}
		Type parameterType = methodInfo.GetParameters()[5].ParameterType;
		Type type2 = parameterType.GetGenericArguments()[0];
		MethodInfo handoverItemMethod = questCtrl.GetType().GetMethod("HandoverItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		Action<object> value = delegate(object selectedItemsObj)
		{
			try
			{
				if (handoverItemMethod != null && handoverItemMethod.Invoke(questCtrl, new object[4] { questObj, handoverCond, selectedItemsObj, true }) is Task task)
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
				onResult2(obj: true);
			}
			catch (Exception ex)
			{
				VisitPlugin.Log.LogWarning((object)("[NativeQuest] acceptWrapper error: " + (ex.InnerException?.Message ?? ex.Message)));
				onResult2(obj: false);
			}
		};
		ParameterExpression parameterExpression = Expression.Parameter(type2, "selectedItems");
		ConstantExpression expression = Expression.Constant(value, typeof(Action<object>));
		UnaryExpression unaryExpression = Expression.Convert(parameterExpression, typeof(object));
		InvocationExpression body = Expression.Invoke(expression, unaryExpression);
		Delegate @delegate = Expression.Lambda(parameterType, body, parameterExpression).Compile();
		((Component)val).gameObject.SetActive(true);
		methodInfo.Invoke(val, new object[7] { handoverCond, 0.0, obj, obj2, obj3, @delegate, true });
		Canvas val3 = ((Component)val).GetComponent<Canvas>();
		if ((UnityEngine.Object)(object)val3 == (UnityEngine.Object)null)
		{
			val3 = ((Component)val).gameObject.AddComponent<Canvas>();
		}
		val3.overrideSorting = true;
		val3.sortingOrder = 2000;
		if ((UnityEngine.Object)(object)((Component)val).GetComponent<GraphicRaycaster>() == (UnityEngine.Object)null)
		{
			((Component)val).gameObject.AddComponent<GraphicRaycaster>();
		}
		VisitPlugin.Log.LogInfo((object)("[NativeQuest] HandoverQuestItemsWindow shown for quest " + questId2 + " (sortingOrder=2000)"));
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
