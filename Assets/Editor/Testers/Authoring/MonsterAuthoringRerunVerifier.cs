using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 2026-10-01: 몬스터 제작 도구 재실행 보존 검증(쓰기 없음).
// 실제 빌더가 쓰는 함수(MonsterThemeAuthoringPolicy, CombatImpactFeelBuilder.ConfigureActor)를
// 제품 자산의 메모리 사본(Object.Instantiate, 저장하지 않는 LoadPrefabContents)에 실행해 보존·반복·신규·편집값·재정렬을 확인한다.
// 폐기 콘텐츠 도구는 쓰기 전에 거부하는지 확인한다.
public static class MonsterAuthoringRerunVerifier
{
    private const string Root = MonsterThemeCombatBuilder.Root;

    [MenuItem("OVERBURST/Codex/Validate/Monster Authoring Rerun (In-Memory)")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required");
        var log = new List<string>();
        int failures = 0;
        void Check(bool ok, string what) { if (!ok) failures++; log.Add((ok ? "PASS " : "FAIL ") + what); }

        var clones = new List<Object>();
        try
        {
            VerifyTables(Check, clones);
            VerifyCatalog(Check, clones);
            VerifyPresetRoster(Check, clones);
            VerifyDeathPresentation(Check);
            VerifyRetiredTools(Check);
        }
        finally
        {
            foreach (var clone in clones) if (clone != null) Object.DestroyImmediate(clone);
        }
        log.Insert(0, "[MonsterAuthoringRerunVerifier] " + (failures == 0 ? "PASS" : "FAIL failures=" + failures));
        return string.Join("\n", log);
    }

    private static T Clone<T>(T source, List<Object> clones) where T : Object
    {
        var copy = Object.Instantiate(source);
        copy.hideFlags = HideFlags.DontSave;
        clones.Add(copy);
        return copy;
    }

    private static void VerifyTables(Action<bool, string> check, List<Object> clones)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyThemeTable", new[] { Root + "/Tables" }))
        {
            var product = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(AssetDatabase.GUIDToAssetPath(guid));
            var table = Clone(product, clones);
            var before = table.Entries.ToArray();
            // 빌더와 같은 입력: 같은 정의를 기본 가중치 1로 다시 제안한다. 순서를 뒤집어 재정렬도 함께 본다.
            var generated = before.Reverse().Select(e => new EnemyThemeTable.Entry { definition = e.definition, tier = e.tier, weight = 1f }).ToArray();
            var touched = new List<Object>();
            bool changed = MonsterThemeAuthoringPolicy.MergeTable(table, false, product.ThemeId, "changed label", product.Catalog, Color.magenta, generated, touched);
            check(!changed && touched.Count == 0 && Same(before, table.Entries.ToArray()) && table.DisplayName == product.DisplayName,
                "table " + product.ThemeId + ": rerun keeps weights/tiers/order/label (" + before.Length + " entries)");
            changed = MonsterThemeAuthoringPolicy.MergeTable(table, false, product.ThemeId, "changed label", product.Catalog, Color.magenta, generated, touched);
            check(!changed, "table " + product.ThemeId + ": second run no change");

            // 편집값 왕복: 합법적인 가중치로 바꾼 뒤 다시 실행해도 유지된다.
            var edited = table.Entries.ToArray();
            edited[0].weight = edited[0].weight + 1.5f;
            table.Configure(table.ThemeId, table.DisplayName, table.Catalog, table.Accent, edited);
            MonsterThemeAuthoringPolicy.MergeTable(table, false, product.ThemeId, "x", product.Catalog, Color.magenta, generated, touched);
            check(Mathf.Approximately(table.Entries[0].weight, edited[0].weight), "table " + product.ThemeId + ": edited weight survives rerun");
        }

        // 신규 항목·삭제된 항목: 새 정의는 기본 가중치로 뒤에 붙고, 참조가 끊긴 항목은 되살리지 않는다.
        var cavern = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root + "/Tables/CavernMutants.asset");
        var copy = Clone(cavern, clones);
        var fresh = Clone(cavern.Entries[0].definition, clones);
        var so = new SerializedObject(fresh); so.FindProperty("enemyId").stringValue = "CavernMutants_VerifierOnly"; so.ApplyModifiedPropertiesWithoutUndo();
        var withNull = copy.Entries.Concat(new[] { new EnemyThemeTable.Entry { definition = null, tier = EnemyThemeTier.Small, weight = 9f } }).ToArray();
        copy.Configure(copy.ThemeId, copy.DisplayName, copy.Catalog, copy.Accent, withNull);
        var original = cavern.Entries.ToArray();
        var list = new List<Object>();
        bool added = MonsterThemeAuthoringPolicy.MergeTable(copy, false, copy.ThemeId, copy.DisplayName, copy.Catalog, copy.Accent,
            original.Select(e => new EnemyThemeTable.Entry { definition = e.definition, tier = e.tier, weight = 1f })
                .Concat(new[] { new EnemyThemeTable.Entry { definition = fresh, tier = EnemyThemeTier.Small, weight = 5f } }), list);
        var after = copy.Entries.ToArray();
        check(added && after.Length == original.Length + 1 && Same(original, after.Take(original.Length).ToArray())
            && after[after.Length - 1].definition == fresh && Mathf.Approximately(after[after.Length - 1].weight, 5f)
            && after.All(e => e.definition != null),
            "table new entry appended with default weight, existing kept, dangling entry dropped");
    }

    private static bool Same(EnemyThemeTable.Entry[] a, EnemyThemeTable.Entry[] b) =>
        a.Length == b.Length && a.Zip(b, (x, y) => x.definition == y.definition && x.tier == y.tier && x.weight == y.weight).All(v => v);

    private static void VerifyCatalog(Action<bool, string> check, List<Object> clones)
    {
        var product = AssetDatabase.LoadAssetAtPath<EnemyCatalog>(Root + "/Catalog.asset");
        var catalog = Clone(product, clones);
        var before = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToArray();
        var touched = new List<Object>();
        bool changed = MonsterThemeAuthoringPolicy.MergeCatalog(catalog, before.Reverse(), touched);
        var after = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToArray();
        check(!changed && touched.Count == 0 && before.SequenceEqual(after), "catalog rerun keeps order and entries (" + before.Length + ")");
    }

    private static void VerifyPresetRoster(Action<bool, string> check, List<Object> clones)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyAiPreset", new[] { Root + "/Presets" }))
        {
            var product = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(AssetDatabase.GUIDToAssetPath(guid));
            var preset = Clone(product, clones);
            var before = Enumerable.Range(0, preset.DefaultMonsterCount).Select(preset.GetDefaultMonsterPrefab).ToArray();
            var touched = new List<Object>();
            bool changed = MonsterThemeAuthoringPolicy.AppendPresetRoster(preset, Array.Empty<GameObject>(), touched);
            var after = Enumerable.Range(0, preset.DefaultMonsterCount).Select(preset.GetDefaultMonsterPrefab).ToArray();
            check(!changed && before.SequenceEqual(after) && preset.PresetId == product.PresetId,
                "preset " + product.name + ": no created actors -> roster and squad settings unchanged (" + before.Length + ")");
            if (before.Length > 1)
            {
                // 의도적으로 뺀 멤버는 새로 만든 액터 목록에 없으면 다시 붙지 않는다.
                preset.ConfigureIdentity(preset.PresetId, preset.DisplayName, before.Skip(1).ToArray());
                MonsterThemeAuthoringPolicy.AppendPresetRoster(preset, Array.Empty<GameObject>(), touched);
                check(preset.DefaultMonsterCount == before.Length - 1, "preset " + product.name + ": removed member stays removed");
            }
        }
    }

    private static void VerifyDeathPresentation(Action<bool, string> check)
    {
        int preserved = 0, total = 0;
        EnemyDefinition sample = null;
        foreach (string guid in AssetDatabase.FindAssets("t:EnemyDefinition", new[] { Root + "/Definitions" }))
        {
            var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (definition == null || definition.ActorPrefab == null) continue;
            total++;
            string path = AssetDatabase.GetAssetPath(definition.ActorPrefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var presentation = root.GetComponent<EnemyDeathPresentation>();
                string before = Snapshot(presentation);
                bool configured = CombatImpactFeelBuilder.ConfigureActor(root.GetComponent<EnemyActor>(), definition);
                if (!configured && before == Snapshot(root.GetComponent<EnemyDeathPresentation>())) preserved++;
                if (sample == null && definition.EnemyId.StartsWith("CavernMutants_", StringComparison.Ordinal)) sample = definition;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        check(preserved == total && total > 0, "death presentation rerun preserved " + preserved + "/" + total + " actors");

        string samplePath = AssetDatabase.GetAssetPath(sample.ActorPrefab);
        var edit = PrefabUtility.LoadPrefabContents(samplePath);
        try
        {
            var presentation = edit.GetComponent<EnemyDeathPresentation>();
            var so = new SerializedObject(presentation);
            so.FindProperty("landingNormalizedTime").floatValue = .71f;
            so.FindProperty("strongDisplacement").floatValue = .5f;
            so.ApplyModifiedPropertiesWithoutUndo();
            bool configured = CombatImpactFeelBuilder.ConfigureActor(edit.GetComponent<EnemyActor>(), sample);
            var reread = new SerializedObject(edit.GetComponent<EnemyDeathPresentation>());
            check(!configured && Mathf.Approximately(reread.FindProperty("landingNormalizedTime").floatValue, .71f)
                && Mathf.Approximately(reread.FindProperty("strongDisplacement").floatValue, .5f),
                "death presentation edited values survive rerun (" + sample.EnemyId + ")");

            Object.DestroyImmediate(presentation);
            var feel = edit.transform.Find("Death Feel");
            if (feel != null) Object.DestroyImmediate(feel.gameObject);
            configured = CombatImpactFeelBuilder.ConfigureActor(edit.GetComponent<EnemyActor>(), sample);
            var created = edit.GetComponent<EnemyDeathPresentation>();
            var createdSo = created != null ? new SerializedObject(created) : null;
            check(configured && created != null && createdSo.FindProperty("visualRoot").objectReferenceValue != null
                && createdSo.FindProperty("feedback").objectReferenceValue != null,
                "death presentation is created with defaults when missing (new actor path)");
        }
        finally { PrefabUtility.UnloadPrefabContents(edit); }
    }

    private static string Snapshot(Object target)
    {
        if (target == null) return "null";
        var so = new SerializedObject(target);
        var it = so.GetIterator();
        var parts = new List<string>();
        bool enter = true;
        while (it.Next(enter))
        {
            enter = true;
            if (it.propertyType == SerializedPropertyType.Float) parts.Add(it.propertyPath + "=" + it.floatValue.ToString("R"));
            else if (it.propertyType == SerializedPropertyType.ObjectReference) parts.Add(it.propertyPath + "=" + (it.objectReferenceValue != null ? it.objectReferenceValue.GetInstanceID() : 0));
            else if (it.propertyType == SerializedPropertyType.Enum) parts.Add(it.propertyPath + "=" + it.enumValueIndex);
        }
        return string.Join(";", parts);
    }

    private static void VerifyRetiredTools(Action<bool, string> check)
    {
        void Refuses(string name, Action run)
        {
            bool refused = false;
            try { run(); }
            catch (InvalidOperationException ex) { refused = ex.Message.Contains("retired in 188dcdb"); }
            check(refused, name + " refuses before writing");
        }
        Refuses("Build Protofactor Enemy Pilot", ProtofactorEnemyPilotBuilder.RunOnceFromCommandLine);
        Refuses("Formalize State Pattern AI Prefabs", EnemyStatePatternPrefabFormalizer.RunOnceFromCommandLine);
        Refuses("Formalize Elemental Status Assembly", EnemyStatePatternPrefabFormalizer.FormalizeElementalStatusAssemblyFromCommandLine);
        Refuses("Configure Murloc Spawn Packs", MurlocSpawnPackConfigBuilder.RunOnceFromCommandLine);
    }
}
