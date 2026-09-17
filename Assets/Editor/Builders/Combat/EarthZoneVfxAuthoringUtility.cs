#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using InvalidOperationException = System.InvalidOperationException;

[InitializeOnLoad]
public static class EarthZoneVfxAuthoringUtility
{
    private const string Root = "Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs/EarthZones";
    private const string MaterialPath = Root + "/MAT_EarthZone_TEMP_RANGE_GUIDE.mat";
    private const string CatalogPath = "Assets/ProjectOverburst/Resources/Combat/VFX/EarthZoneVfxCatalog.asset";
    private const string LegacyRoot = "Assets/Prefabs/VFX/Combat/ElementalReaction/EarthZones";
    private const string LegacyCatalogPath = "Assets/Resources/Combat/VFX/EarthZoneVfxCatalog.asset";

    private readonly struct Definition
    {
        public readonly EarthZoneVfxKind Kind;
        public readonly string Folder;
        public readonly string FileName;
        public readonly float Radius;
        public readonly float Lifetime;
        public readonly int Capacity;

        public Definition(EarthZoneVfxKind kind, string folder, string fileName, float radius, float lifetime, int capacity)
        {
            Kind = kind;
            Folder = folder;
            FileName = fileName;
            Radius = radius;
            Lifetime = lifetime;
            Capacity = capacity;
        }
    }

    private static readonly Definition[] Definitions =
    {
        new Definition(EarthZoneVfxKind.BasicEarthZone, "BasicEarthZone", "PF_VFX_EarthZone_Basic.prefab", 1.75f, 6f, 12),
        new Definition(EarthZoneVfxKind.LavaEruption, "LavaEruption", "PF_VFX_EarthZone_LavaEruption.prefab", 1.75f, 6f, 12),
        new Definition(EarthZoneVfxKind.Mud, "Mud", "PF_VFX_EarthZone_Mud.prefab", 1.75f, 6f, 12),
        new Definition(EarthZoneVfxKind.Crystallization, "Crystallization", "PF_VFX_EarthZone_Crystallization.prefab", 1.75f, 6f, 12),
        new Definition(EarthZoneVfxKind.MagneticField, "MagneticField", "PF_VFX_EarthZone_MagneticField.prefab", 1.75f, 6f, 12),
        new Definition(EarthZoneVfxKind.CrystalPending, "Crystallization", "PF_VFX_EarthZone_CrystalPending.prefab", 0f, 2f, 36),
        new Definition(EarthZoneVfxKind.CrystalExplosion, "Crystallization", "PF_VFX_EarthZone_CrystalExplosion.prefab", 0.9f, 1f, 36)
    };

    static EarthZoneVfxAuthoringUtility()
    {
        EditorApplication.delayCall += EnsureMissingAuthoringAssets;
    }

    [MenuItem("Tools/ProjectVTP/Combat/Create Earth Zone VFX Wrappers")]
    public static void Run()
    {
        MigrateLegacyAssets();
        EnsureFolder(Root);
        Material guideMaterial = EnsureGuideMaterial();
        List<EarthZoneVfxCatalogEntry> entries = new List<EarthZoneVfxCatalogEntry>(Definitions.Length);

        for (int i = 0; i < Definitions.Length; i++)
        {
            Definition definition = Definitions[i];
            string folder = Root + "/" + definition.Folder;
            EnsureFolder(folder);
            string path = folder + "/" + definition.FileName;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null && prefab.GetComponent<EarthZoneVfxAuthoring>() == null)
            {
                AssetDatabase.DeleteAsset(path); // 분리 전 MonoScript GUID wrapper만 재작성
                prefab = null;
            }
            if (prefab == null)
                prefab = CreateWrapper(definition, path, guideMaterial);
            entries.Add(new EarthZoneVfxCatalogEntry(definition.Kind, prefab));
        }

        EnsureFolder("Assets/ProjectOverburst/Resources/Combat/VFX");
        EarthZoneVfxCatalog catalog = AssetDatabase.LoadAssetAtPath<EarthZoneVfxCatalog>(CatalogPath);
        if (catalog == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(CatalogPath) != null)
                AssetDatabase.DeleteAsset(CatalogPath);
            catalog = ScriptableObject.CreateInstance<EarthZoneVfxCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.ReplaceEntries(entries);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EarthZoneVfxAuthoringUtility] Earth wrapper 7개와 catalog를 생성/검증했습니다.");
    }

    public static void RunOnceFromCommandLine()
    {
        Run();
    }

    private static void EnsureMissingAuthoringAssets()
    {
        if (AssetDatabase.IsValidFolder(LegacyRoot)
            || AssetDatabase.LoadMainAssetAtPath(LegacyCatalogPath) != null
            || AssetDatabase.LoadAssetAtPath<EarthZoneVfxCatalog>(CatalogPath) == null)
            Run();
    }

    private static void MigrateLegacyAssets()
    {
        EnsureFolder("Assets/ProjectOverburst/02_Shared/Combat/ElementalReactions/VFX/Prefabs");
        EnsureFolder("Assets/ProjectOverburst/Resources/Combat/VFX");

        if (AssetDatabase.IsValidFolder(LegacyRoot) && !AssetDatabase.IsValidFolder(Root))
            MoveAssetOrThrow(LegacyRoot, Root);

        if (AssetDatabase.LoadMainAssetAtPath(LegacyCatalogPath) != null
            && AssetDatabase.LoadMainAssetAtPath(CatalogPath) == null)
        {
            MoveAssetOrThrow(LegacyCatalogPath, CatalogPath);
        }

        if (AssetDatabase.IsValidFolder(LegacyRoot))
        {
            for (int i = 0; i < Definitions.Length; i++)
            {
                Definition definition = Definitions[i];
                string relativePath = definition.Folder + "/" + definition.FileName;
                MoveLegacyAssetIfDestinationMissing(
                    LegacyRoot + "/" + relativePath,
                    Root + "/" + relativePath);
            }
            MoveLegacyAssetIfDestinationMissing(
                LegacyRoot + "/MAT_EarthZone_TEMP_RANGE_GUIDE.mat",
                MaterialPath);
            DeleteFolderIfEmpty(LegacyRoot);
        }

        if (AssetDatabase.LoadMainAssetAtPath(LegacyCatalogPath) != null
            && AssetDatabase.LoadMainAssetAtPath(CatalogPath) != null)
        {
            AssetDatabase.DeleteAsset(LegacyCatalogPath);
        }
    }

    private static void MoveLegacyAssetIfDestinationMissing(string sourcePath, string destinationPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(sourcePath) == null
            || AssetDatabase.LoadMainAssetAtPath(destinationPath) != null)
        {
            return;
        }

        string destinationFolder = System.IO.Path.GetDirectoryName(destinationPath)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(destinationFolder))
            EnsureFolder(destinationFolder);
        MoveAssetOrThrow(sourcePath, destinationPath);
    }

    private static void MoveAssetOrThrow(string sourcePath, string destinationPath)
    {
        string error = AssetDatabase.MoveAsset(sourcePath, destinationPath);
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException(
                $"Earth VFX asset migration 실패: {sourcePath} -> {destinationPath}: {error}");
    }

    private static void DeleteFolderIfEmpty(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath)
            || AssetDatabase.GetSubFolders(folderPath).Length > 0
            || AssetDatabase.FindAssets("t:Object", new[] { folderPath }).Length > 0)
        {
            return;
        }

        AssetDatabase.DeleteAsset(folderPath);
    }

    private static GameObject CreateWrapper(Definition definition, string path, Material guideMaterial)
    {
        GameObject root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
        try
        {
            EarthZoneVfxAuthoring authoring = root.AddComponent<EarthZoneVfxAuthoring>();
            authoring.Configure(definition.Kind, definition.Radius, definition.Lifetime, definition.Capacity);

            GameObject content = new GameObject("VFX_CONTENT");
            content.transform.SetParent(root.transform, false);

            if (definition.Kind == EarthZoneVfxKind.CrystalPending)
                CreateCrystalGuide(root.transform, guideMaterial);
            else
                CreateRangeGuide(root.transform, guideMaterial, definition.Radius);

            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void CreateRangeGuide(Transform parent, Material material, float radius)
    {
        GameObject guide = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        guide.name = "TEMP_RANGE_GUIDE";
        guide.transform.SetParent(parent, false);
        guide.transform.localPosition = new Vector3(0f, 0.025f, 0f);
        guide.transform.localScale = new Vector3(radius * 2f, 0.025f, radius * 2f);
        Object.DestroyImmediate(guide.GetComponent<Collider>());
        guide.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void CreateCrystalGuide(Transform parent, Material material)
    {
        GameObject guide = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        guide.name = "TEMP_CRYSTAL_GUIDE";
        guide.transform.SetParent(parent, false);
        guide.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        guide.transform.localScale = new Vector3(0.35f, 0.55f, 0.35f);
        guide.transform.localRotation = Quaternion.Euler(0f, 0f, 12f);
        Object.DestroyImmediate(guide.GetComponent<Collider>());
        guide.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static Material EnsureGuideMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "MAT_EarthZone_TEMP_RANGE_GUIDE" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        material.color = new Color(0.45f, 0.3f, 0.12f, 0.28f);
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetOverrideTag("RenderType", "Transparent");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
