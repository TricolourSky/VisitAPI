using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Services.Mod;

namespace VisitAPI.Server;

[Injectable(/*Could not decode attribute arguments.*/)]
public class VisitApiQuestLoader : IOnLoad
{
	private sealed class ListOrTConverterFactory : JsonConverterFactory
	{
		public override bool CanConvert(Type t)
		{
			if (t.IsGenericType)
			{
				return t.Name.StartsWith("ListOrT", StringComparison.Ordinal);
			}
			return false;
		}

		public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
		{
			return (JsonConverter)Activator.CreateInstance(typeof(ListOrTConverter<>).MakeGenericType(typeToConvert));
		}
	}

	private sealed class ListOrTConverter<T> : JsonConverter<T>
	{
		private static readonly Type _elemType;

		private static readonly ConstructorInfo? _listCtor;

		private static readonly ConstructorInfo? _defaultCtor;

		private static readonly MethodInfo? _addMethod;

		static ListOrTConverter()
		{
			Type typeFromHandle = typeof(T);
			_elemType = (typeFromHandle.IsGenericType ? typeFromHandle.GetGenericArguments()[0] : typeof(object));
			Type type = typeof(List<>).MakeGenericType(_elemType);
			_listCtor = typeFromHandle.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[2] { type, _elemType }, null);
			_defaultCtor = typeFromHandle.GetConstructor(System.Type.EmptyTypes);
			_addMethod = typeFromHandle.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { _elemType }, null);
		}

		public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			bool flag = reader.TokenType == JsonTokenType.StartArray;
			IList list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_elemType));
			if (flag)
			{
				while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
				{
					object obj = JsonSerializer.Deserialize(ref reader, _elemType, options);
					if (obj != null)
					{
						list.Add(obj);
					}
				}
			}
			else if (reader.TokenType != JsonTokenType.Null)
			{
				object obj2 = JsonSerializer.Deserialize(ref reader, _elemType, options);
				if (obj2 != null)
				{
					list.Add(obj2);
				}
			}
			if (_listCtor != null)
			{
				object obj3 = ((!flag && list.Count == 1) ? list[0] : null);
				try
				{
					return (T)_listCtor.Invoke(new object[2] { list, obj3 });
				}
				catch
				{
				}
			}
			if (_defaultCtor != null && _addMethod != null)
			{
				T val = (T)_defaultCtor.Invoke(null);
				{
					foreach (object item in list)
					{
						_addMethod.Invoke(val, new object[1] { item });
					}
					return val;
				}
			}
			return default(T);
		}

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		{
			if (value is IEnumerable enumerable)
			{
				writer.WriteStartArray();
				foreach (object item in enumerable)
				{
					JsonSerializer.Serialize(writer, item, _elemType, options);
				}
				writer.WriteEndArray();
			}
			else
			{
				writer.WriteNullValue();
			}
		}
	}

	private sealed class RuntimeMongoIdConverterFactory : JsonConverterFactory
	{
		private readonly Type _mongoIdType;

		public RuntimeMongoIdConverterFactory(Type t)
		{
			_mongoIdType = t;
		}

		public override bool CanConvert(Type t)
		{
			return t == _mongoIdType;
		}

		public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
		{
			return (JsonConverter)Activator.CreateInstance(typeof(MongoIdConverter<>).MakeGenericType(typeToConvert), _mongoIdType);
		}
	}

	private sealed class MongoIdConverter<T> : JsonConverter<T>
	{
		private readonly ConstructorInfo? _ctor;

		private readonly MethodInfo? _imp;

		public MongoIdConverter(Type mongoIdType)
		{
			_ctor = mongoIdType.GetConstructor(new Type[1] { typeof(string) });
			_imp = mongoIdType.GetMethod("op_Implicit", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(string) }, null);
		}

		public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType == JsonTokenType.Null)
			{
				return default(T);
			}
			string text = reader.GetString() ?? "";
			try
			{
				if (_ctor != null)
				{
					return (T)_ctor.Invoke(new object[1] { text });
				}
			}
			catch
			{
			}
			try
			{
				if (_imp != null)
				{
					return (T)_imp.Invoke(null, new object[1] { text });
				}
			}
			catch
			{
			}
			return default(T);
		}

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		{
			object obj = value?.ToString();
			if (obj == null)
			{
				obj = "";
			}
			writer.WriteStringValue((string?)obj);
		}
	}

	public static readonly HashSet<string> RegisteredQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public static readonly Dictionary<string, string> QuestJsons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private readonly ISptLogger<VisitApiQuestLoader> _logger;

	private readonly CustomQuestService _questService;

	private readonly ImageRouter _imageRouter;

	public VisitApiQuestLoader(ISptLogger<VisitApiQuestLoader> logger, CustomQuestService questService, ImageRouter imageRouter)
	{
		_logger = logger;
		_questService = questService;
		_imageRouter = imageRouter;
	}

	public Task OnLoad()
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		RegisterCustomQuestImages(directoryName);
		LoadQuestsAndLocales(directoryName);
		return Task.CompletedTask;
	}

	private void RegisterCustomQuestImages(string modDir)
	{
		string imagesDir = Path.Combine(modDir, "images", "quest");
		Directory.CreateDirectory(imagesDir);

		string[] files = Directory.GetFiles(imagesDir, "*.*");
		int count = 0;
		foreach (string file in files)
		{
			string ext = Path.GetExtension(file).ToLowerInvariant();
			if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;

			string name = Path.GetFileNameWithoutExtension(file);
			// URL format: /visitapi/image/<name>  (no extension, matching SPT ImageRouter convention)
			_imageRouter.AddRoute("/visitapi/image/" + name, file);
			count++;
		}

		if (count > 0)
			_logger.Success($"[VisitAPI] Registered {count} custom quest image(s) from {imagesDir}", null);
	}

	private void LoadQuestsAndLocales(string modDir)
	{
		string text = Path.Combine(modDir, "db", "quests");
		string localeDir = Path.Combine(modDir, "db", "locales");
		if (!Directory.Exists(text))
		{
			_logger.Warning("[VisitAPI] Quest directory not found: " + text, (Exception)null);
			return;
		}
		Type typeFromHandle = typeof(NewQuestDetails);
		PropertyInfo property = typeFromHandle.GetProperty("NewQuest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (property == null)
		{
			_logger.Warning("[VisitAPI] NewQuestDetails.NewQuest property not found", (Exception)null);
			return;
		}
		Type propertyType = property.PropertyType;
		JsonSerializerOptions options = BuildJsonOptions(FindMongoIdType(propertyType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
		Dictionary<string, Dictionary<string, string>> dictionary = LoadLocales(localeDir);
		int num = 0;
		string[] files = Directory.GetFiles(text, "*.json");
		foreach (string path in files)
		{
			try
			{
				string text2 = File.ReadAllText(path, Encoding.UTF8);
				object obj = JsonSerializer.Deserialize(text2, propertyType, options);
				if (obj == null)
				{
					_logger.Warning("[VisitAPI] Deserialize returned null: " + Path.GetFileName(path), (Exception)null);
					continue;
				}
				NewQuestDetails val = BuildNewQuestDetails(typeFromHandle, property, obj, dictionary, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (val == (NewQuestDetails)null)
				{
					continue;
				}
				CreateQuestResult val2 = _questService.CreateQuest(val);
				if (val2 != null && val2.Success)
				{
					Dictionary<string, Dictionary<string, string>> dictionary2 = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
					foreach (KeyValuePair<string, Dictionary<string, string>> item in dictionary)
					{
						dictionary2[item.Key] = item.Value;
					}
					if (dictionary.TryGetValue("zh-cn", out var value))
					{
						dictionary2.TryAdd("ch", value);
					}
					((object)_questService).GetType().GetMethod("AddQuestLocales", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_questService, new object[2] { dictionary2, val2 });
				}
				string text3 = GetQuestId(obj, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? Path.GetFileNameWithoutExtension(path);
				RegisteredQuestIds.Add(text3);
				QuestJsons[text3] = text2;
				num++;
			}
			catch (Exception ex)
			{
				_logger.Warning("[VisitAPI] Error loading " + Path.GetFileName(path) + ": " + ex.Message, (Exception)null);
			}
		}
	}

	private NewQuestDetails? BuildNewQuestDetails(Type nqdType, PropertyInfo questProp, object quest, Dictionary<string, Dictionary<string, string>> locales, BindingFlags all)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Expected O, but got Unknown
		try
		{
			NewQuestDetails val = (NewQuestDetails)FormatterServices.GetUninitializedObject(nqdType);
			questProp.GetSetMethod(nonPublic: true)?.Invoke(val, new object[1] { quest });
			nqdType.GetProperty("Locales", all)?.GetSetMethod(nonPublic: true)?.Invoke(val, new object[1] { locales });
			return val;
		}
		catch (Exception ex)
		{
			_logger.Warning("[VisitAPI] BuildNewQuestDetails failed: " + ex.Message, (Exception)null);
			return null;
		}
	}

	private static Dictionary<string, Dictionary<string, string>> LoadLocales(string localeDir)
	{
		Dictionary<string, Dictionary<string, string>> dictionary = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
		if (!Directory.Exists(localeDir))
		{
			return dictionary;
		}
		string[] files = Directory.GetFiles(localeDir, "*.json");
		foreach (string path in files)
		{
			string key = Path.GetFileNameWithoutExtension(path).ToLower();
			try
			{
				Dictionary<string, string> dictionary2 = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8));
				if (dictionary2 != null)
				{
					dictionary[key] = dictionary2;
				}
			}
			catch
			{
			}
		}
		return dictionary;
	}

	private static string? GetQuestId(object quest, BindingFlags all)
	{
		Type type = quest.GetType();
		string[] array = new string[3] { "_id", "Id", "QuestId" };
		foreach (string name in array)
		{
			object obj = type.GetProperty(name, all)?.GetValue(quest) ?? type.GetField(name, all)?.GetValue(quest);
			if (obj is string text && !string.IsNullOrEmpty(text))
			{
				return text;
			}
			if (obj != null)
			{
				return obj.ToString();
			}
		}
		return null;
	}

	private static Type? FindMongoIdType(Type questType, BindingFlags all)
	{
		PropertyInfo[] properties = questType.GetProperties(all);
		foreach (PropertyInfo propertyInfo in properties)
		{
			Type propertyType = propertyInfo.PropertyType;
			if (!(propertyType == typeof(string)) && !propertyType.IsPrimitive && !propertyType.IsEnum && (propertyInfo.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) || propertyInfo.Name.Equals("_id", StringComparison.OrdinalIgnoreCase)))
			{
				return propertyType;
			}
		}
		return null;
	}

	private static JsonSerializerOptions BuildJsonOptions(Type? mongoIdType)
	{
		JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			IncludeFields = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
		jsonSerializerOptions.Converters.Add(new ListOrTConverterFactory());
		if (mongoIdType != null && mongoIdType != typeof(string))
		{
			jsonSerializerOptions.Converters.Add(new RuntimeMongoIdConverterFactory(mongoIdType));
		}
		return jsonSerializerOptions;
	}
}
