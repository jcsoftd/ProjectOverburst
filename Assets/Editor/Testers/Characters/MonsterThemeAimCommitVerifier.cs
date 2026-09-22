using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Move the real target during production AI turning; do not force an attack state.
public static class MonsterThemeAimCommitVerifier
{
    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output = SessionState.GetString("MonsterThemePlayVerifier.output", "");
        var results = new List<object>();
        Vector3 home = player.transform.position;
        var playerHealth = player.GetComponent<CombatHealth>();
        var definitions = ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct().ToArray();
        string selected = SessionState.GetString("MonsterThemePreparedAim.species", "");
        if (!string.IsNullOrEmpty(selected)) definitions = definitions.Where(d => selected.Split(',').Contains(d.EnemyId)).ToArray();
        foreach (var definition in definitions)
        {
            Teleport(player, home); playerHealth.ResetHealth();
            var opening = definition.AbilitySet.GetAbility(0);
            float distance = Mathf.Max(opening.MinimumRange + .2f, opening.Range * .8f);
            Vector3 spawn = home + Vector3.forward * distance;
            var request = new EnemySpawnRequest(definition, spawn, Quaternion.identity,
                player.transform, null, player.transform, null, 1, 1, 71);
            Require(EnemySpawnService.Current.TrySpawn(request, out var actor), "Spawn " + definition.EnemyId);
            var locomotion = actor.GetComponent<EnemyLocomotionAnimator>();
            var trace = new List<object>();
            var culling = actor.Animator.cullingMode; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            try
            {
                actor.AI.RequestAggro(player.transform);
                float deadline = Time.time + 16;
                while (!locomotion.IsTurning || !actor.AbilityController.HasPreparedAim(player.transform))
                {
                    Require(Time.time < deadline, "No prepared turn: " + definition.EnemyId + " state=" + actor.AI.CurrentStateName);
                    yield return null;
                }
                Vector3 aimed = actor.AbilityController.ResolveAimPosition(player.transform);
                // A visible lateral dodge leaves the old strike line, while remaining in the encounter.
                Vector3 escaped = home + Vector3.right * 8;
                Teleport(player, escaped);
                float began = Time.time, hp = playerHealth.CurrentHp;
                while (!actor.AbilityController.IsExecuting)
                {
                    trace.Add(new { time = Time.time - began, state = actor.AI.CurrentStateName,
                        turning = locomotion.IsTurning, yaw = actor.transform.eulerAngles.y,
                        aim = actor.AbilityController.ResolveAimPosition(player.transform).ToString("F3") });
                    Require(Time.time - began < 5, "Did not attack original point: " + definition.EnemyId);
                    Require(Vector3.Distance(aimed, actor.AbilityController.ResolveAimPosition(player.transform)) < .01f,
                        "Aim chased moving player: " + definition.EnemyId);
                    yield return null;
                }
                float latency = Time.time - began;
                string ability = actor.AbilityController.LastCommittedAbility.name;
                Vector3 direction = aimed - actor.transform.position; direction.y = 0;
                Require(Vector3.Angle(actor.transform.forward, direction) < 5.1f, "Attack started before prepared alignment");
                Quaternion facing = actor.transform.rotation; float maxYaw = 0;
                float attackStarted = Time.time;
                while (actor.AbilityController.IsExecuting || actor.AnimationBridge.IsBlockingActionActive || actor.Movement.IsActionLocked)
                {
                    Require(Time.time - attackStarted < 8, "Committed attack did not finish");
                    maxYaw = Mathf.Max(maxYaw, Quaternion.Angle(facing, actor.transform.rotation));
                    Require(maxYaw < .3f, "Attack tracked escaped player: " + definition.EnemyId);
                    yield return null;
                }
                Require(Mathf.Approximately(hp, playerHealth.CurrentHp), "Escaped player hit: " + definition.EnemyId);
                float reaimDeadline = Time.time + 4;
                while (actor.AbilityController.HasPreparedAim(player.transform)
                    && Vector3.Distance(actor.AbilityController.ResolveAimPosition(player.transform), aimed) < .01f)
                {
                    Require(Time.time < reaimDeadline, "Finished attack retained old point");
                    yield return null;
                }
                actor.AI.enabled = false;
                MonsterThemeFacingVerifier.Reset(actor, spawn);
                yield return Ready(actor);
                actor.AbilityController.PrepareAttackAim(player.transform);
                Require(actor.AbilityController.HasPreparedAim(player.transform), "Cancellation fixture did not prepare");
                actor.Health.TakeDamage(new DamageInfo(1, actor.transform.position, player.gameObject, Vector3.forward));
                Require(!actor.AbilityController.HasPreparedAim(player.transform), "Damage retained old point");
                MonsterThemeFacingVerifier.Reset(actor, spawn);
                yield return Ready(actor);
                actor.AbilityController.PrepareAttackAim(player.transform);
                Require(actor.AbilityController.HasPreparedAim(player.transform), "Control fixture did not prepare");
                actor.GetComponent<EnemyMovementReaction>().ApplyHitStun(.1f);
                Require(!actor.AbilityController.HasPreparedAim(player.transform), "Control effect retained old point");
                MonsterThemeFacingVerifier.Reset(actor, spawn);
                yield return Ready(actor);
                actor.AbilityController.PrepareAttackAim(player.transform);
                Require(actor.AbilityController.HasPreparedAim(player.transform), "Pool fixture did not prepare");
                uint lease = actor.LeaseVersion; actor.RequestPoolRelease();
                Require(!actor.AbilityController.HasPreparedAim(player.transform), "Pool return retained old point");
                Require(EnemySpawnService.Current.TrySpawn(request, out var reused), "Pool reuse");
                Require(reused == actor && reused.LeaseVersion != lease && !reused.AbilityController.HasPreparedAim(player.transform), "Pool reuse inherited aim");
                reused.AI.enabled = false;
                var other = new GameObject("Prepared aim target change fixture");
                try
                {
                    other.transform.position = home + Vector3.left * 3;
                    reused.AbilityController.PrepareAttackAim(player.transform);
                    reused.AbilityController.PrepareAttackAim(other.transform);
                    Require(reused.AbilityController.HasPreparedAim(other.transform) && !reused.AbilityController.HasPreparedAim(player.transform), "Changed target kept old aim");
                    reused.Health.TakeDamage(new DamageInfo(reused.Health.MaxHp + 1000, reused.transform.position, player.gameObject, Vector3.forward));
                    Require(!reused.AbilityController.HasPreparedAim(other.transform), "Death retained old aim");
                }
                finally { Object.Destroy(other); }
                results.Add(new { id = definition.EnemyId, latency, ability, maxYaw, miss = true,
                    damageClear = true, controlClear = true, poolClear = true, targetChange = true, deathClear = true, status = "PASS", trace });
                File.WriteAllText(Path.Combine(output, "prepared-aim-results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
                Debug.Log("[MonsterPreparedAim] PASS " + definition.EnemyId + " turn-to-attack=" + latency);
            }
            finally
            {
                actor.Animator.cullingMode = culling; actor.RequestPoolRelease(); Teleport(player, home);
            }
        }
    }

    public static IEnumerator VerifyAbilities(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output = SessionState.GetString("MonsterThemePlayVerifier.output", "");
        var results = new List<object>(); Vector3 home = player.transform.position;
        var health = player.GetComponent<CombatHealth>();
        foreach (var definition in ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct())
        {
            for (int index = 0; index < definition.AbilitySet.Count; index++)
            {
                var ability = definition.AbilitySet.GetAbility(index);
                float distance = ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile ? 4f
                    : ability.ExecutionMode == EnemyAbilityExecutionMode.Charge ? 3f : Mathf.Min(1.1f, ability.Range * .75f);
                distance = Mathf.Max(distance, ability.MinimumRange + .1f);
                Teleport(player, home); health.ResetHealth();
                var request = new EnemySpawnRequest(definition, home - Vector3.forward * distance, Quaternion.Euler(0, 90, 0),
                    player.transform, null, player.transform, null, 1, 1, 71);
                Require(EnemySpawnService.Current.TrySpawn(request, out var actor), "Ability fixture spawn");
                actor.AI.enabled = false; actor.Movement.StopMovement();
                var originalCulling = actor.Animator.cullingMode; actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                try
                {
                    yield return Seconds(.2f);
                    Vector3 aim = actor.AbilityController.PrepareAttackAim(player.transform);
                    var locomotion = actor.GetComponent<EnemyLocomotionAnimator>();
                    float deadline = Time.time + 5;
                    actor.Movement.FacePosition(aim);
                    while (!locomotion.IsTurning) { Require(Time.time < deadline, "Ability turn did not begin"); yield return null; }
                    Vector3 escape = home + Vector3.right * (ability.Range + ability.HitRadius + 5);
                    Teleport(player, escape);
                    while (!actor.Movement.IsFacingForAttack(aim))
                    {
                        Require(Time.time < deadline, "Prepared ability turn timeout");
                        actor.Movement.FacePosition(actor.AbilityController.ResolveAimPosition(player.transform)); yield return null;
                    }
                    var executor = actor.GetComponents<EnemyAbilityExecutor>().First(e => e.Supports(ability));
                    var special = actor.GetComponent<EnemyThemeSpecialExecutor>(); int launches = special != null ? special.LaunchCount : 0;
                    Require(executor.TryStart(ability, index, player.transform), "Prepared ability rejected: " + ability.name);
                    Quaternion facing = actor.transform.rotation; float hp = health.CurrentHp, maxYaw = 0, progress = 0, began = Time.time;
                    while (executor.IsExecuting || actor.AnimationBridge.IsBlockingActionActive || actor.Movement.IsActionLocked)
                    {
                        Require(Time.time - began < 8, "Prepared ability did not finish: " + ability.name);
                        if (actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float time)) progress = Mathf.Max(progress, time);
                        maxYaw = Mathf.Max(maxYaw, Quaternion.Angle(facing, actor.transform.rotation));
                        actor.Movement.FacePosition(escape); yield return null;
                    }
                    Require(maxYaw < .3f && Mathf.Approximately(hp, health.CurrentHp), "Prepared attack tracked or hit escaped target: " + ability.name);
                    Require(progress >= ability.GetHitNormalizedTime(ability.HitCount - 1), "Attack lost later impact animation: " + ability.name);
                    float? projectileError = null;
                    if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                    {
                        Require(special.LaunchCount == launches + 1, "Prepared shot did not launch: " + ability.name);
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        Vector3 fired = (Vector3)typeof(EnemyThemeSpecialExecutor).GetField("boltDirection", flags).GetValue(special);
                        projectileError = Vector3.Angle(fired, aim - actor.transform.position);
                        Require(projectileError < .3f, "Projectile retargeted escaped player: " + ability.name);
                    }
                    actor.AbilityController.Cancel();
                    Require(!actor.AbilityController.HasPreparedAim(player.transform), "Cancel retained aim");
                    actor.AbilityController.PrepareAttackAim(player.transform);
                    Require(Vector3.Distance(actor.AbilityController.ResolveAimPosition(player.transform), escape) < .01f, "Next attack did not reacquire");
                    results.Add(new { id = definition.EnemyId, ability = ability.name, maxYaw, progress, projectileError, miss = true, status = "PASS" });
                    File.WriteAllText(Path.Combine(output, "prepared-ability-results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(results, Newtonsoft.Json.Formatting.Indented));
                    Debug.Log("[MonsterPreparedAbility] PASS " + ability.name);
                }
                finally { actor.Animator.cullingMode = originalCulling; actor.RequestPoolRelease(); Teleport(player, home); }
            }
        }
    }

    private static void Teleport(PlayerInputFacade player, Vector3 point)
    {
        var controller = player.GetComponent<CharacterController>(); bool enabled = controller.enabled;
        controller.enabled = false; player.transform.position = point; controller.enabled = enabled;
        player.GetComponent<PlayerMovement>().ResetMotionAfterTeleport(); Physics.SyncTransforms();
    }
    private static IEnumerator Seconds(float seconds) { float end = Time.time + seconds; while (Time.time < end) yield return null; }
    private static IEnumerator Ready(EnemyActor actor)
    {
        yield return Seconds(.2f);
        float deadline = Time.time + 4;
        while (actor.GetComponent<EnemyMovementReaction>().IsStunned || actor.AnimationBridge.IsBlockingActionActive || actor.Movement.IsActionLocked)
        {
            Require(Time.time < deadline, "Cancellation fixture did not leave its previous reaction");
            yield return null;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("[MonsterPreparedAim] " + message); }
}
