using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json;

public static class PlayerCombatFacingVfxBuilder
{
    public const string Root = "Assets/ProjectOverburst/03_Features/Player/VFX/CombatFacing";
    public const string EffectPath = Root + "/Prefabs/PF_VFX_CombatFacing_01_SilverComet_Refined.prefab";
    public const string PlayerPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";

    public static void Install(string output)
    {
        RequireIdle();
        Directory.CreateDirectory(output);
        var playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var effectAsset = AssetDatabase.LoadAssetAtPath<GameObject>(EffectPath);
        if (playerAsset == null || effectAsset == null || EditorUtility.IsDirty(playerAsset))
            throw new InvalidOperationException("플레이어/효과 프리팹이 없거나 미저장 변경이 있습니다.");
        if (effectAsset.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) != 0)
            throw new InvalidOperationException("효과 프리팹의 스크립트 참조가 깨졌습니다.");
        string playerDisk = Path.GetFullPath(PlayerPath);
        foreach (var suffix in new[] { "", ".meta" }) File.Copy(playerDisk + suffix, Path.Combine(output, "PF_PlayerActor.before.prefab" + suffix), false);
        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(PlayerPath);
            if (contents.GetComponent<PlayerCombatFacingVfx>() != null || contents.transform.Find("CombatFacingIndicator") != null)
                throw new InvalidOperationException("기존 방향 표시가 있어 중복 적용을 중단했습니다.");
            string before = JsonConvert.SerializeObject(new { components = contents.GetComponents<Component>().Select(c => c.GetType().FullName).ToArray(), children = Enumerable.Range(0, contents.transform.childCount).Select(i => contents.transform.GetChild(i).name).ToArray() });
            File.WriteAllText(Path.Combine(output, "player_before_structure.json"), before);
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(effectAsset, contents.scene);
            effect.name = "CombatFacingIndicator"; effect.transform.SetParent(contents.transform, false);
            effect.transform.localPosition = Vector3.zero; effect.transform.localRotation = Quaternion.identity;
            foreach (var child in effect.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = contents.layer;
            effect.SetActive(false);
            var controller = contents.AddComponent<PlayerCombatFacingVfx>();
            var fields = new SerializedObject(controller);
            fields.FindProperty("visualRoot").objectReferenceValue = effect.transform;
            fields.FindProperty("worldScale").floatValue = .45f;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerPath);
        }
        finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
        Validate(output);
    }

    public static void Validate(string output)
    {
        GameObject player = null;
        try
        {
            player = PrefabUtility.LoadPrefabContents(PlayerPath);
            var controller = player.GetComponent<PlayerCombatFacingVfx>();
            int missing = player.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var visual = controller != null ? controller.VisualRoot : null;
            var renderers = visual != null ? visual.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            int invalid = renderers.SelectMany(r => r.sharedMaterials).Count(m => m == null || m.shader == null || !m.shader.isSupported);
            var shader = Shader.Find("OVERBURST/VFX/Combat Facing Silver Flow");
            var shaderErrors = shader != null ? ShaderUtil.GetShaderMessages(shader).Where(m => m.severity.ToString() == "Error").Select(m => m.message).ToArray() : new[] { "shader missing" };
            if (controller == null || visual == null || missing != 0 || invalid != 0 || renderers.Length != 7 || shaderErrors.Length != 0)
                throw new InvalidOperationException("전투 방향 프리팹 로드 검증 실패");
            var materials = renderers.SelectMany(r => r.sharedMaterials).Distinct().Select(m => new { path = AssetDatabase.GetAssetPath(m), tint = Rgba(m.GetColor("_Tint")), emission = m.GetFloat("_Emission"), opacity = m.GetFloat("_Opacity") }).ToArray();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "asset_validation.json"), JsonConvert.SerializeObject(new { status = "PASS", player = PlayerPath, playerGuid = AssetDatabase.AssetPathToGUID(PlayerPath), effect = EffectPath, effectGuid = AssetDatabase.AssetPathToGUID(EffectPath), missingScripts = missing, invalidMaterials = invalid, renderers = renderers.Length, initiallyHidden = !visual.gameObject.activeSelf, nestedPrefab = PrefabUtility.GetCorrespondingObjectFromSource(visual.gameObject) != null, fixedSilverTint = true, materials, dependencies = AssetDatabase.GetDependencies(EffectPath, true).Select(p => new { path = p, guid = AssetDatabase.AssetPathToGUID(p) }).ToArray() }, Formatting.Indented));
        }
        finally { if (player != null) PrefabUtility.UnloadPrefabContents(player); }
    }

    static float[] Rgba(Color c) => new[] { c.r, c.g, c.b, c.a };

    public static void RequireIdle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("공유 Play/임포트/격리 계정 반환이 끝난 유휴 Editor에서만 실행합니다.");
    }
}
