using System.Collections.Generic;
using UnityEngine;

// Original controller slots define roles; the replacement clip defines length and appearance.
public static class EnemyAnimationRoleResolver
{
    public static void ResolveParryDurations(RuntimeAnimatorController controller, EnemyAnimationProfile profile,
        out float collapse, out float loop, out float recover)
    {
        collapse = profile != null && profile.ParryCollapse != null ? profile.ParryCollapse.length : 0f;
        loop = profile != null && profile.StunnedLoop != null ? profile.StunnedLoop.length : 0f;
        recover = profile != null && profile.StunRecover != null ? profile.StunRecover.length : 0f;
        foreach (var role in GetRoleClips(controller))
        {
            if (collapse <= 0f && role.Key.name.EndsWith("_ParryCollapse")) collapse = role.Value.length;
            else if (loop <= 0f && role.Key.name.EndsWith("_StunnedLoop")) loop = role.Value.length;
            else if (recover <= 0f && role.Key.name.EndsWith("_StunRecover")) recover = role.Value.length;
        }
    }
    public static IEnumerable<KeyValuePair<AnimationClip, AnimationClip>> GetRoleClips(RuntimeAnimatorController controller)
    {
        if (controller == null) yield break;
        if (controller is AnimatorOverrideController overrides)
        {
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(pairs);
            foreach (var pair in pairs) if (pair.Key != null)
                yield return new KeyValuePair<AnimationClip, AnimationClip>(pair.Key, pair.Value != null ? pair.Value : pair.Key);
        }
        else foreach (var clip in controller.animationClips) if (clip != null)
            yield return new KeyValuePair<AnimationClip, AnimationClip>(clip, clip);
    }
}
