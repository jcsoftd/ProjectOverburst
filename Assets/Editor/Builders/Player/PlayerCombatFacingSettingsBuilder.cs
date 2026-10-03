using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>확정 절제형을 연결하고 기존 메뉴의 전투 표시 행 하나만 갱신한다.</summary>
public static class PlayerCombatFacingSettingsBuilder
{
    public const string ShaderPath = PlayerCombatFacingVfxBuilder.Root + "/QuietFill/Shaders/CombatFacingQuietFlow.shader";
    const string DebugModulePath = "Assets/ProjectOverburst/02_Shared/Debug/Runtime/Modules/CombatFacingDebugModule.cs";

    sealed class ImportEntry { public string source, target, guid; public bool reused; }

    public static void Apply(string sourceProject, string shaderSource, string manifestPath, string output)
    {
        PlayerCombatFacingVfxBuilder.RequireIdle();
        if (IsolatedSavePlayGuard.RequiresAccountChoice) throw new InvalidOperationException("격리 검증의 계정 반환을 기다립니다.");
        Directory.CreateDirectory(output);
        var entries = JsonConvert.DeserializeObject<ImportEntry[]>(File.ReadAllText(manifestPath));
        if (entries == null || entries.Length != 20 || entries.Count(e => !e.reused) != 15)
            throw new InvalidOperationException("확정 프리팹 의존성 매니페스트가 다릅니다.");
        foreach (var e in entries)
        {
            string current = AssetDatabase.GUIDToAssetPath(e.guid);
            if (e.reused && current != e.target) throw new InvalidOperationException("기존 GUID 위치가 다릅니다: " + e.target);
            if (!e.reused && (!e.target.StartsWith(PlayerCombatFacingVfxBuilder.Root + "/QuietFill/", StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(current) && current != e.target) || (File.Exists(e.target) && string.IsNullOrEmpty(current))))
                throw new InvalidOperationException("새 자산 위치/GUID가 이미 사용 중입니다: " + e.target);
        }
        var scenes = SceneState();
        // Unity에서 이미 저작한 파일과 meta를 바이트 그대로 가져온 뒤 AssetDatabase로 로드한다.
        // 직렬화 필드와 GUID는 텍스트로 수정하지 않는다. 기존 자산은 덮어쓰지 않는다.
        foreach (var e in entries.Where(e => !e.reused && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(e.guid))))
        {
            string source = Path.GetFullPath(Path.Combine(sourceProject, e.source));
            string sourceAssets = Path.GetFullPath(Path.Combine(sourceProject, "Assets")) + Path.DirectorySeparatorChar;
            if (!source.StartsWith(sourceAssets, StringComparison.OrdinalIgnoreCase) || !File.Exists(source) || !File.Exists(source + ".meta"))
                throw new InvalidOperationException("Unity 저작 원본 경로가 다릅니다: " + e.source);
            EnsureFolder(Path.GetDirectoryName(e.target).Replace('\\', '/'));
            File.Copy(source, e.target, false);
            File.Copy(source + ".meta", e.target + ".meta", false);
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        foreach (var e in entries)
            if (AssetDatabase.AssetPathToGUID(e.target) != e.guid)
                throw new InvalidOperationException("임포트 후 GUID가 다릅니다: " + e.target);

        // 셰이더 소스만 제품 시계/페이드 입력을 더한다. 원본 Mesh/Material 픽셀 입력은 보존한다.
        File.WriteAllText(ShaderPath, File.ReadAllText(shaderSource));
        AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("절제형 셰이더를 로드하지 못했습니다.");
        foreach (var e in entries.Where(e => e.target.EndsWith(".mat", StringComparison.Ordinal)))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(e.target);
            if (material == null || material.shader != shader) throw new InvalidOperationException("재질의 셰이더 GUID 연결이 다릅니다.");
            material.SetFloat("_SilverVisibility", 1f);
            material.SetFloat("_SilverRuntimeClock", 0f);
            material.SetFloat("_SilverRuntimeTime", 0f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }
        ReplacePlayerVisual();
        OverburstGameMenuBuilder.AddCombatFacingSetting();
        if (AssetDatabase.LoadAssetAtPath<MonoScript>(DebugModulePath) != null)
            throw new InvalidOperationException("임시 디버그 등록 소스가 아직 남아 있습니다.");
        PlayerCombatFacingVfxBuilder.Validate(output);
        ValidateMenu(output);
        if (scenes != SceneState()) throw new InvalidOperationException("작업 중 열린 씬의 저장/구조 상태가 바뀌었습니다.");
        File.WriteAllText(Path.Combine(output, "apply_validation.json"), JsonConvert.SerializeObject(new { status = "PASS", imported = entries.Count(e => !e.reused), reused = entries.Count(e => e.reused), sourceQuietGuid = AssetDatabase.AssetPathToGUID(PlayerCombatFacingVfxBuilder.EffectPath), productApplied = true, defaultEnabled = true, temporaryDebugToggleRemoved = true, openedScenesPreserved = true }, Formatting.Indented));
    }

    static void ReplacePlayerVisual()
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCombatFacingVfxBuilder.PlayerPath);
        if (asset == null || EditorUtility.IsDirty(asset)) throw new InvalidOperationException("플레이어 프리팹이 없거나 dirty입니다.");
        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(PlayerCombatFacingVfxBuilder.PlayerPath);
            var controller = contents.GetComponent<PlayerCombatFacingVfx>();
            if (controller == null || controller.VisualRoot == null) throw new InvalidOperationException("기존 방향 표시 연결이 없습니다.");
            var components = contents.GetComponents<Component>().Select(c => c.GetType().FullName).ToArray();
            var children = Enumerable.Range(0, contents.transform.childCount).Select(i => contents.transform.GetChild(i).name).ToArray();
            int sibling = controller.VisualRoot.GetSiblingIndex();
            Object.DestroyImmediate(controller.VisualRoot.gameObject);
            var effectAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerCombatFacingVfxBuilder.EffectPath);
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(effectAsset, contents.scene);
            effect.name = "CombatFacingIndicator";
            effect.transform.SetParent(contents.transform, false);
            effect.transform.SetSiblingIndex(sibling);
            effect.transform.localPosition = Vector3.zero;
            effect.transform.localRotation = Quaternion.identity;
            effect.transform.localScale = Vector3.one * .45f;
            foreach (var t in effect.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = contents.layer;
            effect.SetActive(false);
            var fields = new SerializedObject(controller);
            fields.FindProperty("visualRoot").objectReferenceValue = effect.transform;
            fields.FindProperty("worldScale").floatValue = .45f;
            fields.ApplyModifiedPropertiesWithoutUndo();
            if (!components.SequenceEqual(contents.GetComponents<Component>().Select(c => c.GetType().FullName))
                || !children.SequenceEqual(Enumerable.Range(0, contents.transform.childCount).Select(i => contents.transform.GetChild(i).name)))
                throw new InvalidOperationException("기존 플레이어 컴포넌트/직계 자식 구조가 바뀌었습니다.");
            PrefabUtility.SaveAsPrefabAsset(contents, PlayerCombatFacingVfxBuilder.PlayerPath, out bool saved);
            if (!saved) throw new InvalidOperationException("플레이어 저장 실패");
        }
        finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
    }

    public static void ValidateMenu(string output)
    {
        GameObject menu = null;
        try
        {
            menu = PrefabUtility.LoadPrefabContents(OverburstGameMenuBuilder.PrefabPath);
            var panel = menu.GetComponentInChildren<OverburstSettingsPanel>(true);
            var toggle = panel != null ? panel.combatFacingIndicator : null;
            int missing = menu.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (panel == null || toggle == null || !toggle.isOn || !toggle.transform.IsChildOf(panel.pages[2].transform) || missing != 0)
                throw new InvalidOperationException("전투 표시 설정 프리팹 검증 실패");
            var label = toggle.transform.parent.parent.Find("Label").GetComponent<UnityEngine.UI.Text>();
            if (label.text != "전투 방향 표시") throw new InvalidOperationException("설정 라벨이 다릅니다.");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "menu_asset_validation.json"), JsonConvert.SerializeObject(new { status = "PASS", path = OverburstGameMenuBuilder.PrefabPath, guid = AssetDatabase.AssetPathToGUID(OverburstGameMenuBuilder.PrefabPath), tabs = panel.tabs.Length, defaultEnabled = toggle.isOn, label = label.text, missingScripts = missing, combatPageRows = panel.pages[2].transform.childCount }, Formatting.Indented));
        }
        finally { if (menu != null) PrefabUtility.UnloadPrefabContents(menu); }
    }

    static string SceneState() => JsonConvert.SerializeObject(Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i => { var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i); return new { scene.path, scene.isDirty, scene.rootCount }; }).ToArray());

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
