using System;
using UnityEngine;

// Optional per-material overrides. Old materials preserve their speed, damage and parry rules.
[Serializable]
public sealed class EnemyBossAttackTuning
{
    [Min(0f)] public float damageMultiplier = 1f;
    [Min(.01f)] public float animationSpeedMultiplier = 1f;
    public EnemyBossStrikeParryTuning[] parries = Array.Empty<EnemyBossStrikeParryTuning>();

    public bool AllowsParry(int phase) => parries == null || phase < 0 || phase >= parries.Length
        || parries[phase] == null || parries[phase].canParry;
    public bool WindowOpen(int phase, float progress, float remaining, float defaultLead)
    {
        if (!AllowsParry(phase)) return false;
        var entry = parries != null && phase >= 0 && phase < parries.Length ? parries[phase] : null;
        return entry != null && entry.overrideWindow
            ? progress >= entry.startNormalized && progress <= entry.endNormalized
            : remaining >= -.03f && remaining <= defaultLead;
    }
    public bool Validate(EnemyBossMaterialStrike[] strikes)
    {
        if (!EnemyBossMaterialStrike.Finite(damageMultiplier) || damageMultiplier < 0f
            || !EnemyBossMaterialStrike.Finite(animationSpeedMultiplier) || animationSpeedMultiplier < .01f) return false;
        if (parries == null || parries.Length == 0) return true;
        if (strikes == null || parries.Length != strikes.Length) return false;
        for (int i = 0; i < parries.Length; i++)
        {
            var p = parries[i];
            if (p == null) continue;
            if (!EnemyBossMaterialStrike.Finite(p.cueLeadSeconds) || p.cueLeadSeconds < 0f) return false;
            if (!p.overrideWindow) continue;
            if (!EnemyBossMaterialStrike.Finite(p.startNormalized) || !EnemyBossMaterialStrike.Finite(p.endNormalized)
                || p.startNormalized < 0f || p.startNormalized > p.endNormalized || strikes[i] == null
                || p.endNormalized > strikes[i].impact) return false;
        }
        return true;
    }
}

[Serializable]
public sealed class EnemyBossStrikeParryTuning
{
    public bool canParry = true;
    public bool overrideWindow;
    [Min(0f)] public float cueLeadSeconds;
    [Range(0f, 1f)] public float startNormalized;
    [Range(0f, 1f)] public float endNormalized = 1f;
}
