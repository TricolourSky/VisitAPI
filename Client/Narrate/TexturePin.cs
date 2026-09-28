using UnityEngine;

namespace VisitAPI.Native;

public static class TexturePin
{
    static int _was = -1;

    public static void Apply()
    {
        if (_was >= 0) return;
        _was = QualitySettings.globalTextureMipmapLimit;
        QualitySettings.globalTextureMipmapLimit = 0;
    }

    public static void Restore()
    {
        if (_was < 0) return;
        QualitySettings.globalTextureMipmapLimit = _was;
        _was = -1;
    }
}
