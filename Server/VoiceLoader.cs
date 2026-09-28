using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Routers;
using VisitAPI.Packs;
using Path = System.IO.Path;

namespace VisitAPI.Server;

/// <summary>09-24 「回复」接对话：没有房间的商人（Kerman 等）的台词语音不在 0.16.9 的场景包里，从 1.1 的口型包抠出来放在内容包
/// `voice\&lt;商人id&gt;\&lt;lipSyncId&gt;.ogg|.wav`，这里按 `/files/visitapi/voice/&lt;商人id&gt;/&lt;lipSyncId&gt;` 登记给 ImageRouter
/// （它按去掉扩展名的小写路径找文件、按文件扩展名给 MIME；客户端拿到字节后自己认 OggS / RIFF，所以请求用什么扩展名都行）。</summary>
[Injectable(InjectionType.Transient, 1000000)]
public class VoiceLoader(ImageRouter images, ISptLogger<VoiceLoader> log) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var p in ContentPacks.All(log))
        {
            var root = p.IsLegacy ? Path.Combine(p.Folder, PackLayout.LegacyDb, "voice") : Path.Combine(p.Folder, "voice");
            if (!Directory.Exists(root)) continue;
            foreach (var traderDir in Directory.GetDirectories(root).OrderBy(d => d, System.StringComparer.OrdinalIgnoreCase))
            {
                var trader = Path.GetFileName(traderDir);
                foreach (var file in Directory.GetFiles(traderDir).OrderBy(f => f, System.StringComparer.OrdinalIgnoreCase))
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext != ".ogg" && ext != ".wav" && ext != ".mp3") continue;
                    images.AddRoute($"/files/visitapi/voice/{trader}/{Path.GetFileNameWithoutExtension(file)}", file);
                }
            }
        }
        return Task.CompletedTask;
    }
}
