using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using Path = System.IO.Path;

namespace VisitApiServer;

// Serve custom quest icons straight from this mod — no dropping files into SPT_Data. Drop an image into
// VisitAPI-Server/images/quest/, and a quest JSON references it as "/files/quest/icon/<filename-no-ext>.<ext>".
// On load we register one image route per file through SPT's ImageRouter: key = the request URL with its
// extension stripped (ImageRouter lowercases it), value = the absolute file path WITH extension (SPT serves that
// file and derives the MIME from its extension; the incoming request's extension is ignored after matching).
[Injectable]
public class VisitApiImageLoader : IOnLoad
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp" };

    private readonly ISptLogger<VisitApiImageLoader> _logger;
    private readonly ModHelper _modHelper;
    private readonly ImageRouter _imageRouter;

    public VisitApiImageLoader(ISptLogger<VisitApiImageLoader> logger, ModHelper modHelper, ImageRouter imageRouter)
    {
        _logger = logger;
        _modHelper = modHelper;
        _imageRouter = imageRouter;
    }

    public Task OnLoad()
    {
        try
        {
            string modFolder = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            string imagesDir = Path.Combine(modFolder, "images", "quest");
            if (!Directory.Exists(imagesDir))
            {
                _logger.Debug("[VisitAPI-Server] no images/quest folder; no custom quest icons to serve");
                return Task.CompletedTask;
            }

            int count = 0;
            foreach (string file in Directory.GetFiles(imagesDir))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(Extensions, ext) < 0) continue;
                string name = Path.GetFileNameWithoutExtension(file);
                // Author references this in the quest JSON image field, e.g. "/files/quest/icon/<name>.<ext>".
                string urlKey = "/files/quest/icon/" + name;
                _imageRouter.AddRoute(urlKey, file);
                _logger.Debug($"[VisitAPI-Server] quest icon '{name}{ext}' -> {urlKey}{ext}");
                count++;
            }
            _logger.Debug($"[VisitAPI-Server] registered {count} custom quest icon(s) from images/quest");
        }
        catch (Exception ex)
        {
            _logger.Error("[VisitAPI-Server] image loader error: " + ex);
        }
        return Task.CompletedTask;
    }
}
