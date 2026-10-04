using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// 2026-10-01: 테마 몬스터 제작 도구의 재실행 규칙.
// 이미 있는 액터(정의 + 액터 프리팹 + 프로필)는 현재 자산이 조정값의 원본이므로 다시 만들지 않는다.
// 빌더 상수는 새로 만드는 액터·테이블 항목의 기본값으로만 쓴다. 없어진 정의는 되살리지 않는다.
// 저장은 이번 실행이 바꾼 자산만 한다. 다른 작업이 편집 중인 자산을 AssetDatabase.SaveAssets로 함께 저장하지 않는다.
public static class MonsterThemeAuthoringPolicy
{
    private static string Root => MonsterThemeCombatBuilder.Root;

    public static string DefinitionPath(string enemyId) => Root + "/Definitions/" + enemyId + ".asset";

    // 완성된 기존 액터를 돌려준다. 없으면 null. 일부만 남은 액터는 임의로 합치지 않고 멈춘다.
    public static EnemyDefinition FindExisting(string enemyId)
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(DefinitionPath(enemyId));
        if (definition == null)
        {
            RequireNoLeftovers(enemyId);
            return null;
        }
        if (definition.ActorPrefab == null || definition.AbilitySet == null || definition.AnimationProfile == null
            || definition.MovementProfile == null || definition.BehaviorProfile == null)
            throw new InvalidOperationException("Existing actor is incomplete: " + enemyId
                + ". It is not merged or regenerated automatically; repair it or remove all of its assets first.");
        return definition;
    }

    // 정의가 없는데 같은 ID의 프로필·프리팹이 남아 있으면, 조정된 잔여 자산을 기본값으로 덮지 않도록 멈춘다.
    private static void RequireNoLeftovers(string enemyId)
    {
        string[] paths =
        {
            Root + "/Actors/PF_" + enemyId + ".prefab",
            Root + "/Species/" + enemyId + ".asset",
            Root + "/Movement/" + enemyId + ".asset",
            Root + "/Behavior/" + enemyId + ".asset",
            Root + "/Animations/" + enemyId + ".asset",
            Root + "/Abilities/" + enemyId + "_Set.asset"
        };
        string[] found = paths.Where(p => AssetDatabase.LoadMainAssetAtPath(p) != null).ToArray();
        if (found.Length > 0)
            throw new InvalidOperationException("Definition is missing but authored assets remain for " + enemyId
                + ": " + string.Join(", ", found) + ". Restore the definition or remove these assets before creating it.");
    }

    // 새 액터를 만들 때만 템플릿이 필요하다. 기존 액터만 다시 확인하는 실행은 템플릿 없이 끝난다.
    public static void RequireTemplate(GameObject template, EnemyDefinition source, string enemyId)
    {
        if (template == null || source == null)
            throw new InvalidOperationException(NoTemplateMessage(new[] { enemyId }));
    }

    // 2026-10-01: 없는 액터가 하나라도 있으면 폴더·등급·재질·프리셋을 만들기 전에 템플릿을 확인한다.
    // 템플릿이 없으면 아무것도 쓰지 않고 멈춘다. 기존 액터만 있으면 그대로 통과하고 없는 액터 ID 목록(빈 목록)을 돌려준다.
    public static List<string> RequireTemplateBeforeWrites(IEnumerable<string> enemyIds, GameObject template, EnemyDefinition source)
    {
        var missing = enemyIds.Where(id => FindExisting(id) == null).ToList();
        if (missing.Count > 0 && (template == null || source == null))
            throw new InvalidOperationException(NoTemplateMessage(missing));
        return missing;
    }

    private static string NoTemplateMessage(IEnumerable<string> enemyIds) =>
        "Creating " + string.Join(", ", enemyIds) + " needs a new actor template (MonsterThemeTemplate), which has not been chosen yet. "
        + "The old Protofactor template was retired in 188dcdb. Nothing was written; existing actors were left unchanged.";

    public static T LoadOrCreate<T>(string relative, out bool created) where T : ScriptableObject
    {
        string path = Root + "/" + relative + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        created = asset == null;
        if (created)
        {
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
        }
        return asset;
    }

    // 등급 프로필은 처음 만들 때만 기본 배율을 쓴다. 이후 배율은 밸런스 조정값이다.
    public static EnemyGradeProfile Grade(string relative, string id, string label, EnemyGradeType type, ICollection<Object> touched)
    {
        var grade = LoadOrCreate<EnemyGradeProfile>(relative, out bool created);
        if (created)
        {
            grade.Configure(id, label, type, 1, 1, 1, 1);
            EditorUtility.SetDirty(grade);
            touched.Add(grade);
        }
        return grade;
    }

    // 신호 재질도 처음 만들 때만 테마 색을 칠한다.
    public static Material SignalMaterial(string relative, Color color, ICollection<Object> touched)
    {
        string path = Root + "/" + relative + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        AssetDatabase.CreateAsset(material, path);
        material.SetColor("_BaseColor", color);
        EditorUtility.SetDirty(material);
        touched.Add(material);
        return material;
    }

    // AI 프리셋은 없을 때만 원본 프리셋을 복제한다. 있는 프리셋의 부대 설정은 그대로 둔다.
    public static EnemyAiPreset Preset(string relative, EnemyDefinition source, string presetId, string label, ICollection<Object> touched)
    {
        string path = Root + "/" + relative + ".asset";
        var preset = AssetDatabase.LoadAssetAtPath<EnemyAiPreset>(path);
        if (preset != null) return preset;
        if (source == null || source.AiPreset == null)
            throw new InvalidOperationException("Creating AI preset " + relative + " needs a new actor template (MonsterThemeTemplate). Nothing was written.");
        preset = ScriptableObject.CreateInstance<EnemyAiPreset>();
        AssetDatabase.CreateAsset(preset, path);
        EditorUtility.CopySerialized(source.AiPreset, preset);
        preset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        preset.ConfigureIdentity(presetId, label, Array.Empty<GameObject>());
        EditorUtility.SetDirty(preset);
        touched.Add(preset);
        return preset;
    }

    // 카탈로그: 기존 순서와 항목을 유지하고 새 정의만 뒤에 붙인다. 참조가 끊긴 항목은 뺀다.
    public static bool MergeCatalog(EnemyCatalog catalog, IEnumerable<EnemyDefinition> definitions, ICollection<Object> touched)
    {
        if (catalog.IsApprovedRosterLocked) return false;
        var current = Enumerable.Range(0, catalog.Count).Select(catalog.GetDefinition).ToList();
        var merged = current.Where(d => d != null).ToList();
        foreach (var definition in definitions)
            if (definition != null && !merged.Any(d => d.EnemyId == definition.EnemyId))
                merged.Add(definition);
        if (merged.SequenceEqual(current)) return false;
        catalog.Configure(merged.ToArray());
        EditorUtility.SetDirty(catalog);
        touched.Add(catalog);
        return true;
    }

    // 테마 테이블: 기존 항목의 등급·가중치·순서와 테마 이름·색을 유지하고 새 정의만 기본 가중치로 붙인다.
    // 이 빌더가 만들지 않은 항목(다른 빌더가 추가한 종)도 지우지 않는다.
    public static bool MergeTable(EnemyThemeTable table, bool created, string id, string label, EnemyCatalog catalog,
        Color accent, IEnumerable<EnemyThemeTable.Entry> generated, ICollection<Object> touched)
    {
        if (table.IsApprovedRosterLocked) return false;
        var current = table.Entries.ToList();
        var merged = current.Where(e => e.definition != null).ToList();
        foreach (var entry in generated)
            if (entry.definition != null && !merged.Any(e => e.definition.EnemyId == entry.definition.EnemyId))
                merged.Add(entry);
        string themeId = created || string.IsNullOrEmpty(table.ThemeId) ? id : table.ThemeId;
        string displayName = created || string.IsNullOrEmpty(table.DisplayName) ? label : table.DisplayName;
        Color color = created ? accent : table.Accent;
        EnemyCatalog source = table.Catalog != null ? table.Catalog : catalog;
        bool same = !created && themeId == table.ThemeId && displayName == table.DisplayName && source == table.Catalog
            && merged.Count == current.Count
            && merged.Zip(current, (a, b) => a.definition == b.definition && a.tier == b.tier && a.weight == b.weight).All(x => x);
        if (same) return false;
        table.Configure(themeId, displayName, source, color, merged.ToArray());
        EditorUtility.SetDirty(table);
        touched.Add(table);
        return true;
    }

    // 프리셋 로스터: 기존 로스터를 유지하고 새 비정예 액터만 붙인다.
    public static bool AppendPresetRoster(EnemyAiPreset preset, IEnumerable<GameObject> newMembers, ICollection<Object> touched)
    {
        var approvedTable = AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(Root + "/Tables/"
            + System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(preset)) + ".asset");
        if (approvedTable != null && approvedTable.IsApprovedRosterLocked) return false;
        var roster = Enumerable.Range(0, preset.DefaultMonsterCount).Select(preset.GetDefaultMonsterPrefab).ToList();
        int before = roster.Count;
        foreach (var member in newMembers)
            if (member != null && !roster.Contains(member)) roster.Add(member);
        if (roster.Count == before) return false;
        preset.ConfigureIdentity(preset.PresetId, preset.DisplayName, roster.ToArray());
        EditorUtility.SetDirty(preset);
        touched.Add(preset);
        return true;
    }

    public static void Save(IEnumerable<Object> touched)
    {
        foreach (var asset in touched.Where(a => a != null).Distinct())
            AssetDatabase.SaveAssetIfDirty(asset);
    }
}
