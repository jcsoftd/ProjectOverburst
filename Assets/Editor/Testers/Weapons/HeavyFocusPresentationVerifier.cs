using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class HeavyFocusPresentationVerifier
{
    public static void VerifyAssets(string output, bool includeDashAssets = true)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("유휴 Editor 필요");
        Directory.CreateDirectory(output); var checks = new List<string>();
        void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks.Add(name); }
        var tail = Resources.Load<Material>(DashHeavyFocusPresentation.MaterialResource);
        var head = Resources.Load<Material>(DashHeavyFocusPresentation.HeadMaterialResource);
        var mesh = Resources.Load<Mesh>(DashHeavyFocusPresentation.HeadMeshResource);
        Require(tail != null && head != null && mesh != null, "제품 효과 자산 로드");
        Require(tail.shader == head.shader && !ShaderUtil.ShaderHasError(tail.shader) && tail.shader.isSupported, "URP 빛 모임 셰이더");
        Require(tail.GetFloat("_Mode") == 0 && head.GetFloat("_Mode") == 1 && mesh.vertexCount == 4, "곡선 궤적과 부드러운 빛 알갱이 분리");
        var definition = AssetDatabase.LoadAssetAtPath<MeleeWeaponDefinition>(PlayerEvadeBuilder.DefinitionPath);
        var normal = definition.heavyAttackDefinition; var parried = definition.parriedHeavyAttackDefinition;
        var windows = new[] { HeavyFocusWindow.Dash, HeavyFocusWindow.Ground(normal.attack.animationClip.length), HeavyFocusWindow.Parried(parried.attack.animationClip.length) };
        for (int k = 0; k < windows.Length; k++)
        {
            var window = windows[k];
            Require(window.TimeScale(window.Start - .1f) == 1 && window.TimeScale(window.End + .3f) == 1, "감속 시작·끝 정상 복귀 " + k);
            for (int i = 0; i <= 1000; i++)
            {
                float source = Mathf.Lerp(window.Start - .1f, window.End + .3f, i / 1000f);
                float expectedScale = .3f;
                Require(window.TimeScale(source) >= expectedScale - .00001f && window.TimeScale(source) <= 1f, "동작별 정지 없는 집중 감속 " + k + "/" + i);
                Require(window.Zoom(source) >= 0f && window.Zoom(source) <= .04501f, "작은 줌 범위 " + k + "/" + i);
            }
        }
        foreach (var window in windows)
        {
            Require(window.PulseScale(0f) == 1f && window.PulseScale(.2f) == 1f && Mathf.Abs(window.PulseScale(.1f) - .3f) < .00001f, "0.2초 펄스와 30% 최저 속도");
            foreach (float speed in new[] { .5f, 1f, 1.5f, 2.5f })
            {
                float end = window.Start + window.ScaledPulseDuration * speed;
                Require(Mathf.Abs(window.UnscaledDuration(window.Start, end, false, speed) - .2f) < .00001f, "공속별 실제 0.2초 " + speed);
            }
        }
        foreach (var window in windows)
        {
            Require(Mathf.Abs(window.PulseScale(.04f) - .65f) < .00001f
                && Mathf.Abs(window.PulseScale(.16f) - .65f) < .00001f, "진입·복귀0.08초 보간 중간 속도");
            Require(Mathf.Abs(window.PulseScale(.00001f) - 1f) < .00001f
                && Mathf.Abs(window.PulseScale(.19999f) - 1f) < .00001f, "경계 속도 연속");
        }
        Require(DashHeavyFocusPresentation.MinimumEnergyFraction == .8f, "원소 게이지80% 시작 조건");
        float previous = -1;
        for (int i = 0; i <= 2000; i++)
        {
            float elapsed = i / 1000f, source = DashHeavyFocusClock.Sample(elapsed);
            Require(source > previous, "대시 원본 포즈 프레임 정지 없음 " + i); previous = source;
            Require(Mathf.Abs(DashHeavyFocusClock.RealAt(source) - elapsed) < .00001f, "포즈·판정 시계 역변환 " + i);
        }
        foreach (WeaponElement element in new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light })
            Require(DashHeavyFocusPresentation.ColorFor(element) != Color.white, "원소 색상 " + element);
        Require(windows[1].Start < normal.attack.animationClip.length * .23f && windows[1].End > normal.attack.animationClip.length * .23f, "일반 강공 점프 최고점 포함");
        Require(windows[1].TimeScale(normal.attack.attackPhases[normal.SafeDischargePhaseIndex].SafeStart * normal.attack.animationClip.length) == 1f,
            "일반 강공 타격 전에 집중 감속 복귀");
        Require(windows[2].End < parried.attack.attackPhases[parried.SafeDischargePhaseIndex].SafeStart * parried.attack.animationClip.length, "패링 강공 최종 타격 전에 모임 완료");
        if (includeDashAssets) PlayerEvadeVerifier.VerifyDashHeavyAssets(Path.Combine(output, "DashAssets"));
        File.WriteAllText(Path.Combine(output, "Assets.json"), JsonConvert.SerializeObject(new { status = "PASS", count = checks.Count, checks }, Formatting.Indented));
    }
}
