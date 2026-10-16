using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class MonsterMediumStrongCooldownBuilder
{
    internal static string Output(string relative)
    {
        string workspace = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string path = Path.GetFullPath(Path.Combine(workspace, relative));
        string allowed = Path.GetFullPath(Path.Combine(workspace, "개인파일/코덱스산출/Monsters")) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Private monster output required.");
        return path;
    }
    internal static bool Medium(EnemyDefinition d) => d.Grade.GradeType != EnemyGradeType.Boss
        && d.Grade.GradeType != EnemyGradeType.Elite && d.Grade.GradeType != EnemyGradeType.GreaterElite
        && d.MovementProfile.HitWeightProfile.Weight == EnemyHitWeight.Standard;
    internal static EnemyDefinition[] Roster()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
        return Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToArray();
    }
    internal static JObject Fields(EnemyAbilityDefinition a) => JObject.Parse(EditorJsonUtility.ToJson(a));
    static string Hash(string path) => MonsterBlenderParryR6Builder.Hash(Path.GetFullPath(Path.Combine(Application.dataPath, "..", path)));
    public static string Apply(string relative)
    {
        MonsterBlenderParryR6Builder.RequireIdle();
        string output = Output(relative), receipt = Path.Combine(output, "apply-result.json");
        if (File.Exists(receipt)) throw new InvalidOperationException("Inspect the existing cooldown receipt.");
        var plan = JObject.Parse(File.ReadAllText(Path.Combine(output, "plan.json")));
        var rows = plan["rows"].ToArray(); var roster = Roster();
        var live = roster.Where(Medium).SelectMany(d => Enumerable.Range(0, d.AbilitySet.Count)
            .Select(i => d.AbilitySet.GetAbility(i)).Where(a => a.IsTelegraphedStrongAttack)
            .Select(a => new { d.EnemyId, ability = a })).ToDictionary(r => r.EnemyId);
        if (rows.Length != 21 || live.Count != 21 || rows.Select(r => (string)r["id"]).Distinct().Count() != 21)
            throw new InvalidOperationException("21 distinct saved medium strong attacks required.");
        foreach (var row in rows)
        {
            var a = live[(string)row["id"]].ability; string path = (string)row["path"];
            if (AssetDatabase.GetAssetPath(a) != path || Hash(path) != (string)row["sha256"] || EditorUtility.IsDirty(a)
                || (float)row["cooldown"] < 3f || (float)row["cooldown"] > 6f)
                throw new InvalidOperationException("Cooldown baseline changed: " + row["id"]);
        }
        var edits = new List<MonsterStrongParryTimingBuilder.WarningEdit>(); var saved = new JArray();
        try
        {
            foreach (var row in rows)
            {
                var a = live[(string)row["id"]].ability; string path = (string)row["path"];
                if (Hash(path) != (string)row["sha256"] || EditorUtility.IsDirty(a)) throw new InvalidOperationException("Attack changed before save.");
                var edit = new MonsterStrongParryTimingBuilder.WarningEdit { ability = a, path = path, before = Fields(a),
                    diskHash = Hash(path), metaHash = Hash(path + ".meta") };
                edit.written = edit.before; edits.Add(edit);
                Undo.RecordObject(a, "중형 강공의 종별 고정 쿨타임 적용");
                var so = new SerializedObject(a); so.FindProperty("cooldown").floatValue = (float)row["cooldown"]; so.ApplyModifiedPropertiesWithoutUndo();
                edit.written = Fields(a); EditorUtility.SetDirty(a); AssetDatabase.SaveAssetIfDirty(a);
                edit.diskHash = Hash(path); edit.written = Fields(a);
                var expected = (JObject)edit.before.DeepClone(); ((JObject)(expected["MonoBehaviour"] ?? expected))["cooldown"] = (float)row["cooldown"];
                if (!JToken.DeepEquals(expected, edit.written) || Hash(path + ".meta") != edit.metaHash)
                    throw new InvalidOperationException("Non-cooldown field changed: " + row["id"]);
                saved.Add(new JObject { ["id"] = row["id"], ["path"] = path, ["cooldown"] = a.Cooldown, ["sha256"] = edit.diskHash });
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "rollback-result.json"), new JObject { ["error"] = error.ToString(),
                ["records"] = MonsterStrongParryTimingBuilder.RestoreWarningEdits(edits) }.ToString()); throw;
        }
        File.WriteAllText(receipt, new JObject { ["status"] = "APPLIED_MEDIUM_STRONG_FIXED_COOLDOWNS", ["records"] = saved }.ToString());
        return receipt;
    }
}
