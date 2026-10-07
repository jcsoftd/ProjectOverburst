using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class CrustaspikanTemporaryReactionVerifier
{
    public static string StartAuthoredRecoil(string output) => StartInternal(output, false, false, true);

    static IEnumerator AuthoredRecoilCases()
    {
        var profile = AssetDatabase.LoadAssetAtPath<CrustaspikanParryRecoilProfile>(CrustaspikanParryRecoilBuilder.ProfilePath);
        Require(profile != null && profile.motions.Length == 10 && profile.playbackSpeed == 1.5f, "Saved recoil profile missing.");
        foreach (var motion in profile.motions) yield return AuthoredParry(motion);
        var basic = profile.motions.First(m => m.attack == "LeftHandSmashAttack");
        yield return AuthoredParry(basic, 0, "lower-grade");
        yield return AuthoredParry(basic, 50, "lower-grade");
        yield return AuthoredParry(basic, 100, "freeze-pause");
        yield return AuthoredParry(basic, 100, "death");
        yield return AuthoredParry(basic, 100, "pool");
        yield return DazedEdges();
        var boss = Spawn(); var reaction = boss.GetComponent<CrustaspikanTemporaryReaction>();
        var material = collection.attacks.Single(m => m.runtimeClip.name == basic.attack);
        Warp(Origin + Vector3.forward * 5f); yield return new WaitForSeconds(.3f);
        Require(boss.AbilityController.TryStartAbility(material.ability, player.transform,
            EnemyAbilityStartContext.RearCounter(player.transform.position, boss.Movement.PhysicalRotation)), "Ordinary cancel probe start failed.");
        yield return new WaitForSeconds(.2f); int before = reaction.AuthoredRecoilCount;
        boss.AbilityController.Cancel(); yield return new WaitForSeconds(.2f);
        Require(!reaction.BlocksActions && reaction.AuthoredRecoilCount == before, "Ordinary cancellation acquired an authored recoil.");
        Record("authored-ordinary-cancel-no-recoil"); Release(boss);
    }

    static IEnumerator AuthoredParry(CrustaspikanParryRecoilProfile.Motion motion, float energyAmount = 100, string edge = null)
    {
        PrepareHeavy(energyAmount); var boss = Spawn();
        var encounterObject = new GameObject("Owned authored recoil encounter context"); owned.Add(encounterObject);
        encounterObject.transform.position = Origin;
        var encounter = encounterObject.AddComponent<CrustaspikanEncounter>(); encounter.enabled = false;
        var settings = AssetDatabase.LoadAssetAtPath<CrustaspikanEncounterSettings>(CrustaspikanParryRecoilBuilder.EncounterPath);
        typeof(CrustaspikanEncounter).GetProperty("Settings").SetValue(encounter, settings);
        brain = new CrustaspikanEncounterBrain(encounter, boss, player); brain.ReviewMode = true;
        boss.Health.SetMaxHp(1000000, true);
        var executor = boss.GetComponent<EnemyBossMaterialExecutor>(); var reaction = boss.GetComponent<CrustaspikanTemporaryReaction>();
        var material = executor.Collection.attacks.Single(m => m.runtimeClip.name == motion.attack);
        var strike = material.strikes[motion.strikeIndex]; var tuning = material.tuning.parries[motion.strikeIndex];
        Require(tuning.canParry && tuning.overrideWindow && Mathf.Abs(tuning.cueLeadSeconds - .25f) < .00001f && Mathf.Abs(tuning.startNormalized - motion.contactNormalized) < .00001f
            && Mathf.Abs(tuning.endNormalized - strike.impact) < .00001f, "Real encounter did not apply the reviewed strike window.");
        Require(executor.Collection.attacks.Sum(m => m.strikes.Select((s, i) => m.IsStrikeParryable(i) ? 1 : 0).Sum()) == 10,
            "Encounter parry target count differs from ten approved contacts.");
        Vector3 targetPosition = strike.Origin(boss.transform) + strike.Rotation(boss.transform) * Vector3.forward * Mathf.Min(6, strike.radius * .5f);
        targetPosition.y = Origin.y; Warp(targetPosition);
        yield return new WaitForSeconds(.3f);
        Require(boss.AbilityController.TryStartAbility(material.ability, player.transform,
            EnemyAbilityStartContext.RearCounter(player.transform.position, boss.Movement.PhysicalRotation)), "Actual authored attack start failed: " + motion.attack);
        if (motion.strikeIndex > 0) Warp(Origin - Vector3.forward * 18f);
        int recoilBefore = reaction.AuthoredRecoilCount, rewindsBefore = reaction.RewindCount;
        int parryBefore = player.GetComponent<PlayerParryController>().ParriedAttackCount;
        float inputNormalized = motion.contactNormalized - .10f * material.AnimationSpeedMultiplier / material.runtimeClip.length;
        float positionNormalized = motion.contactNormalized - .30f * material.AnimationSpeedMultiplier / material.runtimeClip.length;
        float deadline = Time.unscaledTime + 60f, requestAt = 0f, recoilAt = -1f, dazeAt = -1f, recoverAt = -1f, finishAt = -1f, suspendedGame = 0f;
        bool positioned = motion.strikeIndex == 0, requested = false, observed = false, edgeDone = false, signalled = false;
        int impactsAtRequest = 0, frame = 0; var poses = new JArray(); var stages = new JArray(); var cueSamples = new JArray();
        var expectedCueBone = boss.Animator.GetComponentsInChildren<Transform>(true).Single(t => t.name == motion.cueBone);
        Require(motion.useCueRootPosition && tuning.useCueRootPosition && tuning.cueRootPosition == motion.cueRootPosition, "Reviewed attacking-limb position was not applied.");
        Vector3? fixedCuePosition = null; var visibleParticles = new ParticleSystem.Particle[1];
        Vector3 reactionRoot = boss.transform.position;
        string id = motion.attack + "-hit" + (motion.strikeIndex + 1) + (edge != null ? "-" + edge + "-" + energyAmount : "");
        while (Time.unscaledTime < deadline)
        {
            yield return null;
            if (!positioned && executor.HasEnteredMotion && executor.NormalizedTime >= positionNormalized)
            {
                targetPosition = strike.Origin(boss.transform) + strike.Rotation(boss.transform) * Vector3.forward * Mathf.Min(6, strike.radius * .5f);
                targetPosition.y = Origin.y; Warp(targetPosition); positioned = true;
            }
            var liveCue = boss.GetComponents<EnemyStrongAttackWarning>().FirstOrDefault(w => w.IsVisible && w.FinalSignal && w.AttackCueSocket == expectedCueBone
                && (bool)typeof(EnemyStrongAttackWarning).GetField("signalPlayed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w));
            if (!requested && liveCue != null)
            {
                var particle = (ParticleSystem)typeof(EnemyStrongAttackWarning).GetField("signalParticles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(liveCue);
                // Inspect the actual rendered particle: the arm may move, the flash must not.
                yield return new WaitForEndOfFrame();
                Vector3 position = particle.transform.position;
                Vector3 projected = camera.WorldToViewportPoint(position);
                if (!fixedCuePosition.HasValue) fixedCuePosition = position;
                float positionError = Vector3.Distance(position, fixedCuePosition.Value);
                int visibleCount = particle.GetParticles(visibleParticles);
                float particleError = visibleCount > 0 ? Vector3.Distance(visibleParticles[0].position, fixedCuePosition.Value) : float.PositiveInfinity;
                float size = visibleCount > 0 ? visibleParticles[0].GetCurrentSize(particle) : 0f;
                var cueMaterial = particle.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                Require(projected.z > 0f && projected.x > 0f && projected.x < 1f && projected.y > 0f && projected.y < 1f
                    && positionError < .0001f && particleError < .0001f && size >= liveCue.ResolveCueSize() * .95f && size <= liveCue.ResolveCueSize() * 1.45f
                    && particle.main.simulationSpace == ParticleSystemSimulationSpace.World
                    && cueMaterial.GetFloat("_ZTest") == (float)UnityEngine.Rendering.CompareFunction.Always && !cueMaterial.IsKeywordEnabled("_USESOFTALPHA"),
                    "Rendered glint moved or lost the enlarged visible style: " + id + " / " + positionError + " / " + particleError + " / " + size);
                cueSamples.Add(new JObject { ["bone"] = expectedCueBone.name, ["world"] = new JArray(position.x, position.y, position.z),
                    ["screen"] = new JArray(projected.x, projected.y), ["positionError"] = positionError, ["particleError"] = particleError,
                    ["worldSize"] = size, ["particleCount"] = visibleCount });
            }
            if (!requested && executor.HasEnteredMotion && executor.NormalizedTime >= inputNormalized
                && executor.WouldHit(material.ability, player.GetComponent<CombatTarget>(), motion.strikeIndex))
            {
                signalled = boss.GetComponentsInChildren<EnemyStrongAttackWarning>(true).Any(w => w.IsVisible && w.FinalSignal
                    && (bool)typeof(EnemyStrongAttackWarning).GetField("signalPlayed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(w));
                Require(signalled && cueSamples.Count >= 2, "Authored parry contact had no fixed attacking-side glint/sound request: " + id);
                Vector3 cueViewport = camera.WorldToViewportPoint(fixedCuePosition.Value), handViewport = camera.WorldToViewportPoint(expectedCueBone.position);
                float handDistancePixels = Vector2.Distance(new Vector2(cueViewport.x * 960f, cueViewport.y * 540f), new Vector2(handViewport.x * 960f, handViewport.y * 540f));
                Require(handDistancePixels < 85f, "Flash is detached from the actual attacking limb: " + id + " / " + handDistancePixels);
                cueSamples.Last["contactHandDistancePixels"] = handDistancePixels;
                cueSamples.Last["contactHandWorld"] = new JArray(expectedCueBone.position.x, expectedCueBone.position.y, expectedCueBone.position.z);
                if (edge == null) Capture(id + "-cue", 0);
                Require(material.IsParryCueWindowOpen(motion.strikeIndex, executor.NormalizedTime, 1f, material.AnimationSpeedMultiplier), "Cue did not precede authored parry contact.");
                impactsAtRequest = executor.ImpactCount; reactionRoot = boss.transform.position;
                var action = player.GetComponent<MeleeRuntime>().TryStartHeavyAttack((boss.transform.position - player.transform.position).normalized);
                Require(action == WeaponActionResult.Accepted, "Player heavy input rejected: " + id + " / " + action);
                requested = true; requestAt = Time.unscaledTime;
            }
            if (edge == null && (requested || executor.HasEnteredMotion && executor.NormalizedTime >= positionNormalized)) Capture(id, frame++);
            if (reaction.BlocksActions)
            {
                observed = true; poses.Add(Pose(boss, reaction));
                Require(!boss.AbilityController.IsExecuting && executor.ImpactCount == impactsAtRequest, "Parried attack released a later strike: " + id);
                Require(!boss.Animator.applyRootMotion && Vector3.Distance(boss.transform.position, reactionRoot) < .06f, "Authored recoil moved actor root: " + id);
                Require(reaction.Phase != CrustaspikanTemporaryReaction.ReactionPhase.DazedEnter && reaction.Phase != CrustaspikanTemporaryReaction.ReactionPhase.Rewind
                    && reaction.Phase != CrustaspikanTemporaryReaction.ReactionPhase.ReboundHold, "Authored recoil entered a redundant rewind/hold/Enter stage.");
                if (reaction.Phase == CrustaspikanTemporaryReaction.ReactionPhase.AuthoredRecoil)
                {
                    if (recoilAt < 0f) { recoilAt = Time.time; stages.Add(new JObject { ["phase"] = "AuthoredRecoil", ["gameTime"] = recoilAt }); }
                    Require(reaction.LastAuthoredRecoilClip == motion.clip && reaction.LastAuthoredRecoilStrike == motion.strikeIndex
                        && reaction.LastAuthoredRecoilRate == 1.5f, "Wrong attack/strike recoil or speed selected.");
                    if (!edgeDone && reaction.SampledNormalizedTime >= .2f && edge != null)
                    {
                        edgeDone = true;
                        if (edge == "freeze-pause")
                        {
                            var handle = reaction.ReactionHandle; float normalized = reaction.SampledNormalizedTime, before = Time.time;
                            boss.AnimationBridge.SetFrozen(true); yield return new WaitForSeconds(.15f);
                            Require(reaction.SampledNormalizedTime == normalized && reaction.Phase == CrustaspikanTemporaryReaction.ReactionPhase.AuthoredRecoil,
                                "Freeze advanced authored recoil.");
                            suspendedGame += Time.time - before; boss.AnimationBridge.SetFrozen(false); yield return null;
                            Require(boss.AnimationBridge.OwnsMotion(reaction.ReactionHandle) && reaction.ReactionHandle != handle, "Recoil did not reacquire its owner after thaw.");
                            normalized = reaction.SampledNormalizedTime; float previousScale = Time.timeScale;
                            try { Time.timeScale = 0f; yield return new WaitForSecondsRealtime(.15f);
                                Require(reaction.SampledNormalizedTime == normalized, "Pause advanced authored recoil."); }
                            finally { Time.timeScale = previousScale; }
                            Record("authored-recoil-freeze-pause-resume");
                        }
                        else if (edge == "death")
                        {
                            boss.Health.TakeDamage(new DamageInfo(2000000, boss.transform.position, player.gameObject, Vector3.forward)); yield return null;
                            Require(boss.Health.IsDead && !reaction.BlocksActions && !boss.AnimationBridge.OwnsMotion(reaction.ReactionHandle), "Death retained authored recoil owner.");
                            Record("authored-recoil-death-clears-owner"); break;
                        }
                        else if (edge == "pool")
                        {
                            uint oldLease = boss.LeaseVersion; brain.Dispose(); brain = null; Release(boss); var reused = Spawn();
                            Require(reused == boss && reused.LeaseVersion != oldLease && !reaction.BlocksActions
                                && !reused.AnimationBridge.OwnsMotion(reaction.ReactionHandle), "Pool reuse retained authored recoil pose/owner.");
                            Record("authored-recoil-pool-reuse-clears-owner"); Release(reused); break;
                        }
                    }
                }
                if (reaction.Phase == CrustaspikanTemporaryReaction.ReactionPhase.Dazed && dazeAt < 0f)
                { dazeAt = Time.time; stages.Add(new JObject { ["phase"] = "Dazed", ["gameTime"] = dazeAt }); }
                if (reaction.Phase == CrustaspikanTemporaryReaction.ReactionPhase.DazedRecover && recoverAt < 0f)
                { recoverAt = Time.time; stages.Add(new JObject { ["phase"] = "DazedRecover", ["gameTime"] = recoverAt }); }
            }
            if (requested && Time.unscaledTime - requestAt > .8f && !reaction.BlocksActions)
            { if (energyAmount >= 80 && !observed) continue; finishAt = Time.time; break; }
        }
        Require(requested && Time.unscaledTime < deadline, "Authored parry flow timed out: " + id);
        Require(player.GetComponent<PlayerParryController>().ParriedAttackCount == parryBefore + 1, "Actual heavy parry did not confirm exactly once: " + id);
        if (energyAmount >= 80)
        {
            Require(reaction.AuthoredRecoilCount == recoilBefore + 1 && reaction.RewindCount == rewindsBefore, "Authored recoil used the old rewind path.");
            Require(reaction.LastRewindStart >= motion.contactNormalized - .0001f && reaction.LastRewindStart <= strike.impact + .0001f,
                "Recoil was accepted before the reviewed attack had advanced.");
            if (edge != "death" && edge != "pool")
            {
                Require(dazeAt > recoilAt && recoverAt > dazeAt && finishAt > recoverAt && reaction.LastDazedCycles >= 1f, "Standing loop/recovery did not finish.");
                Require(Mathf.Abs((dazeAt - recoilAt - suspendedGame) - motion.clip.length / 1.5f) <= .075f,
                    "Actual recoil stage clock differs from 1.5x playback: " + id + " / " + (dazeAt - recoilAt - suspendedGame));
                Require(Mathf.Abs((recoverAt - dazeAt) - reaction.ParryDazedClip.length) <= .075f
                    && Mathf.Abs((finishAt - recoverAt) - reaction.ParryDazedRecoverClip.length) <= .075f, "Recoil speed leaked into daze or recovery.");
                Require(!reaction.BlocksActions && boss.Animator.speed > .99f, "Recoil/recovery left action or animator lock behind.");
            }
        }
        else Require(!observed && reaction.AuthoredRecoilCount == recoilBefore, "Lower-grade parry acquired perfect recoil.");
        Record(id, new JObject { ["attack"] = motion.attack, ["strikeIndex"] = motion.strikeIndex, ["actualPlayerParry"] = true,
            ["advanceSignal"] = signalled, ["recoilRate"] = reaction.LastAuthoredRecoilRate, ["clipSeconds"] = motion.clip.length,
            ["recoilStageSeconds"] = dazeAt >= 0 ? dazeAt - recoilAt - suspendedGame : -1f, ["dazeSeconds"] = recoverAt >= 0 ? recoverAt - dazeAt : -1f,
            ["recoverSeconds"] = finishAt >= 0 && recoverAt >= 0 ? finishAt - recoverAt : -1f, ["acceptedNormalized"] = reaction.LastRewindStart,
            ["cueBone"] = expectedCueBone.name, ["cueSamples"] = cueSamples,
            ["authoredNormalized"] = motion.contactNormalized, ["frames"] = frame, ["stages"] = stages, ["poses"] = poses });
        brain?.Dispose(); brain = null; Release(boss); player.GetComponent<MeleeRuntime>().CancelCurrentAttackState(); yield return new WaitForSeconds(.4f);
    }
}
