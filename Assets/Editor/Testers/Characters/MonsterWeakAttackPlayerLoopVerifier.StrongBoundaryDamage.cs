using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    // Run the Animator's normal hit path and compare actual HP loss with the active parry window.
    static IEnumerator VerifyLiveStrongBoundaryDamage(EnemyActor actor, EnemyAbilityDefinition ability,
        CombatTarget target, float startDistance, float speed, JObject proof)
    {
        var controller = actor.AbilityController;
        var health = target.GetComponent<CombatHealth>();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.name = "Boundary sight obstruction";
        wall.transform.localScale = new Vector3(2, 3, .3f); wall.SetActive(false);
        var nearer = new GameObject("Boundary nearer player body"); owned.Add(nearer); nearer.layer = target.gameObject.layer;
        var originalCapsule = target.GetComponent<CapsuleCollider>();
        var capsule = nearer.AddComponent<CapsuleCollider>(); capsule.center = originalCapsule.center;
        capsule.radius = originalCapsule.radius; capsule.height = originalCapsule.height;
        var body = nearer.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false;
        var nearerTarget = nearer.AddComponent<CombatTarget>(); nearerTarget.Configure(CombatTeam.PlayerParty, false);
        var targetVolume = target.ResolveVolumeAtRootPosition(Vector3.zero);
        nearerTarget.ConfigureVolume(targetVolume.Center, targetVolume.Radius, targetVolume.HalfHeight * 2);
        var nearerHealth = nearer.GetComponent<CombatHealth>();
        var hpSettings = new SerializedObject(nearerHealth); hpSettings.FindProperty("showDamageNumbers").boolValue = false;
        hpSettings.ApplyModifiedPropertiesWithoutUndo(); nearer.SetActive(false);
        var samples = new JArray(); bool all = true;
        bool charge = ability.ExecutionMode == EnemyAbilityExecutionMode.Charge;
        float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability);
        float outer = radius + (charge ? EnemyAttackThreatGeometry.ChargeHalfWidth : 0);
        ability.TryGetParryMotionWindow(0, out var window);
        try
        {
            foreach (string scenario in new[] { "inside-edge", "outside-edge", "removed-band", "wall", "nearer-target" })
            {
                controller.Cancel(); wall.SetActive(false); nearer.SetActive(false);
                actor.transform.rotation = Quaternion.identity; actor.Movement.StopMovement();
                controller.Configure(actor.Definition.AbilitySet, 1, speed, actor.Definition.ResolveRuntimeStats().AttackInterval);
                controller.ClearPreparedAim(); health.SetMaxHp(1000000, true); nearerHealth.SetMaxHp(1000000, true);
                target.transform.position = actor.transform.position + actor.transform.forward * startDistance;
                Physics.SyncTransforms(); yield return null; yield return new WaitForFixedUpdate();
                float deadline = Time.time + 7;
                bool started = false;
                while (Time.time < deadline && !(started = controller.TryStartAbility(ability, target.transform))) yield return null;
                if (!started) throw new Exception("Boundary attack refused: " + actor.Definition.EnemyId + "/" + scenario);
                float offset = scenario == "inside-edge" ? -.12f : scenario == "removed-band" ? .25f : .12f;
                float distance = scenario == "wall" || scenario == "nearer-target" ? radius * .75f : outer + target.CurrentVolume.Radius + offset;
                bool expected = scenario == "inside-edge" || scenario == "nearer-target" && !charge;
                var damage = new JArray();
                Action<CombatHealth, DamageInfo, float, bool> observer = (victim, info, loss, dead) =>
                {
                    if (info.enemyAbility == ability && info.source == actor.gameObject && loss > 0)
                        damage.Add(new JObject { ["at"] = Time.time, ["strike"] = info.sourceAttackPhaseIndex, ["hpLoss"] = loss });
                };
                health.OnDamageResolved += observer;
                int parrySamples = 0; bool parryMatches = true, parrySeen = false, closedAfterImpact = true;
                float hpBefore = health.CurrentHp, nearerBefore = nearerHealth.CurrentHp;
                void PositionTargets()
                {
                    Vector3 origin = actor.transform.position, forward = actor.transform.forward;
                    target.transform.position = origin + forward * distance;
                    if (scenario == "wall") { wall.SetActive(true); wall.transform.position = origin + forward * (distance * .5f) + Vector3.up * 1.3f; }
                    if (scenario == "nearer-target") { nearer.transform.position = origin + forward * (distance * .55f); nearer.SetActive(true); }
                    Physics.SyncTransforms();
                }
                try
                {
                    PositionTargets(); deadline = Time.time + ability.ResolveExecutionDuration(speed) + 5;
                    while (controller.IsExecuting && Time.time < deadline)
                    {
                        PositionTargets();
                        if (actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float phase))
                        {
                            bool eligible = controller.IsParryThreatTo(target);
                            if (phase >= window.x && phase < window.y && damage.Count == 0)
                            { parrySamples++; parryMatches &= eligible == expected; parrySeen |= eligible; }
                            if (phase > window.y + .01f) closedAfterImpact &= !eligible;
                        }
                        yield return null;
                    }
                }
                finally { health.OnDamageResolved -= observer; wall.SetActive(false); nearer.SetActive(false); }
                float hpLoss = hpBefore - health.CurrentHp, nearerLoss = nearerBefore - nearerHealth.CurrentHp;
                bool pass = !controller.IsExecuting && parrySamples > 0 && parryMatches && parrySeen == expected
                    && (hpLoss > 0) == expected && damage.Count == (expected ? ability.HitCount : 0) && closedAfterImpact
                    && (scenario != "nearer-target" || nearerLoss > 0);
                all &= pass;
                samples.Add(new JObject { ["scenario"] = scenario, ["pass"] = pass, ["expectedHitAndParry"] = expected,
                    ["hpBefore"] = hpBefore, ["hpAfter"] = health.CurrentHp, ["hpLoss"] = hpLoss, ["nearerHpLoss"] = nearerLoss,
                    ["activeWindowSamples"] = parrySamples, ["parryMatchesExpected"] = parryMatches, ["parrySeen"] = parrySeen,
                    ["closedAfterFirstImpact"] = closedAfterImpact, ["damageEvents"] = damage });
            }
        }
        finally { controller.Cancel(); wall.SetActive(false); nearer.SetActive(false); }
        proof["pass"] = all; proof["mode"] = "live-animator-hit-path-hp-and-active-parry-window";
        proof["radius"] = radius; proof["samples"] = samples;
    }
}
