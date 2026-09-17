using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// P08 Federica 패키지 전체를 프로젝트 소유 URP 자산으로 정식화한다.
/// ThirdParty 원본은 수정하지 않고 재질 36개와 프리팹 34개를 모두 복제한다.
/// </summary>
public static class P08FedericaUrpMaterialBuilder
{
    private const string SourceRoot =
        "Assets/ThirdParty/02_인간캐릭터/P08_Federica_v1.1";
    private const string OutputMaterialRoot =
        "Assets/ProjectOverburst/02_Shared/CharacterVisual/Materials/P08_Federica";
    private const string OutputPrefabRoot =
        "Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/P08_Federica";
    private const string ProjectSceneRoot =
        "Assets/ProjectOverburst/00_Scenes";
    private const string LogPath =
        "Logs/P08FedericaUrpMaterialBuilder.log";
    private const string InPlaceLogPath =
        "Logs/P08FedericaInPlaceUrpFix.log";
    private const string SourceBackupRoot =
        "_LegacyBackup/UrpSourceMaterialBackup_20260725/P08_Federica_v1.1";
    [MenuItem("Tools/ProjectVTP/Characters/P08 Federica 전체 URP 정식화")]
    public static void RunFromMenu()
    {
        try
        {
            Run(true);
            EditorUtility.DisplayDialog(
                "P08 Federica 전체 URP 정식화",
                "재질·프리팹 전체 변환과 현재 프로젝트 씬 연결을 완료했습니다.",
                "확인");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "P08 Federica 전체 URP 정식화 실패",
                exception.Message,
                "확인");
            throw;
        }
    }

    public static void RunFromCommandLine()
    {
        try
        {
            Run(true);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void RunInPlaceFromCommandLine()
    {
        try
        {
            RunInPlace();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void RunInPlace()
    {
        string[] sourceMaterialPaths = FindAssetFiles(SourceRoot, "*.mat");
        string[] sourcePrefabPaths = FindAssetFiles(SourceRoot, "*.prefab");
        Require(sourceMaterialPaths.Length == 36,
            $"P08 원본 재질 수 불일치: {sourceMaterialPaths.Length}");
        Require(sourcePrefabPaths.Length == 34,
            $"P08 원본 프리팹 수 불일치: {sourcePrefabPaths.Length}");

        BackupSourceMaterials(sourceMaterialPaths);

        Material[] sourceMaterials = sourceMaterialPaths
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .ToArray();
        Require(sourceMaterials.All(material => material != null),
            "P08 원본 재질 로드에 실패했습니다.");

        ConvertUts2CopiesWithOfficialConverter(sourceMaterials);
        Dictionary<Material, Material> inPlaceMap =
            sourceMaterials.ToDictionary(
                material => material,
                material => material);
        ConvertRemainingLegacyMaterials(inPlaceMap);
        ConfigureGlassMaterials(inPlaceMap);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        ValidateConvertedMaterials(sourceMaterials);
        ValidateSourcePrefabsNoPink(sourcePrefabPaths);

        StringBuilder report = new();
        report.AppendLine("[P08FedericaInPlaceUrpFix] PASS");
        report.AppendLine($"SourceMaterialCount={sourceMaterialPaths.Length}");
        report.AppendLine($"SourcePrefabCount={sourcePrefabPaths.Length}");
        report.AppendLine($"BackupRoot={SourceBackupRoot}");
        report.AppendLine("ThirdPartyMaterialsModifiedInPlace=True");
        foreach (Material material in sourceMaterials.OrderBy(
                     material => AssetDatabase.GetAssetPath(material),
                     StringComparer.Ordinal))
        {
            report.AppendLine(
                $"Material={AssetDatabase.GetAssetPath(material)}"
                + $", Shader={material.shader?.name ?? "<null>"}");
        }

        WriteLog(InPlaceLogPath, report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Run(bool updateProjectScenes)
    {
        Require(AssetDatabase.IsValidFolder(SourceRoot),
            $"P08 원본 폴더 누락: {SourceRoot}");

        string[] sourceMaterialPaths = FindAssetFiles(SourceRoot, "*.mat");
        string[] sourcePrefabPaths = FindAssetFiles(SourceRoot, "*.prefab");
        Require(sourceMaterialPaths.Length == 36,
            $"P08 원본 재질 수 불일치: 예상 36, 실제 {sourceMaterialPaths.Length}");
        Require(sourcePrefabPaths.Length == 34,
            $"P08 원본 프리팹 수 불일치: 예상 34, 실제 {sourcePrefabPaths.Length}");

        string[] protectedSourcePaths = sourceMaterialPaths
            .Concat(sourcePrefabPaths)
            .ToArray();
        Dictionary<string, string> sourceHashes =
            CaptureHashes(protectedSourcePaths);

        EnsureFolder(OutputMaterialRoot);
        EnsureFolder(OutputPrefabRoot);

        Dictionary<Material, Material> convertedMaterials =
            CopyProjectOwnedMaterials(sourceMaterialPaths);
        ConvertUts2CopiesWithOfficialConverter(
            convertedMaterials.Values.ToArray());
        ConvertRemainingLegacyMaterials(convertedMaterials);
        ConfigureGlassMaterials(convertedMaterials);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        PrefabBuildResult prefabResult =
            BuildProjectOwnedPrefabs(sourcePrefabPaths, convertedMaterials);
        SceneBuildResult sceneResult =
            updateProjectScenes
                ? UpdateProjectScenes(convertedMaterials)
                : new SceneBuildResult();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        ValidateConvertedMaterials(convertedMaterials.Values);
        ValidateProjectOwnedPrefabs(sourcePrefabPaths, convertedMaterials);
        if (updateProjectScenes)
            ValidateProjectScenes(convertedMaterials.Keys);
        ValidateSourcesUnchanged(sourceHashes);

        StringBuilder report = new();
        report.AppendLine("[P08FedericaUrpMaterialBuilder] PASS");
        report.AppendLine($"SourceRoot={SourceRoot}");
        report.AppendLine(
            $"Mode={(updateProjectScenes ? "AssetsAndScenes" : "AssetsOnly")}");
        report.AppendLine($"SourceMaterialCount={sourceMaterialPaths.Length}");
        report.AppendLine($"ConvertedMaterialCount={convertedMaterials.Count}");
        report.AppendLine($"SourcePrefabCount={sourcePrefabPaths.Length}");
        report.AppendLine($"FormalizedPrefabCount={prefabResult.PrefabCount}");
        report.AppendLine(
            $"FormalizedPrefabRendererCount={prefabResult.RendererCount}");
        report.AppendLine(
            $"FormalizedPrefabReplacedSlots={prefabResult.ReplacedSlotCount}");
        report.AppendLine(
            $"RemovedMissingScripts={prefabResult.RemovedMissingScriptCount}");
        report.AppendLine($"ScannedSceneCount={sceneResult.ScannedSceneCount}");
        report.AppendLine($"ChangedSceneCount={sceneResult.ChangedSceneCount}");
        report.AppendLine(
            $"SceneReplacedMaterialSlots={sceneResult.ReplacedSlotCount}");
        report.AppendLine($"OutputMaterialRoot={OutputMaterialRoot}");
        report.AppendLine($"OutputPrefabRoot={OutputPrefabRoot}");

        foreach (KeyValuePair<Material, Material> pair in convertedMaterials
                     .OrderBy(pair => AssetDatabase.GetAssetPath(pair.Key),
                         StringComparer.Ordinal))
        {
            report.AppendLine(
                $"Material={AssetDatabase.GetAssetPath(pair.Key)}"
                + $" -> {AssetDatabase.GetAssetPath(pair.Value)}"
                + $", Shader={pair.Value.shader?.name ?? "<null>"}");
        }

        foreach (string sourcePrefabPath in sourcePrefabPaths)
        {
            report.AppendLine(
                $"Prefab={sourcePrefabPath} -> "
                + GetOutputPrefabPath(sourcePrefabPath));
        }

        WriteLog(report.ToString());
        Debug.Log(report.ToString());
    }

    private static string[] FindAssetFiles(string root, string pattern)
    {
        string absoluteRoot = Path.GetFullPath(root);
        return Directory.GetFiles(
                absoluteRoot,
                pattern,
                SearchOption.AllDirectories)
            .Select(ToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string ToAssetPath(string absolutePath)
    {
        string projectRoot = Path.GetFullPath(".")
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedAbsolute = Path.GetFullPath(absolutePath);
        Require(
            normalizedAbsolute.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase),
            $"프로젝트 외부 경로입니다: {absolutePath}");
        return normalizedAbsolute
            .Substring(projectRoot.Length + 1)
            .Replace('\\', '/');
    }

    private static Dictionary<string, string> CaptureHashes(
        IEnumerable<string> assetPaths)
    {
        return assetPaths.ToDictionary(
            path => path,
            ComputeSha256,
            StringComparer.Ordinal);
    }

    private static Dictionary<Material, Material> CopyProjectOwnedMaterials(
        IEnumerable<string> sourcePaths)
    {
        Dictionary<Material, Material> result = new();
        foreach (string sourcePath in sourcePaths)
        {
            Material source =
                AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            Require(source != null, $"P08 원본 재질 로드 실패: {sourcePath}");

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string fileName =
                $"{SanitizeFileName(source.name)}_{sourceGuid.Substring(0, 8)}_Toon.mat";
            string destinationPath = $"{OutputMaterialRoot}/{fileName}";

            Material destination =
                AssetDatabase.LoadAssetAtPath<Material>(destinationPath);
            if (destination == null)
            {
                Require(
                    AssetDatabase.CopyAsset(sourcePath, destinationPath),
                    $"P08 재질 복제 실패: {sourcePath}");
                AssetDatabase.ImportAsset(
                    destinationPath,
                    ImportAssetOptions.ForceSynchronousImport);
                destination =
                    AssetDatabase.LoadAssetAtPath<Material>(destinationPath);
            }
            else
            {
                EditorUtility.CopySerialized(source, destination);
                destination.name = Path.GetFileNameWithoutExtension(destinationPath);
                EditorUtility.SetDirty(destination);
            }

            Require(destination != null,
                $"P08 복제 재질 로드 실패: {destinationPath}");
            result.Add(source, destination);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return result;
    }

    private static void ConvertUts2CopiesWithOfficialConverter(
        IReadOnlyCollection<Material> materials)
    {
        Type converterType = FindType(
            "UnityEditor.Rendering.Toon.BuiltInUTS2toIntegratedConverter");
        Type converterBaseType = FindType(
            "UnityEditor.Rendering.Toon.RenderPipelineConverterContainer");
        Type converterWindowType = FindType(
            "UnityEditor.Rendering.Toon.UTS3Converter");

        PropertyInfo scrollViewProperty = converterWindowType.GetProperty(
            "scrollView",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(
                converterWindowType.FullName,
                "scrollView");
        scrollViewProperty.SetValue(null, new ScrollView());

        object converter = Activator.CreateInstance(converterType, true)
                           ?? throw new InvalidOperationException(
                               "Unity Toon Shader 변환기를 만들 수 없습니다.");

        FieldInfo materialGuidsField = converterBaseType.GetField(
            "m_materialGuids",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(
                converterBaseType.FullName,
                "m_materialGuids");
        string[] materialGuids = materials
            .Select(AssetDatabase.GetAssetPath)
            .Select(AssetDatabase.AssetPathToGUID)
            .Where(guid => !string.IsNullOrWhiteSpace(guid))
            .ToArray();
        materialGuidsField.SetValue(converter, materialGuids);

        MethodInfo setup = converterType.GetMethod(
            "SetupConverter",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(
                converterType.FullName,
                "SetupConverter");
        MethodInfo convert = converterType.GetMethod(
            "Convert",
            BindingFlags.Instance | BindingFlags.Public)
            ?? throw new MissingMethodException(
                converterType.FullName,
                "Convert");

        setup.Invoke(converter, null);
        convert.Invoke(converter, null);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    private static void ConvertRemainingLegacyMaterials(
        IReadOnlyDictionary<Material, Material> materials)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Require(urpLit != null, "URP Lit 셰이더를 찾지 못했습니다.");

        foreach (KeyValuePair<Material, Material> pair in materials)
        {
            Material source = pair.Key;
            Material destination = pair.Value;
            if (IsSupportedShader(destination.shader))
                continue;

            Texture mainTexture =
                source.HasProperty("_MainTex")
                    ? source.GetTexture("_MainTex")
                    : null;
            Color color =
                source.HasProperty("_Color")
                    ? source.GetColor("_Color")
                    : Color.white;
            Texture normalTexture =
                source.HasProperty("_BumpMap")
                    ? source.GetTexture("_BumpMap")
                    : null;
            bool transparent =
                source.renderQueue >= (int)RenderQueue.Transparent;
            bool alphaCutout =
                !transparent
                && (source.IsKeywordEnabled("_ALPHATEST_ON")
                    || source.renderQueue >= (int)RenderQueue.AlphaTest);

            destination.shader = urpLit;
            destination.SetTexture("_BaseMap", mainTexture);
            destination.SetColor("_BaseColor", color);
            if (normalTexture != null)
            {
                destination.SetTexture("_BumpMap", normalTexture);
                destination.EnableKeyword("_NORMALMAP");
            }

            if (transparent)
                ConfigureUrpTransparent(destination);
            else if (alphaCutout)
                ConfigureUrpCutout(destination, source);
            else
                ConfigureUrpOpaque(destination);

            EditorUtility.SetDirty(destination);
        }
    }

    private static void ConfigureGlassMaterials(
        IReadOnlyDictionary<Material, Material> materials)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Require(urpLit != null, "URP Lit 셰이더를 찾지 못했습니다.");

        foreach (KeyValuePair<Material, Material> pair in materials)
        {
            if (!string.Equals(pair.Key.name, "P08_Glass",
                    StringComparison.Ordinal))
                continue;

            Material source = pair.Key;
            Material destination = pair.Value;
            Texture mainTexture =
                source.HasProperty("_MainTex")
                    ? source.GetTexture("_MainTex")
                    : null;
            Color color =
                source.HasProperty("_Color")
                    ? source.GetColor("_Color")
                    : Color.white;

            destination.shader = urpLit;
            destination.SetTexture("_BaseMap", mainTexture);
            destination.SetColor("_BaseColor", color);
            ConfigureUrpTransparent(destination);
            EditorUtility.SetDirty(destination);
        }
    }

    private static void ConfigureUrpOpaque(Material material)
    {
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_ZWrite", 1f);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = -1;
        material.SetOverrideTag("RenderType", "Opaque");
    }

    private static void ConfigureUrpCutout(
        Material material,
        Material source)
    {
        float cutoff =
            source.HasProperty("_Cutoff")
                ? source.GetFloat("_Cutoff")
                : 0.5f;
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", cutoff);
        material.SetFloat("_ZWrite", 1f);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetOverrideTag("RenderType", "TransparentCutout");
    }

    private static void ConfigureUrpTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
    }

    private static PrefabBuildResult BuildProjectOwnedPrefabs(
        IEnumerable<string> sourcePrefabPaths,
        IReadOnlyDictionary<Material, Material> convertedMaterials)
    {
        PrefabBuildResult result = new();
        foreach (string sourcePrefabPath in sourcePrefabPaths)
        {
            string destinationPath = GetOutputPrefabPath(sourcePrefabPath);
            EnsureFolder(Path.GetDirectoryName(destinationPath)
                ?.Replace('\\', '/')
                ?? OutputPrefabRoot);

            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
                Require(contents != null,
                    $"P08 원본 프리팹 로드 실패: {sourcePrefabPath}");

                foreach (Renderer renderer in
                         contents.GetComponentsInChildren<Renderer>(true))
                {
                    result.RendererCount++;
                    result.ReplacedSlotCount += ReplaceMaterials(
                        renderer,
                        convertedMaterials);
                }

                foreach (Transform transform in
                         contents.GetComponentsInChildren<Transform>(true))
                {
                    result.RemovedMissingScriptCount +=
                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(
                            transform.gameObject);
                }

                GameObject saved =
                    PrefabUtility.SaveAsPrefabAsset(contents, destinationPath);
                Require(saved != null,
                    $"P08 정식 프리팹 저장 실패: {destinationPath}");
                result.PrefabCount++;
            }
            finally
            {
                if (contents != null)
                    PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return result;
    }

    private static SceneBuildResult UpdateProjectScenes(
        IReadOnlyDictionary<Material, Material> convertedMaterials)
    {
        SceneBuildResult result = new();
        string[] scenePaths = FindAssetFiles(ProjectSceneRoot, "*.unity");
        foreach (string scenePath in scenePaths)
        {
            Scene scene =
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                $"프로젝트 씬 로드 실패: {scenePath}");
            result.ScannedSceneCount++;

            int sceneReplacedSlots = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    int replaced =
                        ReplaceMaterials(renderer, convertedMaterials);
                    if (replaced <= 0)
                        continue;

                    sceneReplacedSlots += replaced;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        renderer);
                    EditorUtility.SetDirty(renderer);
                }
            }

            if (sceneReplacedSlots <= 0)
                continue;

            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene),
                $"P08 재질 연결 씬 저장 실패: {scenePath}");
            result.ChangedSceneCount++;
            result.ReplacedSlotCount += sceneReplacedSlots;
        }

        return result;
    }

    private static int ReplaceMaterials(
        Renderer renderer,
        IReadOnlyDictionary<Material, Material> convertedMaterials)
    {
        Material[] materials = renderer.sharedMaterials;
        int replacedCount = 0;
        for (int index = 0; index < materials.Length; index++)
        {
            Material source = materials[index];
            if (source == null
                || !convertedMaterials.TryGetValue(
                    source,
                    out Material replacement))
                continue;

            materials[index] = replacement;
            replacedCount++;
        }

        if (replacedCount > 0)
            renderer.sharedMaterials = materials;
        return replacedCount;
    }

    private static string GetOutputPrefabPath(string sourcePrefabPath)
    {
        string relative = sourcePrefabPath
            .Substring(SourceRoot.Length)
            .TrimStart('/');
        return $"{OutputPrefabRoot}/{relative}";
    }

    private static void ValidateConvertedMaterials(
        IEnumerable<Material> materials)
    {
        Material[] materialArray = materials.ToArray();
        Require(materialArray.Length == 36,
            $"변환 재질 수 불일치: {materialArray.Length}");

        foreach (Material material in materialArray)
        {
            Require(material != null, "변환 재질 참조가 null입니다.");
            Require(material.shader != null,
                $"변환 재질 셰이더 누락: {material.name}");
            Require(IsSupportedShader(material.shader),
                $"지원하지 않는 셰이더: {material.name} -> {material.shader.name}");
            Require(!ShaderUtil.ShaderHasError(material.shader),
                $"셰이더 컴파일 오류: {material.name} -> {material.shader.name}");
        }
    }

    private static bool IsSupportedShader(Shader shader)
    {
        return shader != null
               && (string.Equals(shader.name, "Toon", StringComparison.Ordinal)
                   || string.Equals(
                       shader.name,
                       "Universal Render Pipeline/Lit",
                       StringComparison.Ordinal));
    }

    private static void ValidateProjectOwnedPrefabs(
        IEnumerable<string> sourcePrefabPaths,
        IReadOnlyDictionary<Material, Material> convertedMaterials)
    {
        HashSet<Material> sourceMaterials =
            new(convertedMaterials.Keys);
        int prefabCount = 0;
        int convertedSlotCount = 0;
        foreach (string sourcePrefabPath in sourcePrefabPaths)
        {
            string destinationPath = GetOutputPrefabPath(sourcePrefabPath);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath);
            Require(prefab != null,
                $"P08 정식 프리팹 누락: {destinationPath}");
            prefabCount++;

            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material != null,
                        $"P08 정식 프리팹 재질 누락: {destinationPath}"
                        + $" / {GetHierarchyPath(renderer.transform)}");
                    Require(!sourceMaterials.Contains(material),
                        $"P08 원본 재질 참조 잔존: {destinationPath}"
                        + $" / {material.name}");
                    Require(material.shader != null
                            && !material.shader.name.StartsWith(
                                "Hidden/InternalErrorShader",
                                StringComparison.Ordinal),
                        $"P08 핑크 셰이더 잔존: {destinationPath}"
                        + $" / {material.name}");
                    if (convertedMaterials.Values.Contains(material))
                        convertedSlotCount++;
                }
            }

            foreach (Transform transform in
                     prefab.GetComponentsInChildren<Transform>(true))
            {
                Require(
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        transform.gameObject) == 0,
                    $"P08 정식 프리팹 Missing Script 잔존: {destinationPath}"
                    + $" / {GetHierarchyPath(transform)}");
            }
        }

        Require(prefabCount == 34,
            $"P08 정식 프리팹 수 불일치: {prefabCount}");
        Require(convertedSlotCount > 0,
            "P08 정식 프리팹에 변환 재질이 연결되지 않았습니다.");
    }

    private static void ValidateSourcePrefabsNoPink(
        IEnumerable<string> sourcePrefabPaths)
    {
        int prefabCount = 0;
        foreach (string sourcePrefabPath in sourcePrefabPaths)
        {
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            Require(prefab != null,
                $"P08 원본 프리팹 로드 실패: {sourcePrefabPath}");
            prefabCount++;

            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material != null,
                        $"P08 원본 프리팹 재질 누락: {sourcePrefabPath}");
                    Require(material.shader != null
                            && !material.shader.name.StartsWith(
                                "Hidden/InternalErrorShader",
                                StringComparison.Ordinal),
                        $"P08 원본 프리팹 핑크 셰이더 잔존: "
                        + $"{sourcePrefabPath} / {material.name}");
                }
            }
        }

        Require(prefabCount == 34,
            $"P08 원본 프리팹 검증 수 불일치: {prefabCount}");
    }

    private static void ValidateProjectScenes(
        IEnumerable<Material> sourceMaterialEnumerable)
    {
        HashSet<Material> sourceMaterials = new(sourceMaterialEnumerable);
        string[] scenePaths = FindAssetFiles(ProjectSceneRoot, "*.unity");
        foreach (string scenePath in scenePaths)
        {
            Scene scene =
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                $"검증 씬 로드 실패: {scenePath}");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null)
                            continue;
                        Require(!sourceMaterials.Contains(material),
                            $"프로젝트 씬에 P08 원본 재질 참조 잔존: {scenePath}"
                            + $" / {GetHierarchyPath(renderer.transform)}"
                            + $" / {material.name}");
                    }
                }
            }
        }
    }

    private static void ValidateSourcesUnchanged(
        IReadOnlyDictionary<string, string> sourceHashes)
    {
        foreach (KeyValuePair<string, string> pair in sourceHashes)
        {
            string actualHash = ComputeSha256(pair.Key);
            Require(
                string.Equals(pair.Value, actualHash,
                    StringComparison.OrdinalIgnoreCase),
                $"ThirdParty P08 원본이 변경되었습니다: {pair.Key}");
        }
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName, false);
            if (type != null)
                return type;
        }

        throw new TypeLoadException($"타입을 찾지 못했습니다: {fullName}");
    }

    private static void EnsureFolder(string folderPath)
    {
        string normalized = folderPath.Replace('\\', '/').TrimEnd('/');
        string[] parts = normalized.Split('/');
        string current = parts[0];
        for (int index = 1; index < parts.Length; index++)
        {
            string next = $"{current}/{parts[index]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                string createdGuid =
                    AssetDatabase.CreateFolder(current, parts[index]);
                Require(!string.IsNullOrEmpty(createdGuid),
                    $"폴더 생성 실패: {next}");
            }

            current = next;
        }
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
            builder.Append(invalid.Contains(character) ? '_' : character);
        return builder.ToString();
    }

    private static string ComputeSha256(string assetPath)
    {
        string absolutePath = Path.GetFullPath(assetPath);
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(File.ReadAllBytes(absolutePath));
        return BitConverter.ToString(hash).Replace("-", string.Empty);
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
            return "<null>";

        Stack<string> names = new();
        Transform current = target;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", names);
    }

    private static void BackupSourceMaterials(
        IEnumerable<string> sourceMaterialPaths)
    {
        foreach (string sourcePath in sourceMaterialPaths)
        {
            string relative = sourcePath
                .Substring(SourceRoot.Length)
                .TrimStart('/');
            BackupFile(sourcePath, $"{SourceBackupRoot}/{relative}");
            string metaPath = sourcePath + ".meta";
            if (File.Exists(Path.GetFullPath(metaPath)))
                BackupFile(metaPath, $"{SourceBackupRoot}/{relative}.meta");
        }
    }

    private static void BackupFile(string sourcePath, string backupPath)
    {
        string absoluteSource = Path.GetFullPath(sourcePath);
        string absoluteBackup = Path.GetFullPath(backupPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(absoluteBackup)
            ?? throw new InvalidOperationException(
                $"백업 폴더 확인 실패: {backupPath}"));
        if (!File.Exists(absoluteBackup))
            File.Copy(absoluteSource, absoluteBackup, false);
    }

    private static void WriteLog(string contents)
    {
        WriteLog(LogPath, contents);
    }

    private static void WriteLog(string logPath, string contents)
    {
        string absoluteLogPath = Path.GetFullPath(logPath);
        Directory.CreateDirectory(
            Path.GetDirectoryName(absoluteLogPath)
            ?? throw new InvalidOperationException(
                "로그 폴더를 확인할 수 없습니다."));
        File.WriteAllText(absoluteLogPath, contents, Encoding.UTF8);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class PrefabBuildResult
    {
        public int PrefabCount;
        public int RendererCount;
        public int ReplacedSlotCount;
        public int RemovedMissingScriptCount;
    }

    private sealed class SceneBuildResult
    {
        public int ScannedSceneCount;
        public int ChangedSceneCount;
        public int ReplacedSlotCount;
    }
}
