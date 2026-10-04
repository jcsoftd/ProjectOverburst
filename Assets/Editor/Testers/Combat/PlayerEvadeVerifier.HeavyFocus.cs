using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    const string HeavyFocusOnlyKey = "Overburst.PlayerEvadeVerifier.HeavyFocusOnly";
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterHeavyFocusModeReturn()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= ClearHeavyFocusModeOnReturn;
        UnityEditor.EditorApplication.playModeStateChanged += ClearHeavyFocusModeOnReturn;
        if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            ClearHeavyFocusModeOnReturn(UnityEditor.PlayModeStateChange.EnteredEditMode);
    }
    static void ClearHeavyFocusModeOnReturn(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.EnteredEditMode
            && UnityEditor.SessionState.GetString(PendingKey, "") == "")
            UnityEditor.SessionState.EraseBool(HeavyFocusOnlyKey);
    }
    public static void StartIsolatedHeavyFocus(string directory)
    {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating)
            throw new System.InvalidOperationException("유휴 Editor 필요");
        UnityEditor.SessionState.SetBool(HeavyFocusOnlyKey, true);
        try { StartIsolated(directory, false, false, false, true); }
        catch { UnityEditor.SessionState.EraseBool(HeavyFocusOnlyKey); throw; }
    }
    static IEnumerator StartFocusHeavy(string label)
    {
        float limit = Time.unscaledTime + 2f;
        WeaponActionResult result;
        do
        {
            result = melee.TryStartHeavyAttack(forward);
            if (result == WeaponActionResult.Accepted) break;
            Check(result == WeaponActionResult.RejectedNotReady && Time.unscaledTime < limit,
                label + " result=" + result + " grounded=" + movement.IsGrounded
                + " condition=" + actor.GetComponent<PlayerStateCoordinator>().CurrentCondition);
            yield return null;
        } while (true);
        Check(result == WeaponActionResult.Accepted, label);
    }
    static IEnumerator VerifyHeavyFocusMotions()
    {
        yield return Reset(); EquipDashHeavyGem(WeaponElement.Fire);
        yield return StartFocusHeavy("집중 측정 전 렌더 준비");
        float warmLimit = Time.unscaledTime + 8f;
        while (melee.IsAttackInProgress) { Check(Time.unscaledTime < warmLimit, "집중 준비 완주"); yield return null; }
        var energy = actor.GetComponent<OverburstElementEnergy>();
        if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
        var report = new List<object>();
        foreach (string motion in new[] { "normal", "parried", "dash" })
        foreach (float fraction in new[] { 0f, .799f, .8f, 1f })
        {
            yield return Reset(); EquipDashHeavyGem(motion == "parried" ? WeaponElement.Ice : WeaponElement.Fire);
            FillEnergy(energy, 90000);
            // Probe the exact presentation boundary independently of per-hit energy gain.
            typeof(OverburstElementEnergy).GetProperty(nameof(OverburstElementEnergy.Amount)).SetValue(energy, energy.BaseMaximum * fraction);
            Check(Mathf.Abs(energy.Normalized - fraction) < .00001f, "실제 게이지 경계 준비 " + fraction);
            bool eligible = fraction >= .8f;
            Check(DashHeavyFocusPresentation.CanBegin(actor.Equipment, new ElementGemAttackSnapshot(actor.Equipment)) == eligible, "실제 장비80% 조건 " + fraction);
            if (motion == "dash") { yield return StartDodge(false, false, true); Send(); }
            else
            {
                yield return StartFocusHeavy("집중 검증 실제 일반 강공 시작 " + motion + "/" + fraction);
                if (motion == "parried") { yield return Frames(2); melee.NotifyHeavyParried(Field<int>(melee, "activeActionId")); }
            }
            float limit = Time.unscaledTime + 8f, minimumScale = 1f, zoomMaximum = 0f, pulseFinished = -1f;
            int visible = 0, slowFrames = 0, maximumHeads = 0;
            var rows = new List<object>();
            do
            {
                if (Time.unscaledTime >= limit)
                {
                    File.WriteAllText(Path.Combine(output, "FocusTimeout.json"), JsonConvert.SerializeObject(new { motion, fraction,
                        scale = Time.timeScale, kind = OverburstTimeEffectArbiter.ActiveKind, movement.IsGrounded,
                        attacking = melee.IsAttackInProgress, evading = evade.IsEvading, windup = melee.IsDashHeavyWindupActive,
                        duration = Field<float>(melee, "attackDuration"), start = Field<float>(melee, "attackStartTime"), time = Time.time,
                        condition = actor.GetComponent<PlayerStateCoordinator>().CurrentCondition, rows }, Formatting.Indented));
                    Check(false, "집중 강공 완주 시간 제한 " + motion + "/" + fraction);
                }
                var focus = actor.Equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true);
                int heads = focus != null ? focus.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled) : 0;
                if (focus != null) Check(focus.GetComponentsInChildren<MeshRenderer>(true).Length == 24, "빛 알갱이24개 준비");
                if (heads > 0) visible++;
                maximumHeads = Mathf.Max(maximumHeads, heads);
                bool slow = OverburstTimeEffectArbiter.ActiveKind == OverburstTimeEffectKind.HeavyFocus;
                if (slow) { slowFrames++; minimumScale = Mathf.Min(minimumScale, Time.timeScale); }
                float age = focus != null && focus.enabled && Field<float>(focus, "focusStartedAt") >= 0f
                    ? OverburstGameClock.UnscaledTime - Field<float>(focus, "focusStartedAt") : -1f;
                var camera = QuarterViewCamera.ActiveInstance;
                float zoom = camera != null ? (float)typeof(QuarterViewCamera).GetMethod("CurrentHeavyFocusZoom", Private).Invoke(camera, null) : 0f;
                zoomMaximum = Mathf.Max(zoomMaximum, zoom);
                if (age >= .2f && !slow && pulseFinished < 0f) pulseFinished = age;
                if (age > .2f + Time.unscaledDeltaTime * 2f) Check(!slow, "실제0.2초 뒤 집중 반환");
                rows.Add(new { frame = Time.frameCount, real = OverburstGameClock.UnscaledTime, scale = Time.timeScale, heads, age, slow, zoom });
                yield return null;
            } while (evade.IsEvading || melee.IsDashHeavyWindupActive || melee.IsAttackInProgress);
            yield return Frames(2);
            File.WriteAllText(Path.Combine(output, "Focus_" + motion + "_" + fraction.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + ".json"),
                JsonConvert.SerializeObject(new { motion, fraction, minimumScale, visible, slowFrames, maximumHeads, pulseFinished, zoomMaximum, rows }, Formatting.Indented));
            if (eligible)
            {
                Check(visible > 0 && maximumHeads > 9 && slowFrames > 0 && minimumScale >= .299f && minimumScale <= .32f,
                    "80% 이상24빛·30% 실제 감속 " + motion + "/" + fraction + " min=" + minimumScale);
                Check(pulseFinished >= .2f && pulseFinished < .28f, "실시간0.2초 보간 반환 " + motion + "/" + fraction + " elapsed=" + pulseFinished);
                if (QuarterViewCamera.ActiveInstance != null) Check(zoomMaximum > 0f, "80% 이상 집중 확대");
            }
            else Check(visible == 0 && slowFrames == 0 && zoomMaximum < .0001f, "80% 미만 모임·슬로우·확대 없음 " + motion + "/" + fraction);
            Check(!OverburstTimeEffectArbiter.IsActive && Mathf.Approximately(Time.timeScale, 1f), "집중 완료 후 시간 반환");
            yield return Wait(.15f);
            var returnedCamera = QuarterViewCamera.ActiveInstance;
            if (returnedCamera != null) Check((float)typeof(QuarterViewCamera).GetMethod("CurrentHeavyFocusZoom", Private).Invoke(returnedCamera, null) < .0001f, "집중 완료 후 확대 반환");
            report.Add(new { motion, fraction, minimumScale, visible, slowFrames, maximumHeads, pulseFinished, zoomMaximum, rows });
            Progress("focus80 " + motion + "/" + fraction);
        }
        foreach (bool pause in new[] { false, true })
        {
            yield return Reset(); EquipDashHeavyGem(WeaponElement.Electric); FillEnergy(energy, 95000);
            yield return StartFocusHeavy("집중 취소·정지 시험 시작");
            float limit = Time.unscaledTime + 5f;
            while (OverburstTimeEffectArbiter.ActiveKind != OverburstTimeEffectKind.HeavyFocus)
            { Check(Time.unscaledTime < limit && melee.IsAttackInProgress, "집중 구간 도달"); yield return null; }
            if (pause)
            {
                OverburstGameMenu.Instance.Open(); float frozen = Time.time; yield return Frames(6);
                Check(Time.timeScale == 0 && Time.time == frozen, "메뉴는 집중보다 우선"); OverburstGameMenu.Instance.Close(); yield return Frames(1);
            }
            melee.CancelCurrentAction(); yield return Frames(2);
            var focus = actor.Equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true);
            Check(focus != null && !focus.enabled && !focus.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "취소 후 소유 효과 재사용 대기");
            Check(!OverburstTimeEffectArbiter.IsActive && Mathf.Approximately(Time.timeScale, 1f), "취소·메뉴 후 시간 반환");
            yield return Wait(.15f);
            var camera = QuarterViewCamera.ActiveInstance;
            if (camera != null) Check((float)typeof(QuarterViewCamera).GetMethod("CurrentHeavyFocusZoom", Private).Invoke(camera, null) < .0001f, "취소·메뉴 후 확대 반환");
        }
        File.WriteAllText(Path.Combine(output, "HeavyFocusRuntime.json"), JsonConvert.SerializeObject(new { status = "PASS", report }, Formatting.Indented));
    }
}
