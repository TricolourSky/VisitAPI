using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace VisitAPI.Native;

public static class DecalDraw
{
    static readonly Dictionary<Camera, CommandBuffer> Buffers = new();

    static readonly RenderTargetIdentifier[] Mrt =
    {
        BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.GBuffer1,
        BuiltinRenderTextureType.GBuffer2, BuiltinRenderTextureType.CameraTarget,
    };

    public static void Schedule()
    {
        if (!Plugin.DecalDirect.Value) { Plugin.Log.LogInfo("[narrate] 贴花直画: 关（0.16 自己的间接绘制，坑 #125）"); return; }
        Plugin.Instance.StartCoroutine(Run());
    }

    static IEnumerator Run()
    {
        yield return new WaitForSeconds(1f);
        var renderer = StaticDeferredDecalRenderer.Instance;
        if (!Narrating.Now || renderer == null) { Plugin.Log.LogWarning("[narrate] 贴花直画：访问已结束或场上没有贴花渲染器"); yield break; }
        DecalGuard.Clear();
        renderer.ClearCamerasData();
        var registered = 0;
        foreach (var d in Object.FindObjectsOfType<StaticDeferredDecal>()) { renderer.RegisterDecal(d, false); registered++; }
        renderer.UpdateInstancesBuffers();
        yield return new WaitForSeconds(1f);
        if (!Narrating.Now) yield break;
        Attach(renderer, EFT.CameraControl.CameraManager.Instance?.Camera, registered);
    }

    static void Attach(StaticDeferredDecalRenderer renderer, Camera cam, int registered)
    {
        var mesh = Traverse.Create(renderer).Field("_decalMesh").GetValue<Mesh>();
        if (cam == null || mesh == null) { Plugin.Log.LogWarning("[narrate] 贴花直画：没有访问相机或贴花网格"); return; }
        if (Buffers.TryGetValue(cam, out var old)) { cam.RemoveCommandBuffer(CameraEvent.BeforeReflections, old); old.Dispose(); }
        var cb = new CommandBuffer { name = "VisitAPI static decals direct" };
        cb.SetRenderTarget(Mrt, BuiltinRenderTextureType.CameraTarget);
        var draws = 0;
        foreach (var inst in renderer.GetDecalDrawInstancesArray())
            for (var i = 0; i < inst.Count; i++)
            {
                var block = new MaterialPropertyBlock();
                block.SetInt("decalID", inst.StartIndex + i);
                cb.DrawMesh(mesh, Matrix4x4.identity, inst.Material, 0, -1, block);
                draws++;
            }
        cam.AddCommandBuffer(CameraEvent.BeforeReflections, cb);
        Buffers[cam] = cb;
        Plugin.Log.LogInfo($"[narrate] 贴花直画：重注册 {registered} 个、{renderer.GetDecalDrawInstancesArray().Length} 批 → 相机 '{cam.name}' {draws} 次 DrawMesh（{cb.sizeInBytes}B，坑 #116）");
    }

    public static void Reset()
    {
        foreach (var pair in Buffers)
        {
            if (pair.Key != null) pair.Key.RemoveCommandBuffer(CameraEvent.BeforeReflections, pair.Value);
            pair.Value.Dispose();
        }
        Buffers.Clear();
    }
}
