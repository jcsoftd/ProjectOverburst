using System;
using System.Collections.Generic;
using UnityEngine;

// Owned by one accepted melee execution, never by a global victim cooldown.
public sealed class EnemyWeakAttackReactionScope
{
    private readonly GameObject source;
    private readonly EnemyActor actor;
    private readonly int sequence;
    private readonly uint lease;
    private readonly bool hasActor;
    private bool active = true;
    private readonly HashSet<(CombatHealth health, uint lease)> contacts = new HashSet<(CombatHealth, uint)>();

    public EnemyWeakAttackReactionScope(GameObject source, int sequence, EnemyActor actor = null)
    {
        if (source == null || sequence <= 0 || actor != null && actor.gameObject != source)
            throw new ArgumentException("An attack reaction scope requires its exact source and sequence.");
        this.source = source; this.sequence = sequence; this.actor = actor; hasActor = actor != null;
        lease = actor != null ? actor.LeaseVersion : 0;
    }

    public bool TryConsume(GameObject damageSource, int damageSequence, CombatHealth target, float actualDamage)
    {
        if (!active || source == null || !source.activeInHierarchy || source != damageSource || sequence != damageSequence
            || hasActor && (actor == null || actor.LeaseVersion != lease) || target == null
            || actualDamage <= 0 || float.IsNaN(actualDamage) || float.IsInfinity(actualDamage)) return false;
        var targetActor = target.GetComponentInParent<EnemyActor>();
        return contacts.Add((target, targetActor != null ? targetActor.LeaseVersion : 0));
    }

    public void Cancel() { active = false; contacts.Clear(); }
}
