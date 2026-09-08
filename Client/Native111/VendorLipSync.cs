using System;
using System.Collections.Generic;
using UnityEngine;
using uLipSync;

namespace Cutscene;

#pragma warning disable 0649 // Unity 从 bundle 反序列化这些字段

/// <summary>1.1 独有：只放音频、不驱动口型的台词数据（Fence 全部 105 句都是它，09-06 查明——之前打包时被当成 0.16 接不住的类整批剥掉，Fence 无声）。
/// 1.1 里它就是 BakedData 的空壳子类；uLipSyncBakedDataPlayer 当普通 BakedData 播：有 audioClip、没音素数据 → 声音照放、嘴不动，正是 1.1 的意图。
/// bundle 里的资产按 (VisitAPI 程序集, Cutscene, BakedDataAudioOnly) 绑到这里；SDK 桩在 IsolatedSDK\…\_VisitStubs\BakedDataAudioOnly.cs（同 guid）。</summary>
public class BakedDataAudioOnly : BakedData
{
}

public class ULipSyncCurvesPlayer : MonoBehaviour
{
    [SerializeField] MonoBehaviour _player;
    [SerializeField] MonoBehaviour _blendShape;
    [SerializeField] Animator _animator;
    [SerializeField] float _minCurvesDuration;
    SkinnedMeshRenderer _mesh;
    BakedData _last;
    BakedDataWithCurves.CurveData[] _curves = Array.Empty<BakedDataWithCurves.CurveData>();
    readonly Dictionary<string, int> _hashes = new();
    bool _playing;
    uLipSyncBakedDataPlayer Player => _player as uLipSyncBakedDataPlayer;
    public float Volume { get => Player == null ? 0f : Player.volume; set { if (Player != null) Player.volume = value; } }

    void Awake()
    {
        if (_blendShape is not uLipSyncBlendShape blend) return;
        _mesh = blend.skinnedMeshRenderer;
        // ⚠️ 这里绝不能无条件把 blend.updateMethod 抢成 External：Prapor 全部台词是**普通 BakedData（无曲线）**，
        // 要靠 uLipSync 自己的事件路径驱动嘴型；抢了=全部台词嘴被锁死（2026-09-02 大修实证）。接管改到 Cache 按数据类型决定。
    }
    void OnDisable() => Stop();
    void Update()
    {
        var p = Player;
        if (p == null) return;
        if (!p.isPlaying) { if (_playing) Stop(); return; }
        if (!_playing || _last != p.bakedData) { Cache(p.bakedData); _playing = true; }
        if (p.audioSource != null && _last != null && _last.duration > 0f) Apply(p.audioSource.time / _last.duration, _last.duration);
    }
    public void Play(BakedData data) { Player?.Play(data); Cache(data); _playing = true; }
    public void Stop() { if (Player?.isPlaying == true) Player.Stop(); ResetCurves(); _playing = false; }
    void Cache(BakedData data)
    {
        _last = data;
        _curves = (data as BakedDataWithCurves)?.curves?.ToArray() ?? Array.Empty<BakedDataWithCurves.CurveData>();
        // 带曲线的数据才接管（External）；普通 BakedData 交还 uLipSync 默认路径（LateUpdate）驱动嘴型
        if (_blendShape is uLipSyncBlendShape blend) blend.updateMethod = _curves.Length > 0 ? UpdateMethod.External : UpdateMethod.LateUpdate;
    }
    void Apply(float time, float duration)
    {
        if (duration < _minCurvesDuration) return;
        foreach (var c in _curves) if (c.isBlendShape) _mesh?.SetBlendShapeWeight(c.blendShapeIndex, c.curve.Evaluate(time * duration)); else _animator?.SetFloat(Hash(c.paramName), c.curve.Evaluate(time * duration));
    }
    void ResetCurves() { foreach (var c in _curves) if (c.isBlendShape) _mesh?.SetBlendShapeWeight(c.blendShapeIndex, c.resetValue); else _animator?.SetFloat(Hash(c.paramName), c.resetValue); }
    int Hash(string name) { if (!_hashes.TryGetValue(name, out var hash)) _hashes[name] = hash = Animator.StringToHash(name); return hash; }
}
