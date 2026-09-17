using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonPortalAuthoringBuilder
{
    public const string EntryPrefabPath =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Prefabs/Portals/"
        + "PF_DungeonPortalEntry.prefab";
    public const string ExitPrefabPath =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Prefabs/Portals/"
        + "PF_DungeonPortalExit.prefab";

    private const string SourcePortalPrefabPath =
        "Assets/ProjectOverburst/01_Core/SceneFlow/Prefabs/"
        + "PF_HubScenePortal.prefab";
    private const string HideoutScenePath =
        "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    private const string EntryObjectName =
        "DungeonPortal_ToMultistory";
    private const string ReturnPointObjectName =
        "HubReturnPoint_DungeonPortal";
    private const string LogPath =
        "Logs/DungeonPortalAuthoring.log";
    private const float PortalDistanceFromSpawn = 4.8f;
    private const float ReturnPointDistanceFromPortal = 2.15f;

    private readonly struct PromptReferences
    {
        public PromptReferences(
            GameObject root,
            TMP_Text text,
            Transform facingRoot)
        {
            Root = root;
            Text = text;
            FacingRoot = facingRoot;
        }

        public GameObject Root { get; }
        public TMP_Text Text { get; }
        public Transform FacingRoot { get; }
    }

    [MenuItem(
        "OVERBURST/Codex/Setup/World/Dungeon/"
        + "Build Hideout Dungeon Portal")]
    public static void BuildFromMenu()
    {
        Debug.Log(BuildAndValidate());
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
        EnsureFolder(
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Prefabs/Portals");
        GameObject entryPrefab = BuildEntryPrefab();
        GameObject exitPrefab = BuildExitPrefab();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);

        string runSceneReport =
            DungeonRunSceneAuthoringBuilder.BuildAndValidate();
        Scene hideoutScene = EditorSceneManager.OpenScene(
            HideoutScenePath,
            OpenSceneMode.Single);
        PlaceHideoutPortal(
            hideoutScene,
            entryPrefab);
        Require(
            EditorSceneManager.SaveScene(
                hideoutScene,
                HideoutScenePath),
            "HideoutScene 던전 포탈 저장 실패");
        AssetDatabase.SaveAssets();

        string validation = ValidateOrThrow();
        return "[DungeonPortalAuthoringBuilder] 완료\n"
            + $"EntryPrefab={EntryPrefabPath}\n"
            + $"ExitPrefab={ExitPrefabPath}\n"
            + runSceneReport + "\n"
            + validation;
    }

    public static string ValidateOrThrow()
    {
        GameObject entryPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                EntryPrefabPath);
        GameObject exitPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                ExitPrefabPath);
        Require(entryPrefab != null, "던전 진입 포탈 프리팹 누락");
        Require(exitPrefab != null, "던전 출구 포탈 프리팹 누락");
        ValidatePortalPrefab(entryPrefab, true);
        ValidatePortalPrefab(exitPrefab, false);
        ValidateKoreanPromptFont(entryPrefab);
        ValidateKoreanPromptFont(exitPrefab);

        Scene scene = EditorSceneManager.OpenScene(
            HideoutScenePath,
            OpenSceneMode.Single);
        DungeonPortalEntry[] entries =
            FindComponentsInScene<DungeonPortalEntry>(scene);
        Require(entries.Length == 1,
            $"Hideout DungeonPortalEntry 수={entries.Length}");
        DungeonPortalEntry entry = entries[0];
        Require(entry.name == EntryObjectName,
            "Hideout 던전 포탈 이름 불일치");
        Require(
            entry.ReturnPointId
                == DungeonPortalEntry.DefaultReturnPointId,
            "DungeonPortalEntry 복귀 ID 불일치");
        Require(entry.PromptRoot != null,
            "DungeonPortalEntry Prompt Root 누락");
        Require(
            entry.transform.parent != null
            && GetHierarchyPath(entry.transform.parent)
                == "HubPortals",
            "DungeonPortalEntry 부모 경로 불일치");
        Require(
            PrefabUtility.GetPrefabInstanceStatus(entry.gameObject)
                == PrefabInstanceStatus.Connected,
            "DungeonPortalEntry 씬 인스턴스 연결 해제");

        HubReturnPoint[] points =
            FindComponentsInScene<HubReturnPoint>(scene);
        HubReturnPoint dungeonPoint = points.SingleOrDefault(point =>
            point != null
            && point.gameObject.scene == scene
            && point.ReturnPointId
                == DungeonPortalEntry.DefaultReturnPointId);
        Require(dungeonPoint != null,
            "DungeonPortal HubReturnPoint 누락");
        Require(
            Vector3.Distance(
                new Vector3(
                    entry.transform.position.x,
                    0f,
                    entry.transform.position.z),
                new Vector3(
                    dungeonPoint.transform.position.x,
                    0f,
                    dungeonPoint.transform.position.z))
                <= entry.InteractionRadius,
            "복귀 지점이 던전 포탈 상호작용 범위 밖");
        Require(
            HasSceneGroundBelow(entry.transform.position, scene),
            "DungeonPortalEntry 아래 Ground 누락");
        Require(
            HasSceneGroundBelow(dungeonPoint.transform.position, scene),
            "DungeonPortal HubReturnPoint 아래 Ground 누락");

        int missingScriptCount =
            GameObjectUtility
                .GetMonoBehavioursWithMissingScriptCount(
                    entry.gameObject)
            + GameObjectUtility
                .GetMonoBehavioursWithMissingScriptCount(
                    dungeonPoint.gameObject);
        Require(missingScriptCount == 0,
            $"Dungeon portal authored objects Missing Script="
            + missingScriptCount);

        return "[DungeonPortalAuthoringValidator] PASS\n"
            + "HideoutEntryCount=1\n"
            + "DungeonReturnPointCount=1\n"
            + "NestedHubPortalPrefab=2\n"
            + "MissingScriptCount=0";
    }

    private static GameObject BuildEntryPrefab()
    {
        GameObject root = BuildPortalWrapper(
            "PF_DungeonPortalEntry",
            out PromptReferences prompt);
        try
        {
            DungeonPortalEntry entry =
                root.AddComponent<DungeonPortalEntry>();
            entry.ConfigureAuthoring(
                DungeonPortalEntry.DefaultReturnPointId,
                2.8f,
                "F : 다층 던전 진입",
                prompt.Root,
                prompt.Text,
                prompt.FacingRoot);
            EditorUtility.SetDirty(entry);
            return SaveAndReloadPrefab(root, EntryPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static GameObject BuildExitPrefab()
    {
        GameObject root = BuildPortalWrapper(
            "PF_DungeonPortalExit",
            out PromptReferences prompt);
        try
        {
            DungeonPortalExit exit =
                root.AddComponent<DungeonPortalExit>();
            exit.ConfigureAuthoring(
                2.8f,
                "F : 하이드아웃으로 복귀",
                prompt.Root,
                prompt.Text,
                prompt.FacingRoot);
            EditorUtility.SetDirty(exit);
            return SaveAndReloadPrefab(root, ExitPrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateKoreanPromptFont(GameObject root)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        Require(texts.Length > 0, "포탈 안내 텍스트 누락");
        const string required = "F : 다층 던전 진입 하이드아웃으로 복귀";
        foreach (TMP_Text text in texts)
        {
            Require(text.font != null, "포탈 안내 폰트 누락");
            foreach (char character in required)
                Require(char.IsWhiteSpace(character) || text.font.HasCharacter(character),
                    "포탈 안내 폰트 글리프 누락: " + character);
        }
    }

    private static GameObject BuildPortalWrapper(
        string name,
        out PromptReferences prompt)
    {
        GameObject source =
            AssetDatabase.LoadAssetAtPath<GameObject>(
                SourcePortalPrefabPath);
        Require(source != null,
            "PF_HubScenePortal 원본 누락");

        GameObject root = new(name);
        GameObject visual = PrefabUtility.InstantiatePrefab(source)
            as GameObject;
        Require(visual != null,
            "PF_HubScenePortal 중첩 인스턴스 생성 실패");
        visual.name = "PortalVisual";
        visual.transform.SetParent(root.transform, false);

        HubScenePortal hubPortal =
            visual.GetComponent<HubScenePortal>();
        Require(hubPortal != null,
            "PF_HubScenePortal HubScenePortal 누락");
        SerializedObject serializedHub =
            new(hubPortal);
        GameObject promptRoot =
            serializedHub.FindProperty("promptRoot")
                .objectReferenceValue as GameObject;
        TMP_Text promptText =
            serializedHub.FindProperty("promptText")
                .objectReferenceValue as TMP_Text;
        Transform promptFacingRoot =
            serializedHub.FindProperty("promptFacingRoot")
                .objectReferenceValue as Transform;
        Require(promptRoot != null
            && promptText != null
            && promptFacingRoot != null,
            "PF_HubScenePortal Prompt 참조 누락");
        hubPortal.enabled = false;
        EditorUtility.SetDirty(hubPortal);
        prompt = new PromptReferences(
            promptRoot,
            promptText,
            promptFacingRoot);
        return root;
    }

    private static GameObject SaveAndReloadPrefab(
        GameObject root,
        string path)
    {
        GameObject saved =
            PrefabUtility.SaveAsPrefabAsset(root, path);
        Require(saved != null, "포탈 프리팹 저장 실패: " + path);
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    private static void PlaceHideoutPortal(Scene scene, GameObject entryPrefab)
    {
        Transform portalsRoot = FindTransform(scene, "HubPortals"); // 하이드아웃 정식 포탈 루트
        Transform spawn = FindTransform(scene, "HubReturnPoint_Default");
        Require(portalsRoot != null && spawn != null, "Hideout 포탈 루트 또는 기본 복귀 지점 누락");
        Transform existing = FindDirectChildrenByName(portalsRoot, EntryObjectName).SingleOrDefault();
        Vector3 candidate = existing != null ? existing.position : spawn.position + new Vector3(-4f, 0f, -4f);
        Physics.SyncTransforms();
        Require(TrySnapToSceneGround(scene, candidate, spawn.position.y, 0.06f, out Vector3 portalPosition), "Hideout 포탈 Ground 누락");
        Vector3 direction = spawn.position - portalPosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
        direction.Normalize();
        GameObject portal = GetOrCreatePortalInstance(scene, portalsRoot, entryPrefab);
        portal.transform.SetPositionAndRotation(portalPosition, Quaternion.LookRotation(direction));
        Require(TrySnapToSceneGround(scene, portalPosition + direction * ReturnPointDistanceFromPortal, spawn.position.y, 0.12f, out Vector3 returnPosition), "Hideout 던전 복귀 Ground 누락");
        GameObject returnObject = GetOrCreateReturnPoint(scene, portalsRoot);
        returnObject.transform.SetPositionAndRotation(returnPosition, Quaternion.LookRotation(direction));
        HubReturnPoint point = returnObject.GetComponent<HubReturnPoint>();
        if (point == null) point = returnObject.AddComponent<HubReturnPoint>();
        var serializedPoint = new SerializedObject(point);
        serializedPoint.FindProperty("returnPointId").stringValue = DungeonPortalEntry.DefaultReturnPointId;
        serializedPoint.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(returnObject);
        Physics.SyncTransforms();
    }

    private static GameObject GetOrCreatePortalInstance(
        Scene scene,
        Transform parent,
        GameObject entryPrefab)
    {
        List<Transform> matches = FindDirectChildrenByName(
            parent,
            EntryObjectName);
        GameObject keep = null;
        for (int i = 0; i < matches.Count; i++)
        {
            GameObject candidate = matches[i].gameObject;
            GameObject source =
                PrefabUtility.GetCorrespondingObjectFromSource(
                    candidate);
            if (keep == null
                && candidate.GetComponent<DungeonPortalEntry>()
                    != null
                && source == entryPrefab)
            {
                keep = candidate;
                continue;
            }

            UnityEngine.Object.DestroyImmediate(candidate);
        }

        if (keep != null)
            return keep;

        keep = PrefabUtility.InstantiatePrefab(
            entryPrefab,
            scene) as GameObject;
        Require(keep != null,
            "Hideout 던전 포탈 인스턴스 생성 실패");
        keep.name = EntryObjectName;
        keep.transform.SetParent(parent, true);
        return keep;
    }

    private static GameObject GetOrCreateReturnPoint(
        Scene scene,
        Transform parent)
    {
        List<Transform> matches = FindDirectChildrenByName(
            parent,
            ReturnPointObjectName);
        GameObject keep = null;
        for (int i = 0; i < matches.Count; i++)
        {
            GameObject candidate = matches[i].gameObject;
            if (keep == null
                && candidate.GetComponent<HubReturnPoint>() != null)
            {
                keep = candidate;
                continue;
            }

            UnityEngine.Object.DestroyImmediate(candidate);
        }

        if (keep != null)
            return keep;

        keep = new GameObject(ReturnPointObjectName);
        SceneManager.MoveGameObjectToScene(keep, scene);
        keep.transform.SetParent(parent, true);
        keep.AddComponent<HubReturnPoint>();
        return keep;
    }

    private static List<Transform> FindDirectChildrenByName(
        Transform parent,
        string objectName)
    {
        List<Transform> results = new();
        if (parent == null)
            return results;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == objectName)
                results.Add(child);
        }

        return results;
    }

    private static Vector3 ResolvePortalPosition(
        Scene scene,
        Vector3 spawnPosition,
        Vector3 hideoutPortalPosition)
    {
        Vector3 away =
            spawnPosition - hideoutPortalPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.001f)
            away = Vector3.left;
        away.Normalize();

        float[] angles = { 0f, 25f, -25f, 50f, -50f, 75f, -75f };
        for (int i = 0; i < angles.Length; i++)
        {
            Vector3 direction =
                Quaternion.Euler(0f, angles[i], 0f) * away;
            Vector3 candidate =
                spawnPosition
                + direction * PortalDistanceFromSpawn;
            if (!TrySnapToSceneGround(
                    scene,
                    candidate,
                    hideoutPortalPosition.y,
                    0.04f,
                    out Vector3 snapped))
            {
                continue;
            }

            Vector2 snappedXz =
                new(snapped.x, snapped.z);
            Vector2 hideoutXz =
                new(
                    hideoutPortalPosition.x,
                    hideoutPortalPosition.z);
            if (Vector2.Distance(snappedXz, hideoutXz) >= 3.5f)
                return snapped;
        }

        throw new InvalidOperationException(
            "PL 근처 던전 포탈용 안전 Ground 후보를 찾지 못했습니다.");
    }

    private static bool TrySnapToSceneGround(
        Scene scene,
        Vector3 candidate,
        float preferredY,
        float surfaceOffset,
        out Vector3 snapped)
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        int groundMask = groundLayer >= 0
            ? 1 << groundLayer
            : Physics.DefaultRaycastLayers;
        RaycastHit[] hits = Physics.RaycastAll(
            candidate + Vector3.up * 60f,
            Vector3.down,
            140f,
            groundMask,
            QueryTriggerInteraction.Ignore);

        bool found = false;
        RaycastHit best = default;
        float bestScore = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null
                || hit.collider.gameObject.scene != scene
                || Vector3.Dot(hit.normal, Vector3.up) < 0.55f)
            {
                continue;
            }

            float score = Mathf.Abs(hit.point.y - preferredY);
            if (!found || score < bestScore)
            {
                found = true;
                best = hit;
                bestScore = score;
            }
        }

        if (!found)
        {
            snapped = default;
            return false;
        }

        snapped = new Vector3(
            candidate.x,
            best.point.y + surfaceOffset,
            candidate.z);
        return true;
    }

    private static bool HasSceneGroundBelow(
        Vector3 position,
        Scene scene)
    {
        return TrySnapToSceneGround(
            scene,
            position,
            position.y,
            0f,
            out _);
    }

    private static void ValidatePortalPrefab(
        GameObject prefab,
        bool entry)
    {
        Require(
            GameObjectUtility
                .GetMonoBehavioursWithMissingScriptCount(prefab) == 0,
            prefab.name + " Missing Script");
        HubScenePortal nestedHub =
            prefab.GetComponentInChildren<HubScenePortal>(true);
        Require(nestedHub != null && !nestedHub.enabled,
            prefab.name + " 중첩 HubScenePortal 비활성화 누락");
        Require(
            PrefabUtility.GetCorrespondingObjectFromSource(
                nestedHub.gameObject) != null,
            prefab.name + " 중첩 원본 프리팹 연결 해제");
        Require(
            entry
                ? prefab.GetComponent<DungeonPortalEntry>() != null
                : prefab.GetComponent<DungeonPortalExit>() != null,
            prefab.name + " 던전 포탈 컴포넌트 누락");
    }

    private static void DestroyNamedChild(
        Transform parent,
        string objectName)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child != null && child.name == objectName)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }

    private static Transform FindTransform(
        Scene scene,
        string path)
    {
        string[] segments =
            path.Split(
                new[] { '/' },
                StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        Transform current = null;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == segments[0])
            {
                current = roots[i].transform;
                break;
            }
        }

        for (int i = 1; current != null && i < segments.Length; i++)
            current = current.Find(segments[i]);
        return current;
    }

    private static Transform FindDescendantByName(
        Transform root,
        string name)
    {
        if (root == null)
            return null;
        Transform[] transforms =
            root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null
                && transforms[i].name == name)
            {
                return transforms[i];
            }
        }

        return null;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        List<string> names = new();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }
        names.Reverse();
        return string.Join("/", names);
    }

    private static T[] FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        List<T> results = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        return results.ToArray();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent =
            Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        Require(
            !string.IsNullOrWhiteSpace(parent),
            "폴더 부모 경로 누락: " + folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
