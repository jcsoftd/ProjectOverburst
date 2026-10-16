using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    static IEnumerator VerifyMediumStrongCooldownAfterAttack(EnemyActor actor, EnemyAbilityDefinition strong,
        Transform target, float strongDistance, JObject proof)
    {
        var controller = actor.AbilityController;
        float endedAt = Time.time, initial = controller.GetRemainingCooldown(strong), readyAt = endedAt + initial;
        bool full = Mathf.Abs(initial - strong.Cooldown) < .085f;
        bool blocked = !controller.TryStartAbility(strong, target);
        float deadline = Time.time + 3;
        while (!controller.IsAttackIntervalReady && Time.time < deadline) yield return null;
        var weak = Enumerable.Range(0, actor.Definition.AbilitySet.Count).Select(actor.Definition.AbilitySet.GetAbility)
            .Where(a => !a.IsTelegraphedStrongAttack && a.HasWeakAttackExecution && !a.WeakAttackExecution.RequiresBlockedApproach)
            .OrderBy(a => a.WeakAttackExecution.UsesAdvance).First();
        float weakDistance = Mathf.Max(.3f, weak.WeakAttackExecution.ApproachStartRange - .1f);
        target.position = actor.transform.position + Vector3.forward * weakDistance;
        actor.transform.rotation = Quaternion.identity; controller.ClearPreparedAim(); Physics.SyncTransforms();
        bool weakStarted = false;
        while (Time.time < readyAt - .1f && !(weakStarted = controller.TryStartAbility(weak, target))) yield return null;
        while (controller.IsExecuting && Time.time < readyAt + 5) yield return null;
        bool weakCompleted = weakStarted && !controller.IsExecuting;
        bool clockPreserved = Mathf.Abs(controller.GetRemainingCooldown(strong) - Mathf.Max(0, readyAt - Time.time)) < .085f;
        int rejectionSamples = 0;
        while (Time.time < readyAt - .05f)
        {
            blocked &= !controller.TryStartAbility(strong, target); rejectionSamples++; yield return null;
        }
        target.position = actor.transform.position + Vector3.forward * strongDistance;
        actor.transform.rotation = Quaternion.identity; controller.ClearPreparedAim(); Physics.SyncTransforms();
        bool restarted = false; deadline = readyAt + 7;
        while (Time.time < deadline && !(restarted = controller.TryStartAbility(strong, target))) yield return null;
        float restartedAt = Time.time;
        controller.Cancel(); float afterCancel = controller.GetRemainingCooldown(strong);
        controller.ClearTarget(); bool lossPreserved = controller.GetRemainingCooldown(strong) > strong.Cooldown - .1f;
        bool pass = full && blocked && weakCompleted && clockPreserved && restarted && restartedAt >= readyAt - .02f
            && Mathf.Abs(afterCancel - strong.Cooldown) < .085f && lossPreserved;
        proof["pass"] = pass; proof["endedAt"] = endedAt; proof["configuredCooldown"] = strong.Cooldown;
        proof["initialRemaining"] = initial; proof["readyAt"] = readyAt; proof["nextStrongAt"] = restartedAt;
        proof["completionToNextStrong"] = restartedAt - endedAt; proof["blockedDuringCooldown"] = blocked;
        proof["rejectionSamples"] = rejectionSamples; proof["weakStartedAndCompleted"] = weakCompleted;
        proof["weakDoesNotResetCooldown"] = clockPreserved; proof["cancelRemaining"] = afterCancel; proof["targetLossPreservesCooldown"] = lossPreserved;
        if (!pass) throw new Exception("Medium strong cooldown Play failure: " + actor.Definition.EnemyId + " " + proof);
    }
}
