using System;
using UnityEngine;

/// <summary>Selected monster sounds layered over the existing combat mix.</summary>
[DisallowMultipleComponent]
public sealed class EnemySoundEmitter : MonoBehaviour
{
    [Serializable]
    public struct Choice
    {
        public AudioClip clip;
        [Range(0f, .8f)] public float volume;
    }

    [SerializeField] private Choice[] attack = Array.Empty<Choice>();
    [SerializeField] private Choice[] hurt = Array.Empty<Choice>();
    [SerializeField] private Choice[] death = Array.Empty<Choice>();
    [SerializeField] private Choice[] melee = Array.Empty<Choice>();
    [SerializeField, Range(.7f, 1.3f)] private float pitch = 1f;
    private CombatHealth health;
    private int lastAttack = -1, lastHurt = -1, lastDeath = -1, lastMelee = -1;
    private float nextHurtAt, nextMeleeAt;
    private bool deadPlayed;

    private void OnEnable()
    {
        health = GetComponent<CombatHealth>();
        deadPlayed = false;
        nextHurtAt = nextMeleeAt = 0f;
        if (health == null) return;
        health.OnDamageResolved += Damaged;
        health.OnDead += Dead;
    }

    private void OnDisable()
    {
        if (health == null) return;
        health.OnDamageResolved -= Damaged;
        health.OnDead -= Dead;
    }

    public bool PlayAttack()
    {
        if (!isActiveAndEnabled || health != null && health.IsDead) return false;
        return Play(attack, ref lastAttack, transform.position);
    }

    private void Damaged(CombatHealth target, DamageInfo info, float actualDamage, bool lethal)
    {
        if (actualDamage <= 0f || lethal || info.isDamageOverTime || !info.triggersOnHitEffects
            || Time.time < nextHurtAt) return;
        if (Play(hurt, ref lastHurt, transform.position)) nextHurtAt = Time.time + .12f;
    }

    private void Dead(CombatHealth target, DamageInfo info)
    {
        // Audio uses the shared detached voices, so pooling cannot cut off the death cue.
        if (deadPlayed) return;
        deadPlayed = true;
        Play(death, ref lastDeath, transform.position);
    }

    public static void PlayConfirmedHit(DamageInfo info, float actualDamage)
    {
        if (actualDamage <= 0f || info.isDamageOverTime || !info.triggersOnHitEffects
            || info.enemyAbility == null || info.source == null
            || !info.source.TryGetComponent<EnemySoundEmitter>(out var emitter)
            || !emitter.isActiveAndEnabled || Time.time < emitter.nextMeleeAt) return;
        if (emitter.Play(emitter.melee, ref emitter.lastMelee, info.hitPoint))
            emitter.nextMeleeAt = Time.time + .06f;
    }

    private bool Play(Choice[] choices, ref int last, Vector3 position)
    {
        if (choices == null || choices.Length == 0) return false;
        int pick = UnityEngine.Random.Range(0, choices.Length);
        if (choices.Length > 1 && pick == last)
            pick = (pick + 1 + UnityEngine.Random.Range(0, choices.Length - 1)) % choices.Length;
        var choice = choices[pick];
        if (!CombatActionSfxService.PlayMonsterLayer(choice.clip, position, choice.volume, pitch)) return false;
        last = pick;
        return true;
    }
}
