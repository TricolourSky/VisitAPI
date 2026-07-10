using System;
using UnityEngine;

namespace VisitAPI.Native
{
    // In-world dialog actor (Lightkeeper-style): each dialog line can play one animation (`| anim: <state>`
    // / node-header `anim:`) — the same per-line pacing the native dialog uses (GInterface462
    // .ExecuteDialogOption -> NPCObject.PlayAction per NPC line). The target is the in-world model named by
    // the `actor:` header. Vanilla NPCObject needs an asset-authored SequenceReader, which modded models
    // don't have, so we drive the model's plain Animator by state name instead — any bundled
    // AnimatorController works, no EFT SDK needed.
    internal static class VisitNpcActor
    {
        private static string _boundName = "";
        private static Animator? _animator;

        internal static void Play(string? actorName, string? state)
        {
            if (string.IsNullOrEmpty(state)) return;
            if (string.IsNullOrEmpty(actorName)) return;
            Animator? animator = Resolve(actorName!);
            if (animator == null)
            {
                Plugin.Log.LogWarning("[NpcActor] actor '" + actorName + "' not found or has no Animator (anim '" + state + "')");
                return;
            }
            try
            {
                animator.CrossFadeInFixedTime(state, 0.25f);
                Plugin.Log.LogInfo("[NpcActor] '" + animator.gameObject.name + "' plays '" + state + "'");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NpcActor] play '" + state + "': " + ex.Message);
            }
        }

        private static Animator? Resolve(string name)
        {
            if (_animator != null && _boundName == name && _animator.gameObject.activeInHierarchy) return _animator;
            Transform? actor = VisitTrigger.FindNpcTransform(name);
            _animator = actor != null ? actor.GetComponentInChildren<Animator>(true) : null;
            _boundName = name;
            return _animator;
        }
    }
}
