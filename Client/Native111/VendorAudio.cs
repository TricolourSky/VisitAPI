using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

// ══ 1.1 独有的 NPC 音频类，0.16 没有 → 按 Memory.MD §6.2.0「套不上就重写」在插件里实现 ══
//
// 机制（DEV_NOTES #69 的 VISIT_STUBS 同款）：SDK 那边生成**带 asmdef 的 `VisitAPI` 程序集**空壳桩，
// Unity 按（程序集名, 命名空间, 类名）存进 bundle；运行时 `VisitAPI` 就是本插件（AssemblyName=VisitAPI），
// 于是数据直接绑到下面这几个真类上。
//
// ⚠️ 铁律：**字段名/类型/顺序必须和桩逐字一致**（`tools\VendorScene` 自动生成的那份），
//    差一个，那个字段的数据在打包时就静默没了。改这里之前先看 `_VisitStubs\*.cs`。
//
// 关于 CS0436（`AudioClipConfig` 和游戏里的同名类冲突）：**这是故意的，别改成用游戏那份。**
// bundle 里的数据形状是按 SDK 桩 `_AudioClipConfig.cs`（1.1 的 7 个字段）打的，
// 只有我们自己这份能保证逐字段对上；游戏 0.16 那份字段集未必一样，一旦不一样数据就整体错位。
#pragma warning disable 0169, 0436, 0649   // 字段由 Unity 反序列化赋值；AudioClipConfig 同名是为保留 1.1 第七字段

namespace Audio.ConfiguredAudioPlayer
{
	/// <summary>一条音效的播放参数（1.1 原样：7 个字段）。</summary>
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

	/// <summary>
	/// 1.1 里是 `SerializedDictionary&lt;AudioClipConfig&gt;` 的子类，**是个挂在物体上的组件**（不是内联数据）。
	/// 这里只保留「按 soundID 查参数」这一件事 —— 播放交给 <see cref="NPC.NPCCrossfadeAnimationSoundPlayer"/>。
	/// </summary>
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
	/// <summary>
	/// 1.1 商人台词/音效的播放器。驱动链：
	/// Animator 上的音效事件 → `NPCAnimationsEventReceiver.OnNeedToPlaySomeSound(soundID)`（这个 0.16 有）
	/// → 这里查 <see cref="Audio.ConfiguredAudioPlayer.AudioClipConfigDictionary"/> 拿参数 → 两个源交叉淡入淡出地播。
	/// </summary>
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
			// 未命中降 Debug：两个播放器（音效/音乐）都订阅同一事件流、各查各的表，miss 属常态噪音
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
			// _mixerGroup 是随包进来的 1.1 混音台拷贝，直接挂=静音；解析成游戏主混音台上的同名组
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

	/// <summary>
	/// 1.1 商人房间的场景音频总控。1.1 原版还管 Meta XR 的混响/声学模型 ——
	/// **那套我们没有**（Meta XR Audio SDK 在独立 DLL 里，M4i 已拍板砍掉），所以 `_enabledReverb` 不用。
	/// 这里只留真正出声的那件事：把环境音组（`AmbientSoundPlayerGroup`，0.16 自己有）打开。
	/// </summary>
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
