using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Servers;

namespace VisitAPI.Server;

[Injectable(/*Could not decode attribute arguments.*/)]
public class VisitApiQuestHelper
{
	private record HandoverCondition(string Id, List<string> Targets, int Value);

	private record QuestTransition(string TriggerQuestId, string DependentQuestId, int TargetStatus);

	private static readonly JsonSerializerOptions CiOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private readonly ISptLogger<VisitApiQuestHelper> _logger;

	private readonly SaveServer _saveServer;

	private static Type? _mongoIdType;

	private static List<QuestTransition>? _transitionCache;

	public VisitApiQuestHelper(ISptLogger<VisitApiQuestHelper> logger, SaveServer saveServer)
	{
		_logger = logger;
		_saveServer = saveServer;
	}

	public Task<string> AcceptQuestAsync(string body)
	{
		try
		{
			QuestRequest questRequest = JsonSerializer.Deserialize<QuestRequest>(body, CiOptions);
			if (questRequest == null || string.IsNullOrEmpty(questRequest.ProfileId) || string.IsNullOrEmpty(questRequest.QuestId))
			{
				return Task.FromResult("{\"success\":false,\"error\":\"invalid request\"}");
			}
			// 旁路状态文件：供前置联动判定与 ResolveQuestStatus 回填使用
			SaveQuestAccepted(questRequest.ProfileId, questRequest.QuestId);
			// 真正把任务写进存档 Quests 列表（状态 Started），否则游戏端只显示"可接取"。
			// 客户端 NativeQuestController.AcceptQuest 刻意不走原生接取，故档案必须由服务端落地。
			object pmcData = GetPmcData(questRequest.ProfileId);
			if (pmcData == null)
			{
				return Task.FromResult("{\"success\":false,\"error\":\"profile not found\"}");
			}
			int questStatusValue = GetQuestStatusValue(pmcData, questRequest.QuestId);
			// 仅在尚未接取（锁定/可接取）时写档，避免覆盖已有进度（Started/可完成/成功）
			if (questStatusValue == QuestStatusValue.Locked || questStatusValue == QuestStatusValue.AvailableForStart)
			{
				SetQuestStatus(pmcData, questRequest.QuestId, QuestStatusValue.Started);
				SaveProfile(questRequest.ProfileId);
			}
			return Task.FromResult("{\"success\":true}");
		}
		catch (Exception ex)
		{
			_logger.Error("AcceptQuestAsync: " + ex.Message, (Exception)null);
			return Task.FromResult("{\"success\":false,\"error\":\"" + Esc(ex.Message) + "\"}");
		}
	}

	public Task<string> HandoverQuestAsync(string body)
	{
		try
		{
			QuestRequest questRequest = JsonSerializer.Deserialize<QuestRequest>(body, CiOptions);
			if (questRequest == null || string.IsNullOrEmpty(questRequest.ProfileId) || string.IsNullOrEmpty(questRequest.QuestId))
			{
				return Task.FromResult("{\"success\":false,\"error\":\"invalid request\"}");
			}
			object pmcData = GetPmcData(questRequest.ProfileId);
			if (pmcData == null)
			{
				return Task.FromResult("{\"success\":false,\"error\":\"profile not found\"}");
			}
			int questStatusValue = GetQuestStatusValue(pmcData, questRequest.QuestId);
			if (questStatusValue != QuestStatusValue.Started)
			{
				return Task.FromResult($"{{\"success\":false,\"error\":\"quest not in Started state (current={questStatusValue})\"}}");
			}
			TryRemoveHandoverItems(pmcData, questRequest.QuestId);
			SetQuestStatus(pmcData, questRequest.QuestId, QuestStatusValue.AvailableForFinish);
			SaveProfile(questRequest.ProfileId);
			return Task.FromResult("{\"success\":true}");
		}
		catch (Exception ex)
		{
			_logger.Error("HandoverQuestAsync: " + ex.Message, (Exception)null);
			return Task.FromResult("{\"success\":false,\"error\":\"" + Esc(ex.Message) + "\"}");
		}
	}

	public async Task<string> CompleteQuestAsync(string body)
	{
		try
		{
			QuestRequest questRequest = JsonSerializer.Deserialize<QuestRequest>(body, CiOptions);
			if (questRequest == null || string.IsNullOrEmpty(questRequest.ProfileId) || string.IsNullOrEmpty(questRequest.QuestId))
			{
				return "{\"success\":false,\"error\":\"invalid request\"}";
			}
			object pmcData = GetPmcData(questRequest.ProfileId);
			if (pmcData == null)
			{
				return "{\"success\":false,\"error\":\"profile not found\"}";
			}
			if (!questRequest.Native)
			{
				int questStatusValue = GetQuestStatusValue(pmcData, questRequest.QuestId);
				if (questStatusValue == QuestStatusValue.Locked || questStatusValue == QuestStatusValue.Success || questStatusValue == QuestStatusValue.Fail)
				{
					return $"{{\"success\":false,\"error\":\"quest not ready for completion (current={questStatusValue})\"}}";
				}
				SetQuestStatus(pmcData, questRequest.QuestId, QuestStatusValue.Success);
				TryApplySuccessRewards(pmcData, questRequest.QuestId);
				SaveProfile(questRequest.ProfileId);
			}
			SaveQuestCompleted(questRequest.ProfileId, questRequest.QuestId);
			List<(string, int)> list = CollectAndApplyTransitions(questRequest.ProfileId, pmcData);
			if (list.Count == 0)
			{
				return "{\"success\":true}";
			}
			string updated = string.Join(",", list.Select(u => $"{{\"questId\":\"{u.Item1}\",\"status\":{u.Item2}}}"));
			return "{\"success\":true,\"updatedQuests\":[" + updated + "]}";
		}
		catch (Exception ex)
		{
			_logger.Error("CompleteQuestAsync: " + ex.Message, (Exception)null);
			return "{\"success\":false,\"error\":\"" + Esc(ex.Message) + "\"}";
		}
	}

	public Task<string> GetQuestStatusAsync(string body)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("ProfileId", out var value) && !rootElement.TryGetProperty("profileId", out value))
			{
				return Task.FromResult("{\"success\":false,\"error\":\"missing ProfileId\"}");
			}
			string text = value.GetString() ?? "";
			if (string.IsNullOrEmpty(text))
			{
				return Task.FromResult("{\"success\":false,\"error\":\"invalid request\"}");
			}
			List<string> list = new List<string>();
			if (rootElement.TryGetProperty("QuestIds", out var value2) || rootElement.TryGetProperty("questIds", out value2))
			{
				if (value2.ValueKind == JsonValueKind.Array)
				{
					foreach (JsonElement item in value2.EnumerateArray())
					{
						string @string = item.GetString();
						if (@string != null)
						{
							list.Add(@string);
						}
					}
				}
				else if (value2.ValueKind == JsonValueKind.String)
				{
					string string2 = value2.GetString();
					if (string2 != null)
					{
						list.Add(string2);
					}
				}
			}
			object pmcData = GetPmcData(text);
			if (pmcData == null)
			{
				return Task.FromResult("{\"success\":false,\"error\":\"profile not found\"}");
			}
			string statuses = string.Join(",", list.Select(qid => $"\"{qid}\":{ResolveQuestStatus(text, pmcData, qid)}"));
			return Task.FromResult("{\"success\":true,\"statuses\":{" + statuses + "}}");
		}
		catch (Exception ex)
		{
			_logger.Error("GetQuestStatusAsync: " + ex.Message, (Exception)null);
			return Task.FromResult("{\"success\":false,\"error\":\"" + Esc(ex.Message) + "\"}");
		}
	}

	// 档案里已有真实进度（Started/可完成/成功/失败）时以档案为准；
	// 仅当档案还没有该任务条目时，才用接取/完成状态文件回填
	//（旧实现"已接取文件优先"会把已完成任务永远报成 Started）
	private static int ResolveQuestStatus(string profileId, object pmcData, string questId)
	{
		int status = GetQuestStatusValue(pmcData, questId);
		if (status == QuestStatusValue.Locked || status == QuestStatusValue.AvailableForStart)
		{
			if (IsQuestCompleted(profileId, questId))
			{
				return QuestStatusValue.Success;
			}
			if (IsQuestAccepted(profileId, questId))
			{
				return QuestStatusValue.Started;
			}
		}
		return status;
	}

	private static object MakeMongoId(string id)
	{
		if (_mongoIdType == null)
		{
			return id;
		}
		ConstructorInfo constructor = _mongoIdType.GetConstructor(new Type[1] { typeof(string) });
		if (constructor != null)
		{
			try
			{
				return constructor.Invoke(new object[1] { id });
			}
			catch
			{
			}
		}
		object obj2 = (_mongoIdType.IsValueType ? Activator.CreateInstance(_mongoIdType) : FormatterServices.GetUninitializedObject(_mongoIdType));
		FieldInfo[] fields = _mongoIdType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.FieldType == typeof(string))
			{
				fieldInfo.SetValue(obj2, id);
				break;
			}
		}
		return obj2;
	}

	private object? GetPmcData(string profileId)
	{
		MethodInfo method = ((object)_saveServer).GetType().GetMethod("GetProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (method == null)
		{
			_logger.Warning("[Quest] SaveServer.GetProfile not found", (Exception)null);
			return null;
		}
		if (_mongoIdType == null)
		{
			_mongoIdType = method.GetParameters().FirstOrDefault()?.ParameterType;
		}
		object obj = MakeMongoId(profileId);
		object obj2;
		try
		{
			obj2 = method.Invoke(_saveServer, new object[1] { obj });
		}
		catch (Exception ex)
		{
			_logger.Warning("[Quest] GetProfile threw: " + ex.Message, (Exception)null);
			return null;
		}
		if (obj2 == null)
		{
			_logger.Warning("[Quest] Profile null for " + profileId, (Exception)null);
			return null;
		}
		Type type = obj2.GetType();
		string[] array = new string[2] { "Pmc", "PmcData" };
		foreach (string name in array)
		{
			object obj3 = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2) ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2);
			if (obj3 != null)
			{
				return obj3;
			}
		}
		array = new string[2] { "CharacterData", "Characters" };
		foreach (string name2 in array)
		{
			object obj4 = type.GetProperty(name2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2) ?? type.GetField(name2, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj2);
			if (obj4 == null)
			{
				continue;
			}
			Type type2 = obj4.GetType();
			string[] array2 = new string[4] { "PmcData", "Pmc", "PMC", "pmc" };
			foreach (string name3 in array2)
			{
				object obj5 = type2.GetProperty(name3, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj4) ?? type2.GetField(name3, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj4);
				if (obj5 != null)
				{
					return obj5;
				}
			}
			PropertyInfo[] properties = type2.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			foreach (PropertyInfo propertyInfo in properties)
			{
				try
				{
					object value = propertyInfo.GetValue(obj4);
					if (value != null)
					{
						Type type3 = value.GetType();
						if (type3.GetProperty("Quests", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null || type3.GetField("Quests", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) != null)
						{
							return value;
						}
					}
				}
				catch
				{
				}
			}
		}
		_logger.Warning("[Quest] PmcData not found on " + type.Name, (Exception)null);
		return null;
	}

	private void SaveProfile(string profileId)
	{
		MethodInfo methodInfo = ((object)_saveServer).GetType().GetMethod("SaveProfileAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? ((object)_saveServer).GetType().GetMethod("SaveProfile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		try
		{
			if (methodInfo?.Invoke(_saveServer, new object[1] { MakeMongoId(profileId) }) is Task task)
			{
				task.GetAwaiter().GetResult();
			}
		}
		catch (Exception ex)
		{
			_logger.Warning("SaveProfile: " + ex.Message, (Exception)null);
		}
	}

	private static int GetQuestStatusValue(object pmcData, string questId)
	{
		IList questsList = GetQuestsList(pmcData, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (questsList == null)
		{
			return VisitApiQuestLoader.RegisteredQuestIds.Contains(questId) ? QuestStatusValue.AvailableForStart : QuestStatusValue.Locked;
		}
		foreach (object item in questsList)
		{
			if (item != null && QuestIdMatches(item, questId, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				object obj = item.GetType().GetProperty("Status", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item) ?? item.GetType().GetField("Status", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
				return (obj != null) ? Convert.ToInt32(obj) : 0;
			}
		}
		return VisitApiQuestLoader.RegisteredQuestIds.Contains(questId) ? QuestStatusValue.AvailableForStart : QuestStatusValue.Locked;
	}

	private void SetQuestStatus(object pmcData, string questId, int newStatus)
	{
		IList questsList = GetQuestsList(pmcData, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (questsList == null)
		{
			_logger.Warning("[Quest] Quests list not found on PMC data", (Exception)null);
			return;
		}
		long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		foreach (object item in questsList)
		{
			if (item != null && QuestIdMatches(item, questId, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				Type type = item.GetType();
				PropertyInfo property = type.GetProperty("Status", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				FieldInfo field = type.GetField("Status", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null)
				{
					property.SetValue(item, ConvertStatus(property.PropertyType, newStatus));
				}
				else if (field != null)
				{
					field.SetValue(item, ConvertStatus(field.FieldType, newStatus));
				}
				EnsureStatusTimers(item, newStatus, now, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				return;
			}
		}
		if (!TryReclaimEmptyEntry(questsList, questId, newStatus, now, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
		{
			TryAddNewQuest(pmcData, questsList, questId, newStatus, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
	}

	private static bool TryReclaimEmptyEntry(IList quests, string questId, int status, long now, BindingFlags all)
	{
		foreach (object quest in quests)
		{
			if (quest == null)
			{
				continue;
			}
			Type type = quest.GetType();
			string value = null;
			bool flag = false;
			string[] array = new string[4] { "QId", "Qid", "qid", "QID" };
			foreach (string name in array)
			{
				object obj = type.GetProperty(name, all)?.GetValue(quest) ?? type.GetField(name, all)?.GetValue(quest);
				if (obj != null)
				{
					flag = true;
					if (obj is string text)
					{
						value = text;
						break;
					}
					string text2 = (obj.GetType().GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { obj.GetType() }, null)?.Invoke(null, new object[1] { obj }) as string) ?? obj.ToString();
					value = ((text2 == obj.GetType().FullName) ? "" : text2);
					break;
				}
			}
			if (!flag || string.IsNullOrEmpty(value))
			{
				Type type2 = type.GetProperty("Qid", all)?.PropertyType ?? type.GetField("Qid", all)?.FieldType ?? type.GetProperty("qid", all)?.PropertyType ?? type.GetField("qid", all)?.FieldType;
				object value2 = ((type2 != null) ? MakeQuestId(type2, questId) : questId);
				TrySetMember(quest, type, new string[4] { "QId", "Qid", "qid", "QID" }, value2, all);
				Type targetType = type.GetProperty("Status", all)?.PropertyType ?? type.GetField("Status", all)?.FieldType ?? typeof(int);
				TrySetMember(quest, type, new string[2] { "Status", "status" }, ConvertStatus(targetType, status), all);
				TrySetMember(quest, type, new string[2] { "StartTime", "startTime" }, now, all);
				EnsureStatusTimers(quest, status, now, all);
				return true;
			}
		}
		return false;
	}

	private static bool TrySetMember(object obj, Type t, string[] names, object? value, BindingFlags all)
	{
		foreach (string name in names)
		{
			try
			{
				PropertyInfo property = t.GetProperty(name, all);
				if (property != null && property.CanWrite)
				{
					property.SetValue(obj, value);
					return true;
				}
				FieldInfo field = t.GetField(name, all);
				if (field != null)
				{
					field.SetValue(obj, value);
					return true;
				}
			}
			catch
			{
			}
		}
		return false;
	}

	private static void EnsureStatusTimers(object quest, int status, long now, BindingFlags all)
	{
		PropertyInfo[] properties = quest.GetType().GetProperties(all);
		foreach (PropertyInfo propertyInfo in properties)
		{
			if (!propertyInfo.Name.Equals("StatusTimers", StringComparison.OrdinalIgnoreCase) || !propertyInfo.CanRead)
			{
				continue;
			}
			object obj = null;
			try
			{
				obj = propertyInfo.GetValue(quest);
			}
			catch
			{
			}
			if (obj != null)
			{
				break;
			}
			MethodInfo setMethod = propertyInfo.GetSetMethod(nonPublic: true);
			if (setMethod == null)
			{
				break;
			}
			try
			{
				Type propertyType = propertyInfo.PropertyType;
				if (Activator.CreateInstance(propertyType) is IDictionary dictionary)
				{
					Type[] array = (propertyType.IsGenericType ? propertyType.GetGenericArguments() : Array.Empty<Type>());
					Type type = ((array.Length != 0) ? array[0] : typeof(string));
					object key = (type.IsEnum ? Enum.ToObject(type, status) : ((type == typeof(string)) ? status.ToString() : Convert.ChangeType(status, type)));
					Type type2 = ((array.Length > 1) ? array[1] : typeof(long));
					dictionary[key] = ((type2 == typeof(long)) ? ((object)now) : Convert.ChangeType(now, type2));
					setMethod.Invoke(quest, new object[1] { dictionary });
				}
				break;
			}
			catch
			{
				break;
			}
		}
	}

	private void TryAddNewQuest(object pmcData, IList quests, string questId, int status, BindingFlags all)
	{
		try
		{
			Type type = quests.GetType();
			Type type2 = (type.IsGenericType ? type.GetGenericArguments()[0] : typeof(object));
			long num = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			object obj = null;
			foreach (ConstructorInfo item in from c in type2.GetConstructors(all)
				orderby c.GetParameters().Length descending
				select c)
			{
				ParameterInfo[] parameters = item.GetParameters();
				if (parameters.Length == 0)
				{
					continue;
				}
				object[] array = new object[parameters.Length];
				bool flag = true;
				for (int i = 0; i < parameters.Length && flag; i++)
				{
					Type parameterType = parameters[i].ParameterType;
					string text = (parameters[i].Name ?? "").ToLowerInvariant();
					switch (text)
					{
					case "qid":
					case "questid":
						array[i] = MakeQuestId(parameterType, questId);
						if (array[i] == null)
						{
							flag = false;
						}
						continue;
					case "status":
						array[i] = ConvertStatus(parameterType, status);
						continue;
					}
					if (text.Contains("starttime") || text == "start")
					{
						array[i] = ((parameterType == typeof(long)) ? ((object)num) : ((object)(int)num));
					}
					else if (text.Contains("available"))
					{
						array[i] = ((parameterType == typeof(long)) ? ((object)0L) : ((object)0));
					}
					else if (text.Contains("statustimer"))
					{
						try
						{
							if (Activator.CreateInstance(parameterType) is IDictionary dictionary)
							{
								Type[] array2 = (parameterType.IsGenericType ? parameterType.GetGenericArguments() : Array.Empty<Type>());
								Type type3 = ((array2.Length != 0) ? array2[0] : typeof(string));
								object key = (type3.IsEnum ? Enum.ToObject(type3, status) : ((type3 == typeof(string)) ? status.ToString() : Convert.ChangeType(status, type3)));
								Type type4 = ((array2.Length > 1) ? array2[1] : typeof(long));
								object value = ((type4 == typeof(long)) ? ((object)num) : ((type4 == typeof(int)) ? ((object)(int)num) : Convert.ChangeType(num, type4)));
								dictionary[key] = value;
								array[i] = dictionary;
							}
							else
							{
								array[i] = TryCreateEmptyCollection(parameterType);
							}
						}
						catch
						{
							array[i] = TryCreateEmptyCollection(parameterType);
						}
					}
					else if (!parameterType.IsValueType && parameterType != typeof(string))
					{
						array[i] = TryActivate(parameterType) ?? TryCreateEmptyCollection(parameterType);
					}
					else if (parameterType.IsValueType)
					{
						array[i] = Activator.CreateInstance(parameterType);
					}
					else
					{
						array[i] = null;
					}
				}
				if (!flag)
				{
					continue;
				}
				try
				{
					obj = item.Invoke(array);
					if (obj == null)
					{
						continue;
					}
					object value2 = MakeQuestId((type2.GetProperty("QId", all) ?? type2.GetProperty("Qid", all) ?? type2.GetProperty("qid", all))?.PropertyType ?? typeof(string), questId);
					TrySetMember(obj, type2, new string[4] { "QId", "Qid", "qid", "QID" }, value2, all);
					Type targetType = type2.GetProperty("Status", all)?.PropertyType ?? typeof(int);
					TrySetMember(obj, type2, new string[1] { "Status" }, ConvertStatus(targetType, status), all);
					PropertyInfo propertyInfo = type2.GetProperty("StartTime", all) ?? type2.GetProperty("startTime", all);
					if (propertyInfo != null)
					{
						try
						{
							TrySetMember(obj, type2, new string[2] { "StartTime", "startTime" }, Convert.ChangeType(num, propertyInfo.PropertyType), all);
						}
						catch
						{
						}
					}
					EnsureStatusTimers(obj, status, num, all);
					break;
				}
				catch (Exception ex)
				{
					_logger.Debug($"[Quest] .ctor({parameters.Length}) fail: {ex.InnerException?.GetType().Name ?? ex.GetType().Name}", (Exception)null);
				}
			}
			if (obj == null)
			{
				_logger.Warning("[Quest] All constructors failed — cannot add quest entry", (Exception)null);
			}
			else
			{
				quests.Add(obj);
			}
		}
		catch (Exception ex2)
		{
			_logger.Warning("TryAddNewQuest: " + ex2.Message, (Exception)null);
		}
	}

	private static object? MakeQuestId(Type type, string questId)
	{
		if (type == typeof(string))
		{
			return questId;
		}
		ConstructorInfo constructor = type.GetConstructor(new Type[1] { typeof(string) });
		if (constructor != null)
		{
			try
			{
				return constructor.Invoke(new object[1] { questId });
			}
			catch
			{
			}
		}
		MethodInfo method = type.GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(string) }, null);
		if (method != null)
		{
			try
			{
				return method.Invoke(null, new object[1] { questId });
			}
			catch
			{
			}
		}
		try
		{
			object obj3 = (type.IsValueType ? Activator.CreateInstance(type) : Activator.CreateInstance(type));
			if (obj3 != null)
			{
				FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (FieldInfo fieldInfo in fields)
				{
					if (fieldInfo.FieldType == typeof(string))
					{
						fieldInfo.SetValue(obj3, questId);
						return obj3;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static IList? GetQuestsList(object pmcData, BindingFlags all)
	{
		Type type = pmcData.GetType();
		FieldInfo[] fields = type.GetFields(all);
		foreach (FieldInfo fieldInfo in fields)
		{
			if (fieldInfo.Name.IndexOf("Quests", StringComparison.OrdinalIgnoreCase) >= 0 && fieldInfo.GetValue(pmcData) is IList result)
			{
				return result;
			}
		}
		return (type.GetProperty("Quests", all)?.GetValue(pmcData) ?? type.GetField("Quests", all)?.GetValue(pmcData)) as IList;
	}

	private static bool QuestIdMatches(object q, string questId, BindingFlags all)
	{
		Type type = q.GetType();
		string[] array = new string[6] { "QId", "Qid", "QID", "qid", "Id", "id" };
		foreach (string name in array)
		{
			object obj = type.GetProperty(name, all)?.GetValue(q) ?? type.GetField(name, all)?.GetValue(q);
			if (obj is string text)
			{
				return text == questId;
			}
			if (obj != null)
			{
				string text2 = (obj.GetType().GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { obj.GetType() }, null)?.Invoke(null, new object[1] { obj }) as string) ?? obj.ToString();
				if (!string.IsNullOrEmpty(text2) && text2 != obj.GetType().FullName)
				{
					return text2 == questId;
				}
			}
		}
		return false;
	}

	private static object ConvertStatus(Type targetType, int statusInt)
	{
		if (targetType == typeof(int))
		{
			return statusInt;
		}
		if (targetType.IsEnum)
		{
			return Enum.ToObject(targetType, statusInt);
		}
		return statusInt;
	}

	private static object? TryCreateEmptyCollection(Type type)
	{
		if (!type.IsGenericType)
		{
			return null;
		}
		Type genericTypeDefinition = type.GetGenericTypeDefinition();
		Type[] genericArguments = type.GetGenericArguments();
		try
		{
			if (genericTypeDefinition == typeof(Dictionary<, >) || genericTypeDefinition == typeof(IDictionary<, >))
			{
				return Activator.CreateInstance(typeof(Dictionary<, >).MakeGenericType(genericArguments));
			}
			if (genericTypeDefinition == typeof(HashSet<>) || genericTypeDefinition == typeof(ISet<>) || genericTypeDefinition == typeof(IReadOnlySet<>))
			{
				return Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(genericArguments));
			}
			if (genericTypeDefinition == typeof(List<>) || genericTypeDefinition == typeof(IList<>) || genericTypeDefinition == typeof(IReadOnlyList<>))
			{
				return Activator.CreateInstance(typeof(List<>).MakeGenericType(genericArguments));
			}
		}
		catch
		{
		}
		return null;
	}

	private static object? TryActivate(Type type)
	{
		try
		{
			return Activator.CreateInstance(type);
		}
		catch
		{
			return null;
		}
	}

	private static string Esc(string s)
	{
		return s.Replace("\"", "'");
	}

	private List<HandoverCondition> GetHandoverConditions(string questId)
	{
		if (!VisitApiQuestLoader.QuestJsons.TryGetValue(questId, out string value))
		{
			return new List<HandoverCondition>();
		}
		List<HandoverCondition> list = new List<HandoverCondition>();
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(value);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value2 = default(JsonElement);
			if ((rootElement.TryGetProperty("conditions", out var value3) || rootElement.TryGetProperty("Conditions", out value3)) && !value3.TryGetProperty("AvailableForFinish", out value2))
			{
				value3.TryGetProperty("availableForFinish", out value2);
			}
			if (value2.ValueKind != JsonValueKind.Array)
			{
				return list;
			}
			foreach (JsonElement item in value2.EnumerateArray())
			{
				string text = "";
				if (item.TryGetProperty("conditionType", out var value4))
				{
					text = value4.GetString() ?? "";
				}
				if (string.IsNullOrEmpty(text) && item.TryGetProperty("_parent", out var value5))
				{
					text = value5.GetString() ?? "";
				}
				if (!string.Equals(text, "HandoverItem", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				if (!item.TryGetProperty("_props", out var value6))
				{
					value6 = item;
				}
				List<string> list2 = new List<string>();
				if (value6.TryGetProperty("target", out var value7))
				{
					if (value7.ValueKind == JsonValueKind.Array)
					{
						foreach (JsonElement item2 in value7.EnumerateArray())
						{
							string @string = item2.GetString();
							if (!string.IsNullOrEmpty(@string))
							{
								list2.Add(@string);
							}
						}
					}
					else if (value7.ValueKind == JsonValueKind.String)
					{
						string string2 = value7.GetString();
						if (!string.IsNullOrEmpty(string2))
						{
							list2.Add(string2);
						}
					}
				}
				int value8 = 1;
				if (value6.TryGetProperty("value", out var value9))
				{
					value8 = (int)value9.GetDouble();
				}
				string id = "";
				if (value6.TryGetProperty("id", out var value10))
				{
					id = value10.GetString() ?? "";
				}
				list.Add(new HandoverCondition(id, list2, value8));
			}
		}
		catch (Exception ex)
		{
			_logger.Warning("[Quest] GetHandoverConditions(" + questId + "): " + ex.Message, (Exception)null);
		}
		return list;
	}

	private void TryRemoveHandoverItems(object pmcData, string questId)
	{
		List<HandoverCondition> handoverConditions = GetHandoverConditions(questId);
		if (handoverConditions.Count == 0)
		{
			_logger.Warning("[Quest] No HandoverItem conditions for " + questId + "; skipping item removal", (Exception)null);
			return;
		}
		IList inventoryItemsList = GetInventoryItemsList(pmcData, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (inventoryItemsList == null)
		{
			_logger.Warning("[Quest] Cannot access inventory items for " + questId, (Exception)null);
			return;
		}
		foreach (HandoverCondition item in handoverConditions)
		{
			int needed = item.Value;
			RemoveItemsByTpl(inventoryItemsList, item.Targets, ref needed, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (needed > 0)
			{
				_logger.Warning($"[Quest] Condition {item.Id}: {needed} item(s) still missing after removal", (Exception)null);
			}
		}
	}

	private static IList? GetInventoryItemsList(object pmcData, BindingFlags all)
	{
		object obj = pmcData.GetType().GetProperty("Inventory", all)?.GetValue(pmcData) ?? pmcData.GetType().GetField("Inventory", all)?.GetValue(pmcData);
		if (obj == null)
		{
			return null;
		}
		return (obj.GetType().GetProperty("Items", all)?.GetValue(obj) ?? obj.GetType().GetField("Items", all)?.GetValue(obj)) as IList;
	}

	private static void RemoveItemsByTpl(IList items, List<string> targetTpls, ref int needed, BindingFlags all)
	{
		int num = items.Count - 1;
		while (num >= 0 && needed > 0)
		{
			object obj = items[num];
			if (obj != null)
			{
				string tpl = GetStrField(obj, "_tpl", "Tpl", "TemplateId");
				if (!string.IsNullOrEmpty(tpl) && targetTpls.Exists((string t) => string.Equals(t, tpl, StringComparison.OrdinalIgnoreCase)))
				{
					object objField = GetObjField(obj, "Upd", "upd");
					int num2 = 1;
					if (objField != null)
					{
						object objField2 = GetObjField(objField, "StackObjectsCount", "stackObjectsCount");
						if (objField2 != null)
						{
							num2 = Math.Max(1, Convert.ToInt32(objField2));
						}
					}
					if (num2 <= needed)
					{
						items.RemoveAt(num);
						needed -= num2;
					}
					else
					{
						SetObjField(objField, new string[2] { "StackObjectsCount", "stackObjectsCount" }, num2 - needed);
						needed = 0;
					}
				}
			}
			num--;
		}
	}

	private void TryApplySuccessRewards(object pmcData, string questId)
	{
		if (!VisitApiQuestLoader.QuestJsons.TryGetValue(questId, out string value))
		{
			return;
		}
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(value);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value2 = default(JsonElement);
			if ((rootElement.TryGetProperty("rewards", out var value3) || rootElement.TryGetProperty("Rewards", out value3)) && !value3.TryGetProperty("Success", out value2))
			{
				value3.TryGetProperty("success", out value2);
			}
			if (value2.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (JsonElement item in value2.EnumerateArray())
			{
				string text = "";
				if (item.TryGetProperty("type", out var value4))
				{
					text = value4.GetString() ?? "";
				}
				if (string.IsNullOrEmpty(text) && item.TryGetProperty("_parent", out var value5))
				{
					text = value5.GetString() ?? "";
				}
				JsonElement value6;
				JsonElement jsonElement = (item.TryGetProperty("_props", out value6) ? value6 : item);
				JsonElement value9;
				if (!(text == "Experience"))
				{
					if (text == "TraderStanding" && jsonElement.TryGetProperty("target", out var value7) && jsonElement.TryGetProperty("value", out var value8))
					{
						ApplyTraderStanding(pmcData, value7.GetString() ?? "", value8.GetDouble());
					}
				}
				else if (jsonElement.TryGetProperty("value", out value9))
				{
					long result;
					long xp = ((value9.ValueKind != JsonValueKind.String) ? ((long)value9.GetDouble()) : (long.TryParse(value9.GetString(), out result) ? result : 0));
					ApplyExperience(pmcData, xp);
				}
			}
		}
		catch (Exception ex)
		{
			_logger.Warning("[Quest] TryApplySuccessRewards(" + questId + "): " + ex.Message, (Exception)null);
		}
	}

	private void ApplyExperience(object pmcData, long xp)
	{
		object objField = GetObjField(pmcData, "Info", "info");
		if (objField == null)
		{
			return;
		}
		PropertyInfo property = objField.GetType().GetProperty("Experience", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property != null && property.CanWrite)
		{
			long num = Convert.ToInt64(property.GetValue(objField));
			Type conversionType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
			property.SetValue(objField, Convert.ChangeType(num + xp, conversionType));
			return;
		}
		FieldInfo field = objField.GetType().GetField("Experience", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (field != null)
		{
			long num2 = Convert.ToInt64(field.GetValue(objField));
			field.SetValue(objField, Convert.ChangeType(num2 + xp, field.FieldType));
		}
	}

	private void ApplyTraderStanding(object pmcData, string traderId, double delta)
	{
		if (string.IsNullOrEmpty(traderId))
		{
			return;
		}
		object objField = GetObjField(pmcData, "TradersInfo", "tradersInfo");
		if (objField == null)
		{
			return;
		}
		object obj = MakeMongoId(traderId);
		object obj2 = null;
		try
		{
			obj2 = objField.GetType().GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(objField, new object[1] { obj });
		}
		catch
		{
		}
		if (obj2 != null)
		{
			PropertyInfo property = obj2.GetType().GetProperty("Standing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite)
			{
				double num = Convert.ToDouble(property.GetValue(obj2));
				Type conversionType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
				property.SetValue(obj2, Convert.ChangeType(num + delta, conversionType));
			}
		}
	}

	private static string? GetStrField(object obj, params string[] names)
	{
		foreach (string name in names)
		{
			string text = (obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) as string) ?? (obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) as string);
			if (text != null)
			{
				return text;
			}
		}
		return null;
	}

	private static object? GetObjField(object obj, params string[] names)
	{
		foreach (string name in names)
		{
			object obj2 = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) ?? obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
			if (obj2 != null)
			{
				return obj2;
			}
		}
		return null;
	}

	private static void SetObjField(object obj, string[] names, object value)
	{
		foreach (string name in names)
		{
			PropertyInfo property = obj.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite)
			{
				property.SetValue(obj, Convert.ChangeType(value, property.PropertyType));
				break;
			}
			FieldInfo field = obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				field.SetValue(obj, Convert.ChangeType(value, field.FieldType));
				break;
			}
		}
	}

	// 状态文件：<mod目录>/<subdir>/<profileId>.json，内容为任务 ID 的 JSON 字符串数组
	private const string AcceptedStateSubdir = "quest_state";

	private const string CompletedStateSubdir = "quest_state_completed";

	private static string GetStateFile(string subdir, string profileId)
	{
		string dir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", subdir);
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, profileId + ".json");
	}

	private static List<string> LoadStateList(string subdir, string profileId)
	{
		try
		{
			string path = GetStateFile(subdir, profileId);
			if (!File.Exists(path))
			{
				return new List<string>();
			}
			return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path, Encoding.UTF8)) ?? new List<string>();
		}
		catch
		{
			return new List<string>();
		}
	}

	private static bool IsQuestAccepted(string profileId, string questId)
	{
		return LoadStateList(AcceptedStateSubdir, profileId).Contains(questId, StringComparer.OrdinalIgnoreCase);
	}

	private static bool IsQuestCompleted(string profileId, string questId)
	{
		return LoadStateList(CompletedStateSubdir, profileId).Contains(questId, StringComparer.OrdinalIgnoreCase);
	}

	private void SaveQuestState(string subdir, string profileId, string questId)
	{
		try
		{
			HashSet<string> ids = new HashSet<string>(LoadStateList(subdir, profileId), StringComparer.OrdinalIgnoreCase) { questId };
			File.WriteAllText(GetStateFile(subdir, profileId), JsonSerializer.Serialize(ids), Encoding.UTF8);
		}
		catch (Exception ex)
		{
			_logger.Warning("[QuestState] Save failed (" + subdir + "): " + ex.Message, (Exception)null);
		}
	}

	private void SaveQuestAccepted(string profileId, string questId)
	{
		SaveQuestState(AcceptedStateSubdir, profileId, questId);
	}

	private void SaveQuestCompleted(string profileId, string questId)
	{
		SaveQuestState(CompletedStateSubdir, profileId, questId);
	}

	private static List<QuestTransition> LoadQuestTransitions()
	{
		if (_transitionCache != null)
		{
			return _transitionCache;
		}
		try
		{
			string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".", "db", "quest_transitions.json");
			if (!File.Exists(path))
			{
				return _transitionCache = new List<QuestTransition>();
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
			List<QuestTransition> list = new List<QuestTransition>();
			foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
			{
				JsonElement value;
				string text = (item.TryGetProperty("triggerQuestId", out value) ? (value.GetString() ?? "") : "");
				JsonElement value2;
				string text2 = (item.TryGetProperty("dependentQuestId", out value2) ? (value2.GetString() ?? "") : "");
				JsonElement value3;
				int targetStatus = (item.TryGetProperty("targetStatus", out value3) ? value3.GetInt32() : 3);
				if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(text2))
				{
					list.Add(new QuestTransition(text, text2, targetStatus));
				}
			}
			return _transitionCache = list;
		}
		catch (Exception ex)
		{
			Console.WriteLine("[VisitAPI] LoadQuestTransitions: " + ex.Message);
			return _transitionCache = new List<QuestTransition>();
		}
	}

	private List<(string QuestId, int Status)> CollectAndApplyTransitions(string profileId, object pmcData)
	{
		List<QuestTransition> list = LoadQuestTransitions();
		List<(string, int)> list2 = new List<(string, int)>();
		foreach (QuestTransition item in list)
		{
			if (IsQuestCompleted(profileId, item.TriggerQuestId) && GetQuestStatusValue(pmcData, item.DependentQuestId) < item.TargetStatus)
			{
				SetQuestStatus(pmcData, item.DependentQuestId, item.TargetStatus);
				list2.Add((item.DependentQuestId, item.TargetStatus));
			}
		}
		if (list2.Count > 0)
		{
			SaveProfile(profileId);
		}
		return list2;
	}

	public Task<string> SyncQuestTransitionsAsync(string body)
	{
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(body);
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("ProfileId", out var value) && !rootElement.TryGetProperty("profileId", out value))
			{
				return Task.FromResult("{\"success\":false,\"error\":\"missing ProfileId\"}");
			}
			string text = value.GetString() ?? "";
			if (string.IsNullOrEmpty(text))
			{
				return Task.FromResult("{\"success\":false,\"error\":\"invalid ProfileId\"}");
			}
			object pmcData = GetPmcData(text);
			if (pmcData == null)
			{
				return Task.FromResult("{\"success\":false,\"error\":\"profile not found\"}");
			}
			List<(string, int)> list = CollectAndApplyTransitions(text, pmcData);
			return Task.FromResult($"{{\"success\":true,\"applied\":{list.Count}}}");
		}
		catch (Exception ex)
		{
			_logger.Error("SyncQuestTransitionsAsync: " + ex.Message, (Exception)null);
			return Task.FromResult("{\"success\":false,\"error\":\"" + Esc(ex.Message) + "\"}");
		}
	}
}
