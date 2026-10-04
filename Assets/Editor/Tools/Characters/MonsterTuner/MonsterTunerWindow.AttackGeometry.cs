using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.MonsterTuner
{
    public sealed partial class MonsterTunerWindow
    {
        private Label attackGeometrySummary;
        internal static bool UsesBossMaterial(EnemyActor actor, EnemyAbilityDefinition ability)
        {
            var collection = actor != null ? actor.GetComponent<EnemyBossMaterialExecutor>()?.Collection : null;
            if (collection == null || ability == null) return false;
            foreach (var material in collection.attacks)
                if (material != null && material.IsValid && material.ability != null
                    && (material.ability == ability || material.ability.AbilityId == ability.AbilityId)) return true;
            return false;
        }
        internal static bool UsesContactGeometry(EnemyAbilityDefinition ability) => ability != null
            && EnemyAbilityDefinition.IsWeakMeleeExecution(ability.ExecutionMode)
            && ability.HasWeakAttackExecution && ability.WeakAttackExecution.HasContactGeometry;

        internal static Vector3 AttackCenter(EnemyActor actor, EnemyAbilityDefinition ability)
        {
            if (actor == null) return Vector3.zero;
            if (ability != null && ability.ExecutionMode == EnemyAbilityExecutionMode.Charge && !UsesContactGeometry(ability))
                return actor.transform.position + Vector3.up * .8f;
            var point = actor.Melee != null ? actor.Melee.AttackPoint : null;
            return EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability,
                point != null ? point.position : actor.transform.position);
        }

        internal static IEnumerable<(Vector3, Vector3)> AttackSegments(EnemyActor actor, EnemyAbilityDefinition ability, float? normalizedTime = null)
        {
            if (actor == null || ability == null || UsesBossMaterial(actor, ability)) yield break;
            if (UsesContactGeometry(ability))
            {
                var profile = ability.WeakAttackExecution;
                float time = normalizedTime ?? ability.HitNormalizedTime;
                for (int phase = 0; phase < profile.ContactGeometryCount; phase++)
                {
                    var geometry = profile.GetContactGeometry(phase);
                    if (geometry == null) continue;
                    for (int i = 0; i < geometry.CapsuleCount; i++)
                    {
                        if (!geometry.TryEvaluateCapsule(i, time, out var capsule)) continue;
                        // Authored contact coordinates are already final game-size metres.
                        // TransformPoint would apply the actor scale a second time.
                        Vector3 a = actor.transform.position + actor.transform.rotation * capsule.A;
                        Vector3 b = actor.transform.position + actor.transform.rotation * capsule.B;
                        var axis = b - a;
                        var rotation = axis.sqrMagnitude > .000001f
                            ? Quaternion.FromToRotation(Vector3.up, axis.normalized) : Quaternion.identity;
                        foreach (var segment in CapsuleSegments((a + b) * .5f, rotation, capsule.Radius, axis.magnitude * .5f))
                            yield return segment;
                    }
                }
                yield break;
            }

            Vector3 center = AttackCenter(actor, ability);
            float radius = EnemyAttackThreatGeometry.ResolveRadius(actor, ability);
            float angle = EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability);
            if (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
            {
                float length = Mathf.Max(.8f, radius);
                foreach (var segment in CapsuleSegments(center + actor.transform.forward * length * .5f,
                    actor.transform.rotation * Quaternion.FromToRotation(Vector3.up, Vector3.forward),
                    EnemyAttackThreatGeometry.ChargeHalfWidth, length * .5f)) yield return segment;
                yield break;
            }

            float inner = EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor, ability);
            Vector3 Point(float degrees, float reach) => center
                + Quaternion.AngleAxis(degrees, Vector3.up) * actor.transform.forward * reach;
            const int count = 48;
            for (int i = 0; i < count; i++)
            {
                float start = -angle * .5f + angle * i / count;
                float end = -angle * .5f + angle * (i + 1) / count;
                yield return (Point(start, radius), Point(end, radius));
                if (inner > 0f) yield return (Point(start, inner), Point(end, inner));
            }
            if (angle < 359.9f)
            {
                yield return (Point(-angle * .5f, inner), Point(-angle * .5f, radius));
                yield return (Point(angle * .5f, inner), Point(angle * .5f, radius));
            }
        }

        private void BuildAttackGeometryFields(string address)
        {
            bool contact = UsesContactGeometry(workingAbility);
            Heading("공격 판정");
            Field(address, "hitRadius", "원본 반경", !contact);
            Field(address, "hitAngle", "원본 각도", !contact);
            Field(address, "verticalTolerance", "높이 허용", !contact);
            Field(address, "range", "발동 거리", !workingAbility.HasWeakAttackExecution);
            attackGeometrySummary = new Label { name = "attack-geometry-summary" };
            attackGeometrySummary.AddToClassList("mt-note"); fields.Add(attackGeometrySummary);
            RefreshAttackGeometrySummary();
        }

        private void RefreshAttackGeometrySummary()
        {
            if (attackGeometrySummary == null || workingAbility == null || stage.Enemy == null) return;
            var lines = new List<string>();
            if (workingAbility.HasWeakAttackExecution)
                lines.Add("실효 발동 거리 " + EnemyAttackThreatGeometry.ResolveStartRange(stage.Enemy, workingAbility).ToString("F2")
                    + "m · 약공 프로필 " + workingAbility.WeakAttackExecution.name);
            if (UsesContactGeometry(workingAbility))
            {
                lines.Add("접촉 캡슐은 공격 모션의 접촉 프레임 구간에서 표시합니다. 모션 재생 전에는 첫 타격 자세의 판정을 표시합니다.");
                lines.Add("반경·각도·높이·발동 거리는 연결된 약공 프로필의 접촉 판정과 접근 거리를 사용합니다.");
                var profile = workingAbility.WeakAttackExecution;
                for (int i = 0; i < profile.ContactWindowCount; i++)
                    if (profile.TryGetContactWindow(i, out var window))
                        lines.Add("접촉 " + (i + 1) + " · " + AttackPreviewSeconds(window.x).ToString("F3") + "–"
                            + AttackPreviewSeconds(window.y).ToString("F3") + "s · "
                            + (profile.GetContactGeometry(i)?.CapsuleCount ?? 0) + "개 캡슐");
            }
            else if (UsesBossMaterial(stage.Enemy, workingAbility))
                lines.Add("이 공격은 보스 전용 실행기의 개별 판정을 사용합니다. 공통 범위 프리뷰를 표시하지 않습니다.");
            else
            {
                lines.Add("실효 반경 " + EnemyAttackThreatGeometry.ResolveRadius(stage.Enemy, workingAbility).ToString("F2")
                    + "m · 각도 " + EnemyAttackThreatGeometry.ResolveHitAngle(stage.Enemy, workingAbility).ToString("F0") + "°");
                float inner = EnemyAttackThreatGeometry.ResolveSectorInnerRadius(stage.Enemy, workingAbility);
                if (inner > 0f) lines.Add("안쪽 제외 반경 " + inner.ToString("F2") + "m · 몬스터 중심 기준");
            }
            attackGeometrySummary.text = string.Join("\n", lines);
        }

        private float AttackPreviewSeconds(float normalized) => workingAbility.ResolveWindupDelay(stage.AttackSpeed)
            + workingAbility.ResolvePacedTime(normalized, stage.AttackSpeed);
    }
}
