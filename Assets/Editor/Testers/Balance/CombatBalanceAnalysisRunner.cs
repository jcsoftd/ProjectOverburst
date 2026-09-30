using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Overburst.EditorBalance.Analysis
{
    public static class CombatBalanceAnalysisRunner
    {
        // 설계 목표(통합 상세 수치 설계 v2 11절·4절). 판정 기준이며 게임 규칙이 아니다.
        public const float DesignPrepSeconds = 3.227f;
        public const float PrepSurvivalTarget = .95f, CycleKillTarget = .80f;

        public static readonly FlagRule[] Rules =
        {
            new FlagRule("PREP_DEATH", "준비 중 사망", "일반·고급·희귀, 같은 레벨 중형이 에너지 완충 전에 죽는 표본이 5% 이상(목표 생존 95%)"),
            new FlagRule("CYCLE_FAIL", "1주기 처치 실패", "일반·고급·희귀, 같은 레벨 중형을 완충 강공 1회로 끝내는 비율 80% 미만"),
            new FlagRule("ELITE_PREP_DEATH", "정예 준비 중 사망", "정예가 첫 완충 전에 죽음(약공만으로 정리됨)"),
            new FlagRule("ELEMENT_OUTLIER", "원소 편차", "같은 조건 다섯 원소 처치 시간 중앙값 대비 ±35% 초과"),
            new FlagRule("CROWD_OUTLIER", "군집 편차", "같은 조건 다섯 원소 군집 정리 시간 중앙값 대비 +50%/−40% 초과"),
            new FlagRule("CHARGE_SLOW", "강공 충전 느림", "단일 대상 기본 게이지(100) 완충 시간이 설계 3.23초의 1.5배 초과"),
            new FlagRule("CHARGE_FAST", "강공 충전 빠름", "단일 대상 완충 시간 설계의 0.5배 미만"),
            new FlagRule("LETHAL_NORMAL", "평타 치명적", "가장 아픈 일반 공격 패턴(모든 타 적중)을 2회 이하로만 버팀"),
            new FlagRule("LETHAL_STRONG", "강공 치명적", "가장 아픈 강공 패턴을 1회 이하로만 버팀"),
            new FlagRule("SAFE_STRONG", "강공 위협 약함", "일반·고급·희귀에서 가장 아픈 강공 패턴을 13회 이상 버팀"),
            new FlagRule("POWER_GAP", "기준 곡선 이탈", "표준 추첨 일반 품질 기대 1타가 몬스터 기준 Qref(L) 대비 +30%/−20% 초과"),
            new FlagRule("QUALITY_GAP", "품질 격차 과대", "같은 조건 신화의 처치 시간이 일반의 30% 미만"),
            new FlagRule("DOT_LOW", "지속 피해 미미", "불·번개 단일 대상 처치에서 지속 피해 비중 3% 미만"),
            new FlagRule("HEAVY_OVERKILL", "강공 과잉", "중형·정예 단일 첫 강공 기대 피해가 대상 최대 체력의 3배 초과"),
        };

        public static string RuleLabel(string code) => Rules.FirstOrDefault(r => r.code == code)?.label ?? code;
        public static bool CrowdApplies(EnemyClass cls) => cls != EnemyClass.Elite;

        public static int ExpectedRows(AnalysisConditions c)
            => c.levelBuckets.Distinct().Count() * c.grades.Count * c.presets.Count * c.elements.Count
               * c.enemies.Sum(e => c.modes.Count(m => m == CombatMode.Single || CrowdApplies(e)));

        public static AnalysisResult Run(AnalysisConditions c, Action<float, string> progress = null)
        {
            var catalog = CombatBalanceAnalysisModel.Catalog.Load();
            WeaponItemData weapon = catalog.Weapon(c.weaponPath);
            var result = new AnalysisResult
            {
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion, weapon = weapon != null ? weapon.name + " " + weapon.itemName : "없음",
                conditions = c.Clone()
            };
            if (weapon == null) { result.selfChecks.Add("FAIL 무기 없음: " + c.weaponPath); return result; }
            var problems = new List<string>();
            var buckets = c.levelBuckets.Distinct().OrderBy(b => b).ToList();
            int total = buckets.Count * c.grades.Count * c.presets.Count, done = 0;
            foreach (int bucket in buckets)
            foreach (ItemGrade grade in c.grades)
            foreach (GearPreset preset in c.presets)
            {
                progress?.Invoke(done++ / (float)Math.Max(1, total), $"Lv{AnalysisLabels.Bucket(bucket)} {AnalysisLabels.Grade(grade)} {AnalysisLabels.Preset(preset)}");
                int level = CombatBalanceAnalysisModel.LevelFor(bucket, c.levelPoint);
                int enemyLevel = Mathf.Clamp(level + c.monsterLevelOffset, 1, 100);
                int seeds = preset == GearPreset.Rolled ? Mathf.Max(1, c.seedCount) : 1;
                var builds = new List<PlayerBuild>();
                for (int k = 0; k < seeds; k++)
                    builds.Add(CombatBalanceAnalysisModel.BuildPlayer(catalog, weapon, level, grade, preset, WeaponElement.None,
                        c.seedBase + k * 7 + bucket * 1009 + (int)grade * 97, c.combatStance, problems));
                foreach (WeaponElement element in c.elements)
                foreach (EnemyClass cls in c.enemies)
                {
                    if (!catalog.representative.TryGetValue(cls, out var def)) continue;
                    foreach (CombatMode mode in c.modes)
                    {
                        if (mode == CombatMode.Crowd && !CrowdApplies(cls)) continue; // 정예는 조우당 1~2마리라 군집 비교에서 뺀다.
                        result.rows.Add(Evaluate(c, weapon, builds, element, def, cls, mode, bucket, level, enemyLevel));
                    }
                }
            }
            ApplyGroupFlags(result.rows);
            result.selfChecks.AddRange(problems.Distinct().Select(p => "FAIL " + p));
            result.selfChecks.AddRange(CombatBalanceAnalysisFindings.SelfChecks(catalog, weapon, c));
            result.findings.AddRange(CombatBalanceAnalysisFindings.Build(catalog, weapon, result));
            progress?.Invoke(1f, "완료");
            return result;
        }

        static float Median(IEnumerable<float> values)
        {
            var v = values.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList();
            return v.Count == 0 ? float.NaN : v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) * .5f;
        }

        static float Percentile(IEnumerable<float> values, float q)
        {
            var v = values.Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList();
            if (v.Count == 0) return float.NaN;
            return v[Mathf.Clamp(Mathf.RoundToInt(q * (v.Count - 1)), 0, v.Count - 1)];
        }

        static ResultRow Evaluate(AnalysisConditions c, WeaponItemData weapon, List<PlayerBuild> builds, WeaponElement element,
            EnemyDefinition def, EnemyClass cls, CombatMode mode, int bucket, int level, int enemyLevel)
        {
            var outcomes = new List<CombatBalanceAnalysisModel.SimOutcome>();
            var charges = new List<CombatBalanceAnalysisModel.SimOutcome>();
            var weakOnly = new List<float>();
            var enemies = new List<EnemyView>();
            var players = new List<PlayerBuild>();
            foreach (var baseBuild in builds)
            {
                var p = baseBuild.WithElement(element);
                var enemy = CombatBalanceAnalysisModel.BuildEnemy(def, enemyLevel, p);
                players.Add(p); enemies.Add(enemy);
                outcomes.Add(CombatBalanceAnalysisModel.Simulate(p, enemy, weapon, c, mode));
                if (mode == CombatMode.Single)
                {
                    charges.Add(CombatBalanceAnalysisModel.ChargeOnly(p, enemy, weapon, c));
                    weakOnly.Add(CombatBalanceAnalysisModel.WeakOnlyKillTime(p, enemy, weapon, c));
                }
            }
            var p0 = players[players.Count / 2]; var e0 = enemies[enemies.Count / 2];
            var row = new ResultRow
            {
                bucket = bucket, level = level, enemyLevel = enemyLevel, grade = p0.grade, preset = p0.preset, element = element,
                enemyClass = cls, mode = mode, enemyId = e0.id, enemyName = e0.name, samples = outcomes.Count,
                attack = Median(players.Select(x => x.attack)), crit = Median(players.Select(x => x.crit)),
                critDamage = Median(players.Select(x => x.critDamage)), attackSpeed = Median(players.Select(x => x.attackSpeed)),
                playerHealth = Median(players.Select(x => x.maxHealth)), playerArmor = Median(players.Select(x => x.armor)),
                enemyHealth = e0.maxHealth, enemyCount = mode == CombatMode.Single ? 1 : c.crowdCount,
            };
            row.referenceRatio = Median(players.Select(x => x.ReferenceQ)) / Mathf.Max(1f, OverburstCombatBalance.ReferenceExpectedHit(enemyLevel));
            row.weakHit = Median(outcomes.Select(o => o.weakExpected));
            row.weakDps = Median(outcomes.Select(o => o.weakDps));
            row.cycleDuration = Median(outcomes.Select(o => o.cycleDuration));
            row.killTime = Median(outcomes.Select(o => o.killed ? o.killTime : float.NaN));
            if (outcomes.All(o => !o.killed)) row.killTime = float.PositiveInfinity;
            row.spread = Percentile(outcomes.Select(o => o.killTime), .9f) / Mathf.Max(.001f, Percentile(outcomes.Select(o => o.killTime), .1f));
            row.heavyDirect = Median(outcomes.Select(o => o.heavyDirect));
            row.heavyEnergy = Median(outcomes.Select(o => o.heavyEnergy));
            row.heavyCount = Median(outcomes.Select(o => o.heavyCount));
            row.totalDamage = Median(outcomes.Select(o => o.Total));
            row.weakDamage = Median(outcomes.Select(o => o.weak));
            row.heavyDamage = Median(outcomes.Select(o => o.heavy));
            row.derivedDamage = Median(outcomes.Select(o => o.derived));
            row.dotDamage = Median(outcomes.Select(o => o.dot));
            float Share(Func<CombatBalanceAnalysisModel.SimOutcome, float> f) => Median(outcomes.Select(o => o.Total > 0 ? f(o) / o.Total : 0f)) * 100f;
            row.dotShare = Share(o => o.dot);
            row.heavyShare = Share(o => o.heavy);
            row.derivedShare = Share(o => o.derived);
            row.aoeShare = Share(o => o.heavy + o.derived);
            if (mode == CombatMode.Single)
            {
                row.chargeTime = Median(charges.Select(o => o.chargeTime));
                row.chargePhases = Median(charges.Select(o => o.chargePhases));
                row.weakOnlyKillTime = Median(weakOnly);
                row.prepSurvivalRate = outcomes.Count(o => o.prepSurvived) / (float)outcomes.Count;
                row.heavyKillRate = outcomes.Average(o => o.prepSurvived ? o.heavyKillChance : 0f);
                row.prepSurvived = row.prepSurvivalRate >= .5f;
                row.heavyKilled = row.heavyKillRate >= .5f;
                row.prepDamage = Median(outcomes.Select(o => o.prepDamage));
                row.prepHealthLeftPercent = Median(outcomes.Select(o => o.prepHealthLeft)) * 100f;
            }
            else
            {
                row.crowdClearTime = row.killTime;
                row.chargeTime = Median(outcomes.Select(o => o.chargeTime)); // 군집 첫 완충(여러 대상 치명 확률 반영)
                row.chargePhases = Median(outcomes.Select(o => o.chargePhases));
                row.weakOnlyKillTime = row.prepSurvivalRate = row.heavyKillRate = row.prepDamage = row.prepHealthLeftPercent = float.NaN;
                row.crowdKillsFirstHeavy = Median(outcomes.Select(o => o.firstHeavyKills));
                row.crowdWeakTargets = Median(outcomes.Select(o => o.weakTargets));
                row.crowdHeavyTargets = Median(outcomes.Select(o => o.heavyTargets));
            }
            // 패링 보상: 기절 중 받는 피해 ×1.4로 들어가는 완충 강공, 환급 50% 뒤 재충전 시간.
            if (cls != EnemyClass.Small && OverburstElementRules.IsActive(element))
            {
                row.parryHeavyBonus = row.heavyDirect * (CombatBalanceFormulas.ParryStunDamageMultiplier - 1f);
                row.parryRefundChargeTime = row.chargeTime * (1f - OverburstElementEnergy.ParriedHeavyRefundFraction);
            }
            if (e0.worstNormal != null) { row.normalSurvivable = e0.worstNormal.survivablePatterns; row.normalHitDamage = e0.worstNormal.patternTotal; row.normalAbility = e0.worstNormal.abilityId; }
            else row.normalSurvivable = -1;
            if (e0.worstStrong != null) { row.strongSurvivable = e0.worstStrong.survivablePatterns; row.strongHitDamage = e0.worstStrong.patternTotal; row.strongAbility = e0.worstStrong.abilityId; }
            else row.strongSurvivable = -1;
            if (mode == CombatMode.Single && element == WeaponElement.Light) row.lightTripleTime = Median(charges.Select(o => o.tripleTime));
            row.key = string.Join("|", level, p0.grade, p0.preset, element, cls, mode);
            RowFlags(row);
            Trace(row, p0, e0, weapon, c, outcomes[outcomes.Count / 2]);
            return row;
        }

        static void RowFlags(ResultRow r)
        {
            bool lowGrade = r.grade <= ItemGrade.Rare;
            bool sameLevel = r.level == r.enemyLevel;
            if (r.mode == CombatMode.Single)
            {
                if (r.enemyClass == EnemyClass.Medium && lowGrade && sameLevel && OverburstElementRules.IsActive(r.element))
                {
                    if (r.prepSurvivalRate < PrepSurvivalTarget) r.flags.Add("PREP_DEATH");
                    if (r.heavyKillRate < CycleKillTarget && r.prepSurvivalRate > 0f) r.flags.Add("CYCLE_FAIL");
                }
                if (r.enemyClass == EnemyClass.Elite && sameLevel && OverburstElementRules.IsActive(r.element) && r.prepSurvivalRate < .5f)
                    r.flags.Add("ELITE_PREP_DEATH");
                if (!float.IsNaN(r.chargeTime))
                {
                    if (r.chargeTime > DesignPrepSeconds * 1.5f) r.flags.Add("CHARGE_SLOW");
                    else if (r.chargeTime < DesignPrepSeconds * .5f) r.flags.Add("CHARGE_FAST");
                }
                if ((r.element == WeaponElement.Fire || r.element == WeaponElement.Electric) && r.dotShare < 3f && r.killTime > 1f)
                    r.flags.Add("DOT_LOW");
                if (r.enemyClass != EnemyClass.Small && r.heavyDirect > r.enemyHealth * 3f) r.flags.Add("HEAVY_OVERKILL");
            }
            if (r.mode == CombatMode.Single)
            {
                if (r.normalSurvivable >= 0 && r.normalSurvivable <= 2) r.flags.Add("LETHAL_NORMAL");
                if (r.strongSurvivable >= 0 && r.strongSurvivable <= 1) r.flags.Add("LETHAL_STRONG");
                if (lowGrade && r.strongSurvivable >= 13) r.flags.Add("SAFE_STRONG");
                if (r.preset == GearPreset.Rolled && r.grade == ItemGrade.Common && (r.referenceRatio > 1.3f || r.referenceRatio < .8f))
                    r.flags.Add("POWER_GAP");
            }
        }

        static void ApplyGroupFlags(List<ResultRow> rows)
        {
            foreach (var g in rows.GroupBy(r => (r.level, r.grade, r.preset, r.enemyClass, r.mode)))
            {
                var active = g.Where(r => OverburstElementRules.IsActive(r.element) && !float.IsInfinity(r.killTime) && !float.IsNaN(r.killTime)).ToList();
                if (active.Count < 3) continue;
                float median = Median(active.Select(r => r.killTime));
                foreach (var r in active)
                {
                    float ratio = r.killTime / Mathf.Max(.001f, median);
                    if (r.mode == CombatMode.Single && (ratio > 1.35f || ratio < .65f)) r.flags.Add("ELEMENT_OUTLIER");
                    if (r.mode == CombatMode.Crowd && (ratio > 1.5f || ratio < .6f)) r.flags.Add("CROWD_OUTLIER");
                }
            }
            foreach (var g in rows.GroupBy(r => (r.level, r.preset, r.element, r.enemyClass, r.mode)))
            {
                var common = g.FirstOrDefault(r => r.grade == ItemGrade.Common);
                var mythic = g.FirstOrDefault(r => r.grade == ItemGrade.Mythic);
                if (common == null || mythic == null || float.IsInfinity(common.killTime) || float.IsNaN(common.killTime)) continue;
                if (mythic.killTime / Mathf.Max(.001f, common.killTime) < .3f) mythic.flags.Add("QUALITY_GAP");
            }
        }

        static string F(float v, string fmt = "0.##") => float.IsNaN(v) ? "—" : float.IsInfinity(v) ? "∞" : v.ToString(fmt, CultureInfo.InvariantCulture);

        static void Trace(ResultRow r, PlayerBuild p, EnemyView e, WeaponItemData weapon, AnalysisConditions c, CombatBalanceAnalysisModel.SimOutcome o)
        {
            var t = r.trace; var tu = OverburstElementTuning.Current;
            t.Add(new TraceLine("무기 단독 공격", F(p.weaponAttack), "원본×(1+0.03×(Lv−1))×(1+0.10×공격별)", "WeaponStatCalculator.Calculate"));
            t.Add(new TraceLine("최종 공격 D", F(p.attack), $"round(({F(p.weaponAttack)}+장비 {F(p.gearAttack)})×(1+0.005×(Lv−1)))", "CombatBalanceFormulas.ComposePlayerWeaponStats ← PlayerEquipment"));
            t.Add(new TraceLine("치명 확률", F(p.crit) + "%", $"min(65, 무기+장갑 {F(p.statCrit)} + 자세 {(c.combatStance ? 10 : 0)})", "CombatBalanceFormulas.EffectiveCriticalChance ← MeleeRuntime"));
            t.Add(new TraceLine("치명 피해", F(p.critDamage * 100f) + "%", "min(300%, 무기+목걸이/보조)", "ComposePlayerWeaponStats"));
            t.Add(new TraceLine("공격 속도", "×" + F(p.attackSpeed, "0.###"), "min(1.5, 무기+장비 공속/100)", "ComposePlayerWeaponStats"));
            t.Add(new TraceLine("장비 피해 보조", $"일반 {F(p.normalBonus)}% 정예 {F(p.eliteBonus)}% 약공 {F(p.weakBonus)}% 강공 {F(p.heavyBonus)}% 원소 {F(p.elementalBonus)}%",
                "×max(0.1, 1+합/100)", "CombatBalanceFormulas.ApplyPlayerOutgoing ← CombatHealth"));
            t.Add(new TraceLine("약공 1판정(1타)", $"{F(o.weakNormal)} / 치명 {F(o.weakCrit)} / 기대 {F(o.weakExpected)}", "round(D×Phase×0.35[×치피]) → 보조 적용", "MeleeAttackStatResolver·RoundedHitDamage·ApplyPlayerOutgoing"));
            t.Add(new TraceLine("4타 1순환", $"{F(o.cycleDuration)}초, DPS {F(o.weakDps)}", "클립 길이/(콤보 기본×재생 배율×타 배율), 가속 구간 ToElapsed, 다음 입력 구간 시작", "MeleeComboStepData·MeleePlaybackAcceleration"));
            t.Add(new TraceLine("에너지", $"Phase당 {F(CombatBalanceFormulas.PhaseEnergyGain(tu, false, 0))}/치명 {F(CombatBalanceFormulas.PhaseEnergyGain(tu, true, 0))}", "적중 Phase당 1회, 여러 대상 중 하나라도 치명이면 치명값", "CombatBalanceFormulas.PhaseEnergyGain ← OverburstElementEnergy"));
            if (r.mode == CombatMode.Single) t.Add(new TraceLine("완충 시간", $"{F(r.chargeTime)}초 / {F(r.chargePhases, "0.#")} Phase", "적이 죽지 않는다고 본 순수 충전", "모델 시간 진행"));
            float heavyAttack = p.heavyAttackDamage;
            t.Add(new TraceLine("강공 첫 폭발 H(완충)", F(CombatBalanceFormulas.HeavyFirstBlastDamage(heavyAttack, 1f, CombatBalanceFormulas.DischargeEnergyCoefficient(tu, 1f, 0, 0), p.dischargePower)),
                $"D·p0×lerp(0.3,2,e)×(1+{F(tu.dischargeDamageAtFullEnergy)}e)+B×e, B={F(p.dischargePower)}", "CombatBalanceFormulas.HeavyFirstBlastDamage ← OverburstElementDischarge"));
            t.Add(new TraceLine("강공 적중 기대(보조·치명 포함)", F(r.heavyDirect), "lerp(round(H), round(H×치피), 치확) → 강공+원소 보조", "MeleeRuntime.CommitHeavyDischargeAtImpact → DealPatternDamage"));
            t.Add(new TraceLine("몬스터 체력", F(e.maxHealth), e.legacyHealth ? "옛 성장식(기준 계수 없음)" : $"round({F(e.coefficient)}×Qref({e.level}))", "CombatBalanceFormulas.EnemyReferenceHealth ← EnemyRank"));
            if (e.worstNormal != null) t.Add(new TraceLine("가장 아픈 일반 공격", $"{e.worstNormal.abilityId} 타당 {F(e.worstNormal.perHit)} ×{e.worstNormal.hitCount}타 = {F(e.worstNormal.patternTotal)} (최대 HP의 {F(e.worstNormal.percentOfHealth, "0.#")}%) → 패턴 {e.worstNormal.survivablePatterns}회·타 {e.worstNormal.survivableHits}회 버팀",
                "ResolveDamage(L)=round(기준 유효체력×예산%/타수)×등급·변형 → ×max(0.2,100/(100+방어)), 하한 10%", "EnemyAbilityDefinition.ResolveDamage · CombatBalanceFormulas.PlayerArmorMultiplier"));
            if (e.worstStrong != null) t.Add(new TraceLine("가장 아픈 강공", $"{e.worstStrong.abilityId} 타당 {F(e.worstStrong.perHit)} ×{e.worstStrong.hitCount}타 = {F(e.worstStrong.patternTotal)} (최대 HP의 {F(e.worstStrong.percentOfHealth, "0.#")}%) → 패턴 {e.worstStrong.survivablePatterns}회·타 {e.worstStrong.survivableHits}회 버팀",
                "같은 경로", "EnemyAbilityDefinition"));
            if (p.element == WeaponElement.Light && !float.IsNaN(r.lightTripleTime))
                t.Add(new TraceLine("빛 3연타 준비", $"{F(r.lightTripleTime)}초", "에너지 200·광휘 100까지(감쇠 10/초, 가득 차면 2초 유지)", "OverburstElementEnergy(빛 과충전)"));
            t.Add(new TraceLine("피해 구성", $"약공 {F(o.weak, "0")} · 강공 {F(o.heavy, "0")} · 파생 {F(o.derived, "0")} · 틱 {F(o.dot, "0")}", "처치까지 준 기대 피해(초과분 제외)", "모델 시간 진행"));
            if (p.element == WeaponElement.Dark) t.Add(new TraceLine("어둠 후속 규칙", CombatBalanceAnalysisModel.ReadDarkRule(tu).source, "튜닝 SO를 반사로 읽음", "OverburstElementTuning"));
            t.Add(new TraceLine("장비", string.Join(" | ", p.gearLines), "GearQuality.Roll(실제 추첨)", "GearQuality"));
        }
    }
}
