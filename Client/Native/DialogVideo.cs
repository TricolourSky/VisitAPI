using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace VisitAPI.Native;

public static class DialogVideo
{
    public static void Play(RawImage image, string path, bool loop)
    {
        Stop(image);
        if (image.texture is Texture2D old) { image.texture = null; Object.Destroy(old); }
        var rt = new RenderTexture(1920, 1080, 0);
        var player = image.gameObject.AddComponent<VideoPlayer>();
        player.source = VideoSource.Url;
        player.url = "file:///" + path.Replace('\\', '/');
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = rt;
        player.isLooping = loop;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.Play();
        image.texture = rt;
    }

    public static void Stop(RawImage image)
    {
        if (image == null) return;
        var player = image.GetComponent<VideoPlayer>();
        if (player == null) return;
        player.Stop();
        Object.Destroy(player);
        if (image.texture is RenderTexture rt) { image.texture = null; rt.Release(); Object.Destroy(rt); }
    }
}
