using System.Collections.Generic;
using UnityEngine;

// 기절 시작에 공용 상태 플로팅을 한 번 표시한다. 같은 기절의 연장은 중복 표시하지 않는다.
[DefaultExecutionOrder(200)]
public sealed class EnemyParryStunIndicator : MonoBehaviour
{
    private static EnemyParryStunIndicator instance;
    private readonly Dictionary<EnemyActor, EnemyMovementReaction> activeStuns = new Dictionary<EnemyActor, EnemyMovementReaction>();
    private readonly List<EnemyActor> completedStuns = new List<EnemyActor>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    public static void Show(EnemyActor enemy)
    {
        if (!Application.isPlaying || enemy == null || !enemy.isActiveAndEnabled || !enemy.IsLeased
            || enemy.Health == null || enemy.Health.IsDead) return;
        var reaction = enemy.GetComponent<EnemyMovementReaction>();
        if (reaction == null || !reaction.IsParryStunned) return;
        if (instance == null)
        {
            var host = new GameObject(nameof(EnemyParryStunIndicator));
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EnemyParryStunIndicator>();
        }
        instance.Attach(enemy, reaction);
    }

    private void Attach(EnemyActor enemy, EnemyMovementReaction reaction)
    {
        if (activeStuns.ContainsKey(enemy)) return;
        activeStuns.Add(enemy, reaction);
        var target = enemy.GetComponent<CombatTarget>();
        Vector3 head = enemy.transform.position + Vector3.up * 1.7f;
        if (target != null)
        {
            CombatTargetVolume volume = target.CurrentVolume;
            head = volume.Center + Vector3.up * (volume.HalfHeight + .35f);
        }
        DamageNumberSpawner.SpawnStun(head);
    }

    private void LateUpdate()
    {
        completedStuns.Clear();
        foreach (var entry in activeStuns)
        {
            EnemyActor actor = entry.Key;
            if (actor == null || !actor.isActiveAndEnabled || !actor.IsLeased
                || actor.Health == null || actor.Health.IsDead || entry.Value == null || !entry.Value.IsParryStunned)
                completedStuns.Add(actor);
        }
        foreach (var actor in completedStuns) activeStuns.Remove(actor);
    }
}
