using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    // 문서·구현 차이와 결과에서 튄 조합을 재현 조건·원인 코드·영향·수정 후보로 정리한다.
    // 수정 후보는 제안이며 이 도구는 제품 수치·규칙을 바꾸지 않는다.
    public static class CombatBalanceAnalysisFindings
    {
        static string F(float v, string fmt = "0.##") => float.IsNaN(v) ? "—" : float.IsInfinity(v) ? "∞" : v.ToString(fmt, CultureInfo.InvariantCulture);

        public static List<string> SelfChecks(CombatBalanceAnalysisModel.Catalog catalog, WeaponItemData weapon, AnalysisConditions c)
        {
            var list = new List<string>();
            void Check(bool ok, string label, string detail) => list.Add((ok ? "PASS " : "FAIL ") + label + " — " + detail);
            float hp = OverburstCombatBalance.ReferenceHealth(100), armor = OverburstCombatBalance.ReferenceArmor(100), q = OverburstCombatBalance.ReferenceExpectedHit(100);
            Check(Mathf.Approximately(hp, 2985f) && Mathf.Approximately(armor, 204f) && Mathf.Abs(q - 259.825f) < .01f,
                "S0 Lv100 기준(문서 50B·최종 결과)", $"HP {F(hp)} 방어 {F(armor)} Q {F(q, "0.###")} (문서 2985/204/259.825)");
            var tuning = OverburstElementTuning.Current;
            float[] expected = { .30f, 1.15f, 2.425f, 4.125f, 6.25f };
            var got = new List<string>(); bool heavyOk = true;
            for (int i = 0; i < 5; i++)
            {
                float e = i / 4f;
                float h = CombatBalanceFormulas.HeavyFirstBlastDamage(1f, e, CombatBalanceFormulas.DischargeEnergyCoefficient(tuning, e, 0f, 0f), .25f);
                got.Add(F(h, "0.###")); heavyOk &= Mathf.Abs(h - expected[i]) < .001f;
            }
            Check(heavyOk, "강공 H/D 표(문서 60A·v2 4절)", "e=0/.25/.5/.75/1 → " + string.Join(" / ", got) + " (문서 0.30/1.15/2.425/4.125/6.25)");
            Check(Mathf.Approximately(CombatBalanceFormulas.PhaseEnergyGain(tuning, false, 0f), 10f) && Mathf.Approximately(CombatBalanceFormulas.PhaseEnergyGain(tuning, true, 0f), 20f),
                "에너지 Phase당 10/치명 20(문서 60A)", $"{F(CombatBalanceFormulas.PhaseEnergyGain(tuning, false, 0f))}/{F(CombatBalanceFormulas.PhaseEnergyGain(tuning, true, 0f))}");
            Check(Mathf.Abs(tuning.shatterBlastFraction - .6f) < .0001f, "얼음 쇄빙 H×0.60(문서 v2 5절)", "튜닝 SO " + F(tuning.shatterBlastFraction));
            var combo = weapon.GetMeleeComboDefinition();
            int phases = 0; float coefficient = 0f, cycle = 0f;
            for (int i = 0; combo != null && i < combo.StepCount; i++)
            {
                var st = CombatBalanceAnalysisModel.TimeComboStep(weapon, i, i > 0, 1f);
                phases += st.phases.Count; coefficient += st.phases.Sum(p => p.coefficient); cycle += st.chainAt;
            }
            // 문서 v2 4절의 '5입력 8판정·계수 5.65·3.227초'는 4타 1순환(7판정·4.65) + 다음 1타다.
            var again = combo != null && combo.StepCount > 0 ? CombatBalanceAnalysisModel.TimeComboStep(weapon, 0, true, 1f) : null;
            int fivePhases = phases + (again != null ? again.phases.Count : 0);
            float fiveCoefficient = coefficient + (again != null ? again.phases.Sum(p => p.coefficient) : 0f);
            float fiveSeconds = cycle + (again != null ? again.chainAt : 0f);
            Check(combo != null && combo.StepCount == 4 && fivePhases == 8 && Mathf.Abs(fiveCoefficient - 5.65f) < .01f,
                "대검 5입력 8판정·계수 5.65(문서 v2 4절)", $"4타 {phases}판정 계수 {F(coefficient, "0.###")}, 5입력 {fivePhases}판정 계수 {F(fiveCoefficient, "0.###")}");
            Check(Mathf.Abs(fiveSeconds - CombatBalanceAnalysisRunner.DesignPrepSeconds) < .1f, "5입력 준비 시간(문서 3.227초, 공속 1.0)",
                $"모델 {F(fiveSeconds, "0.###")}초 = 4타 순환 {F(cycle, "0.###")} + 1타 {F(again != null ? again.chainAt : 0f, "0.###")}");
            int small = catalog.OfClass(EnemyClass.Small).Count(), medium = catalog.OfClass(EnemyClass.Medium).Count(), elite = catalog.OfClass(EnemyClass.Elite).Count();
            Check(catalog.enemies.Count == 29, "몬스터 정의 29종(문서 20)", $"{catalog.enemies.Count}종 = 소형 {small} · 중형 {medium} · 정예 {elite}");
            Check(catalog.weapons.Count >= 34, "활성 대검", catalog.weapons.Count + "종(05A: 활성 34종)");
            // 조립 경로 일치: 장비 없는 조립은 무기 단독 × 플레이어 성장을 반올림한 값과 같아야 한다.
            var item = CombatBalanceAnalysisModel.NewWeapon(weapon, 50, ItemGrade.Rare, WeaponElement.Fire, 42);
            var baseStats = WeaponStatCalculator.Calculate(item);
            var composed = CombatBalanceFormulas.ComposePlayerWeaponStats(baseStats, default, 50, 0f);
            Check(Mathf.Approximately(composed.damage, OverburstCombatBalance.RoundStat(baseStats.damage * OverburstGrowthRules.PlayerAttackFactor(50))),
                "장비 없는 조립 = 무기×성장", $"{F(composed.damage)} = round({F(baseStats.damage)}×{F(OverburstGrowthRules.PlayerAttackFactor(50), "0.###")})");
            foreach (ItemGrade g in Enum.GetValues(typeof(ItemGrade)))
            {
                var gear = catalog.GearFor(GearKind.Gloves, 55);
                var rolled = CombatBalanceAnalysisModel.NewGear(gear, 55, g, 99);
                if (!GearQuality.IsValid(gear, g, rolled.gearRolls)) { Check(false, "장비 추첨 유효성", g.ToString()); break; }
                if (g == ItemGrade.Cursed) Check(true, "장비 추첨 유효성(8등급)", "GearQuality.IsValid 통과");
            }
            Check(catalog.representative.Count == 3, "체급 대표 몬스터", string.Join(", ", catalog.representative.Select(kv => AnalysisLabels.Enemy(kv.Key) + "=" + kv.Value.EnemyId)));
            return list;
        }

        public static List<Finding> Build(CombatBalanceAnalysisModel.Catalog catalog, WeaponItemData weapon, AnalysisResult result)
        {
            var list = new List<Finding>();
            Tooltip(list, catalog);
            LegacyGrowth(list, catalog);
            StrongBudget(list, catalog);
            TuningDefaults(list);
            DeadCoefficient(list);
            DoubleMultiplier(list, catalog);
            LevelBoundary(list, catalog, weapon, result.conditions);
            UpperElementDocs(list);
            FromRows(list, result);
            return list;
        }

        // 무기 툴팁·장착 비교 '단일 DPS'(MeleeSingleTargetDpsCalculator)를 Play로 대조한 약공 경로(콤보 시간표 + 약공 배율)와 맞춰 본다.
        // 10-01 05:40 수정 전에는 대검 약공 0.35배와 재생 가속을 빼서 2.33배 과대였다.
        static void Tooltip(List<Finding> list, CombatBalanceAnalysisModel.Catalog catalog)
        {
            float worst = 1f; string worstLabel = "", worstDetail = ""; int count = 0;
            foreach (var weapon in catalog.weapons)
            foreach (int level in new[] { 1, 55 })
            {
                var item = CombatBalanceAnalysisModel.NewWeapon(weapon, level, ItemGrade.Common, WeaponElement.Fire, 1);
                var stats = WeaponStatCalculator.Calculate(item);
                MeleeSingleTargetDpsEstimate tooltip = MeleeSingleTargetDpsCalculator.Estimate(weapon, stats);
                float multiplier = CombatBalanceFormulas.AttackDamageMultiplier(weapon, CombatBalanceAnalysisModel.HeavyDefinition(weapon), false, false);
                float crit01 = Mathf.Clamp01(CombatBalanceFormulas.EffectiveCriticalChance(stats.critChance, false) / 100f);
                float cycleDamage = 0f, cycleTime = 0f;
                for (int i = 0; i < weapon.GetMeleeComboDefinition().StepCount; i++)
                {
                    var st = CombatBalanceAnalysisModel.TimeComboStep(weapon, i, true, stats.meleeAttackSpeedMultiplier);
                    foreach (var ph in st.phases)
                    {
                        float b = stats.damage * ph.coefficient * multiplier;
                        cycleDamage += Mathf.Lerp(CombatBalanceFormulas.RoundedHitDamage(b, false, stats.critDamageMultiplier),
                            CombatBalanceFormulas.RoundedHitDamage(b, true, stats.critDamageMultiplier), crit01);
                    }
                    cycleTime += st.chainAt;
                }
                float modelDps = cycleTime > 0 ? cycleDamage / cycleTime : 0f;
                float ratio = tooltip.IsValid && modelDps > 0f ? tooltip.Dps / modelDps : float.NaN;
                count++;
                if (float.IsNaN(ratio) || Mathf.Abs(ratio - 1f) > Mathf.Abs(worst - 1f) || count == 1)
                {
                    worst = float.IsNaN(ratio) ? float.PositiveInfinity : ratio;
                    worstLabel = $"{weapon.name} Lv{level} 일반";
                    worstDetail = $"툴팁 {F(tooltip.Dps)}(순환 {F(tooltip.CycleDuration)}초·판정 {tooltip.HitCount}) vs 실제 경로 {F(modelDps)}(순환 {F(cycleTime)}초)";
                }
            }
            bool match = Mathf.Abs(worst - 1f) <= .01f;
            list.Add(new Finding("D01", "문서·구현 차이", match ? "정보" : "높음",
                match ? "무기 툴팁 DPS가 실제 약공 경로와 일치한다(10-01 수정)" : "무기 툴팁 DPS가 실제 약공 경로와 다르다",
                $"무기 {catalog.weapons.Count}종 × Lv1·Lv55 일반, 무기 툴팁·장착 비교의 단일 대상 DPS. 가장 큰 차이: {worstLabel}",
                "`MeleeSingleTargetDpsCalculator.Estimate`는 `CombatBalanceFormulas.AttackDamageMultiplier`(대검 약공 0.35)·`EffectiveCriticalChance`·`RoundedHitDamage`와 `MeleePlaybackAcceleration`·이어 치기 진입 진행률을 쓴다. 비교 기준은 Play 콤보 시간표·약공 피해로 대조한 모델 경로.",
                $"{worstDetail} → {F(worst, "0.###")}배. " + (match ? "무기 비교 수치가 실제 약공과 맞는다(10-01 수정 전 2.33배 과대)." : "플레이어가 무기를 비교할 때 수치가 실제 약공과 맞지 않는다."),
                match ? "없음. 장비 옵션·원소 에너지·강공은 무기 단독 비교값이라 넣지 않는다." : "툴팁 계산기가 실제 약공 경로와 같은 입력을 쓰게 한다.",
                $"대조 {count}건, 허용 ±1%"));
        }

        // 통합 설계 v2 6절: 패턴 전체 피해 예산 상한(기준 HP %) 소형 6 / 중형 일반 12·강공 18 / 정예 일반 14·강공 24.
        static void StrongBudget(List<Finding> list, CombatBalanceAnalysisModel.Catalog catalog)
        {
            var low = new List<string>(); var same = new List<string>(); var over = new List<string>(); int strongCount = 0;
            foreach (var d in catalog.enemies)
            {
                var cls = CombatBalanceAnalysisModel.Classify(d);
                float normalCap = cls == EnemyClass.Small ? 6f : cls == EnemyClass.Medium ? 12f : 14f;
                float strongCap = cls == EnemyClass.Small ? 6f : cls == EnemyClass.Medium ? 18f : 24f;
                float maxNormal = 0f;
                var strong = new List<EnemyAbilityDefinition>();
                for (int i = 0; d.AbilitySet != null && i < d.AbilitySet.Count; i++)
                {
                    var a = d.AbilitySet.GetAbility(i);
                    if (a == null) continue;
                    if (a.IsTelegraphedStrongAttack) strong.Add(a); else maxNormal = Mathf.Max(maxNormal, a.ReferencePatternDamagePercent);
                    if (a.ReferencePatternDamagePercent > (a.IsTelegraphedStrongAttack ? strongCap : normalCap) + 1e-3f)
                        over.Add($"{d.EnemyId}/{a.AbilityId} {F(a.ReferencePatternDamagePercent)}%>{F(a.IsTelegraphedStrongAttack ? strongCap : normalCap)}%");
                }
                foreach (var a in strong)
                {
                    strongCount++;
                    string label = $"{AnalysisLabels.Enemy(cls)} {d.EnemyId}/{a.AbilityId} {F(a.ReferencePatternDamagePercent)}%(평타 최대 {F(maxNormal)}%, 상한 {F(strongCap)}%)";
                    if (a.ReferencePatternDamagePercent <= maxNormal + 1e-3f) same.Add(label);
                    else if (a.ReferencePatternDamagePercent < strongCap * .75f) low.Add(label);
                }
            }
            list.Add(new Finding("D07", "문서·구현 차이", same.Count + low.Count > 0 ? "높음" : "정보", "강공 예산이 평타와 같거나 설계 강공 상한보다 크게 낮은 공격이 있다",
                $"활성 29종의 강공 {strongCount}개. 10-01 강공 규칙 개편(다른 작업, 미커밋)으로 강공 지정이 바뀐 자산 기준",
                "`EnemyAbilityDefinition.telegraphedStrongAttack`(강공 예고·패링 대상)과 `referencePatternDamagePercent`(피해 예산)가 따로 저장된다. 강공으로 지정을 바꿔도 예산은 그대로 남는다.",
                $"예산이 평타 이하인 강공 {same.Count}개, 상한의 75% 미만 {low.Count}개. 이 강공은 예고·패링 연출은 강공인데 맞아도 평타만큼만 아파 '강공 위협 약함' 판정이 몰린다.",
                "강공 지정과 함께 예산을 체급 상한(중형 18%·정예 24%) 근처로 옮기거나, 약한 강공은 강공 지정을 해제한다(수치 제안). 강공 규칙 개편 작업이 커밋한 뒤 다시 분석한다.",
                "평타 이하: " + (same.Count > 0 ? string.Join(", ", same) : "없음") + " / 상한 75% 미만: " + (low.Count > 0 ? string.Join(", ", low) : "없음")
                + " / 상한 초과: " + (over.Count > 0 ? string.Join(", ", over) : "없음")));
        }

        static void LegacyGrowth(List<Finding> list, CombatBalanceAnalysisModel.Catalog catalog)
        {
            var legacy = new List<string>();
            foreach (var d in catalog.enemies)
                for (int i = 0; d.AbilitySet != null && i < d.AbilitySet.Count; i++)
                {
                    var a = d.AbilitySet.GetAbility(i);
                    if (a != null && !a.UsesLevelDamageBudget) legacy.Add($"{d.EnemyId}/{a.AbilityId}(원본 {F(a.Damage)} → Lv100 {F(a.Damage * OverburstGrowthRules.EnemyDamageFactor(100), "0")})");
                }
            var noCoefficient = catalog.enemies.Where(d => d.ReferenceHealthCoefficient <= 0f).Select(d => d.EnemyId).ToList();
            if (legacy.Count == 0 && noCoefficient.Count == 0)
            {
                list.Add(new Finding("D02", "데이터 점검", "정보", "29종 공격·체력이 모두 기준 곡선을 쓴다",
                    "활성 몬스터 정의 전체", "`EnemyAbilityDefinition.UsesLevelDamageBudget`, `EnemyDefinition.referenceHealthCoefficient`",
                    "옛 성장식(피해 1+0.065(L−1), 체력 1+0.055(L−1))이 섞이지 않는다.", "없음", "옛 경로 사용 0건"));
                return;
            }
            list.Add(new Finding("D02", "데이터 점검", legacy.Count > 0 ? "높음" : "중간", "기준 곡선 밖의 옛 성장식 공격·체력이 남아 있다",
                "활성 몬스터 정의 전체, 레벨이 높을수록 차이 확대",
                "`CombatHealth`가 `referencePatternDamagePercent`가 0인 공격에 `OverburstGrowthRules.EnemyDamageFactor`(Lv100 ×7.44)를 곱하고, 계수 0인 종은 `EnemyHealthFactor`를 쓴다.",
                "해당 공격·종만 기준 예산 곡선과 다르게 커져 고레벨에서 생존 피격 수가 튄다.",
                "해당 공격 SO에 예산 %를 채우거나 옛 성장식 경로를 제거한다(수치 제안).",
                "공격: " + (legacy.Count > 0 ? string.Join(", ", legacy.Take(12)) + (legacy.Count > 12 ? $" 외 {legacy.Count - 12}건" : "") : "없음")
                + " / 체력 계수 없음: " + (noCoefficient.Count > 0 ? string.Join(", ", noCoefficient) : "없음")));
        }

        static void TuningDefaults(List<Finding> list)
        {
            var asset = OverburstElementTuning.Current;
            var defaults = ScriptableObject.CreateInstance<OverburstElementTuning>();
            var diffs = new List<string>();
            try
            {
                foreach (var field in typeof(OverburstElementTuning).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    object a = field.GetValue(asset), b = field.GetValue(defaults);
                    bool same = a is float fa && b is float fb ? Mathf.Abs(fa - fb) < 1e-5f : Equals(a, b);
                    if (!same) diffs.Add($"{field.Name} 코드 {Format(b)} / 자산 {Format(a)}");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(defaults); }
            list.Add(new Finding("D03", "문서·구현 차이", diffs.Count > 0 ? "중간" : "정보", "원소 튜닝 코드 기본값과 SO 값이 다르다",
                "`Resources/Combat/OverburstElementTuning.asset`을 읽지 못하는 경우(경로 변경·빌드 누락)",
                "`OverburstElementTuning.Current`는 자산이 없으면 코드 기본값으로 새 인스턴스를 만든다.",
                "자산이 빠지면 오류 없이 다른 밸런스(예: 쇄빙 1.5, 빛 감쇠 3/초)로 돈다.",
                "코드 기본값을 확정 자산 값에 맞추거나, 자산이 없을 때 오류를 남긴다(코드 제안).",
                diffs.Count > 0 ? string.Join("; ", diffs) : "차이 없음"));
        }

        static string Format(object v) => v is float f ? F(f, "0.###") : v is Vector3 v3 ? $"({F(v3.x)},{F(v3.y)},{F(v3.z)})"
            : v is Vector4 v4 ? $"({F(v4.x)},{F(v4.y)},{F(v4.z)},{F(v4.w)})" : v is Vector2 v2 ? $"({F(v2.x)},{F(v2.y)})" : Convert.ToString(v, CultureInfo.InvariantCulture);

        static void DeadCoefficient(List<Finding> list)
        {
            var t = OverburstElementTuning.Current;
            list.Add(new Finding("D04", "문서·구현 차이", "낮음", "상태 소비 보너스 `statusDamagePerStack`이 다섯 원소 모두에서 쓰이지 않는다",
                "불·얼음·번개·어둠·빛 강공의 대상 상태 소비",
                "`OverburstElementDischarge.TryResolveConfirmedHit`의 보너스 분기에서 얼음은 쇄빙, 불·번개·어둠·빛은 0이고 `attackDamage × 중첩 × stackCoefficient`는 어느 활성 원소에도 닿지 않는다.",
                $"튜닝 SO의 {F(t.statusDamagePerStack)}을 바꿔도 결과가 변하지 않는다. v2 설계 5절의 '어둠·빛 D×0.15s'는 60D 규칙으로 대체됐다.",
                "필드를 사용처가 없다고 표시하거나 튜닝 창·문서에서 뺀다(정리 제안).", "stackCoefficient 사용: 활성 원소 0곳"));
        }

        static void DoubleMultiplier(List<Finding> list, CombatBalanceAnalysisModel.Catalog catalog)
        {
            var multiplied = catalog.enemies.Where(d => Mathf.Abs(d.ResolveRuntimeStats().DamageMultiplier - 1f) > 1e-4f)
                .Select(d => $"{d.EnemyId} 피해×{F(d.ResolveRuntimeStats().DamageMultiplier)}").ToList();
            list.Add(new Finding("D05", "데이터 점검", multiplied.Count > 0 ? "중간" : "정보", "등급·변형 피해 배율이 공격 예산 위에 곱해지는지",
                "29종 등급·변형 프로필", "`EnemyMeleeAttackController`는 `ResolveDamage(L) × 등급·변형 DamageMultiplier`를 쓴다. 예산은 이미 체급별로 정했다.",
                multiplied.Count > 0 ? "배율이 1이 아닌 종은 예산보다 세거나 약하다." : "모든 종이 1이라 이중 배율이 없다.",
                multiplied.Count > 0 ? "예산 사용 공격에서는 배율을 1로 두거나 예산에 흡수한다." : "없음",
                multiplied.Count > 0 ? string.Join(", ", multiplied) : "전 종 DamageMultiplier=1"));
        }

        static void LevelBoundary(List<Finding> list, CombatBalanceAnalysisModel.Catalog catalog, WeaponItemData weapon, AnalysisConditions c)
        {
            var parts = new List<string>(); float worstLow = 9f, worstHigh = 0f; int lowAt = 0, highAt = 0;
            foreach (int level in new[] { 1, 10, 11, 20, 21, 30, 31, 40, 41, 50, 51, 60, 61, 70, 71, 80, 81, 90, 91, 100 })
            {
                var b = CombatBalanceAnalysisModel.BuildPlayer(catalog, weapon, level, ItemGrade.Common, GearPreset.Rolled, WeaponElement.None, c.seedBase, c.combatStance);
                float ratio = b.ReferenceQ / OverburstCombatBalance.ReferenceExpectedHit(level);
                float hpRatio = b.maxHealth / OverburstCombatBalance.ReferenceHealth(level);
                parts.Add($"Lv{level} 공격 {F(ratio, "0.00")} HP {F(hpRatio, "0.00")}");
                if (ratio < worstLow) { worstLow = ratio; lowAt = level; }
                if (ratio > worstHigh) { worstHigh = ratio; highAt = level; }
            }
            list.Add(new Finding("B01", "밸런스", worstHigh - worstLow > .25f ? "중간" : "낮음", "장비는 10레벨 계단, 몬스터 기준은 선형 보간이라 구간 경계에서 체감이 튄다",
                "같은 레벨 비교, 일반 품질 표준 추첨, 각 구간 시작(L=11·21…)과 끝(L=10·20…)",
                "`OverburstBalanceTable.GearBase`는 `(L−1)/10` 구간 계단값, `OverburstCombatBalance.ReferenceExpectedHit`는 10레벨 노드 사이 선형 보간. `EnemyRank`와 공격 예산이 이 보간값을 쓴다.",
                $"플레이어 기대 1타/Qref가 Lv{lowAt}에서 {F(worstLow, "0.00")}, Lv{highAt}에서 {F(worstHigh, "0.00")}. 구간 시작 직후는 새 장비 한 세트만큼 강하고 끝 무렵은 약하다.",
                "몬스터 기준을 구간 계단에 맞추거나(같은 구간 동일), 장비 주능력치를 구간 안에서 아이템 레벨로 보간한다(수치 제안).",
                string.Join(" · ", parts)));
        }

        static void UpperElementDocs(List<Finding> list)
        {
            var rule = CombatBalanceAnalysisModel.ReadDarkRule(OverburstElementTuning.Current);
            list.Add(new Finding("D06", "문서·구현 차이", "중간", "상위 원소 문서가 세 갈래로 나뉘어 있다",
                "어둠·빛 강공 후속",
                "v2 설계 5절(빛 D×0.15s+잔광 H×0.35, 어둠 D×0.15s+인력), 60A 표(삭제된 이전 동작 표시), 60D 4절(10-01 잠식 탄막으로 개정)·9절(빛 3연타)과 현재 코드: " + rule.source,
                "v2 설계 문서와 60A 표가 아직 이전 규칙을 적고 있어 수치 조정 근거가 흔들린다. 어둠은 10-01 잠식 탄막으로 바뀌었고 옛 흡인 폭발 코드는 삭제됐다.",
                "v2 5절 머리에 '60D가 대체'라고 표시하고 60A 표에서 어둠·빛 행을 60D 링크로 줄인다(문서 제안).",
                "OverburstElementTuning: " + rule.source));
        }

        static void FromRows(List<Finding> list, AnalysisResult result)
        {
            var rows = result.rows;
            if (rows.Count == 0) return;
            string Cond(ResultRow r) => $"Lv{r.level} {AnalysisLabels.Grade(r.grade)} {AnalysisLabels.Preset(r.preset)} {AnalysisLabels.Element(r.element)} → {AnalysisLabels.Enemy(r.enemyClass)} {r.enemyName} {AnalysisLabels.Mode(r.mode)}";
            int id = 1;
            foreach (var group in rows.SelectMany(r => r.flags.Select(f => (f, r))).GroupBy(x => x.f).OrderByDescending(g => g.Count()))
            {
                var sample = group.Select(x => x.r).ToList();
                var worst = sample.First();
                string severity = group.Key == "PREP_DEATH" || group.Key == "CYCLE_FAIL" || group.Key.StartsWith("LETHAL") || group.Key == "ELEMENT_OUTLIER" ? "높음"
                    : group.Key == "DOT_LOW" || group.Key == "SAFE_STRONG" ? "낮음" : "중간";
                string cause, impact, proposal;
                Explain(group.Key, sample, out cause, out impact, out proposal);
                list.Add(new Finding("R" + (id++).ToString("00"), "결과 튐", severity, CombatBalanceAnalysisRunner.RuleLabel(group.Key) + $" ({sample.Count}조합)",
                    string.Join(" / ", sample.Take(4).Select(Cond)) + (sample.Count > 4 ? $" 외 {sample.Count - 4}" : ""), cause, impact, proposal,
                    string.Join(" / ", sample.Take(4).Select(r => $"처치 {F(r.killTime)}초 충전 {F(r.chargeTime)}초 준비생존 {F(r.prepSurvivalRate * 100, "0")}% 1주기 {F(r.heavyKillRate * 100, "0")}% 버팀 {r.normalSurvivable}/{r.strongSurvivable}"))));
            }
            // 원소별 군집 기여 비교
            var crowd = rows.Where(r => r.mode == CombatMode.Crowd && OverburstElementRules.IsActive(r.element)).ToList();
            if (crowd.Count > 0)
            {
                var byElement = crowd.GroupBy(r => r.element).Select(g => (g.Key, time: Median(g.Select(r => r.killTime)), aoe: Median(g.Select(r => r.aoeShare)), derived: Median(g.Select(r => r.derivedShare))))
                    .OrderBy(x => x.time).ToList();
                list.Add(new Finding("C01", "밸런스", "정보", "군집 전투 원소별 정리 시간·범위 기여(전 조건 중앙값)",
                    $"군집 {result.conditions.crowdCount}마리, 약공 판정당 {F(result.conditions.crowdWeakTargets)}마리·완충 강공 {F(result.conditions.crowdHeavyTargets)}마리·연쇄 밀착 {F(result.conditions.packingDensity)}/㎡",
                    "강공 반경 `CombatBalanceFormulas.DischargeRadius`, 불 연쇄 `FireChainDamage/Radius`, 번개 `LightningHopDamage`, 어둠 후속 규칙, 빛 3연타 반경 배율",
                    string.Join(" · ", byElement.Select(x => $"{AnalysisLabels.Element(x.Key)} {F(x.time)}초(범위 {F(x.aoe, "0")}%, 파생 {F(x.derived, "0")}%)")),
                    "편차가 크면 원소별 강공 반경·연쇄 계수를 조정한다(수치 제안). 군집 모델은 정리 시간을 Play 실측보다 1.3~2배 길게 잡으므로 순위 비교에만 쓰고, 절대 시간은 Play 군집 측정을 따른다.", "모델 근사: 약공·강공 대상 수와 연쇄 밀착 밀도는 Play 측정으로 맞춘 입력"));
            }
        }

        static float Median(IEnumerable<float> values)
        {
            var v = values.Where(x => !float.IsNaN(x) && !float.IsInfinity(x)).OrderBy(x => x).ToList();
            return v.Count == 0 ? float.NaN : v[v.Count / 2];
        }

        static void Explain(string code, List<ResultRow> rows, out string cause, out string impact, out string proposal)
        {
            switch (code)
            {
                case "PREP_DEATH":
                    cause = "약공 준비 피해(D×0.35×Phase 계수 합)가 중형 체력(종 계수×Qref)보다 먼저 체력을 다 깎는다. 소유: `OverburstCombatBalance.GreatswordWeakDamage`, `EnemyDefinition.referenceHealthCoefficient`.";
                    impact = "강공 마무리가 필요 없어져 원소 방출 규칙을 체감하지 못한다.";
                    proposal = "해당 구간 중형 계수 상향 또는 약공 배율 하향 검토(수치 제안).";
                    break;
                case "CYCLE_FAIL":
                    cause = "완충 강공 H(최대 6.25D)와 준비 피해 합이 중형 체력에 못 미친다. 소유: `CombatBalanceFormulas.HeavyFirstBlastDamage`, 중형 계수.";
                    impact = "한 번 준비·방출로 끝나지 않아 전투가 늘어진다.";
                    proposal = "강공 계수 또는 중형 계수 조정(수치 제안).";
                    break;
                case "ELITE_PREP_DEATH":
                    cause = "정예 체력이 준비 피해보다 낮다."; impact = "정예가 약공만으로 정리된다."; proposal = "정예 계수 확인(수치 제안)."; break;
                case "ELEMENT_OUTLIER":
                    cause = "같은 조건에서 원소 후속 효과(쇄빙 0.6H, 틱, 3연타, 탄막)의 단일 대상 몫이 다르다. 소유: `OverburstElementTuning` 값과 `CombatBalanceFormulas`의 원소 식.";
                    impact = "특정 원소가 단일 대상에서 확연히 빠르거나 느리다.";
                    proposal = "튀는 원소의 단일 대상 후속 계수 조정(수치 제안).";
                    break;
                case "CROWD_OUTLIER":
                    cause = "군집에서 연쇄·범위 수가 원소마다 크게 다르다."; impact = "군집 사냥 원소가 한쪽으로 쏠린다."; proposal = "반경·연쇄 수·탄막 대상 한도 조정(수치 제안)."; break;
                case "CHARGE_SLOW":
                    cause = "완충까지의 Phase 수 × 콤보 시간이 설계 3.23초를 크게 넘는다(빛은 200·광휘 100까지 준비)."; impact = "강공 주기가 길어진다."; proposal = "빛 3연타 준비량 또는 광휘 충전량 조정(수치 제안)."; break;
                case "CHARGE_FAST":
                    cause = "치명 확률이 높아 Phase당 20 충전이 잦다."; impact = "강공만 반복하게 된다."; proposal = "치명 에너지 비율 검토(수치 제안)."; break;
                case "LETHAL_NORMAL":
                case "LETHAL_STRONG":
                    cause = "공격 예산(기준 유효 체력 × 예산%/타수)에 비해 플레이어 체력·방어가 낮다(무기만·저품질·레벨 차). 소유: `EnemyAbilityDefinition.referencePatternDamagePercent`, `CombatBalanceFormulas.PlayerArmorMultiplier`.";
                    impact = "몇 대 맞으면 죽는다."; proposal = "해당 조건이 의도한 난도인지 확인, 아니면 예산 % 조정(수치 제안)."; break;
                case "SAFE_STRONG":
                    cause = "강공 예산이 낮아 여러 번 맞아도 산다."; impact = "강공 예고·패링의 긴장감이 약하다."; proposal = "강공 예산 % 상향 검토(수치 제안)."; break;
                case "POWER_GAP":
                    cause = "장비 계단과 몬스터 기준 보간의 차이(B01)."; impact = "구간 경계에서 난도가 튄다."; proposal = "B01 참조."; break;
                case "QUALITY_GAP":
                    cause = "신화 별 20개·공격 별 10%·치명 상한까지의 합이 커 처치 시간이 크게 준다."; impact = "품질 간 격차가 커 일반 장비가 무의미해진다."; proposal = "별 가중치·상한 검토(수치 제안)."; break;
                case "DOT_LOW":
                    cause = "틱 = 직전 직접 피해×중첩×0.01(불, 0.5초)·0.02(번개, 1.5초)라 전체 피해 비중이 작다. 소유: `OverburstElementTuning.burnTickDamagePerStack/shockTickDamagePerStack`.";
                    impact = "지속 피해가 처치 시간에 거의 영향을 주지 않는다."; proposal = "의도라면 유지, 아니면 틱 계수 조정(수치 제안)."; break;
                case "HEAVY_OVERKILL":
                    cause = "완충 강공이 대상 체력의 3배를 넘는다."; impact = "강공 피해 대부분이 낭비된다(단일 기준)."; proposal = "고품질 조합 한정인지 확인(수치 제안)."; break;
                default:
                    cause = impact = proposal = ""; break;
            }
        }
    }
}
