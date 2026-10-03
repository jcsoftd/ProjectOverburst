using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

public static class HeavyFocusPresentationVerifier
{
    public static void VerifyAssets(string output)
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
                Require(window.TimeScale(source) >= .74999f && window.TimeScale(source) <= 1f, "정지 없는 75%~100% 감속 " + k + "/" + i);
                Require(window.Zoom(source) >= 0f && window.Zoom(source) <= .04501f, "작은 줌 범위 " + k + "/" + i);
            }
        }
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
        Require(windows[2].End < parried.attack.attackPhases[parried.SafeDischargePhaseIndex].SafeStart * parried.attack.animationClip.length, "패링 강공 최종 타격 전에 모임 완료");
        PlayerEvadeVerifier.VerifyDashHeavyAssets(Path.Combine(output, "DashAssets"));
        File.WriteAllText(Path.Combine(output, "Assets.json"), JsonConvert.SerializeObject(new { status = "PASS", count = checks.Count, checks }, Formatting.Indented));
    }
}
