using System;
using System.Collections;
using UnityEditor;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    const string DodgeLightCooldownKey = "Overburst.PlayerEvadeVerifier.DodgeLightCooldown";

    public static void StartDodgeLightCooldownIsolated(string directory)
    {
        SessionState.SetBool(DodgeLightCooldownKey, true);
        try { StartIsolated(directory); }
        catch { SessionState.EraseBool(DodgeLightCooldownKey); throw; }
    }

    static IEnumerator CooldownWaitForLight()
    {
        float limit = Time.unscaledTime + 3f;
        while (melee.ActiveDodgeFollowUp != PlayerDodgeFollowUpKind.Light && Time.unscaledTime < limit)
            yield return null;
        Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "실제 입력 대시 약공 실행");
    }

    static IEnumerator CooldownWaitUntilReady()
    {
        float limit = Time.unscaledTime + 3f;
        while (melee.DodgeLightCooldownRemaining > 0f && Time.unscaledTime < limit) yield return null;
        Check(melee.DodgeLightCooldownRemaining == 0f, "1.2초 만료 후 대시 약공 준비 완료");
    }

    static IEnumerator VerifyDodgeLightCooldown()
    {
        Check(Mathf.Approximately(MeleeRuntime.DodgeLightInternalCooldown, 1.2f), "대시 약공 내부 쿨타임 1.2초");
        yield return Reset();
        yield return CooldownWaitUntilReady();
        yield return StartDodge(false, true);
        Send();
        float limit = Time.unscaledTime + 2f;
        while (!melee.IsDodgeLightWindupActive && Time.unscaledTime < limit) yield return null;
        Check(melee.IsDodgeLightWindupActive && !melee.IsAttackInProgress, "실제 공격 전 준비 모션");
        evade.CancelForKnockdown();
        yield return Frames(2);
        Check(melee.DodgeLightCooldownRemaining == 0f, "준비 중 취소는 쿨타임을 소비하지 않음");

        for (int repeat = 0; repeat < 2; repeat++)
        {
            yield return Reset();
            yield return CooldownWaitUntilReady();
            yield return StartDodge(false, true);
            Send();
            yield return CooldownWaitForLight();
            float consumedAt = Field<float>(melee, "nextDodgeLightTime") - MeleeRuntime.DodgeLightInternalCooldown;
            float remaining = melee.DodgeLightCooldownRemaining;
            samples.Add(new { repeat, phase = "accepted", consumedAt, now = OverburstGameClock.UnscaledTime, remaining });
            Check(remaining > 0f && remaining <= 1.2001f,
                "실제 실행 시에만 1.2초 소비 " + repeat + " remaining=" + remaining);
            int swings = Field<int>(melee, "dodgeLightSwingSequence");

            yield return StartDodge(false, true);
            Send();
            Check(evade.ActiveType == PlayerEvadeType.CombatDodge, "쿨타임 중 두 번째 대시는 허용 " + repeat);
            limit = Time.unscaledTime + 2f;
            while (evade.IsEvading && Time.unscaledTime < limit)
            {
                Check(!melee.IsDodgeLightWindupActive, "두 번째 대시 중 약공 준비 차단 " + repeat);
                yield return null;
            }
            yield return Frames(2);
            Check(!evade.IsEvading && evade.LastEndWasCompleted, "두 번째 대시 정상 종료 " + repeat);
            Check(melee.ActiveDodgeFollowUp != PlayerDodgeFollowUpKind.Light
                && Field<int>(melee, "dodgeLightSwingSequence") == swings,
                "연속 대시 약공·휘두름 소리 차단 " + repeat);
            Check(melee.DodgeLightCooldownRemaining > 0f, "대시 취소 뒤에도 약공 쿨타임 유지 " + repeat);

            Send();
            yield return Frames(2);
            Send(false, false, true);
            limit = Time.unscaledTime + .3f;
            while (!melee.IsAttackInProgress && Time.unscaledTime < limit) yield return null;
            Check(melee.IsAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None,
                "쿨타임 중 일반 약공 허용 " + repeat + " held=" + input.AttackHeld
                + " release=" + Field<bool>(input.CombatInputs, "attackNeedsRelease"));
            Send();
            melee.CancelCurrentAttackState();

            if (repeat == 0)
            {
                float before = melee.DodgeLightCooldownRemaining;
                OverburstGameMenu.Instance.Open();
                yield return Wait(.35f);
                Check(OverburstTimeEffectArbiter.IsPaused
                    && Mathf.Abs(melee.DodgeLightCooldownRemaining - before) < .05f,
                    "메뉴 정지 동안 내부 쿨타임 정지");
                OverburstGameMenu.Instance.Close();
            }

            yield return CooldownWaitUntilReady();
            yield return StartDodge(false, true);
            Send();
            yield return CooldownWaitForLight();
            float secondAt = Field<float>(melee, "nextDodgeLightTime") - MeleeRuntime.DodgeLightInternalCooldown;
            Check(secondAt - consumedAt >= 1.2f, "재실행 간격 1.2초 이상 " + repeat);
            samples.Add(new { repeat, consumedAt, secondAt, interval = secondAt - consumedAt });
        }

        // Heavy follow-up uses its own contract even while the light opener is cooling down.
        Send();
        melee.CancelCurrentAttackState();
        input.CombatInputs.Invalidate();
        ActorTeleportUtility.TeleportSafely(actor.transform, origin, Quaternion.LookRotation(forward));
        yield return Frames(3);
        Check(melee.DodgeLightCooldownRemaining > .5f, "강공 검사 시작 시 약공 쿨타임 유지");
        yield return StartDodge(false, false, true);
        Send();
        limit = Time.unscaledTime + 3f;
        while (melee.ActiveDodgeFollowUp != PlayerDodgeFollowUpKind.Heavy && Time.unscaledTime < limit)
        {
            samples.Add(new { phase = "heavy", now = OverburstGameClock.UnscaledTime,
                remaining = melee.DodgeLightCooldownRemaining, evade = evade.IsEvading, movement.IsGrounded,
                windup = melee.IsDashHeavyWindupActive, pending = input.CombatInputs.PendingDodgeFollowUp.ToString(),
                active = melee.ActiveDodgeFollowUp.ToString() });
            yield return null;
        }
        Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy,
            "약공 쿨타임 중 대시 강공 허용 grounded=" + movement.IsGrounded);
        Progress("DodgeLightCooldownCompleted");
    }
}
