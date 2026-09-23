using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

#pragma warning disable 0169, 0436, 0649   // 字段由 Unity 反序列化赋值；AudioClipConfig 同名是为保留 1.1 第七字段

namespace Audio.ConfiguredAudioPlayer
{
	[Serializable]
	public class AudioClipConfig
	{
		public AudioClip clip;
		public float volume;
		public float pitch;
		public bool loop;
		public float fadeInTime;
		public float fadeOutTime;
		public float spatialBlend;
	}

	public class AudioClipConfigDictionary : MonoBehaviour
	{
		[SerializeField] private List<EFT.AnimationSequencePlayer.SerializableKeyValuePair<AudioClipConfig>> entries;

		private Dictionary<string, AudioClipConfig> _map;

		public bool TryGetValue(string key, out AudioClipConfig value)
		{
			if (_map == null)
			{
				_map = new Dictionary<string, AudioClipConfig>(StringComparer.Ordinal);
				if (entries != null)
				{
					foreach (var e in entries)
					{
						if (e != null && !string.IsNullOrEmpty(e.key)) _map[e.key] = e.value;
					}
				}
			}
			return _map.TryGetValue(key, out value);
		}

		public int Count => entries == null ? 0 : entries.Count;
	}
}

namespace NPC
{
	public class NPCCrossfadeAnimationSoundPlayer : MonoBehaviour
	{
		[SerializeField] private MonoBehaviour _animationsEventReceiver;
		[SerializeField] private Transform _transformForPlaySounds;
		[SerializeField] private MonoBehaviour _audioConfigDictionary;
		[SerializeField] private AudioMixerGroup _mixerGroup;

		private AudioSource[] _sources;
		private int _slot;
		private Coroutine _fade;

		private void OnEnable()
		{
			var r = _animationsEventReceiver as EFT.NPC.NPCAnimationsEventReceiver;
			if (r != null) r.OnNeedToPlaySomeSound += Play;
			var dict = _audioConfigDictionary as Audio.ConfiguredAudioPlayer.AudioClipConfigDictionary;
			VisitAPI.Plugin.Log.LogInfo($"[foley] crossfade '{name}' 上线: 事件接收器={(r != null ? "绑定" : "空!")} 配置表={(dict != null ? dict.Count.ToString() + " 条" : "空!")}");
		}

		private void OnDisable()
		{
			if (_animationsEventReceiver is EFT.NPC.NPCAnimationsEventReceiver r) r.OnNeedToPlaySomeSound -= Play;
		}

		private void Play(string soundID)
		{
			var dict = _audioConfigDictionary as Audio.ConfiguredAudioPlayer.AudioClipConfigDictionary;
			var hit = dict != null && !string.IsNullOrEmpty(soundID) && dict.TryGetValue(soundID, out var probe) && probe?.clip != null;
			if (hit) VisitAPI.Plugin.Log.LogInfo($"[foley] 动画音效事件 '{soundID}' → 命中");
			else VisitAPI.Plugin.Log.LogDebug($"[foley] 动画音效事件 '{soundID}' → 本表没有");
			if (dict == null || string.IsNullOrEmpty(soundID) || !dict.TryGetValue(soundID, out var cfg) || cfg?.clip == null)
			{
				return;
			}
			if (_sources == null) _sources = new[] { NewSource(), NewSource() };
			var next = _sources[_slot = 1 - _slot];
			next.clip = cfg.clip;
			next.pitch = cfg.pitch <= 0f ? 1f : cfg.pitch;
			next.loop = cfg.loop;
			next.spatialBlend = cfg.spatialBlend;
			next.volume = cfg.fadeInTime > 0f ? 0f : cfg.volume;
			next.Play();
			if (_fade != null) StopCoroutine(_fade);
			_fade = StartCoroutine(Crossfade(next, _sources[1 - _slot], cfg));
		}

		private AudioSource NewSource()
		{
			var go = new GameObject("VisitAPI_NpcSound");
			go.transform.SetParent(_transformForPlaySounds == null ? transform : _transformForPlaySounds, false);
			var src = go.AddComponent<AudioSource>();
			src.playOnAwake = false;
			src.outputAudioMixerGroup = VisitAPI.Native.AudioRouting.Resolve(_mixerGroup);
			return src;
		}

		private IEnumerator Crossfade(AudioSource inSrc, AudioSource outSrc, Audio.ConfiguredAudioPlayer.AudioClipConfig cfg)
		{
			var outVol = outSrc.volume;
			for (float t = 0f; t < Mathf.Max(cfg.fadeInTime, cfg.fadeOutTime); t += Time.unscaledDeltaTime)
			{
				if (cfg.fadeInTime > 0f) inSrc.volume = Mathf.Lerp(0f, cfg.volume, t / cfg.fadeInTime);
				if (cfg.fadeOutTime > 0f && outSrc.isPlaying) outSrc.volume = Mathf.Lerp(outVol, 0f, t / cfg.fadeOutTime);
				yield return null;
			}
			inSrc.volume = cfg.volume;
			outSrc.Stop();
			_fade = null;
		}
	}

	public class NPCSceneAudioController : MonoBehaviour
	{
		[SerializeField] private MonoBehaviour _ambientGroup;
		[SerializeField] private bool _enabledReverb;
		[SerializeField] private float _fadeInTime;
		[SerializeField] private float _fadeOutTime;

		private void OnEnable()
		{
			var g = _ambientGroup as Audio.AmbientSubsystem.AmbientSoundPlayerGroup;
			VisitAPI.Plugin.Log.LogInfo($"[ambient] 场景音总控上线: group={(g != null ? g.name + " players=" + g._soundPlayers.Count : "空!")}");
			if (g != null && !g.IsPlaying) g.Play();
		}

		private void OnDisable()
		{
			var g = _ambientGroup as Audio.AmbientSubsystem.AmbientSoundPlayerGroup;
			if (g != null && g.IsPlaying) g.Stop();
		}
	}
}
