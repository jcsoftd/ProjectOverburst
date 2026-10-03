using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public static partial class PlayerEvadeVerifier
{
    static IEnumerator VerifyHeavyFocusMotions()
    {
        var report = new List<object>();
        foreach (bool parried in new[] { false, true })
        foreach (float fraction in new[] { 0f, 1f })
        {
            yield return Reset(); EquipDashHeavyGem(parried ? WeaponElement.Ice : WeaponElement.Fire);
            var energy = actor.GetComponent<OverburstElementEnergy>();
            if (energy == null) energy = actor.gameObject.AddComponent<OverburstElementEnergy>();
            energy.Clear();
            if (fraction > 0f) FillEnergy(energy, 90000 + (parried ? 100 : 0));
            Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "집중 검증 실제 일반 강공 시작");
            if (parried) { yield return Frames(2); melee.NotifyHeavyParried(Field<int>(melee, "activeActionId")); }
            float limit = Time.unscaledTime + 8f, minimumScale = 1f; int visible = 0;
            var rows = new List<object>();
            while (melee.IsAttackInProgress)
            {
                Check(Time.unscaledTime < limit, "집중 강공 완주 시간 제한");
                var focus = actor.Equipment.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true);
                int heads = focus != null ? focus.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled) : 0;
                if (heads > 0) visible++;
                minimumScale = Mathf.Min(minimumScale, Time.timeScale);
                rows.Add(new { frame = Time.frameCount, time = Time.time, real = Time.unscaledTime, scale = Time.timeScale, heads,
                    progress = Field<MeleeComboStepData>(melee, "activeAttackStep").playbackAcceleration.ToClipProgress((Time.time - Field<float>(melee, "attackStartTime")) / Field<float>(melee, "attackDuration")) });
                yield return null;
            }
            yield return Frames(2);
            Check(visible > 0 && minimumScale >= .749f && minimumScale <= .8f, "일반/패링·빈/가득 원소 집중 렌더와 약한 감속 " + parried + "/" + fraction);
            Check(!OverburstTimeEffectArbiter.IsActive && Mathf.Approximately(Time.timeScale, 1f), "집중 완료 후 시간 반환");
            yield return Wait(.15f);
            var camera = QuarterViewCamera.ActiveInstance;
            if (camera != null) Check((float)typeof(QuarterViewCamera).GetMethod("CurrentHeavyFocusZoom", Private).Invoke(camera, null) < .0001f, "집중 완료 후 확대 반환");
            report.Add(new { parried, fraction, minimumScale, visible, rows });
        }
        foreach (bool pause in new[] { false, true })
        {
            yield return Reset(); EquipDashHeavyGem(WeaponElement.Electric);
            Check(melee.TryStartHeavyAttack(forward) == WeaponActionResult.Accepted, "집중 취소·정지 시험 시작");
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
