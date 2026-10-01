using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Overburst.EditorBalance.Analysis
{
    // 조합별 전투 결과를 실제 코드·SO의 계산 함수만 호출해 구한다. 새 피해식은 만들지 않는다.
    // 호출하는 계산: WeaponStatCalculator, GearQuality, GearStatTotals, OverburstCombatBalance, OverburstGrowthRules,
    // CombatBalanceFormulas(런타임과 공유), EnemyAbilityDefinition.ResolveDamage, EnemyDefinition.ResolveRuntimeStats,
    // MeleePlaybackAcceleration.ToElapsed, MeleeAttackSpeedPolicy, OverburstElementTuning 값.
    // 시간 흐름(콤보·강공 순서, 대상 선택, 군집 밀도)만 이 모델이 소유하며 Play 측정으로 대조한다.
    public static partial class CombatBalanceAnalysisModel
    {
        public const string DefaultWeaponPath =
            "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset";
        const string WeaponFolder = "Assets/ProjectOverburst/03_Features/Weapons";
        const string GearFolder = "Assets/ProjectOverburst/Resources/Items/Gear";
        const string EnemyFolder = "Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions";
        const string PlayerPrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
        const float MinAttackDuration = .2f; // MeleeRuntime.MinAttackDuration과 같은 값(비공개 상수)
        const int CandidateRolls = 32;       // 공격·생존 치중 구성에서 비교하는 실제 추첨 표본 수

        public static readonly GearSlot[] Slots =
            { GearSlot.Helmet, GearSlot.Chest, GearSlot.Gloves, GearSlot.Boots, GearSlot.EarringOne, GearSlot.EarringTwo, GearSlot.Necklace };

        public sealed class Catalog
        {
            public readonly List<WeaponItemData> weapons = new List<WeaponItemData>();
            public readonly Dictionary<GearKind, List<GearItemData>> gear = new Dictionary<GearKind, List<GearItemData>>();
            public readonly List<EnemyDefinition> enemies = new List<EnemyDefinition>();
            public readonly Dictionary<EnemyClass, EnemyDefinition> representative = new Dictionary<EnemyClass, EnemyDefinition>();
            public OverburstElementTuning tuning;
            public float playerBaseHealth = 100f;
            public string playerBaseHealthSource;

            public static Catalog Load()
            {
                var c = new Catalog();
                foreach (var w in Find<WeaponItemData>(WeaponFolder))
                    if (w.weaponClass == WeaponClass.Greatsword && WeaponContentPolicy.IsActiveWeapon(w)) c.weapons.Add(w);
                c.weapons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                foreach (var g in Find<GearItemData>(GearFolder))
                {
                    if (!c.gear.TryGetValue(g.kind, out var list)) c.gear[g.kind] = list = new List<GearItemData>();
                    list.Add(g);
                }
                foreach (var list in c.gear.Values) list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                c.enemies.AddRange(Find<EnemyDefinition>(EnemyFolder).Where(d => d.IsValid).OrderBy(d => d.EnemyId, StringComparer.Ordinal));
                foreach (EnemyClass cls in Enum.GetValues(typeof(EnemyClass)))
                {
                    var members = c.enemies.Where(d => Classify(d) == cls && d.ReferenceHealthCoefficient > 0f)
                        .OrderBy(d => d.ReferenceHealthCoefficient).ThenBy(d => d.EnemyId, StringComparer.Ordinal).ToList();
                    if (members.Count > 0) c.representative[cls] = members[members.Count / 2];
                }
                c.tuning = OverburstElementTuning.Current;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                var health = prefab != null ? prefab.GetComponent<CombatHealth>() : null;
                if (health != null) { c.playerBaseHealth = health.MaxHp; c.playerBaseHealthSource = PlayerPrefabPath; }
                else c.playerBaseHealthSource = "PF_PlayerActor 없음 → 100";
                return c;
            }

            public WeaponItemData Weapon(string path)
                => AssetDatabase.LoadAssetAtPath<WeaponItemData>(path) ?? weapons.FirstOrDefault();

            public GearItemData GearFor(GearKind kind, int level)
            {
                if (!gear.TryGetValue(kind, out var list) || list.Count == 0) return null;
                return list.FirstOrDefault(g => g.AppearsAtLevel(level)) ?? list[0];
            }

            public IEnumerable<EnemyDefinition> OfClass(EnemyClass cls) => enemies.Where(d => Classify(d) == cls);
        }

        static IEnumerable<T> Find<T>(string folder) where T : Object
            => AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder })
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(x => x != null);

        // 체급 구분은 BalanceTableDocument의 소형 판정(일반 등급 + 가벼운 피격 체급)을 따른다.
        public static EnemyClass Classify(EnemyDefinition d)
        {
            EnemyGradeType grade = d.Grade != null ? d.Grade.GradeType : EnemyGradeType.Normal;
            if (grade != EnemyGradeType.Normal) return EnemyClass.Elite;
            var weight = d.MovementProfile != null && d.MovementProfile.HitWeightProfile != null
                ? d.MovementProfile.HitWeightProfile.Weight : EnemyHitWeight.Standard;
            return weight == EnemyHitWeight.Light ? EnemyClass.Small : EnemyClass.Medium;
        }

        public static int LevelFor(int bucket, LevelPoint point)
            => Mathf.Clamp(bucket * 10 + (point == LevelPoint.Start ? 1 : point == LevelPoint.Middle ? 5 : 10), 1, 100);

        // ───────────────────────── 플레이어 구성
        public static PlayerBuild BuildPlayer(Catalog catalog, WeaponItemData weapon, int level, ItemGrade grade, GearPreset preset,
            WeaponElement element, int seed, bool combatStance, List<string> problems = null)
        {
            var saved = UnityEngine.Random.state;
            try
            {
                var build = new PlayerBuild { level = level, grade = grade, preset = preset, element = element, seed = seed, weaponName = weapon.name };
                build.weaponItem = preset == GearPreset.Offense ? BestWeapon(weapon, level, grade, element, seed) : NewWeapon(weapon, level, grade, element, seed);
                build.gearItems = new List<ItemData>();
                if (preset != GearPreset.WeaponOnly)
                    for (int s = 0; s < Slots.Length; s++)
                    {
                        GearKind kind = KindOf(Slots[s]);
                        GearItemData data = catalog.GearFor(kind, level);
                        if (data == null) { problems?.Add("장비 자산 없음: " + kind); continue; }
                        ItemData item = preset == GearPreset.Rolled ? NewGear(data, level, grade, seed * 131 + s * 7919)
                            : BestGear(data, level, grade, seed * 131 + s * 7919, preset == GearPreset.Offense);
                        if (!GearQuality.IsValid(data, grade, item.gearRolls)) problems?.Add($"유효하지 않은 장비 추첨 {data.name} {grade}");
                        build.gearItems.Add(item);
                    }
                Resolve(catalog, build, combatStance);
                return build;
            }
            finally { UnityEngine.Random.state = saved; }
        }

        public static GearKind KindOf(GearSlot slot) => slot == GearSlot.Helmet ? GearKind.Helmet : slot == GearSlot.Chest ? GearKind.Chest
            : slot == GearSlot.Gloves ? GearKind.Gloves : slot == GearSlot.Boots ? GearKind.Boots
            : slot == GearSlot.Necklace ? GearKind.Necklace : GearKind.Earring;

        public static ItemData NewWeapon(WeaponItemData weapon, int level, ItemGrade grade, WeaponElement element, int seed)
        {
            UnityEngine.Random.InitState(seed);
            return new ItemData(weapon, level, grade, 1, element);
        }

        public static ItemData NewGear(GearItemData data, int level, ItemGrade grade, int seed)
        {
            var item = new ItemData(data, level, grade);
            item.gearRolls = GearQuality.Roll(data, grade, seed);
            return item;
        }

        static ItemData BestWeapon(WeaponItemData weapon, int level, ItemGrade grade, WeaponElement element, int seed)
        {
            ItemData best = null; float bestScore = float.MinValue;
            for (int i = 0; i < CandidateRolls; i++)
            {
                var item = NewWeapon(weapon, level, grade, element, seed + i * 104729);
                var s = WeaponStatCalculator.Calculate(item);
                float score = s.damage * (1f + Mathf.Clamp01(s.critChance / 100f) * (s.critDamageMultiplier - 1f)) * s.meleeAttackSpeedMultiplier;
                if (score > bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        static ItemData BestGear(GearItemData data, int level, ItemGrade grade, int seed, bool offense)
        {
            ItemData best = null; float bestScore = float.MinValue;
            float hp = OverburstCombatBalance.GearBase(GearKind.Helmet, level), armor = OverburstCombatBalance.GearBase(GearKind.Chest, level);
            float attack = OverburstCombatBalance.GearBase(GearKind.Earring, level);
            for (int i = 0; i < CandidateRolls; i++)
            {
                var item = NewGear(data, level, grade, seed + i * 15485863);
                var t = GearStatTotals.FromItems(new[] { item });
                float score = offense
                    ? t.Attack / Mathf.Max(1f, attack) * 10f + t.CriticalChance * 3f + t.CriticalDamage * 1.5f + t.AttackSpeed * 2f
                      + t.WeakDamage + t.HeavyDamage + t.ElementalDamage + t.NormalDamage + t.EliteBossDamage
                    : t.MaxHealth / Mathf.Max(1f, hp) * 100f + t.Armor / Mathf.Max(1f, armor) * 100f;
                if (score > bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        static void Resolve(Catalog catalog, PlayerBuild b, bool combatStance)
        {
            WeaponFinalStats weaponOnly = WeaponStatCalculator.Calculate(b.weaponItem);
            b.weaponAttack = weaponOnly.damage;
            b.totals = GearStatTotals.FromItems(b.gearItems);
            b.stats = CombatBalanceFormulas.ComposePlayerWeaponStats(weaponOnly, b.totals, b.level, 0f);
            b.attack = b.stats.damage;
            b.statCrit = b.stats.critChance;
            b.crit = CombatBalanceFormulas.EffectiveCriticalChance(b.stats.critChance, combatStance);
            b.critDamage = b.stats.critDamageMultiplier;
            b.attackSpeed = b.stats.meleeAttackSpeedMultiplier;
            b.range = b.stats.range;
            b.slashAngle = b.stats.meleeSlashAngle;
            b.maxHealth = catalog.playerBaseHealth + CombatBalanceFormulas.PlayerPermanentHealthBonus(b.level, b.totals);
            b.armor = CombatBalanceFormulas.PlayerArmor(b.level, b.totals, 0f);
            b.armorMultiplier = CombatBalanceFormulas.PlayerArmorMultiplier(b.armor);
            var heavy = HeavyDefinition(b.weaponItem.baseData as WeaponItemData);
            float p0 = heavy != null && heavy.attack.attackPhases != null && heavy.attack.attackPhases.Length > 0
                ? heavy.attack.attackPhases[0].impact.SafeDamageMultiplier : 1f;
            b.heavyAttackDamage = b.attack * p0;
            b.dischargePower = WeaponStatCalculator.GetElementalDischargePower(b.weaponItem, b.heavyAttackDamage);
            b.gearAttack = b.totals.Attack; b.gearCrit = b.totals.CriticalChance; b.gearCritDamage = b.totals.CriticalDamage;
            b.gearAttackSpeed = b.totals.AttackSpeed; b.gearHealth = b.totals.MaxHealth; b.gearArmor = b.totals.Armor;
            b.normalBonus = b.totals.NormalDamage; b.eliteBonus = b.totals.EliteBossDamage; b.weakBonus = b.totals.WeakDamage;
            b.heavyBonus = b.totals.HeavyDamage; b.elementalBonus = b.totals.ElementalDamage;
            b.gearLines.Clear();
            foreach (var item in b.gearItems)
                b.gearLines.Add(item.baseData.name + ": " + string.Join(" / ", item.gearRolls.Select(r =>
                    $"{r.stat} {GearQuality.Value(item, r):0.##}({r.stars.Count}별)")));
        }

        public static MeleeHeavyAttackDefinition HeavyDefinition(WeaponItemData weapon)
        {
            var melee = weapon != null ? weapon.GetMeleeDefinition() : null;
            return melee != null ? melee.heavyAttackDefinition : null;
        }

        // ───────────────────────── 몬스터
        public static EnemyView BuildEnemy(EnemyDefinition d, int level, PlayerBuild player)
        {
            var v = new EnemyView
            {
                id = d.EnemyId, name = d.DisplayName, definition = d, enemyClass = Classify(d),
                grade = d.Grade != null ? d.Grade.GradeType : EnemyGradeType.Normal, level = level,
                coefficient = d.ReferenceHealthCoefficient, shield = d.BehaviorProfile != null && d.BehaviorProfile.HasShield
            };
            EnemyRuntimeStats runtime = d.ResolveRuntimeStats();
            v.legacyHealth = d.ReferenceHealthCoefficient <= 0f;
            v.maxHealth = v.legacyHealth ? runtime.MaxHealth * OverburstGrowthRules.EnemyHealthFactor(level)
                : CombatBalanceFormulas.EnemyReferenceHealth(d.ReferenceHealthCoefficient, level);
            var set = d.AbilitySet;
            for (int i = 0; set != null && i < set.Count; i++)
            {
                EnemyAbilityDefinition a = set.GetAbility(i);
                if (a == null) continue;
                var view = new EnemyAttackView
                {
                    abilityId = a.AbilityId, strong = a.IsTelegraphedStrongAttack, parryable = a.IsParryable, hitCount = a.HitCount,
                    legacyGrowth = CombatBalanceFormulas.UsesLegacyEnemyDamageGrowth(a)
                };
                view.rawPerHit = a.ResolveDamage(level) * runtime.DamageMultiplier;
                if (view.legacyGrowth) view.rawPerHit *= OverburstGrowthRules.EnemyDamageFactor(level);
                if (player != null)
                {
                    // CombatHealth: 방어 배율 뒤 최종 하한(방어 전 피해의 10%).
                    view.perHit = Mathf.Max(view.rawPerHit * player.armorMultiplier, view.rawPerHit * CombatBalanceFormulas.PlayerIncomingDamageFloor);
                    view.patternTotal = view.perHit * view.hitCount;
                    view.percentOfHealth = view.patternTotal / Mathf.Max(1f, player.maxHealth) * 100f;
                    view.survivableHits = view.perHit > 0f ? Mathf.CeilToInt(player.maxHealth / view.perHit) - 1 : 9999;
                    view.survivablePatterns = view.patternTotal > 0f ? Mathf.CeilToInt(player.maxHealth / view.patternTotal) - 1 : 9999;
                }
                v.attacks.Add(view);
                // 가장 위험한 공격은 패턴 전체 피해(타당 × 타수)로 고른다. 여러 타 공격을 타당으로만 보면 버팀 수가 과대해진다.
                if (view.strong) { if (v.worstStrong == null || view.patternTotal > v.worstStrong.patternTotal) v.worstStrong = view; }
                else if (v.worstNormal == null || view.patternTotal > v.worstNormal.patternTotal) v.worstNormal = view;
            }
            return v;
        }

        // ───────────────────────── 콤보·강공 시간 (MeleeRuntime.ResolveAttackAnimation / ResolveAttackDuration 과 같은 입력)
        public struct PhaseTime { public float time, coefficient; public int index; }

        public sealed class StepTiming
        {
            public int step;
            public float duration, chainAt, cancelAt, endAt;
            public readonly List<PhaseTime> phases = new List<PhaseTime>();
            public readonly List<PhaseTime> tailPhases = new List<PhaseTime>(); // 다음 타로 넘어가지 않을 때만 나오는 판정
        }

        public static float PlaybackMultiplier(WeaponItemData weapon, float attackSpeed)
        {
            var melee = weapon.GetMeleeDefinition();
            float baseline = melee != null ? melee.baseSettings.SafeAnimationPlaybackBaseline : MeleeAttackSpeedPolicy.BaselineAnimationSpeedMultiplier;
            return MeleeAttackSpeedPolicy.ToPlaybackMultiplier(Mathf.Max(.01f, attackSpeed), baseline);
        }

        public static StepTiming TimeComboStep(WeaponItemData weapon, int stepIndex, bool continuation, float attackSpeed)
        {
            MeleeComboDefinition combo = weapon.GetMeleeComboDefinition();
            MeleeComboStepData step = combo.GetStep(stepIndex);
            AnimationClip clip = step.animationClip != null ? step.animationClip
                : weapon.combatDefinition != null ? weapon.combatDefinition.animation.primaryAttackClip : null;
            float speed = Mathf.Max(.01f, combo.baseAnimationSpeed * PlaybackMultiplier(weapon, attackSpeed) * Mathf.Max(.01f, step.animationSpeedMultiplier));
            float duration = Mathf.Max(MinAttackDuration, clip != null ? clip.length / speed : MinAttackDuration);
            float entry = continuation ? Mathf.Clamp(step.continuationStartNormalizedTime, 0f, .95f) : 0f;
            MeleePlaybackAcceleration acc = step.playbackAcceleration;
            float Elapsed(float p) => duration * (acc.ToElapsed(p) - acc.ToElapsed(entry));
            float chain = Mathf.Clamp(step.comboInputWindow.SafeStart, .01f, 1f);
            var t = new StepTiming { step = stepIndex, duration = duration, chainAt = Elapsed(Mathf.Max(chain, entry)), endAt = Elapsed(1f) };
            t.cancelAt = step.actionCancelStartNormalized > entry ? Elapsed(step.actionCancelStartNormalized) : t.endAt;
            AttackPhaseData[] phases = step.attackPhases ?? Array.Empty<AttackPhaseData>();
            for (int i = 0; i < phases.Length; i++)
            {
                float start = phases[i].SafeStart;
                if (start < entry) continue;
                var pt = new PhaseTime { time = Elapsed(start), coefficient = phases[i].impact.SafeDamageMultiplier, index = i };
                if (start < chain) t.phases.Add(pt); else t.tailPhases.Add(pt);
            }
            return t;
        }

        public sealed class HeavyTiming { public float duration, impactAt, endAt; public float phaseMultiplier; }

        public static HeavyTiming TimeHeavy(WeaponItemData weapon, float attackSpeed)
        {
            var heavy = HeavyDefinition(weapon);
            var h = new HeavyTiming();
            if (heavy == null || !heavy.IsConfigured) { h.duration = h.endAt = 1f; h.impactAt = .5f; h.phaseMultiplier = 1f; return h; }
            MeleeComboStepData step = heavy.attack;
            float speed = Mathf.Max(.01f, PlaybackMultiplier(weapon, attackSpeed) * Mathf.Max(.01f, step.animationSpeedMultiplier));
            h.duration = Mathf.Max(MinAttackDuration, step.animationClip.length / speed);
            h.impactAt = h.duration * step.playbackAcceleration.ToElapsed(step.attackPhases[0].SafeStart);
            h.endAt = h.duration * step.playbackAcceleration.ToElapsed(1f);
            h.phaseMultiplier = step.attackPhases[0].impact.SafeDamageMultiplier;
            return h;
        }

        // ───────────────────────── 어둠 강공 규칙: 잠식 탄막(DarkBarrageScheduler, dfb874e 이후 제품 경로)
        // 튜닝 속성을 직접 읽는다. 이름이 바뀌면 조용히 기본값을 쓰지 않고 컴파일 오류로 드러난다.
        public sealed class DarkRule
        {
            public float shotFraction, finisherFraction, searchMultiplier;
            public float interval, riseTime, riseStagger, riseHeight, speed, flightMin, flightMax;
            public int maxConcurrent;
            public OverburstElementTuning tuning;
            public string source;
            public int TargetLimit(float e) => tuning.DarkBarrageTargetLimit(e);
            // 첫 발사 시각(강공 확정 기준): 제출 프레임 다음부터 시계가 돈다 + 솟기 + 최대 지연.
            public float FirstRelease => 1f / 60f + riseTime + riseStagger;
            // 유도 비행 시간: 휜 경로 길이(직선 ×1.35) ÷ 속도, 최소·최대로 자른다(DarkBarrageScheduler.BeginHoming).
            public float Flight(float horizontal) => Mathf.Clamp(Mathf.Sqrt(horizontal * horizontal + riseHeight * riseHeight) * 1.35f / Mathf.Max(.01f, speed), flightMin, flightMax);
        }

        public static DarkRule ReadDarkRule(OverburstElementTuning t)
        {
            var r = new DarkRule
            {
                tuning = t, searchMultiplier = t.SafeDarkGatherRadiusMultiplier,
                shotFraction = t.SafeDarkBarrageShotDamage, finisherFraction = t.SafeDarkBarrageFinisherDamage,
                interval = t.SafeDarkBarrageFireInterval, riseTime = t.SafeDarkBarrageRiseTime, riseStagger = t.SafeDarkBarrageRiseStagger,
                riseHeight = (t.SafeDarkBarrageRiseHeightMin + t.SafeDarkBarrageRiseHeightMax) * .5f, speed = t.SafeDarkBarrageSpeed,
                flightMin = t.SafeDarkBarrageFlightMin, flightMax = t.SafeDarkBarrageFlightMax, maxConcurrent = t.SafeDarkBarrageMaxConcurrent,
            };
            r.source = $"DarkBarrageScheduler(잠식 탄막): 중첩 차수마다 대상을 가까운 순으로 돌며 탄 1발 H×{r.shotFraction}, 완충이면 대상마다 대형탄 H×{r.finisherFraction}. "
                + $"대상 {t.SafeDarkBarrageMinTargets}~{t.SafeDarkBarrageMaxTargets}, 탐색 반경 강공×{r.searchMultiplier}, 첫 발사 {r.FirstRelease:0.###}초 뒤 {r.interval}초 간격, 비행 {r.flightMin}~{r.flightMax}초, 동시 {r.maxConcurrent}";
            return r;
        }
    }
}
