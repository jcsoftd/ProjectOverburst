using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FlaskBalanceValidation
{
    [Serializable] public sealed class GradeRow
    {
        public string grade;
        public int samples;
        public float meanPrimaryMultiplier, meanDuration, meanCooldown, maximumLongRunUptime, maximumRegenPerUse;
    }
    [Serializable] public sealed class Report
    {
        public string result = "PASS";
        public string scope = "실제 물약 생성·계산 코드, 7등급 각 1000 표본, 재사용 대기시간·저장 값 보존";
        public List<GradeRow> grades = new List<GradeRow>();
        public string notes = "회복 2종과 과충전은 기본 재사용 30초, 나머지는 24초. 플레이 난이도 최종 확정은 별도.";
    }

    [MenuItem("JC Tool/Items/Validate Flask Balance")]
    public static void RunMenu() { Debug.Log(Run()); }

    public static string Run()
    {
        var report = new Report();
        FlaskItemData offensive = ScriptableObject.CreateInstance<FlaskItemData>();
        FlaskItemData regeneration = ScriptableObject.CreateInstance<FlaskItemData>();
        try
        {
            offensive.Configure(FlaskKind.Berserker);
            regeneration.Configure(FlaskKind.Regeneration);
            for (int g = 0; g <= (int)ItemGrade.Mythic; g++)
            {
                var row = new GradeRow { grade = ((ItemGrade)g).ToString(), samples = 1000 };
                for (int n = 0; n < row.samples; n++)
                {
                    FlaskInstanceState state = FlaskGradeRoller.Roll((ItemGrade)g, g * 100000 + n);
                    Require(FlaskGradeRoller.IsValid(state, (ItemGrade)g), "별 총량·보장·색상·상한");
                    string saved = JsonUtility.ToJson(state);
                    FlaskInstanceState restored = JsonUtility.FromJson<FlaskInstanceState>(saved);
                    Require(saved == JsonUtility.ToJson(restored), "별 저장 보존");
                    FlaskStats stats = FlaskStats.Calculate(offensive, state);
                    FlaskStats heal = FlaskStats.Calculate(regeneration, state);
                    float uptime = stats.duration / stats.cooldown;
                    Require(stats.cooldown >= 18.24f - .001f && stats.duration <= 9.601f && uptime < 1f, "무한 유지 제한");
                    row.meanPrimaryMultiplier += stats.primary / offensive.primaryValue;
                    row.meanDuration += stats.duration; row.meanCooldown += stats.cooldown;
                    row.maximumLongRunUptime = Mathf.Max(row.maximumLongRunUptime, uptime);
                    row.maximumRegenPerUse = Mathf.Max(row.maximumRegenPerUse, heal.primary * heal.duration);
                }
                row.meanPrimaryMultiplier /= row.samples; row.meanDuration /= row.samples; row.meanCooldown /= row.samples;
                report.grades.Add(row);
            }
            FlaskInstanceState common = FlaskGradeRoller.Roll(ItemGrade.Common, 7);
            FlaskStats commonStats = FlaskStats.Calculate(offensive, common);
            common.cooldownRemaining = commonStats.cooldown;
            FlaskInstanceState loaded = JsonUtility.FromJson<FlaskInstanceState>(JsonUtility.ToJson(common));
            Require(Mathf.Approximately(loaded.cooldownRemaining, commonStats.cooldown), "재사용 대기 저장 보존");
            string json = JsonUtility.ToJson(report, true);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,
                "../../개인파일/코덱스산출/Flasks/20260923_CooldownMigration/balance-report.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, json);
            return json;
        }
        finally { UnityEngine.Object.DestroyImmediate(offensive); UnityEngine.Object.DestroyImmediate(regeneration); }
    }

    private static void Require(bool condition, string label)
    { if (!condition) throw new InvalidOperationException("Flask FAIL: " + label); }
}
