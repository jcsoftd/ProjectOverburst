using System;
using System.Collections.Generic;
using UnityEngine;

// Authored diamond floor, boundaries and markers. Monsters retain the production attack/parry executors.
public sealed class TutorialArena : MonoBehaviour
{
    public Transform playerSpawn, moveTarget;
    public Material skyMaterial;
    public Transform[] targets;
    public EnemyPoolService pool;
    public EnemyDefinition definition;
    public EnemyAbilityDefinition strongAbility;
    private readonly List<EnemyActor> actors = new List<EnemyActor>();
    private EnemyAbilitySet practiceSet;
    private Transform player;
    private float nextAttack;
    private int turn;
    public bool AttacksEnabled { get; set; }
    public IReadOnlyList<EnemyActor> Actors => actors;
    public event Action<CombatHealth, DamageInfo, float, bool> Hit;
    public event Action<CombatHealth, DamageInfo> Defeated;

    public void Prepare(Transform target, int count, bool combat)
    {
        ClearActors(); player = target;
        if (!practiceSet)
        {
            practiceSet = ScriptableObject.CreateInstance<EnemyAbilitySet>();
            practiceSet.name = "TutorialPractice";
            practiceSet.Configure(practiceSet.name, new[] { strongAbility });
        }
        for (int i = 0; i < count; i++)
        {
            var point = targets[i];
            var actor = pool.Acquire(definition);
            if (!actor) throw new InvalidOperationException("훈련 적을 준비하지 못했습니다.");
            actors.Add(actor);
            actor.transform.SetParent(transform, false);
            actor.transform.SetPositionAndRotation(point.position, point.rotation);
            actor.transform.localScale = Vector3.one;
            var request = new EnemySpawnRequest(definition, point.position, point.rotation, player,
                gameObject, point, transform, context: new EncounterContext(null, 1, ItemGrade.Common, rewardsAllowed: false));
            if (!actor.PrepareForLease(definition, definition.ResolveRuntimeStats(), request))
                throw new InvalidOperationException("훈련 적 초기화에 실패했습니다.");
            actor.ConfigureRunFallGuard(point.position);
            actor.gameObject.SetActive(true);
            if (!actor.FinalizeLeaseAfterActivation()) throw new InvalidOperationException("훈련 적 활성화에 실패했습니다.");
            actor.AI.enabled = false;
            Disable<EnemySensor>(actor); Disable<EnemyCrowdAgent>(actor);
            Disable<EnemyThemeSpecialExecutor>(actor); Disable<EnemyLootDropper>(actor);
            Disable<EnemyOverheadHpBar>(actor); Disable<EnemyTargetHpReporter>(actor);
            actor.Movement.StopMovement();
            actor.Health.SetMaxHp(combat ? 120f : 1000000f, true);
            actor.Health.ResetHealth();
            actor.AbilityController.Configure(practiceSet, 1f, 1f);
            actor.Health.OnDamageResolved += OnHit;
            actor.Health.OnDead += OnDefeated;
        }
        nextAttack = Time.time + 2f; turn = 0;
    }
    private void OnHit(CombatHealth health, DamageInfo info, float damage, bool dead) => Hit?.Invoke(health, info, damage, dead);
    private void OnDefeated(CombatHealth health, DamageInfo info) => Defeated?.Invoke(health, info);
    private void Update()
    {
        if (!AttacksEnabled || !player || GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0
            || Time.time < nextAttack || actors.Count == 0) return;
        nextAttack = Time.time + .15f;
        for (int offset = 0; offset < actors.Count; offset++)
        {
            var actor = actors[(turn + offset) % actors.Count];
            if (!actor || !actor.IsLeased || actor.Health.IsDead || actor.AbilityController.IsExecuting) continue;
            var delta = player.position - actor.transform.position; delta.y = 0;
            if (delta.sqrMagnitude > strongAbility.Range * strongAbility.Range) continue;
            actor.Movement.FacePosition(player.position);
            if (!actor.AbilityController.TryStart(player)) continue;
            turn = (turn + offset + 1) % actors.Count; nextAttack = Time.time + 3f; break;
        }
    }
    public void ClearActors()
    {
        AttacksEnabled = false;
        foreach (var actor in actors)
        {
            if (!actor) continue;
            actor.Health.OnDamageResolved -= OnHit; actor.Health.OnDead -= OnDefeated;
            if (actor.IsLeased) pool.Release(actor);
        }
        actors.Clear();
    }
    public void PauseCombat()
    {
        AttacksEnabled = false;
        foreach (var actor in actors) if (actor && actor.IsLeased) actor.AbilityController.Cancel();
    }
    private void OnDestroy() { ClearActors(); if (practiceSet) Destroy(practiceSet); }
    private static void Disable<T>(EnemyActor actor) where T : Behaviour { var value = actor.GetComponent<T>(); if (value) value.enabled = false; }
}
