using System;
using System.Collections.Generic;

namespace Overburst.EditorBalance.Analysis
{
    public enum GearPreset { WeaponOnly, Rolled, Offense, Survival }
    public enum EnemyClass { Small, Medium, Elite }
    public enum LevelPoint { Start, Middle, End }
    public enum CombatMode { Single, Crowd }

    public static class AnalysisLabels
    {
        public static string Preset(GearPreset p) => p == GearPreset.WeaponOnly ? "무기만" : p == GearPreset.Rolled ? "표준 추첨"
            : p == GearPreset.Offense ? "공격 치중" : "생존 치중";
        public static string Enemy(EnemyClass c) => c == EnemyClass.Small ? "소형" : c == EnemyClass.Medium ? "중형" : "정예";
        public static string Grade(ItemGrade g)
        {
            switch (g)
            {
                case ItemGrade.Common: return "일반";
                case ItemGrade.Uncommon: return "고급";
                case ItemGrade.Rare: return "희귀";
                case ItemGrade.Epic: return "영웅";
                case ItemGrade.Legendary: return "전설";
                case ItemGrade.Artifact: return "유물";
                case ItemGrade.Mythic: return "신화";
                default: return "저주";
            }
        }
        public static string Element(WeaponElement e) => e == WeaponElement.None ? "무원소" : OverburstElementRules.Label(e);
        public static string Bucket(int bucket) => $"{bucket * 10 + 1}–{bucket * 10 + 10}";
        public static string Mode(CombatMode m) => m == CombatMode.Single ? "단일" : "군집";
    }

    [Serializable]
    public sealed class AnalysisConditions
    {
        public List<int> levelBuckets = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        public LevelPoint levelPoint = LevelPoint.Middle;
        public int monsterLevelOffset;
        public List<ItemGrade> grades = new List<ItemGrade> { ItemGrade.Common, ItemGrade.Rare, ItemGrade.Epic, ItemGrade.Mythic };
        public List<GearPreset> presets = new List<GearPreset> { GearPreset.Rolled };
        public List<WeaponElement> elements = new List<WeaponElement>
            { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
        public List<EnemyClass> enemies = new List<EnemyClass> { EnemyClass.Small, EnemyClass.Medium, EnemyClass.Elite };
        public List<CombatMode> modes = new List<CombatMode> { CombatMode.Single, CombatMode.Crowd };
        public string weaponPath = CombatBalanceAnalysisModel.DefaultWeaponPath;
        public int seedCount = 8;
        public int seedBase = 731;
        public bool combatStance = true;
        public int crowdCount = 20;
        // 군집 입력(10-01 05:20 Play 측정 소형 20마리·적 AI 켬, 불·번개·얼음·어둠 정리 시간에 맞춘 기본값). 창에서 바꿀 수 있다.
        // 맞출 때 쓴 조합은 측정 계획에서 '보정' 역할로 분리하고, 모델 검증은 다른 시드·배치의 '검증' 역할 조합으로만 한다.
        // 모델과 실측의 차이는 원소마다 달라 원소 순위도 보장하지 않는다(보고서 '군집 정리 시간' 표).
        public float crowdWeakTargets = 4f;     // 약공 판정 1회에 맞는 평균 마리 수
        public float crowdHeavyTargets = 8f;    // 완충(반경 4m) 강공 첫 폭발에 맞는 평균 마리 수. 반경² 비례로 줄어든다
        public float packingDensity = 1.4f;     // 불 연쇄·번개 도약이 이웃을 찾는 밀착 밀도(마리/㎡)
        public bool lightTriple;
        public float maxSimulationSeconds = 60f;

        public AnalysisConditions Clone() => (AnalysisConditions)MemberwiseClone();
    }

    [Serializable]
    public sealed class TraceLine
    {
        public string label, value, formula, owner;
        public TraceLine(string label, string value, string formula, string owner)
        { this.label = label; this.value = value; this.formula = formula; this.owner = owner; }
    }

    [Serializable]
    public sealed class PlayerBuild
    {
        public int level, seed;
        public ItemGrade grade;
        public GearPreset preset;
        public WeaponElement element;
        public string weaponName;
        public float weaponAttack, attack, statCrit, crit, critDamage, attackSpeed, range, slashAngle;
        public float maxHealth, armor, armorMultiplier, heavyAttackDamage, dischargePower;
        public float gearAttack, gearCrit, gearCritDamage, gearAttackSpeed, gearHealth, gearArmor;
        public float normalBonus, eliteBonus, weakBonus, heavyBonus, elementalBonus;
        [NonSerialized] public GearStatTotals totals;
        [NonSerialized] public WeaponFinalStats stats;
        [NonSerialized] public ItemData weaponItem;
        [NonSerialized] public List<ItemData> gearItems;
        public List<string> gearLines = new List<string>();
        public float ReferenceQ => attack * (1f + crit / 100f * (critDamage - 1f));
        public PlayerBuild WithElement(WeaponElement value) { var copy = (PlayerBuild)MemberwiseClone(); copy.element = value; return copy; }
    }

    [Serializable]
    public sealed class EnemyAttackView
    {
        public string abilityId;
        public bool strong, parryable;
        public int hitCount;
        public float rawPerHit, perHit, patternTotal, percentOfHealth;
        public int survivableHits, survivablePatterns;
        public bool legacyGrowth;
    }

    [Serializable]
    public sealed class EnemyView
    {
        public string id, name;
        public EnemyClass enemyClass;
        public EnemyGradeType grade;
        public int level;
        public float coefficient, maxHealth;
        public bool legacyHealth, shield;
        public EnemyAttackView worstNormal, worstStrong;
        public List<EnemyAttackView> attacks = new List<EnemyAttackView>();
        [NonSerialized] public EnemyDefinition definition;
    }

    [Serializable]
    public sealed class ResultRow
    {
        public string key;
        public int bucket, level, enemyLevel;
        public ItemGrade grade;
        public GearPreset preset;
        public WeaponElement element;
        public EnemyClass enemyClass;
        public CombatMode mode;
        public string enemyId, enemyName;
        public int samples;

        public float attack, crit, critDamage, attackSpeed, playerHealth, playerArmor, referenceRatio;
        public float enemyHealth, enemyCount;

        public float weakHit, weakDps, weakOnlyKillTime, cycleDuration;
        public float chargeTime, chargePhases, prepDamage, prepHealthLeftPercent, lightTripleTime = float.NaN;
        public bool prepSurvived, heavyKilled;
        public float heavyDirect, heavyEnergy, killTime, heavyCount;
        public float dotDamage, dotShare, heavyShare, derivedShare, aoeShare;
        public float crowdClearTime, crowdKillsFirstHeavy, crowdWeakTargets, crowdHeavyTargets;
        public float parryHeavyBonus, parryRefundChargeTime;

        public int normalSurvivable, strongSurvivable;
        public float normalHitDamage, strongHitDamage;
        public string normalAbility, strongAbility;

        public float totalDamage, weakDamage, heavyDamage, derivedDamage;
        public List<string> flags = new List<string>();
        public List<TraceLine> trace = new List<TraceLine>();
        public float spread; // 표본 간 처치 시간 P90/P10 배율
        public float prepSurvivalRate, heavyKillRate;

        public bool HasFlags => flags.Count > 0;
        public string FlagText => string.Join(", ", flags);
        public string Title => $"Lv{level} {AnalysisLabels.Grade(grade)} {AnalysisLabels.Preset(preset)} {AnalysisLabels.Element(element)} "
            + $"→ {AnalysisLabels.Enemy(enemyClass)}({enemyName}) {AnalysisLabels.Mode(mode)}";
    }

    [Serializable]
    public sealed class Finding
    {
        public string id, severity, title, condition, cause, impact, proposal, evidence, category;
        public Finding(string id, string category, string severity, string title, string condition, string cause,
            string impact, string proposal, string evidence)
        {
            this.id = id; this.category = category; this.severity = severity; this.title = title; this.condition = condition;
            this.cause = cause; this.impact = impact; this.proposal = proposal; this.evidence = evidence;
        }
    }

    [Serializable]
    public sealed class FlagRule
    {
        public string code, label, description;
        public FlagRule(string code, string label, string description) { this.code = code; this.label = label; this.description = description; }
    }

    [Serializable]
    public sealed class AnalysisResult
    {
        public string createdAt, unityVersion, weapon;
        public AnalysisConditions conditions;
        public List<ResultRow> rows = new List<ResultRow>();
        public List<Finding> findings = new List<Finding>();
        public List<string> selfChecks = new List<string>();
        public int rowCount => rows.Count;
    }

    // ── Play 측정 결과 (CombatBalancePlayMeasurement가 기록, 창이 읽는다)
    [Serializable]
    public sealed class MeasuredHit
    {
        public float time, damage;
        public bool critical, heavy, derived, dot;
        public int phase, sequence;
    }

    [Serializable]
    public sealed class MeasurementScenario
    {
        public string key, status, note;
        public int level, seed;
        public ItemGrade grade;
        public GearPreset preset;
        public WeaponElement element;
        public string enemyId;
        public EnemyClass enemyClass;
        public float modelAttack, measuredAttack, modelCrit, measuredCrit, modelArmor, measuredArmor, modelMaxHealth, measuredMaxHealth;
        public float modelEnemyHealth, measuredEnemyHealth;
        public float modelWeakNormalHit, measuredWeakNormalHit, modelWeakCritHit, measuredWeakCritHit;
        public float modelChargeTime, measuredChargeTime, modelChargePhases, measuredChargePhases;
        public float modelHeavyDirect, measuredHeavyDirect;
        public bool modelPrepSurvived, measuredPrepSurvived, modelHeavyKilled, measuredHeavyKilled;
        public float modelIncomingNormal, measuredIncomingNormal, modelIncomingStrong, measuredIncomingStrong;
        public float measuredDot, measuredDerived, measuredElapsed;
        public int measuredWeakHits, measuredCrits;
        public string role;                                        // 보정용(군집 입력을 맞춘 조합) / 검증용
        public float modelHeavyKillChance = float.NaN;             // 모델 1주기 처치 확률(단일)
        public float modelClearTime = float.NaN;                   // 모델 군집 정리 시간
        public List<float> modelDerivedTimes = new List<float>();  // 첫 강공 뒤 대상이 받는 파생 피해 시각(강공 적중 기준, 모델)
        public List<string> checks = new List<string>();
        public List<MeasuredHit> hits = new List<MeasuredHit>();
    }

    [Serializable]
    public sealed class MeasurementReport
    {
        public string status, startedAt, finishedAt, unityVersion, scope;
        public string planVersion, fingerprint;                     // 이어 하기·병합은 같은 계획·같은 지문 회차끼리만
        public List<MeasurementScenario> scenarios = new List<MeasurementScenario>();
        public List<string> errors = new List<string>();
    }
}
