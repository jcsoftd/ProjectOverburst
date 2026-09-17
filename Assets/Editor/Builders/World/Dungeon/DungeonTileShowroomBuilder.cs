using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonTileShowroomBuilder
{
    public const string ShowroomRootName =
        "TileShowroom_EditorOnly";
    public const int AuthoringVersion = 2;

    private const string LogPath =
        "Logs/DungeonTileShowroomAuthoring.log";
    private const float TileGap = 12f;
    private const float RowGap = 18f;
    private const float RoleGap = 48f;
    private static readonly Vector3 ShowroomOrigin =
        new(0f, 0f, -500f);

    private static readonly RoleSpec[] RoleSpecs =
    {
        new(
            "Bridge",
            "01_Bridge",
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath,
            6),
        new(
            "Arena",
            "02_Arena",
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath,
            4),
        new(
            "Start",
            "03_Start",
            DungeonContentAuthoringBuilder.StartTileSetOutputPath,
            3),
        new(
            "Exit",
            "04_Exit",
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath,
            2),
        new(
            "EndCap",
            "05_EndCap",
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath,
            1),
        new(
            "TopologyRepair",
            "06_TopologyRepair",
            string.Empty,
            2,
            true),
    };

    [MenuItem(
        "OVERBURST/Tools/World/Dungeon/Arrange Tile Showroom")]
    public static void BuildFromMenu()
    {
        string report = BuildAndValidate();
        Debug.Log(report);

        DungeonTileShowroomMarker marker =
            UnityEngine.Object.FindFirstObjectByType<
                DungeonTileShowroomMarker>(
                FindObjectsInactive.Include);
        if (marker == null)
            return;

        Selection.activeGameObject = marker.gameObject;
        if (SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.FrameSelected();
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = BuildAndValidate();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(LogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string BuildAndValidate()
    {
        Scene scene = EditorSceneManager.OpenScene(
            DungeonRunSceneAuthoringBuilder.ScenePath,
            OpenSceneMode.Single);
        Require(scene.IsValid() && scene.isLoaded,
            "DungeonRunScene을 열지 못했습니다.");

        DungeonRunFlow flow = FindSingleSceneComponent<
            DungeonRunFlow>(scene);
        Require(flow != null,
            "DungeonRunFlow를 찾지 못했습니다.");

        Transform previousRoot =
            flow.transform.Find(ShowroomRootName);
        if (previousRoot != null)
        {
            Require(
                previousRoot.GetComponent<
                    DungeonTileShowroomMarker>() != null,
                $"{ShowroomRootName}은 빌더 소유 루트가 아닙니다.");
            UnityEngine.Object.DestroyImmediate(
                previousRoot.gameObject);
        }

        GameObject showroomRoot =
            new(ShowroomRootName);
        showroomRoot.tag = "EditorOnly";
        showroomRoot.transform.SetParent(flow.transform, false);
        showroomRoot.transform.localPosition = ShowroomOrigin;
        DungeonTileShowroomMarker marker =
            showroomRoot.AddComponent<DungeonTileShowroomMarker>();

        List<PlacedTile> placedTiles = new();
        HashSet<string> usedPrefabPaths =
            new(StringComparer.OrdinalIgnoreCase);
        List<string> roleReports = new();
        float nextFrontZ = showroomRoot.transform.position.z;

        for (int roleIndex = 0;
             roleIndex < RoleSpecs.Length;
             roleIndex++)
        {
            RoleSpec role = RoleSpecs[roleIndex];
            IReadOnlyList<GameObject> prefabs =
                role.LoadTopologyRepairCandidates
                    ? LoadTopologyRepairPrefabs()
                    : LoadTileSetPrefabs(role.TileSetPath);
            Require(prefabs.Count > 0,
                $"{role.Name} TileSet이 비어 있습니다.");

            GameObject roleRoot =
                new($"{role.HierarchyName}_{prefabs.Count:D2}");
            roleRoot.transform.SetParent(
                showroomRoot.transform,
                false);

            int rowCount = Mathf.CeilToInt(
                prefabs.Count / (float)role.ColumnCount);
            for (int rowIndex = 0;
                 rowIndex < rowCount;
                 rowIndex++)
            {
                int firstIndex = rowIndex * role.ColumnCount;
                int count = Mathf.Min(
                    role.ColumnCount,
                    prefabs.Count - firstIndex);
                GameObject rowRoot =
                    new($"Row_{rowIndex + 1:D2}");
                rowRoot.transform.SetParent(
                    roleRoot.transform,
                    false);

                List<PlacedTile> rowTiles = new();
                for (int columnIndex = 0;
                     columnIndex < count;
                     columnIndex++)
                {
                    GameObject prefab =
                        prefabs[firstIndex + columnIndex];
                    string prefabPath =
                        AssetDatabase.GetAssetPath(prefab);
                    Require(
                        usedPrefabPaths.Add(prefabPath),
                        "여러 역할 TileSet에서 같은 Prefab을 "
                        + $"참조합니다: {prefabPath}");

                    GameObject instance =
                        PrefabUtility.InstantiatePrefab(
                            prefab,
                            rowRoot.transform) as GameObject;
                    Require(instance != null,
                        $"Prefab 배치 실패: {prefabPath}");

                    Vector3 localPosition =
                        instance.transform.localPosition;
                    instance.transform.localPosition =
                        new Vector3(0f, localPosition.y, 0f);
                    Bounds bounds = CalculateVisualBounds(
                        instance);
                    rowTiles.Add(
                        new PlacedTile(
                            prefabPath,
                            instance,
                            bounds));
                }

                float totalWidth =
                    rowTiles.Sum(tile => tile.Bounds.size.x)
                    + TileGap * Mathf.Max(0, rowTiles.Count - 1);
                float rowDepth =
                    rowTiles.Max(tile => tile.Bounds.size.z);
                float cursorX =
                    showroomRoot.transform.position.x
                    - totalWidth * 0.5f;
                float rowCenterZ = nextFrontZ - rowDepth * 0.5f;

                for (int tileIndex = 0;
                     tileIndex < rowTiles.Count;
                     tileIndex++)
                {
                    PlacedTile tile = rowTiles[tileIndex];
                    float targetCenterX =
                        cursorX + tile.Bounds.size.x * 0.5f;
                    Vector3 position =
                        tile.Instance.transform.position;
                    position.x +=
                        targetCenterX - tile.Bounds.center.x;
                    position.z +=
                        rowCenterZ - tile.Bounds.center.z;
                    tile.Instance.transform.position = position;
                    tile.Bounds = CalculateVisualBounds(
                        tile.Instance);
                    placedTiles.Add(tile);
                    cursorX += tile.Bounds.size.x + TileGap;
                }

                nextFrontZ -= rowDepth + RowGap;
            }

            nextFrontZ += RowGap;
            nextFrontZ -= RoleGap;
            roleReports.Add(
                $"{role.Name}={prefabs.Count}");
        }

        marker.Configure(AuthoringVersion, placedTiles.Count);
        EditorUtility.SetDirty(marker);
        ValidateShowroom(flow, placedTiles);

        Require(
            EditorSceneManager.SaveScene(
                scene,
                DungeonRunSceneAuthoringBuilder.ScenePath),
            "DungeonRunScene 저장에 실패했습니다.");
        AssetDatabase.SaveAssets();

        Bounds layoutBounds =
            CombineBounds(placedTiles.Select(tile => tile.Bounds));
        return "[DungeonTileShowroomBuilder] PASS\n"
            + $"TileCount={placedTiles.Count}\n"
            + string.Join("\n", roleReports) + "\n"
            + $"Origin={FormatVector(ShowroomOrigin)}\n"
            + "LayoutSize="
            + FormatVector(layoutBounds.size) + "\n"
            + "PrefabLinks=Preserved\n"
            + "RuntimeGuard=Enabled\n"
            + "OverlapCount=0";
    }

    private static void ValidateShowroom(
        DungeonRunFlow flow,
        IReadOnlyList<PlacedTile> placedTiles)
    {
        Transform root = flow.transform.Find(ShowroomRootName);
        Require(root != null,
            "쇼룸 루트가 생성되지 않았습니다.");
        Require(root.CompareTag("EditorOnly"),
            "쇼룸 루트의 EditorOnly 태그가 누락됐습니다.");

        DungeonTileShowroomMarker marker =
            root.GetComponent<DungeonTileShowroomMarker>();
        Require(marker != null
            && marker.AuthoringVersion == AuthoringVersion
            && marker.TileCount == placedTiles.Count,
            "쇼룸 마커 계약이 올바르지 않습니다.");

        for (int i = 0; i < placedTiles.Count; i++)
        {
            PlacedTile tile = placedTiles[i];
            Require(
                PrefabUtility.IsAnyPrefabInstanceRoot(
                    tile.Instance),
                "Prefab 연결이 풀린 쇼룸 타일입니다: "
                + tile.Instance.name);
            Require(
                AssetDatabase.GetAssetPath(
                    PrefabUtility
                        .GetCorrespondingObjectFromSource(
                            tile.Instance))
                    == tile.PrefabPath,
                "쇼룸 타일의 원본 Prefab 연결이 다릅니다: "
                + tile.Instance.name);

            for (int otherIndex = i + 1;
                 otherIndex < placedTiles.Count;
                 otherIndex++)
            {
                Require(
                    !OverlapsOnGroundPlane(
                        tile.Bounds,
                        placedTiles[otherIndex].Bounds),
                    "쇼룸 타일 Bounds가 겹칩니다: "
                    + $"{tile.Instance.name}, "
                    + placedTiles[otherIndex].Instance.name);
            }
        }
    }

    private static IReadOnlyList<GameObject> LoadTileSetPrefabs(
        string tileSetPath)
    {
        TileSet tileSet =
            AssetDatabase.LoadAssetAtPath<TileSet>(tileSetPath);
        Require(tileSet != null,
            $"TileSet을 찾지 못했습니다: {tileSetPath}");

        SerializedObject serializedTileSet =
            new(tileSet);
        SerializedProperty tileWeights =
            serializedTileSet.FindProperty("TileWeights");
        SerializedProperty weights =
            tileWeights?.FindPropertyRelative("Weights");
        Require(weights != null && weights.isArray,
            $"TileSet 직렬화 계약이 다릅니다: {tileSetPath}");

        List<GameObject> prefabs = new();
        HashSet<string> uniquePaths =
            new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < weights.arraySize; i++)
        {
            SerializedProperty entry =
                weights.GetArrayElementAtIndex(i);
            GameObject prefab =
                entry.FindPropertyRelative("Value")
                    ?.objectReferenceValue as GameObject;
            if (prefab == null)
                continue;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (uniquePaths.Add(path))
                prefabs.Add(prefab);
        }

        prefabs.Sort((left, right) =>
            string.Compare(
                left.name,
                right.name,
                StringComparison.Ordinal));
        return prefabs;
    }

    private static IReadOnlyList<GameObject>
        LoadTopologyRepairPrefabs()
    {
        DungeonTopologyRepairCatalog catalog =
            AssetDatabase.LoadAssetAtPath<
                DungeonTopologyRepairCatalog>(
                DungeonTopologyRepairAuthoringUtility.CatalogPath);
        Require(catalog != null,
            "형태 보정 Catalog를 찾지 못했습니다.");
        return catalog.GetCandidatePrefabs()
            .OrderBy(prefab => prefab.name, StringComparer.Ordinal)
            .ToArray();
    }

    private static Bounds CalculateVisualBounds(
        GameObject instance)
    {
        MeshRenderer[] renderers =
            instance.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return EnsureMinimumFootprint(bounds);
        }

        Collider[] colliders =
            instance.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                bounds.Encapsulate(colliders[i].bounds);
            return EnsureMinimumFootprint(bounds);
        }

        return new Bounds(
            instance.transform.position,
            new Vector3(1f, 1f, 1f));
    }

    private static Bounds EnsureMinimumFootprint(Bounds bounds)
    {
        Vector3 size = bounds.size;
        size.x = Mathf.Max(1f, size.x);
        size.z = Mathf.Max(1f, size.z);
        bounds.size = size;
        return bounds;
    }

    private static bool OverlapsOnGroundPlane(
        Bounds left,
        Bounds right)
    {
        const float tolerance = 0.01f;
        return left.min.x < right.max.x - tolerance
            && left.max.x > right.min.x + tolerance
            && left.min.z < right.max.z - tolerance
            && left.max.z > right.min.z + tolerance;
    }

    private static Bounds CombineBounds(
        IEnumerable<Bounds> source)
    {
        using IEnumerator<Bounds> enumerator =
            source.GetEnumerator();
        Require(enumerator.MoveNext(),
            "배치 Bounds가 비어 있습니다.");
        Bounds result = enumerator.Current;
        while (enumerator.MoveNext())
            result.Encapsulate(enumerator.Current);
        return result;
    }

    private static T FindSingleSceneComponent<T>(Scene scene)
        where T : Component
    {
        T[] components = scene.GetRootGameObjects()
            .SelectMany(root =>
                root.GetComponentsInChildren<T>(true))
            .Where(component =>
                component.gameObject.scene == scene)
            .ToArray();
        Require(
            components.Length == 1,
            $"{typeof(T).Name} count={components.Length}");
        return components[0];
    }

    private static string FormatVector(Vector3 value)
    {
        return $"({value.x:F1}, {value.y:F1}, {value.z:F1})";
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class RoleSpec
    {
        public RoleSpec(
            string name,
            string hierarchyName,
            string tileSetPath,
            int columnCount,
            bool loadTopologyRepairCandidates = false)
        {
            Name = name;
            HierarchyName = hierarchyName;
            TileSetPath = tileSetPath;
            ColumnCount = Mathf.Max(1, columnCount);
            LoadTopologyRepairCandidates =
                loadTopologyRepairCandidates;
        }

        public string Name { get; }
        public string HierarchyName { get; }
        public string TileSetPath { get; }
        public int ColumnCount { get; }
        public bool LoadTopologyRepairCandidates { get; }
    }

    private sealed class PlacedTile
    {
        public PlacedTile(
            string prefabPath,
            GameObject instance,
            Bounds bounds)
        {
            PrefabPath = prefabPath;
            Instance = instance;
            Bounds = bounds;
        }

        public string PrefabPath { get; }
        public GameObject Instance { get; }
        public Bounds Bounds { get; set; }
    }
}
