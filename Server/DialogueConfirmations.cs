using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace VisitAPI.Server;

public static class DialogueConfirmations
{
    static readonly Dictionary<string, string> _byLine = new(StringComparer.Ordinal);

    public static int Count => _byLine.Count;

    public static void Scan(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj.TryGetPropertyValue("ConfirmationKey", out var keyNode) && keyNode is JsonValue keyValue
                    && keyValue.TryGetValue<string>(out var key) && !string.IsNullOrEmpty(key)
                    && obj.TryGetPropertyValue("Id", out var idNode) && idNode is JsonValue idValue
                    && idValue.TryGetValue<string>(out var id) && id.Length == 24)
                    _byLine[id] = key;
                foreach (var (_, child) in obj) Scan(child);
                break;
            case JsonArray arr:
                foreach (var child in arr) Scan(child);
                break;
        }
    }

    public static Dictionary<string, string> Payload() => new(_byLine, StringComparer.Ordinal);
}
