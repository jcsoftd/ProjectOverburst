using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Reuses the product input fixture, isolated account and finally cleanup from PlayerEvadeVerifier.
public static partial class PlayerEvadeVerifier
{
    const string SwordIdleVerificationKey = "Overburst.PlayerEvadeVerifier.SwordIdle";

    public static void StartSwordIdleIsolated(string directory)
    {
        SessionState.SetBool(SwordIdleVerificationKey, true);
        try { StartIsolated(directory); }
        catch { SessionState.EraseBool(SwordIdleVerificationKey); throw; }
    }

    static float SwordIdleProgress() => (float)typeof(MeleeRuntime).GetMethod("GetAttackNormalizedTime", Private).Invoke(melee, null);
    static void SetSwordIdleProgress(float progress)
    {
        var step = Field<MeleeComboStepData>(melee, "activeAttackStep");
        float duration = Field<float>(melee, "attackDuration");
        typeof(MeleeRuntime).GetField("attackStartTime", Private).SetValue(melee,
            Time.time - duration * step.playbackAcceleration.ToElapsed(progress));
    }
    static bool SwordIdleClipVisible(AnimationClip expected)
    {
        int layer = animator.GetLayerIndex("Combat_MeleeWeapon");
        return animator.GetCurrentAnimatorClipInfo(layer).Any(c => c.clip == expected && c.weight > .001f)
            || animator.GetNextAnimatorClipInfo(layer).Any(c => c.clip == expected && c.weight > .001f);
    }
    static IEnumerator CheckSwordIdleClip(AnimationClip expected, string label)
    {
        yield return Frames(3);
        Check(Field<AnimationClip>(melee, "activeAttackAnimationClip") == expected, label + " runtime 클립");
        Check(SwordIdleClipVisible(expected), label + " 실제 Animator 클립");
        var footLock = animator.GetComponent<PlayerFootLock>();
        Check(footLock != null, label + " 제품 발 보정 컴포넌트");
        var left = typeof(PlayerFootLock).GetField("leftFoot", Private).GetValue(footLock);
        var right = typeof(PlayerFootLock).GetField("rightFoot", Private).GetValue(footLock);
        var weight = left.GetType().GetField("Weight");
        Check((float)weight.GetValue(left) == 0f && (float)weight.GetValue(right) == 0f, label + " 공격 중 발 IK 즉시 해제");
    }
    static IEnumerator WaitForSwordIdle(AnimationClip idle)
    {
        float limit = Time.unscaledTime + 3f;
        while (!SwordIdleClipVisible(idle) && Time.unscaledTime < limit) yield return null;
        Check(SwordIdleClipVisible(idle), "Sword Idle 안정 복귀");
    }
    static readonly HashSet<AnimationClip> swordIdleObserved = new HashSet<AnimationClip>();
    static int swordIdleObservedFrame;
    static void ObserveSwordIdleClips()
    {
        if (animator == null || Time.frameCount == swordIdleObservedFrame) return;
        swordIdleObservedFrame = Time.frameCount;
        int layer = animator.GetLayerIndex("Combat_MeleeWeapon");
        if (layer < 0) return;
        foreach (var clip in animator.GetCurrentAnimatorClipInfo(layer).Concat(animator.GetNextAnimatorClipInfo(layer)))
            if (clip.weight > .001f) swordIdleObserved.Add(clip.clip);
    }
    static IEnumerator VerifySwordIdleGameplay()
    {
        swordIdleObserved.Clear(); swordIdleObservedFrame = -1;
        EditorApplication.update += ObserveSwordIdleClips;
        try
        {
            yield return VerifySwordIdleSequence();
            foreach (var target in SwordIdleAttackCopyBuilder.Targets())
                Check(swordIdleObserved.Contains(target.step.animationClip), "실제 Animator 관측 " + target.role);
        }
        finally
        {
            EditorApplication.update -= ObserveSwordIdleClips;
            File.WriteAllText(Path.Combine(output, "ObservedClips.json"), Newtonsoft.Json.JsonConvert.SerializeObject(
                swordIdleObserved.Select(c => new { c.name, path = AssetDatabase.GetAssetPath(c) }), Newtonsoft.Json.Formatting.Indented));
            swordIdleObserved.Clear();
        }
    }
    static IEnumerator VerifySwordIdleSequence()
    {
        var definition = actor.Equipment.CurrentWeaponData.GetMeleeDefinition();
        var combo = definition.comboDefinition;
        yield return Reset();
        yield return WaitForSwordIdle(definition.animationProfile.combatIdleClip);
        Check(SwordIdleClipVisible(definition.animationProfile.combatIdleClip), "새 Sword Combat Idle 실제 재생");
        Check(AssetDatabase.GetAssetPath(definition.animationProfile.combatIdleClip) == SwordIdleAttackCopyBuilder.IdlePath,
            "검증한 Sword Idle 연결");
        var request = new WeaponActionRequest(WeaponActionSource.PlayerInput, null, forward);
        // Exercise the real continuation API at exact outgoing clock positions; this is a controlled-clock fixture.
        foreach (int outgoing in new[] { 0, 1, 2 })
        foreach (bool late in new[] { false, true })
        {
            yield return Reset();
            melee.SetManualInputEnabled(true);
            // Reset teleports the fixture and clears the motor's cached contact. Probe real ground before the direct API call.
            var motor = actor.GetComponent<OverburstCharacterMotor3D>();
            motor.ProbeGround(0f);
            var startResult = melee.TryStartAction(request, out var handle);
            Check(startResult == WeaponActionResult.Accepted, "직접 연계 시험 시작 " + startResult
                + " grounded=" + movement.IsGrounded + " blocked=" + GameplayInputBlocker.IsGameplayInputBlocked
                + " attacking=" + melee.IsAttackInProgress + " action=" + Field<int>(melee, "activeActionId")
                + " position=" + actor.transform.position + " groundGap=" + motor.GroundGap);
            for (int i = 0; i < outgoing; i++)
            {
                SetSwordIdleProgress(combo.steps[i].comboInputWindow.startNormalizedTime + .01f);
                Check(melee.TryContinue(handle, request) == WeaponActionResult.Accepted, "시험 타수 준비 " + (i + 2));
            }
            float progress = late ? .91f : combo.steps[outgoing].comboInputWindow.startNormalizedTime + .02f;
            SetSwordIdleProgress(progress);
            bool settled = progress >= combo.steps[outgoing].settledIdleStartNormalizedTime;
            float expectedStart = settled ? 0f : combo.steps[outgoing + 1].continuationStartNormalizedTime;
            Check(melee.TryContinue(handle, request) == WeaponActionResult.Accepted, "연계 수락 " + (outgoing + 1) + "/" + late);
            Check(Mathf.Abs(SwordIdleProgress() - expectedStart) < .00001f, "준비 시작/기존 offset 선택 " + (outgoing + 1) + "/" + late);
            Check(Mathf.Abs(Field<float>(melee, "activeAttackTransitionDuration") - .12f) < .000001f, "0.12초 콤보 전환");
            yield return CheckSwordIdleClip(combo.steps[outgoing + 1].animationClip, "연계 " + (outgoing + 2));
        }

        // Real virtual mouse hold, without clock edits: complete two loops through all four attacks.
        yield return Reset(); Send(false, false, true);
        int changes = 0, lastStep = -1;
        float limit = Time.unscaledTime + 20f;
        while (changes < 8 && Time.unscaledTime < limit)
        {
            if (melee.IsAttackInProgress && Field<int>(melee, "comboStepIndex") != lastStep)
            {
                lastStep = Field<int>(melee, "comboStepIndex"); changes++;
                Check(Field<AnimationClip>(melee, "activeAttackAnimationClip") == combo.steps[lastStep].animationClip,
                    "마우스 홀드 실제 콤보 " + changes);
            }
            yield return null;
        }
        Send(); Check(changes == 8, "실제 입력 콤보 2회 순환");
        yield return Wait(3.5f);
        Check(!melee.IsAttackInProgress && SwordIdleClipVisible(definition.animationProfile.combatIdleClip), "공격 종료 새 Idle 복귀");
        yield return Reset();
        Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "일반 강공 시작");
        yield return CheckSwordIdleClip(definition.heavyAttackDefinition.attack.animationClip, "일반 강공");
        yield return Wait(3f);
        Check(!melee.IsAttackInProgress, "일반 강공 종료");
        yield return Reset(); yield return StartDodge(false, true);
        float dodgeDeadline = Time.unscaledTime + 3f;
        while (Field<AnimationClip>(melee, "activeAttackAnimationClip") != definition.dodgeAttackDefinition.steps[0].animationClip
            && Time.unscaledTime < dodgeDeadline) yield return null;
        yield return CheckSwordIdleClip(definition.dodgeAttackDefinition.steps[0].animationClip, "닷지 약공 전용 슬롯");
        float comboDeadline = Time.unscaledTime + 4f;
        while (Field<AnimationClip>(melee, "activeAttackAnimationClip") != combo.steps[0].animationClip
            && Time.unscaledTime < comboDeadline) yield return null;
        yield return CheckSwordIdleClip(combo.steps[0].animationClip, "닷지 약공 뒤 일반1타");
        Send();
        yield return Reset(); EquipDashHeavyGem(WeaponElement.Fire);
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) { energy = actor.gameObject.AddComponent<OverburstElementEnergy>(); fixtures.Add(energy); }
        FillEnergy(energy, 61000);
        yield return StartDodge(false, false, true);
        float dashDeadline = Time.unscaledTime + 4f;
        while (Field<AnimationClip>(melee, "activeAttackAnimationClip") != definition.dashHeavyAttackDefinition.attack.animationClip
            && Time.unscaledTime < dashDeadline) yield return null;
        yield return CheckSwordIdleClip(definition.dashHeavyAttackDefinition.attack.animationClip, "대시 강공");
        Send();
        float endDeadline = Time.unscaledTime + 10f;
        while ((evade.IsEvading || melee.IsAttackInProgress || melee.IsDashHeavyWindupActive)
            && Time.unscaledTime < endDeadline) yield return null;
        Check(!evade.IsEvading && !melee.IsAttackInProgress && !melee.IsDashHeavyWindupActive, "대시 강공 정상 종료");
        EquipDashHeavyGem(WeaponElement.Fire); yield return Frames(3);
        yield return ParryFollowUp();
        yield return Reset();
        yield return WaitForSwordIdle(definition.animationProfile.combatIdleClip);
        Check(SwordIdleClipVisible(definition.animationProfile.combatIdleClip), "닷지·대시·패링 후 Idle 복귀");
        Progress("SwordIdle gameplay completed");
    }
}
