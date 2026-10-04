using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Overburst.EditorTools.BossMaker
{
    internal static class BossMakerReview
    {
        public static List<string> Changes(BossMakerDraft draft)
        {
            var result = new List<string>(); var a = draft.Source; var b = draft.Material;
            void Add(string label, string before, string after) { if (before != after) result.Add(label + ": " + before + " → " + after); }
            string Number(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            Add("피해 계수", Number(a.DamageMultiplier), Number(b.DamageMultiplier));
            Add("공격 속도", Number(a.AnimationSpeedMultiplier), Number(b.AnimationSpeedMultiplier));
            Add("바닥 전조", a.showTelegraph ? "표시" : "숨김", b.showTelegraph ? "표시" : "숨김");
            Add("전진 거리", Number(a.advanceDistance) + "m", Number(b.advanceDistance) + "m");
            Add("전진 구간", a.advanceWindow.ToString("F3"), b.advanceWindow.ToString("F3"));
            Add("표적 추적", a.tracksTargetDuringWindup.ToString(), b.tracksTargetDuringWindup.ToString());
            Add("조준 고정", Number(a.aimLockLeadSeconds) + "s", Number(b.aimLockLeadSeconds) + "s");
            Add("발사 소켓", a.muzzleBone, b.muzzleBone); Add("발사 오프셋", a.muzzleOffset.ToString("F3"), b.muzzleOffset.ToString("F3"));
            Add("비행체 반경", Number(a.projectileRadius), Number(b.projectileRadius)); Add("비행 속도", Number(a.projectileSpeed), Number(b.projectileSpeed));
            Add("비행 시간", Number(a.flightSeconds), Number(b.flightSeconds)); Add("포물선 높이", Number(a.arcHeight), Number(b.arcHeight));
            for (int i = 0; i < b.strikes.Length; i++)
            {
                var x = a.strikes[i]; var y = b.strikes[i];
                Add((i + 1) + "타 범위", Geometry(x), Geometry(y));
                Add((i + 1) + "타 중심", x.localOrigin.ToString("F3"), y.localOrigin.ToString("F3"));
                Add((i + 1) + "타 방향", Number(x.yaw) + "°", Number(y.yaw) + "°");
                Add((i + 1) + "타 높이", Number(x.minimumHeight) + "~" + Number(x.maximumHeight), Number(y.minimumHeight) + "~" + Number(y.maximumHeight));
                Add((i + 1) + "타 시점", Number(x.impact * draft.FrameCount) + "F", Number(y.impact * draft.FrameCount) + "F");
                Add((i + 1) + "타 판정 끝", Number(x.contactEnd * draft.FrameCount) + "F", Number(y.contactEnd * draft.FrameCount) + "F");
                var xp = a.tuning?.parries != null && i < a.tuning.parries.Length ? a.tuning.parries[i] : null; var yp = b.tuning.parries[i];
                Add((i + 1) + "타 패링", xp == null || xp.canParry ? "허용" : "회피", yp.canParry ? "허용" : "회피");
                string Window(EnemyBossStrikeParryTuning p) => p != null && p.overrideWindow ? Number(p.startNormalized * draft.FrameCount) + "~" + Number(p.endNormalized * draft.FrameCount) + "F" : "실시간 기본 구간";
                Add((i + 1) + "타 패링 구간", Window(xp), Window(yp));
            }
            using (var before = new SerializedObject(draft.AbilitySource)) using (var after = new SerializedObject(draft.Ability))
                foreach (var pair in new[] { ("damage", "고정 피해"), ("referencePatternDamagePercent", "기준 체력 비율"), ("cooldown", "쿨다운"), ("minimumRange", "최소 거리"), ("range", "최대 거리"), ("weight", "선택 가중치"), ("preparationDuration", "준비 시간"), ("releaseDuration", "타격 시간"), ("recoveryDuration", "회복 시간"), ("minimumRecoveryTime", "최소 회복") })
                    Add(pair.Item2, Number(before.FindProperty(pair.Item1).floatValue), Number(after.FindProperty(pair.Item1).floatValue));
            Add("공격 패링 허용", draft.AbilitySource.IsParryable.ToString(), draft.Ability.IsParryable.ToString());
            Add("시간 재생 방식", draft.AbilitySource.UsesPacedTimeline ? "준비·타격·회복" : "원본 모션", draft.Ability.UsesPacedTimeline ? "준비·타격·회복" : "원본 모션");
            if (a.assemblyNotes != b.assemblyNotes) result.Add("조립 메모 변경");
            return result;
        }
        static string Geometry(EnemyBossMaterialStrike s) => s.shape == GroundIndicatorShape.Rectangle ? $"사각형 {s.width:0.###}×{s.length:0.###}m"
            : s.shape == GroundIndicatorShape.Donut ? $"도넛 {s.innerRadius:0.###}~{s.radius:0.###}m"
            : s.shape == GroundIndicatorShape.Sector ? $"부채꼴 {s.radius:0.###}m / {s.angle:0.###}° / 내부 {s.innerRadius:0.###}m" : $"원형 {s.radius:0.###}m";
    }
}
