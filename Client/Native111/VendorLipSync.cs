using System;
using System.Collections.Generic;
using UnityEngine;
using uLipSync;

namespace Cutscene;

#pragma warning disable 0649 // Unity 从 bundle 反序列化这些字段

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
