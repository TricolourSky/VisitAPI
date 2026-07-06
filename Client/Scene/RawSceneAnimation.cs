using System;
using System.Collections.Generic;
using System.IO;
using EFT.AnimationSequencePlayer;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace VisitAPI.Scene
{
    // Raw 1.0.6 scene rebuilds keep the trader model + its AnimatorController, but every BSG
    // MonoBehaviour was stripped in the AssetRipper round-trip (encrypted metadata made the fields
    // undecodable) — including the SequenceReader + AnimationDictionary pair the native dialog engine
    // plays every line's gestures through. NPCObject.PlayAction null-checks its SequenceReader and
    // silently does nothing, so raw-pack visits open fine but the trader never moves (DEV_NOTES #35).
    // Rebuild both at runtime: the dictionary maps a dialogue.json animation key to the Animator
    // int+trigger that drives the matching controller state; anim_map.json (generated offline from
    // the controllers' transition conditions by _ripwork\gen_anim_map.ps1) supplies that mapping.
    internal static class RawSceneAnimation
    {
        private static JObject? _map;
        private static bool _loadTried;

        internal static void Prepare(Animator animator, string sceneName)
        {
            if (animator == null || animator.GetComponent<SequenceReader>() != null) return;
            string? ownerId = SceneAssets.TraderIdForVendorScene(sceneName);
            if (ownerId == null) return;
            if (!(LoadMap()?[ownerId] is JObject entries))
            {
                Plugin.Log.LogWarning("[RawSceneAnim] no entry for " + sceneName + " in anim_map.json — gestures stay off");
                return;
            }

            AnimationDictionary dict = animator.gameObject.AddComponent<AnimationDictionary>();
            int count = 0;
            foreach (KeyValuePair<string, JToken?> pair in entries)
            {
                if (!(pair.Value is JObject e)) continue;
                dict.Add(pair.Key, new AnimationElement
                {
                    triggerName = e.Value<string>("t") ?? "",
                    intName = e.Value<string>("i") ?? "",
                    intValue = e.Value<int?>("v") ?? 0,
                    exitTriggerName = e.Value<string>("x") ?? "",
                    exitTriggerOffset = e.Value<float?>("xo") ?? 0f,
                });
                count++;
            }
            // Add order matters: SequenceReader.Awake caches whatever components exist at that moment,
            // so the voice pieces (LipSyncDictionary + player) must be on the object before it.
            RawSceneVoice.Prepare(animator.gameObject, ownerId);
            animator.gameObject.AddComponent<SequenceReader>();
            Plugin.Log.LogInfo("[RawSceneAnim] rebuilt AnimationDictionary (" + count + " keys) + SequenceReader on " + animator.gameObject.name);
        }

        private static JObject? LoadMap()
        {
            if (_loadTried) return _map;
            _loadTried = true;
            string path = Path.Combine(SceneAssets.VendorsDir, "anim_map.json");
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning("[RawSceneAnim] anim_map.json missing: " + path + " — raw-pack trader gestures disabled");
                return null;
            }
            try
            {
                _map = JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[RawSceneAnim] anim_map.json: " + ex.Message);
            }
            return _map;
        }
    }
}
