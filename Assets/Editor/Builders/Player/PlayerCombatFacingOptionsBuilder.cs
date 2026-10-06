using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

/// <summary>두 방향 표시 메시와 환경설정 모양/밝기 컨트롤을 기존 프리팹에 연결한다.</summary>
public static class PlayerCombatFacingOptionsBuilder
{
    public static readonly string[] Nodes = { "RingToTip_-1", "RingToTip_1", "InnerFeather_-1", "InnerFeather_1", "FilledForwardCap", "QuietNoseCore" };
    static readonly string[] Originals = { "02_Thin45_RingToTip_-1", "02_Thin45_RingToTip_1", "02_Thin45_InnerFeather_-1", "02_Thin45_InnerFeather_1", "02_FullFilledTipSoft_FilledTip", "02_FullFilledTipSoft_NoseCore" };

    [MenuItem("OVERBURST/플레이어/전투 방향 표시/환경설정 모양·밝기 연결")]
    static void ApplyMenu() => Apply(Path.GetFullPath("../개인파일/코덱스산출/VFX/CombatFacingOptions/EditorActions"));

    public static void Apply(string output)
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        if (IsolatedSavePlayGuard.RequiresAccountChoice || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
            throw new InvalidOperationException("실제 계정의 유휴 Editor에서만 적용합니다.");
        Directory.CreateDirectory(output);
        string before = SceneState();
        foreach (var path in new[] { PlayerCombatFacingVfxBuilder.PlayerPath, OverburstGameMenuBuilder.PrefabPath })
        {
            if (EditorUtility.IsDirty(AssetDatabase.LoadAssetAtPath<GameObject>(path))) throw new InvalidOperationException("프리팹이 dirty입니다: " + path);
            string backup = Path.Combine(output, Path.GetFileName(path) + ".before");
            if (!File.Exists(backup)) File.Copy(path, backup);
        }
        ValidateMenuInput();
        GameObject player = null;
        try
        {
            player = PrefabUtility.LoadPrefabContents(PlayerCombatFacingVfxBuilder.PlayerPath);
            var effect = player.GetComponent<PlayerCombatFacingVfx>();
            if (effect == null || effect.VisualRoot == null) throw new Exception("기존 방향 표시 연결이 없습니다.");
            var fields = new SerializedObject(effect);
            foreach (bool extended in new[] { false, true })
            {
                var property = fields.FindProperty(extended ? "extendedMeshes" : "quietMeshes");
                property.arraySize = Nodes.Length;
                for (int i = 0; i < Nodes.Length; i++)
                {
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(extended, i));
                    if (mesh == null) throw new Exception("선택 가능한 메시가 없습니다: " + MeshPath(extended, i));
                    property.GetArrayElementAtIndex(i).objectReferenceValue = mesh;
                }
            }
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(player, PlayerCombatFacingVfxBuilder.PlayerPath);
        }
        finally { if (player != null) PrefabUtility.UnloadPrefabContents(player); }
        OverburstGameMenuBuilder.AddCombatFacingSetting();
        Validate(output);
        if (before != SceneState()) throw new Exception("열린 사용자 씬 상태가 변경됐습니다.");
    }

    static void ValidateMenuInput()
    {
        var menu = PrefabUtility.LoadPrefabContents(OverburstGameMenuBuilder.PrefabPath);
        try
        {
            var panel = menu.GetComponentInChildren<OverburstSettingsPanel>(true);
            if (panel == null || panel.pages == null || panel.pages.Length != 4
                || panel.pages.Any(page => page == null)
                || panel.cameraShake == null || panel.hitEffect == null)
                throw new InvalidOperationException("Settings panel contract changed.");
        }
        finally { PrefabUtility.UnloadPrefabContents(menu); }
    }

    public static string MeshPath(bool extended, int i) => extended
        ? CombatFacingContourPointBuilder.NewRoot + "/Meshes/Mesh_ContourPoint_" + Nodes[i] + ".asset"
        : CombatFacingContourPointBuilder.Root + "/Meshes/" + Originals[i] + ".asset";

    public static void Validate(string output)
    {
        GameObject player = null, menu = null;
        try
        {
            player = PrefabUtility.LoadPrefabContents(PlayerCombatFacingVfxBuilder.PlayerPath);
            var effect = player.GetComponent<PlayerCombatFacingVfx>();
            var fields = new SerializedObject(effect);
            foreach (bool extended in new[] { false, true })
            {
                var meshes = fields.FindProperty(extended ? "extendedMeshes" : "quietMeshes");
                if (meshes.arraySize != Nodes.Length) throw new Exception("형태 메시 개수 불일치");
                for (int i = 0; i < Nodes.Length; i++)
                    if (AssetDatabase.GetAssetPath(meshes.GetArrayElementAtIndex(i).objectReferenceValue) != MeshPath(extended, i)) throw new Exception("형태 메시 참조 불일치");
            }
            menu = PrefabUtility.LoadPrefabContents(OverburstGameMenuBuilder.PrefabPath);
            var panel = menu.GetComponentInChildren<OverburstSettingsPanel>(true);
            if (panel.combatFacingStyle == null || panel.combatFacingBrightness == null
                || !panel.combatFacingStyle.options.SequenceEqual(new[] { "기존 절제형", "끝 연장형" })
                || panel.combatFacingBrightness.minValue != 0 || panel.combatFacingBrightness.maxValue != 2
                || !Mathf.Approximately(panel.combatFacingBrightness.value, 1f)
                || !panel.combatFacingStyle.transform.IsChildOf(panel.pages[2].transform)
                || !panel.combatFacingBrightness.transform.IsChildOf(panel.pages[2].transform)) throw new Exception("모양/밝기 환경설정 연결 불일치");
            int missing = new[] { player, menu }.Sum(root => root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            if (missing != 0) throw new Exception("Missing Script 발견");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(PlayerCombatFacingSettingsBuilder.ShaderPath);
            var errors = ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray();
            if (!shader.isSupported || errors.Length != 0) throw new Exception("밝기 셰이더 오류");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "options_asset_validation.json"), JsonConvert.SerializeObject(new {
                status = "PASS", missingScripts = missing, variants = 2, meshesPerVariant = 6, defaultStyle = "끝 연장형",
                defaultBrightness = 1f, minBrightness = 0f, maxBrightness = 2f, rows = panel.pages[2].transform.childCount,
                playerGuid = AssetDatabase.AssetPathToGUID(PlayerCombatFacingVfxBuilder.PlayerPath), menuGuid = AssetDatabase.AssetPathToGUID(OverburstGameMenuBuilder.PrefabPath), shaderErrors = errors
            }, Formatting.Indented));
        }
        finally
        {
            if (menu != null) PrefabUtility.UnloadPrefabContents(menu);
            if (player != null) PrefabUtility.UnloadPrefabContents(player);
        }
    }
    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => {
        var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, s.rootCount };
    }));
}
