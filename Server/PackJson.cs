using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VisitAPI.Packs;

namespace VisitAPI.Server;

/// <summary>包里手写的 JSON 宽松地读：允许尾逗号和注释。</summary>
static class PackJson
{
    static readonly JsonDocumentOptions Options = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static JsonDocument Parse(string file) => JsonDocument.Parse(File.ReadAllBytes(file), Options);

    /// <summary>各包 kind 子目录下的 *.json 逐个解析，交给 each(标签「包名/文件名」, 根元素)。
    /// 读不动、或 each 里抛了异常，这个文件就此跳过，交给 failed 报（各处报法不一样）；failed 为 null = 不吭声（VariableGroups）。</summary>
    public static void Read(IEnumerable<PackInfo> packs, string kind, Action<string, JsonElement> each, Action<string, Exception> failed)
    {
        foreach (var p in packs)
            foreach (var file in PackLayout.DataFiles(p, kind))
            {
                var label = p.Name + "/" + Path.GetFileName(file);
                try
                {
                    using var doc = Parse(file);
                    each(label, doc.RootElement);
                }
                catch (Exception e) { failed?.Invoke(label, e); }
            }
    }
}
