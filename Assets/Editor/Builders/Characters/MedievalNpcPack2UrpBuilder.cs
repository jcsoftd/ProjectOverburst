using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Medieval NPC Pack 2 전체를 프로젝트 소유 URP 자산으로 정식화한다.
/// ThirdParty 원본은 보존하고 재질과 프리팹 복제본만 수정한다.
/// </summary>
public static class MedievalNpcPack2UrpBuilder
{
    private const string SourceRoot =
        "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2";
    private const string OutputMaterialRoot =
        "Assets/ProjectOverburst/02_Shared/CharacterVisual/Materials/Medieval_NPC_Pack_2";
    private const string OutputPrefabRoot =
        "Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/Medieval_NPC_Pack_2";
    private const string ProjectSceneRoot =
        "Assets/ProjectOverburst/00_Scenes";
    private const string LogPath =
        "Logs/MedievalNpcPack2UrpBuilder.log";
    private const string InPlaceLogPath =
        "Logs/MedievalNpcPack2InPlaceUrpFix.log";
    private const string SourceBackupRoot =
        "_LegacyBackup/UrpSourceMaterialBackup_20260725/Medieval_NPC_Pack_2";

    [MenuItem("Tools/ProjectVTP/Characters/Medieval NPC Pack 2 전체 URP 정식화")]
    public static void RunFromMenu()
    {
        try
        {
            Run();
            EditorUtility.DisplayDialog(
                "Medieval NPC Pack 2 전체 URP 정식화",
                "재질 112개와 프리팹 90개의 URP 정식화를 완료했습니다.",
                "확인");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorUtility.DisplayDialog(
                "Medieval NPC Pack 2 URP 정식화 실패",
                exception.Message,
                "확인");
            throw;
        }
    }

    public static void RunFromCommandLine()
    {
        try
        {
            Run();
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
        Require(sourceMaterialPaths.Length == 112,
            $"원본 재질 수 불일치: {sourceMaterialPaths.Length}");
        Require(sourcePrefabPaths.Length == 90,
            $"원본 프리팹 수 불일치: {sourcePrefabPaths.Length}");

        BackupSourceMaterials(sourceMaterialPaths);

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Require(urpLit != null, "URP Lit 셰이더를 찾지 못했습니다.");
        List<Material> materials = new();
        foreach (string sourcePath in sourceMaterialPaths)
        {
            Material material =
                AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            Require(material != null, $"원본 재질 로드 실패: {sourcePath}");
            ConvertMaterial(material, material, urpLit);
            EditorUtility.SetDirty(material);
            materials.Add(material);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        ValidateMaterials(materials);
        ValidateSourcePrefabsNoPink(sourcePrefabPaths);

        int opaqueCount = materials.Count(
            material => GetSurfaceKind(material) == SurfaceKind.Opaque);
        int cutoutCount = materials.Count(
            material => GetSurfaceKind(material) == SurfaceKind.Cutout);
        int transparentCount = materials.Count(
            material => GetSurfaceKind(material) == SurfaceKind.Transparent);

        StringBuilder report = new();
        report.AppendLine("[MedievalNpcPack2InPlaceUrpFix] PASS");
        report.AppendLine($"SourceMaterialCount={materials.Count}");
        report.AppendLine($"SourcePrefabCount={sourcePrefabPaths.Length}");
        report.AppendLine($"OpaqueMaterialCount={opaqueCount}");
        report.AppendLine($"CutoutMaterialCount={cutoutCount}");
        report.AppendLine($"TransparentMaterialCount={transparentCount}");
        report.AppendLine($"BackupRoot={SourceBackupRoot}");
        report.AppendLine("ThirdPartyMaterialsModifiedInPlace=True");
        foreach (Material material in materials.OrderBy(
                     material => AssetDatabase.GetAssetPath(material),
                     StringComparer.Ordinal))
        {
            report.AppendLine(
                $"Material={AssetDatabase.GetAssetPath(material)}"
                + $", Shader={material.shader?.name ?? "<null>"}"
                + $", Cull={material.GetFloat("_Cull")}");
        }

        WriteLog(InPlaceLogPath, report.ToString());
        Debug.Log(report.ToString());
    }

    private static void Run()
    {
        Require(AssetDatabase.IsValidFolder(SourceRoot),
            $"Medieval NPC Pack 2 원본 폴더 누락: {SourceRoot}");

        string[] sourceMaterialPaths = FindAssetFiles(SourceRoot, "*.mat");
        string[] sourcePrefabPaths = FindAssetFiles(SourceRoot, "*.prefab");
        Require(sourceMaterialPaths.Length == 112,
            $"원본 재질 수 불일치: 예상 112, 실제 {sourceMaterialPaths.Length}");
        Require(sourcePrefabPaths.Length == 90,
            $"원본 프리팹 수 불일치: 예상 90, 실제 {sourcePrefabPaths.Length}");

        Dictionary<string, string> sourceHashes = CaptureHashes(
            sourceMaterialPaths.Concat(sourcePrefabPaths));

        EnsureFolder(OutputMaterialRoot);
        EnsureFolder(OutputPrefabRoot);

        Dictionary<Material, Material> convertedMaterials =
            CopyAndConvertMaterials(sourceMaterialPaths);
        PrefabBuildResult prefabResult =
            BuildProjectOwnedPrefabs(sourcePrefabPaths, convertedMaterials);
        SceneBuildResult sceneResult =
            UpdateProjectScenes(convertedMaterials);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        ValidateMaterials(convertedMaterials.Values);
        ValidatePrefabs(sourcePrefabPaths, convertedMaterials);
        ValidateProjectScenes(convertedMaterials.Keys);
        ValidateSourcesUnchanged(sourceHashes);

        StringBuilder report = new();
        report.AppendLine("[MedievalNpcPack2UrpBuilder] PASS");
        report.AppendLine($"SourceRoot={SourceRoot}");
        report.AppendLine($"SourceMaterialCount={sourceMaterialPaths.Length}");
        report.AppendLine($"ConvertedMaterialCount={convertedMaterials.Count}");
        report.AppendLine($"SourcePrefabCount={sourcePrefabPaths.Length}");
        report.AppendLine($"FormalizedPrefabCount={prefabResult.PrefabCount}");
        report.AppendLine(
            $"FormalizedPrefabRendererCount={prefabResult.RendererCount}");
        report.AppendLine(
            $"FormalizedPrefabReplacedSlots={prefabResult.ReplacedSlotCount}");
        report.AppendLine($"ScannedSceneCount={sceneResult.ScannedSceneCount}");
        report.AppendLine($"ChangedSceneCount={sceneResult.ChangedSceneCount}");
        report.AppendLine(
            $"SceneReplacedMaterialSlots={sceneResult.ReplacedSlotCount}");
        int opaqueCount = convertedMaterials.Count(
            pair => GetSurfaceKind(pair.Key) == SurfaceKind.Opaque);
        int cutoutCount = convertedMaterials.Count(
            pair => GetSurfaceKind(pair.Key) == SurfaceKind.Cutout);
        int transparentCount = convertedMaterials.Count(
            pair => GetSurfaceKind(pair.Key) == SurfaceKind.Transparent);
        int doubleSidedCount = convertedMaterials.Count(
            pair => IsDoubleSidedSource(pair.Key));
        report.AppendLine($"OpaqueMaterialCount={opaqueCount}");
        report.AppendLine($"CutoutMaterialCount={cutoutCount}");
        report.AppendLine($"TransparentMaterialCount={transparentCount}");
        report.AppendLine($"DoubleSidedMaterialCount={doubleSidedCount}");
        report.AppendLine($"OutputMaterialRoot={OutputMaterialRoot}");
        report.AppendLine($"OutputPrefabRoot={OutputPrefabRoot}");

        foreach (KeyValuePair<Material, Material> pair in convertedMaterials
                     .OrderBy(pair => AssetDatabase.GetAssetPath(pair.Key),
                         StringComparer.Ordinal))
        {
            report.AppendLine(
                $"Material={AssetDatabase.GetAssetPath(pair.Key)}"
                + $" -> {AssetDatabase.GetAssetPath(pair.Value)}"
                + $", Surface={GetSurfaceKind(pair.Key)}"
                + $", DoubleSided={IsDoubleSidedSource(pair.Key)}"
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

    private static Dictionary<Material, Material> CopyAndConvertMaterials(
        IEnumerable<string> sourcePaths)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        Require(urpLit != null, "URP Lit 셰이더를 찾지 못했습니다.");

        Dictionary<Material, Material> result = new();
        foreach (string sourcePath in sourcePaths)
        {
            Material source =
                AssetDatabase.LoadAssetAtPath<Material>(sourcePath);
            Require(source != null, $"원본 재질 로드 실패: {sourcePath}");

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string fileName =
                $"{SanitizeFileName(source.name)}_{sourceGuid.Substring(0, 8)}_URP.mat";
            string destinationPath = $"{OutputMaterialRoot}/{fileName}";
            Material destination =
                AssetDatabase.LoadAssetAtPath<Material>(destinationPath);

            if (destination == null)
            {
                Require(AssetDatabase.CopyAsset(sourcePath, destinationPath),
                    $"재질 복제 실패: {sourcePath}");
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
            }

            Require(destination != null,
                $"복제 재질 로드 실패: {destinationPath}");
            ConvertMaterial(source, destination, urpLit);
            EditorUtility.SetDirty(destination);
            result.Add(source, destination);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        return result;
    }

    private static void ConvertMaterial(
        Material source,
        Material destination,
        Shader urpLit)
    {
        Texture baseMap = GetTexture(source, "_MainTex");
        Vector2 baseScale = GetTextureScale(source, "_MainTex");
        Vector2 baseOffset = GetTextureOffset(source, "_MainTex");
        Color baseColor = GetColor(source, "_Color", Color.white);
        Texture normalMap = GetTexture(source, "_BumpMap");
        float normalScale = GetFloat(source, "_BumpScale", 1f);
        Texture metallicMap = GetTexture(source, "_MetallicGlossMap");
        Texture specularMap = GetTexture(source, "_SpecGlossMap");
        Color specularColor = GetColor(source, "_SpecColor", Color.white);
        float metallic = GetFloat(source, "_Metallic", 0f);
        float smoothness = GetFloat(
            source,
            "_Glossiness",
            GetFloat(source, "_Smoothness", 0.5f));
        Texture occlusionMap = GetTexture(source, "_OcclusionMap");
        float occlusionStrength =
            GetFloat(source, "_OcclusionStrength", 1f);
        Texture emissionMap = GetTexture(source, "_EmissionMap");
        Color emissionColor =
            GetColor(source, "_EmissionColor", Color.black);
        Texture detailAlbedoMap = GetTexture(source, "_DetailAlbedoMap");
        Texture detailNormalMap = GetTexture(source, "_DetailNormalMap");
        float detailNormalScale =
            GetFloat(source, "_DetailNormalMapScale", 1f);
        float cutoff = GetFloat(source, "_Cutoff", 0.5f);
        bool specularWorkflow =
            source.shader != null
            && source.shader.name.IndexOf(
                "Specular",
                StringComparison.OrdinalIgnoreCase) >= 0;
        SurfaceKind surfaceKind = GetSurfaceKind(source);
        bool doubleSided = IsDoubleSidedSource(source);

        destination.shader = urpLit;
        destination.SetTexture("_BaseMap", baseMap);
        destination.SetTextureScale("_BaseMap", baseScale);
        destination.SetTextureOffset("_BaseMap", baseOffset);
        destination.SetColor("_BaseColor", baseColor);
        destination.SetFloat("_BumpScale", normalScale);
        destination.SetFloat("_Metallic", metallic);
        destination.SetFloat("_Smoothness", smoothness);
        destination.SetFloat("_OcclusionStrength", occlusionStrength);
        destination.SetFloat("_Cutoff", cutoff);
        destination.SetFloat("_WorkflowMode", specularWorkflow ? 0f : 1f);
        destination.SetColor("_SpecColor", specularColor);

        SetOptionalTexture(destination, "_BumpMap", normalMap, "_NORMALMAP");
        SetOptionalTexture(
            destination,
            "_MetallicGlossMap",
            metallicMap,
            "_METALLICSPECGLOSSMAP");
        if (specularWorkflow)
        {
            SetOptionalTexture(
                destination,
                "_SpecGlossMap",
                specularMap,
                "_METALLICSPECGLOSSMAP");
        }
        SetOptionalTexture(destination, "_OcclusionMap", occlusionMap, null);
        SetOptionalTexture(destination, "_EmissionMap", emissionMap, null);
        if (emissionMap != null || emissionColor.maxColorComponent > 0.0001f)
        {
            destination.SetColor("_EmissionColor", emissionColor);
            destination.EnableKeyword("_EMISSION");
            destination.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        else
        {
            destination.DisableKeyword("_EMISSION");
        }

        SetOptionalTexture(
            destination,
            "_DetailAlbedoMap",
            detailAlbedoMap,
            "_DETAIL_MULX2");
        SetOptionalTexture(
            destination,
            "_DetailNormalMap",
            detailNormalMap,
            "_DETAIL_MULX2");
        destination.SetFloat("_DetailNormalMapScale", detailNormalScale);

        switch (surfaceKind)
        {
            case SurfaceKind.Cutout:
                ConfigureCutout(destination, cutoff);
                break;
            case SurfaceKind.Transparent:
                ConfigureTransparent(destination);
                break;
            default:
                ConfigureOpaque(destination);
                break;
        }

        destination.SetFloat("_Cull", doubleSided
            ? (float)CullMode.Off
            : (float)CullMode.Back);
        destination.doubleSidedGI = doubleSided;
    }

    private static void SetOptionalTexture(
        Material material,
        string property,
        Texture texture,
        string keyword)
    {
        material.SetTexture(property, texture);
        if (string.IsNullOrEmpty(keyword))
            return;

        if (texture != null)
            material.EnableKeyword(keyword);
        else
            material.DisableKeyword(keyword);
    }

    private static void ConfigureOpaque(Material material)
    {
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = -1;
        material.SetOverrideTag("RenderType", "Opaque");
    }

    private static void ConfigureCutout(Material material, float cutoff)
    {
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 1f);
        material.SetFloat("_Cutoff", cutoff);
        material.SetFloat("_ZWrite", 1f);
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.AlphaTest;
        material.SetOverrideTag("RenderType", "TransparentCutout");
    }

    private static void ConfigureTransparent(Material material)
    {
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetOverrideTag("RenderType", "Transparent");
    }

    private static SurfaceKind GetSurfaceKind(Material source)
    {
        if (source.IsKeywordEnabled("_ALPHABLEND_ON")
            || source.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON")
            || source.renderQueue >= (int)RenderQueue.Transparent)
            return SurfaceKind.Transparent;

        if (source.IsKeywordEnabled("_ALPHATEST_ON")
            || (source.renderQueue >= (int)RenderQueue.AlphaTest
                && source.renderQueue < (int)RenderQueue.Transparent))
            return SurfaceKind.Cutout;

        return SurfaceKind.Opaque;
    }

    private static bool IsDoubleSidedSource(Material source)
    {
        return source.shader != null
               && string.Equals(
                   source.shader.name,
                   "2Side",
                   StringComparison.Ordinal);
    }

    private static Texture GetTexture(Material material, string property)
    {
        return material.HasProperty(property)
            ? material.GetTexture(property)
            : null;
    }

    private static Vector2 GetTextureScale(Material material, string property)
    {
        return material.HasProperty(property)
            ? material.GetTextureScale(property)
            : Vector2.one;
    }

    private static Vector2 GetTextureOffset(Material material, string property)
    {
        return material.HasProperty(property)
            ? material.GetTextureOffset(property)
            : Vector2.zero;
    }

    private static float GetFloat(
        Material material,
        string property,
        float fallback)
    {
        return material.HasProperty(property)
            ? material.GetFloat(property)
            : fallback;
    }

    private static Color GetColor(
        Material material,
        string property,
        Color fallback)
    {
        return material.HasProperty(property)
            ? material.GetColor(property)
            : fallback;
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
                    $"원본 프리팹 로드 실패: {sourcePrefabPath}");

                foreach (Renderer renderer in
                         contents.GetComponentsInChildren<Renderer>(true))
                {
                    result.RendererCount++;
                    result.ReplacedSlotCount += ReplaceMaterials(
                        renderer,
                        convertedMaterials);
                }

                GameObject saved =
                    PrefabUtility.SaveAsPrefabAsset(contents, destinationPath);
                Require(saved != null,
                    $"정식 프리팹 저장 실패: {destinationPath}");
                result.PrefabCount++;
            }
            finally
            {
                if (contents != null)
                    PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        return result;
    }

    private static SceneBuildResult UpdateProjectScenes(
        IReadOnlyDictionary<Material, Material> convertedMaterials)
    {
        SceneBuildResult result = new();
        foreach (string scenePath in FindAssetFiles(ProjectSceneRoot, "*.unity"))
        {
            Scene scene =
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Require(scene.IsValid() && scene.isLoaded,
                $"프로젝트 씬 로드 실패: {scenePath}");
            result.ScannedSceneCount++;

            int changedSlots = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in
                         root.GetComponentsInChildren<Renderer>(true))
                {
                    int replaced = ReplaceMaterials(
                        renderer,
                        convertedMaterials);
                    if (replaced <= 0)
                        continue;

                    changedSlots += replaced;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(
                        renderer);
                    EditorUtility.SetDirty(renderer);
                }
            }

            if (changedSlots <= 0)
                continue;

            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene),
                $"씬 저장 실패: {scenePath}");
            result.ChangedSceneCount++;
            result.ReplacedSlotCount += changedSlots;
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

    private static void ValidateMaterials(IEnumerable<Material> materials)
    {
        Material[] array = materials.ToArray();
        Require(array.Length == 112,
            $"변환 재질 수 불일치: {array.Length}");
        foreach (Material material in array)
        {
            Require(material != null, "변환 재질 참조가 null입니다.");
            Require(material.shader != null,
                $"변환 재질 셰이더 누락: {material.name}");
            Require(string.Equals(
                    material.shader.name,
                    "Universal Render Pipeline/Lit",
                    StringComparison.Ordinal),
                $"URP Lit 미변환 재질: {material.name} -> {material.shader.name}");
            Require(!ShaderUtil.ShaderHasError(material.shader),
                $"셰이더 컴파일 오류: {material.name}");
        }
    }

    private static void ValidatePrefabs(
        IEnumerable<string> sourcePrefabPaths,
        IReadOnlyDictionary<Material, Material> convertedMaterials)
    {
        HashSet<Material> sources = new(convertedMaterials.Keys);
        HashSet<Material> destinations = new(convertedMaterials.Values);
        int prefabCount = 0;
        int destinationSlotCount = 0;

        foreach (string sourcePrefabPath in sourcePrefabPaths)
        {
            string destinationPath = GetOutputPrefabPath(sourcePrefabPath);
            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(destinationPath);
            Require(prefab != null,
                $"정식 프리팹 누락: {destinationPath}");
            prefabCount++;

            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material != null,
                        $"정식 프리팹 재질 누락: {destinationPath}"
                        + $" / {GetHierarchyPath(renderer.transform)}");
                    Require(!sources.Contains(material),
                        $"원본 재질 참조 잔존: {destinationPath}"
                        + $" / {material.name}");
                    Require(material.shader != null
                            && !material.shader.name.StartsWith(
                                "Hidden/InternalErrorShader",
                                StringComparison.Ordinal),
                        $"핑크 셰이더 잔존: {destinationPath}"
                        + $" / {material.name}");
                    if (destinations.Contains(material))
                        destinationSlotCount++;
                }
            }
        }

        Require(prefabCount == 90,
            $"정식 프리팹 수 불일치: {prefabCount}");
        Require(destinationSlotCount > 0,
            "정식 프리팹에 변환 재질이 연결되지 않았습니다.");
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
                $"원본 프리팹 로드 실패: {sourcePrefabPath}");
            prefabCount++;

            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    Require(material != null,
                        $"원본 프리팹 재질 누락: {sourcePrefabPath}");
                    Require(material.shader != null
                            && !material.shader.name.StartsWith(
                                "Hidden/InternalErrorShader",
                                StringComparison.Ordinal),
                        $"원본 프리팹 핑크 셰이더 잔존: "
                        + $"{sourcePrefabPath} / {material.name}");
                }
            }
        }

        Require(prefabCount == 90,
            $"원본 프리팹 검증 수 불일치: {prefabCount}");
    }

    private static void ValidateProjectScenes(
        IEnumerable<Material> sourceMaterialEnumerable)
    {
        HashSet<Material> sources = new(sourceMaterialEnumerable);
        foreach (string scenePath in FindAssetFiles(ProjectSceneRoot, "*.unity"))
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
                        Require(!sources.Contains(material),
                            $"프로젝트 씬에 원본 재질 참조 잔존: {scenePath}"
                            + $" / {GetHierarchyPath(renderer.transform)}"
                            + $" / {material.name}");
                    }
                }
            }
        }
    }

    private static string[] FindAssetFiles(string root, string pattern)
    {
        return Directory.GetFiles(
                Path.GetFullPath(root),
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

    private static void ValidateSourcesUnchanged(
        IReadOnlyDictionary<string, string> sourceHashes)
    {
        foreach (KeyValuePair<string, string> pair in sourceHashes)
        {
            Require(string.Equals(
                    pair.Value,
                    ComputeSha256(pair.Key),
                    StringComparison.OrdinalIgnoreCase),
                $"ThirdParty 원본이 변경되었습니다: {pair.Key}");
        }
    }

    private static string GetOutputPrefabPath(string sourcePrefabPath)
    {
        string relative = sourcePrefabPath
            .Substring(SourceRoot.Length)
            .TrimStart('/');
        return $"{OutputPrefabRoot}/{relative}";
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
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(
            File.ReadAllBytes(Path.GetFullPath(assetPath)));
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

    private enum SurfaceKind
    {
        Opaque,
        Cutout,
        Transparent
    }

    private sealed class PrefabBuildResult
    {
        public int PrefabCount;
        public int RendererCount;
        public int ReplacedSlotCount;
    }

    private sealed class SceneBuildResult
    {
        public int ScannedSceneCount;
        public int ChangedSceneCount;
        public int ReplacedSlotCount;
    }
}
