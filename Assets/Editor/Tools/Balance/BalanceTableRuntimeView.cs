using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Overburst.EditorBalance
{
    internal static class BalanceTableRuntimeView
    {
        internal static bool UsesContactGeometry(EnemyAbilityDefinition ability) => ability != null
            && EnemyAbilityDefinition.IsWeakMeleeExecution(ability.ExecutionMode)
            && ability.HasWeakAttackExecution && ability.WeakAttackExecution.HasContactGeometry;

        internal static bool UsesBossMaterial(EnemyAbilityDefinition ability, IEnumerable<EnemyDefinition> definitions)
        {
            if (ability == null) return false;
            foreach (var definition in definitions)
            {
                var collection = definition?.ActorPrefab?.GetComponent<EnemyBossMaterialExecutor>()?.Collection;
                if (collection == null) continue;
                foreach (var material in collection.attacks)
                    if (material != null && material.IsValid && material.ability != null
                        && (material.ability == ability || material.ability.AbilityId == ability.AbilityId)) return true;
            }
            return false;
        }

        internal static bool UsesChannelGeometry(EnemyAbilityDefinition ability, IEnumerable<EnemyDefinition> definitions)
        {
            if (ability == null || !ability.HasWeakAttackExecution || ability.ExecutionMode != EnemyAbilityExecutionMode.Zone) return false;
            foreach (var definition in definitions)
            {
                var executor = definition?.ActorPrefab?.GetComponent<EnemyChannelAbilityExecutor>();
                if (executor == null) continue;
                using (var serialized = new UnityEditor.SerializedObject(executor))
                {
                    var source = serialized.FindProperty("channelAbility")?.objectReferenceValue as EnemyAbilityDefinition;
                    if (source != null && source.AbilityId == ability.AbilityId) return true;
                }
            }
            return false;
        }

        internal static float[] DamageAllocations(EnemyAbilityDefinition ability, int level)
        {
            if (!ability.HasWeakAttackExecution)
                return Enumerable.Repeat(ability.ResolveDamage(level), ability.HitCount).ToArray();
            if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
            {
                float total = ability.UsesLevelDamageBudget
                    ? Mathf.Max(1f, OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)
                        * ability.ReferencePatternDamagePercent / 100f)) : ability.Damage;
                if (EnemyWeakProjectileDamageBudget.TryCreate(total, ability.HitCount, out var projectile))
                    return Enumerable.Range(0, projectile.Count).Select(projectile.ForPhase).ToArray();
                float part = total / ability.HitCount;
                return Enumerable.Range(0, ability.HitCount).Select(i => i == ability.HitCount - 1 ? total - part * i : part).ToArray();
            }
            if (!ability.TryResolveWeakDamageBudget(level, 1f, out var melee))
                return Array.Empty<float>();
            return Enumerable.Range(0, melee.Count).Select(melee.ForPhase).ToArray();
        }

        internal static string DescribeAbility(EnemyAbilityDefinition ability, int level, float speed, bool bossMaterial)
        {
            if (bossMaterial)
                return "보스 재료 실행기 소유 공격입니다. 피해·형상·타격 사건은 보스 메이커에서 확인하세요.\n"
                    + $"정의의 쿨다운 {ability.Cooldown:0.###}s · 피해 예산 {ability.ReferencePatternDamagePercent:0.###}%.";
            float[] allocation = DamageAllocations(ability, level);
            string damage = allocation.Length == 0 ? "약공 피해 예산이 타수의 최소 조건을 만족하지 않습니다."
                : ability.HasWeakAttackExecution
                    ? $"V3 약공 원예산 {allocation.Sum():0.###} / 배분 [{string.Join(" / ", allocation.Select(v => v.ToString("0.###")))}] · {allocation.Length}타"
                    : $"원피해 {ability.ResolveDamage(level):0.###}/타 × {ability.HitCount}타";
            string timing = ability.HasWeakAttackExecution && (EnemyAbilityDefinition.IsWeakMeleeExecution(ability.ExecutionMode)
                || ability.ExecutionMode == EnemyAbilityExecutionMode.Zone)
                ? "접촉·종료는 실제 Animator 진행을 따릅니다. 접촉 구간은 몬스터 튜너에서 확인하세요."
                : $"정의 기준 첫 {(ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile ? "발사" : "타격")} {ability.ResolveFirstImpactTime(speed):0.###}s / 최소 실행 {ability.ResolveExecutionDuration(speed):0.###}s";
            return $"Lv{level} {damage}\n{timing}\n쿨다운 {ability.Cooldown:0.###}s · 등급/변형/지도 피해 배율 전 원본값입니다. 실제 접촉·패링 체감은 Play에서 확인하세요.";
        }
    }
}
