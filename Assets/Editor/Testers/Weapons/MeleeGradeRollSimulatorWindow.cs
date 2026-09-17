using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

public sealed class MeleeGradeRollSimulatorWindow : EditorWindow
{
    private const string DefaultWeaponPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset";
    private const int MaxSimulationCount = 100000;

    private readonly GUIStyle richLabel = new GUIStyle();
    private readonly Dictionary<WeaponGradeStatType, StatAggregate> aggregates = new Dictionary<WeaponGradeStatType, StatAggregate>();
    private WeaponItemData weaponData;
    private ItemGrade grade = ItemGrade.Legendary;
    private int seed = 12345;
    private int simulationCount = 1000;
    private Vector2 scrollPosition;
    private ItemData lastItem;
    private WeaponFinalStats lastStats;
    private MeleeSingleTargetDpsEstimate lastDps;
    private AggregateSummary aggregateSummary;

    [MenuItem("OVERBURST/Codex/Tools/Melee Grade Roll Simulator")]
    public static void Open()
    {
        GetWindow<MeleeGradeRollSimulatorWindow>("Melee Roll Simulator");
    }

    private void OnEnable()
    {
        minSize = new Vector2(560f, 540f);
        if (weaponData == null)
            weaponData = AssetDatabase.LoadAssetAtPath<WeaponItemData>(DefaultWeaponPath);

        richLabel.richText = true;
        richLabel.wordWrap = true;
        richLabel.normal.textColor = EditorStyles.label.normal.textColor;
    }

    private void OnGUI()
    {
        DrawControls();
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawLastRoll();
        DrawAggregateSummary();
        EditorGUILayout.EndScrollView();
    }

    private void DrawControls()
    {
        EditorGUILayout.LabelField("밀리 등급 롤 시뮬레이터", EditorStyles.boldLabel);
        weaponData = (WeaponItemData)EditorGUILayout.ObjectField("무기", weaponData, typeof(WeaponItemData), false);
        grade = (ItemGrade)EditorGUILayout.EnumPopup("등급", grade);
        seed = EditorGUILayout.IntField("시드", seed);
        simulationCount = Mathf.Clamp(EditorGUILayout.IntField("반복 횟수", simulationCount), 1, MaxSimulationCount);

        bool validWeapon = weaponData != null && WeaponGradeStatRoller.IsMeleeWeapon(weaponData);
        if (!validWeapon)
            EditorGUILayout.HelpBox("Melee 계열 WeaponItemData를 선택하세요.", MessageType.Warning);

        using (new EditorGUI.DisabledScope(!validWeapon))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("1회 롤"))
                RollOnce(seed);
            if (GUILayout.Button("다음 시드 롤"))
            {
                seed++;
                RollOnce(seed);
            }
            if (GUILayout.Button("반복 통계"))
                RunAggregateSimulation();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.HelpBox(
            "DPS 기준: 좌클릭 유지 콤보, 단일 대상, 각 Attack Phase 1회 적중, 치명타 기대값 포함. 명중 실패·자세 보너스·상태이상·방어력은 제외.",
            MessageType.Info);
    }

    private void RollOnce(int rollSeed)
    {
        Random.State previousState = Random.state;
        try
        {
            Random.InitState(rollSeed);
            lastItem = new ItemData(weaponData, 1, grade);
            lastStats = WeaponStatCalculator.Calculate(lastItem);
            lastDps = MeleeSingleTargetDpsCalculator.Estimate(weaponData, lastStats);
        }
        finally
        {
            Random.state = previousState;
        }

        Repaint();
    }

    private void RunAggregateSimulation()
    {
        aggregates.Clear();
        IReadOnlyList<WeaponGradeStatType> statTypes = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();
        for (int i = 0; i < statTypes.Count; i++)
            aggregates[statTypes[i]] = new StatAggregate();

        AggregateSummary summary = new AggregateSummary
        {
            SampleCount = simulationCount,
            MinDps = float.PositiveInfinity,
            MaxDps = float.NegativeInfinity
        };

        Random.State previousState = Random.state;
        try
        {
            Random.InitState(seed);
            for (int sampleIndex = 0; sampleIndex < simulationCount; sampleIndex++)
            {
                ItemData item = new ItemData(weaponData, 1, grade);
                WeaponFinalStats stats = WeaponStatCalculator.Calculate(item);
                MeleeSingleTargetDpsEstimate dps = MeleeSingleTargetDpsCalculator.Estimate(weaponData, stats);
                AccumulateProfile(item, ref summary);
                AccumulateRoll(item, aggregates, ref summary);
                if (dps.IsValid)
                {
                    summary.ValidDpsCount++;
                    summary.DpsSum += dps.Dps;
                    summary.MinDps = Mathf.Min(summary.MinDps, dps.Dps);
                    summary.MaxDps = Mathf.Max(summary.MaxDps, dps.Dps);
                }
            }
        }
        finally
        {
            Random.state = previousState;
        }

        aggregateSummary = summary;
        RollOnce(seed);
        Repaint();
    }

    private void DrawLastRoll()
    {
        if (lastItem == null)
            return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("1회 롤 결과", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("배분 성향: " + GetProfileName(lastItem.meleeStarDistributionProfile));
        EditorGUILayout.LabelField("등급 기본 지급: " + BuildGuaranteedStarSummary(lastItem.grade));
        IReadOnlyList<WeaponGradeStatType> statTypes = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();
        for (int i = 0; i < statTypes.Count; i++)
        {
            WeaponGradeStatType statType = statTypes[i];
            WeaponGradeStatRoll roll = WeaponGradeStatRoller.GetRoll(lastItem.weaponGradeStats, statType);
            string stars = BuildStarText(roll);
            string effect = roll != null ? FormatEffect(statType, roll.positiveTotalValue - roll.negativeTotalValue) : "0";
            EditorGUILayout.LabelField(GetStatName(statType) + "  " + effect + "  " + stars, richLabel);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(
            string.Format(
                CultureInfo.InvariantCulture,
                "최종: 데미지 {0:0.0} / 공속 {1:0.0}% / 범위 {2:0.00}m / 치확 {3:0.0}% / 치피 {4:0.0}%",
                lastStats.damage,
                lastStats.meleeAttackSpeedMultiplier * 100f,
                lastStats.range,
                lastStats.critChance,
                lastStats.critDamageMultiplier * 100f));

        if (lastDps.IsValid)
        {
            EditorGUILayout.LabelField(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "단일 DPS {0:0.0}  (콤보 {1:0.0} 피해 / {2:0.00}초 / {3}회 적중)",
                    lastDps.Dps,
                    lastDps.ExpectedCycleDamage,
                    lastDps.CycleDuration,
                    lastDps.HitCount),
                EditorStyles.boldLabel);
        }
    }

    private void DrawAggregateSummary()
    {
        if (aggregateSummary == null)
            return;

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("반복 통계", EditorStyles.boldLabel);
        float colorTotal = Mathf.Max(1f, aggregateSummary.PositiveStarCount);
        EditorGUILayout.LabelField(
            string.Format(
                CultureInfo.InvariantCulture,
                "표본 {0:N0} / 흰 {1:0.0}% / 초록 {2:0.0}% / 노랑 {3:0.0}% / 붉은별 {4:N0}개",
                aggregateSummary.SampleCount,
                aggregateSummary.WhiteStarCount * 100f / colorTotal,
                aggregateSummary.GreenStarCount * 100f / colorTotal,
                aggregateSummary.YellowStarCount * 100f / colorTotal,
                aggregateSummary.RedStarCount));

        int profiledSampleCount = aggregateSummary.FocusedProfileCount
            + aggregateSummary.DualCoreProfileCount
            + aggregateSummary.SpreadProfileCount;
        if (profiledSampleCount > 0)
        {
            EditorGUILayout.LabelField(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "성향: 집중형 {0:0.0}% / 쌍축형 {1:0.0}% / 분산형 {2:0.0}%",
                    aggregateSummary.FocusedProfileCount * 100f / profiledSampleCount,
                    aggregateSummary.DualCoreProfileCount * 100f / profiledSampleCount,
                    aggregateSummary.SpreadProfileCount * 100f / profiledSampleCount));
        }

        if (aggregateSummary.ValidDpsCount > 0)
        {
            EditorGUILayout.LabelField(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "DPS 최소 {0:0.0} / 평균 {1:0.0} / 최대 {2:0.0}",
                    aggregateSummary.MinDps,
                    aggregateSummary.DpsSum / aggregateSummary.ValidDpsCount,
                    aggregateSummary.MaxDps),
                EditorStyles.boldLabel);
        }

        IReadOnlyList<WeaponGradeStatType> statTypes = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();
        for (int statIndex = 0; statIndex < statTypes.Count; statIndex++)
        {
            WeaponGradeStatType statType = statTypes[statIndex];
            StatAggregate value = aggregates[statType];
            float samples = Mathf.Max(1, aggregateSummary.SampleCount);
            EditorGUILayout.LabelField(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: 평균 별 {1:0.00}개 / 평균 효과 {2} / 최대 집중 {3}개",
                    GetStatName(statType),
                    value.TotalStars / samples,
                    FormatEffect(statType, value.EffectSum / samples),
                    value.MaxConcentration));
        }
    }

    private static void AccumulateRoll(
        ItemData item,
        Dictionary<WeaponGradeStatType, StatAggregate> target,
        ref AggregateSummary summary)
    {
        if (item == null || item.weaponGradeStats == null)
            return;

        for (int rollIndex = 0; rollIndex < item.weaponGradeStats.Count; rollIndex++)
        {
            WeaponGradeStatRoll roll = item.weaponGradeStats[rollIndex];
            if (roll == null || !target.TryGetValue(roll.statType, out StatAggregate aggregate))
                continue;

            aggregate.TotalStars += roll.TotalStarCount;
            aggregate.EffectSum += roll.positiveTotalValue - roll.negativeTotalValue;
            aggregate.MaxConcentration = Mathf.Max(aggregate.MaxConcentration, roll.TotalStarCount);
            List<WeaponGradeStarRoll> stars = roll.GetDisplayStars();
            for (int starIndex = 0; starIndex < stars.Count; starIndex++)
            {
                WeaponGradeStarType type = stars[starIndex].starType;
                switch (type)
                {
                    case WeaponGradeStarType.Green: summary.GreenStarCount++; summary.PositiveStarCount++; break;
                    case WeaponGradeStarType.Yellow: summary.YellowStarCount++; summary.PositiveStarCount++; break;
                    case WeaponGradeStarType.Red: summary.RedStarCount++; break;
                    default: summary.WhiteStarCount++; summary.PositiveStarCount++; break;
                }
            }
        }
    }

    private static void AccumulateProfile(ItemData item, ref AggregateSummary summary)
    {
        if (item == null)
            return;

        switch (item.meleeStarDistributionProfile)
        {
            case MeleeStarDistributionProfile.Focused: summary.FocusedProfileCount++; break;
            case MeleeStarDistributionProfile.DualCore: summary.DualCoreProfileCount++; break;
            case MeleeStarDistributionProfile.Spread: summary.SpreadProfileCount++; break;
        }
    }

    private static string BuildStarText(WeaponGradeStatRoll roll)
    {
        if (roll == null || !roll.HasStars)
            return "<color=#888888>별 없음</color>";

        List<WeaponGradeStarRoll> stars = roll.GetDisplayStars();
        System.Text.StringBuilder builder = new System.Text.StringBuilder(stars.Count * 24);
        for (int i = 0; i < stars.Count; i++)
        {
            string color;
            switch (stars[i].starType)
            {
                case WeaponGradeStarType.Green: color = "#59FF59"; break;
                case WeaponGradeStarType.Yellow: color = "#FFD84A"; break;
                case WeaponGradeStarType.Red: color = "#FF4A4A"; break;
                default: color = "#F2F2F2"; break;
            }

            builder.Append("<color=").Append(color).Append(">★</color>");
        }

        return builder.ToString();
    }

    private static string GetStatName(WeaponGradeStatType statType)
    {
        switch (statType)
        {
            case WeaponGradeStatType.Damage: return "데미지";
            case WeaponGradeStatType.AttackSpeed: return "공격 속도";
            case WeaponGradeStatType.AttackRange: return "공격 범위";
            case WeaponGradeStatType.CritChance: return "치명타 확률";
            case WeaponGradeStatType.CritDamage: return "치명타 피해";
            default: return statType.ToString();
        }
    }

    private static string GetProfileName(MeleeStarDistributionProfile profile)
    {
        switch (profile)
        {
            case MeleeStarDistributionProfile.Focused: return "집중형";
            case MeleeStarDistributionProfile.DualCore: return "쌍축형";
            case MeleeStarDistributionProfile.Spread: return "분산형";
            default: return "없음 (별 0개)";
        }
    }

    private static string BuildGuaranteedStarSummary(ItemGrade itemGrade)
    {
        IReadOnlyList<WeaponGradeStatType> statTypes = WeaponGradeStatRoller.GetPlannedMeleeRollableStats();
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < statTypes.Count; i++)
        {
            int count = WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(itemGrade, statTypes[i]);
            if (count <= 0)
                continue;

            if (builder.Length > 0)
                builder.Append(" / ");
            builder.Append(GetStatName(statTypes[i])).Append(' ').Append(count).Append("개");
        }

        return builder.Length > 0 ? builder.ToString() : "없음";
    }

    private static string FormatEffect(WeaponGradeStatType statType, float value)
    {
        float displayed = value * 100f;
        if (statType == WeaponGradeStatType.CritChance)
            displayed = value;

        string suffix = statType == WeaponGradeStatType.CritChance || statType == WeaponGradeStatType.CritDamage
            ? "%p"
            : "%";
        return displayed.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + suffix;
    }

    private sealed class StatAggregate
    {
        public float TotalStars;
        public float EffectSum;
        public int MaxConcentration;
    }

    private sealed class AggregateSummary
    {
        public int SampleCount;
        public int ValidDpsCount;
        public int PositiveStarCount;
        public int WhiteStarCount;
        public int GreenStarCount;
        public int YellowStarCount;
        public int RedStarCount;
        public int FocusedProfileCount;
        public int DualCoreProfileCount;
        public int SpreadProfileCount;
        public float DpsSum;
        public float MinDps;
        public float MaxDps;
    }
}
