using System;
using UnityEngine;

[Serializable]
public struct HitReactionData
{
    public bool overridesTargetDefaults;
    [Min(0f)] public float hitStunDuration;
    [Min(0f)] public float knockbackReactionDuration;

    public HitReactionData(
        bool overridesTargetDefaults,
        float hitStunDuration,
        float knockbackReactionDuration)
    {
        this.overridesTargetDefaults = overridesTargetDefaults;
        this.hitStunDuration = Mathf.Max(0f, hitStunDuration);
        this.knockbackReactionDuration = Mathf.Max(0f, knockbackReactionDuration);
    }
}
