using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    const string DashHeavyRecoveryOnlyKey = "Overburst.PlayerEvadeVerifier.DashHeavyRecoveryOnly";
    static Vector3 dashAttackPointerDirection;

    [InitializeOnLoadMethod]
    static void RegisterDashHeavyRecoveryReturn()
    {
        EditorApplication.playModeStateChanged -= ClearDashHeavyRecoveryMode;
        EditorApplication.playModeStateChanged += ClearDashHeavyRecoveryMode;
        if (!EditorApplication.isPlayingOrWillChangePlaymode) ClearDashHeavyRecoveryMode(PlayModeStateChange.EnteredEditMode);
    }
    static void ClearDashHeavyRecoveryMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(PendingKey, "") == "")
            SessionState.EraseBool(DashHeavyRecoveryOnlyKey);
    }
    public static void StartIsolatedDashHeavyRecovery(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 Editor 필요");
        SessionState.SetBool(DashHeavyRecoveryOnlyKey, true);
        try { StartIsolated(directory, false, false, false, true); }
        catch { SessionState.EraseBool(DashHeavyRecoveryOnlyKey); throw; }
    }
    static IEnumerator VerifyDashHeavyRecovery()
    {
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var report = new List<object>();
        var targets = new List<GameObject>();
        try
        {
            foreach (string follow in new[] { "none", "move", "light", "heavy" })
            {
                yield return Reset();
                EquipDashHeavyGem(follow == "heavy" ? WeaponElement.Light : WeaponElement.Fire);
                FillEnergy(energy, 110000);
                if (follow == "none") typeof(OverburstElementEnergy).GetProperty(nameof(OverburstElementEnergy.Amount)).SetValue(energy, 0f);
                Vector3 entry = actor.transform.position;
                bool move = follow == "move";
                dashAttackPointerDirection = Quaternion.AngleAxis(follow == "heavy" ? -60f : 90f, Vector3.up) * forward;
                Vector3 expectedAttack = dashAttackPointerDirection;
                yield return StartDodge(move, false, true); Send(move);
                float limit = Time.unscaledTime + 8f;
                while (!melee.IsDashHeavyWindupActive && evade.IsEvading)
                { Check(Time.unscaledTime < limit, "대시 준비 제한"); yield return null; }
                Check(melee.IsDashHeavyWindupActive, "판정 전 대시 준비 확보");
                var step = actor.Equipment.CurrentWeaponData.GetMeleeDefinition().dashHeavyAttackDefinition.attack;
                var plan = Field<DashHeavyTravelPlan>(melee, "dashHeavyTravel");
                float speed = Field<float>(melee, "dashHeavyPlaybackSpeed");
                var stats = actor.Equipment.CurrentWeaponStats;
                var phase = step.attackPhases[0];
                var pattern = phase.ResolvePattern(stats.range, stats.meleeSlashAngle,
                    actor.Equipment.CurrentWeaponData.GetMeleeDefinition().baseSettings.hitWidth);
                Vector3 contact = entry + forward * plan.Position(plan.Start + DashHeavyFocusClock.RealAt(.5f) / speed);
                var hits = new List<int>();
                var packets = new List<object>();
                for (int i = 0; i < 3; i++)
                {
                    int index = i;
                    var target = new GameObject("Owned mouse-facing heavy target " + i); targets.Add(target); fixtures.Add(target);
                    target.transform.position = contact + expectedAttack * pattern.Range * (.15f + .30f * i);
                    var combat=target.AddComponent<CombatTarget>();
                    var health = target.GetComponent<CombatHealth>(); health.SetMaxHp(1000000, true); combat.Configure(CombatTeam.Enemy,false);
                    health.OnDamaged += (h, damage) => {
                        packets.Add(new { target=index, hp=h.CurrentHp, damage.triggersOnHitEffects, damage.playerAttackKind });
                        if (damage.triggersOnHitEffects && (damage.playerAttackKind & PlayerAttackKind.Heavy) != 0) hits.Add(index);
                    };
                }
                while (evade.IsEvading) { Check(Time.unscaledTime < limit, "대시 강공 인계 제한"); yield return null; }
                yield return Frames(1);
                Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy, "대시 강공 인계 " + follow);
                Check(Vector3.Dot(Field<Vector3>(melee, "activeAttackDirection"), expectedAttack) > .99f, "강공 마우스 방향 고정 " + follow);
                Check(Vector3.Dot(Field<Vector3>(melee, "dashHeavyTravelDirection"), forward) > .99f, "대시 이동 방향 독립 " + follow);
                int action = Field<int>(melee, "activeActionId");
                float start = Field<float>(melee, "attackStartTime"), duration = Field<float>(melee, "attackDuration");
                var waves = new HashSet<int>();
                float firstWave = -1f, exitProgress = 0f;
                dashAttackPointerDirection = -expectedAttack;
                bool nextInputSent = false;
                object phaseSnapshot = null;
                while (Field<int>(melee, "activeActionId") == action)
                {
                    Check(Time.unscaledTime < limit, "회수 동작 종료 제한 " + follow);
                    float progress = step.playbackAcceleration.ToClipProgress((Time.time - start) / duration);
                    if (phaseSnapshot == null && progress >= phase.SafeStart)
                    {
                        var executor=Field<AttackPhaseExecutor>(melee,"attackPhaseExecutor");
                        var states=Field<System.Collections.IList>(executor,"phases");
                        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public;
                        var state=states[0];
                        if ((bool)state.GetType().GetField("Started",flags).GetValue(state))
                        {
                            var actualBasis=(AttackPatternBasis)state.GetType().GetField("Basis",flags).GetValue(state);
                            var actualPattern=(AttackPatternRuntimeData)state.GetType().GetField("Pattern",flags).GetValue(state);
                            var snapshot=(AttackTargetSnapshot)state.GetType().GetField("Targets",flags).GetValue(state);
                            phaseSnapshot=new {origin=DashHeavyDiagnosticVector(actualBasis.Origin),facing=DashHeavyDiagnosticVector(actualBasis.Forward),
                                actualPattern.Range,actualPattern.Width,count=snapshot.Entries.Count,progress,source=actor.GetComponent<CombatTarget>().Team};
                        }
                    }
                    foreach (var wave in UnityEngine.Object.FindObjectsByType<SwordShockwavePlayback>(FindObjectsSortMode.None))
                        if (wave.name.StartsWith("PF_GRS_CircleShockwave") && wave.GetComponentsInChildren<Renderer>().Any(r => r.enabled))
                        {
                            waves.Add(wave.GetInstanceID());
                            if (firstWave < 0f) firstWave = progress * step.animationClip.length;
                        }
                    Check(Vector3.Dot(Field<Vector3>(melee, "activeAttackDirection"), expectedAttack) > .99f, "발동 후 포인터 변경 무시");
                    if (progress >= step.actionCancelStartNormalized - .04f && !nextInputSent)
                    {
                        nextInputSent = true;
                        Send(move, false, follow == "light", follow == "heavy");
                    }
                    if (progress < step.actionCancelStartNormalized - .025f)
                        Check(melee.IsHeavyAttackInProgress, "착지·순차 판정 전 회수 금지");
                    yield return null;
                }
                exitProgress = step.playbackAcceleration.ToClipProgress((Time.time - start) / duration);
                Send();
                File.WriteAllText(Path.Combine(output,"Recovery_"+follow+".json"),JsonConvert.SerializeObject(new {follow,
                    entry=DashHeavyDiagnosticVector(entry),contact=DashHeavyDiagnosticVector(contact),expected=DashHeavyDiagnosticVector(expectedAttack),
                    forward=DashHeavyDiagnosticVector(forward),phaseSnapshot,hits,packets,firstWave,exitProgress,
                    targets=targets.Select(t=>new{position=DashHeavyDiagnosticVector(t.transform.position),hp=t.GetComponent<CombatHealth>().CurrentHp,
                        team=t.GetComponent<CombatTarget>().Team,alive=t.GetComponent<CombatTarget>().IsAlive}).ToArray()},Formatting.Indented));
                Check(waves.Count == 1 && firstWave >= .48f && firstWave < .65f, "베기 충격파1회·판정 시작 일치 " + follow);
                Check(hits.SequenceEqual(new[] { 0, 1, 2 }), "마우스 방향으로 가까운 대상부터1회 순차 피해 " + string.Join(",", hits));
                float travel = Vector3.Dot(actor.transform.position - entry, forward);
                Check(Mathf.Abs(travel - 5f) < .2f, "독립 방향의 총5m 착지 완료 " + travel);
                if (follow == "none") Check(exitProgress >= .99f, "입력 없으면 후반 모션 완주");
                else Check(exitProgress >= step.actionCancelStartNormalized - .005f && exitProgress < .75f, "63프레임 이후 조기 회수 " + exitProgress);
                if (follow == "move") Check(!melee.IsAttackInProgress && !movement.IsMeleeAttackMoveLocked, "이동 전환·잠금 반환");
                if (follow == "light") Check(melee.IsAttackInProgress && !melee.IsHeavyAttackInProgress && Field<int>(melee, "comboStepIndex") == 0, "Idle 경유 없는 일반1타 연결");
                if (follow == "heavy") Check(melee.IsHeavyAttackInProgress && melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.None, "일반 강공으로 직접 연결");
                report.Add(new { follow, firstWave, shockwaves = waves.Count, exitProgress, travel, hits, directionDot = Vector3.Dot(expectedAttack, forward) });
                foreach (var target in targets) { fixtures.Remove(target); UnityEngine.Object.DestroyImmediate(target); }
                targets.Clear();
            }

            yield return Reset();
            dashAttackPointerDirection = Quaternion.AngleAxis(90f, Vector3.up) * forward;
            Vector3 expectedLight = dashAttackPointerDirection, lightEntry = actor.transform.position;
            yield return StartDodge(false, true); Send();
            float lightLimit = Time.unscaledTime + 5f;
            while (evade.IsEvading) { Check(Time.unscaledTime < lightLimit, "대시 약공 인계 제한"); yield return null; }
            yield return Frames(1);
            Check(melee.ActiveDodgeFollowUp == PlayerDodgeFollowUpKind.Light, "대시 약공 마우스 전환");
            Check(Vector3.Dot(Field<Vector3>(melee, "activeAttackDirection"), expectedLight) > .99f, "대시 약공 마우스 방향 판정");
            Check(Mathf.Abs(Vector3.Dot(actor.transform.position - lightEntry, forward) - 5f) < .15f, "약공 대시 이동5m 유지");
            dashAttackPointerDirection = -expectedLight;
            yield return Frames(3);
            Check(Vector3.Dot(Field<Vector3>(melee, "activeAttackDirection"), expectedLight) > .99f, "약공 발동 후 방향 고정");
            File.WriteAllText(Path.Combine(output, "DashHeavyRecoveryCases.json"), JsonConvert.SerializeObject(new { status = "PASS", cases = report, lightMouseFacing = true }, Formatting.Indented));
        }
        finally
        {
            Send(); dashAttackPointerDirection = Vector3.zero; melee.CancelCurrentAction();
            foreach (var target in targets) { fixtures.Remove(target); UnityEngine.Object.DestroyImmediate(target); }
        }
    }
}
