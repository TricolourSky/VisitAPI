using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace VisitAPI.Native;

/// <summary>
/// 静态贴花在 0.16 上一个像素都画不出来（坑 #116，09-04 G-buffer 逐层截图 + 二分实证）：
/// `StaticDeferredDecalRenderer.UpdateBuffer` 把每批贴花用 `DrawMeshInstancedIndirect` 录进 "Static decals begin"，
/// 命令发了、贴图数组 / 参数缓冲 / shader 全齐，贴花后的 G-buffer0 与贴花前逐像素相同；
/// 同样的材质、同样的目标，改成**逐个 DrawMesh**（decalID 走属性块）喷字 / 污渍 / 旧化全部出来。
/// 这是 1.1 那间房「暗、脏、有层次」的主要来源，缺了它整屋就是干净发亮没细节——SORA 一直说的「发白」。
///
/// 修法必须逐条照抄验证过的那一档，前四版各差一处、全空白：
///   ① 追加进原方法自己的 begin 缓冲 → 空白；② 每帧摘掉重挂（排到 end 之后，_NormalsCopy 已释放）→ 空白；
///   ③ 自己 GetTemporaryRT 补一份 _NormalsCopy → 空白；④ 只挂一次但沿用缓存属性块 → 空白。
/// 现在这版 = 二分实验 O 档原样：进场就位后一次性录一条缓冲（每个贴花一个新属性块），挂上再不碰。
/// 原方法每帧摘挂自己的 begin/end，于是这条被压在最前面执行，读上一帧留下的 `_NormalsCopy`——实机成立。
/// 原来的间接绘制留着不动（无害）。只在原生模式访问期挂，退出即摘。
/// </summary>
public static class DecalDraw
{
    static readonly Dictionary<Camera, CommandBuffer> Buffers = new();

    static readonly RenderTargetIdentifier[] Mrt =
    {
        BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.GBuffer1,
        BuiltinRenderTextureType.GBuffer2, BuiltinRenderTextureType.CameraTarget,
    };

    /// 房间场景就位后排一次：贴花注册表重建 → 录绘制缓冲。
    /// ⚠️ **默认关，坑 #125**：上面那段立论的 G-buffer 对比是在**法线被剥光**的包上做的。法线补回来（#122）之后
    /// 0.16 自己的间接绘制就画得出贴花，这条直画等于同一批贴花画两遍——09-05 SORA 实机对照 1.1 正式版：
    /// 关掉才对得上。代码留着当 A/B 底档，别再默认打开。
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
        // 重建一次注册表：验证过的那一档是在「清表 → 全部重注册 → 重建实例」之后画出来的，照做（贴花数 44 / 5 批）。
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

    /// 退出访问：摘掉缓冲（相机本身随访问销毁）
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
