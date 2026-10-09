using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstMinimapTerrainBuilder
{
    const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstWorldMinimap_Rpg11.prefab";
    const string MapRoot = "Assets/ProjectOverburst/Resources/UI/Minimap/Terrain";
    static readonly string[] HubScenes = { "MainScene", "HideoutScene" };

    [MenuItem("OVERBURST/UI/Bake Terrain Minimap")]
    public static void Run() => Debug.Log(Apply());

    public static string Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Terrain minimap baking requires an idle Editor.");
        EditorSceneSafety.RequireNoUnsavedScenes("Bake Terrain Minimap");
        if (LayerMask.NameToLayer("Ground") < 0) throw new InvalidOperationException("Ground layer is missing.");
        string backup = Path.GetFullPath("../개인파일/코덱스산출/UI/MinimapTerrainBake/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "/Before");
        Directory.CreateDirectory(backup);
        Backup(PrefabPath, backup);
        EnsureFolder(MapRoot);
        var maps = new WorldMinimapController.TerrainMap[HubScenes.Length];
        for (int i = 0; i < HubScenes.Length; i++) maps[i] = BakeHub(HubScenes[i], backup);

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var view = root.GetComponent<MinimapView>();
            var controller = root.GetComponent<WorldMinimapController>();
            if (view == null || controller == null) throw new InvalidOperationException("Production minimap components are missing.");
            var viewSo = new SerializedObject(view);
            var viewport = (RectTransform)viewSo.FindProperty("viewport").objectReferenceValue;
            if (viewport == null) throw new InvalidOperationException("Production minimap viewport is missing.");
            Transform existing = viewport.Find("TerrainMap");
            RawImage image;
            if (existing == null)
            {
                var go = new GameObject("TerrainMap", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                go.layer = viewport.gameObject.layer;
                go.transform.SetParent(viewport, false);
                image = go.GetComponent<RawImage>();
            }
            else
            {
                image = existing.GetComponent<RawImage>();
                if (image == null) throw new InvalidOperationException("Existing TerrainMap has an unexpected component layout.");
            }
            RectTransform rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.SetAsFirstSibling();
            image.raycastTarget = false;
            image.maskable = true;
            image.color = Color.white;
            image.texture = null;
            image.gameObject.SetActive(false);
            viewSo.FindProperty("terrainImage").objectReferenceValue = image;
            viewSo.ApplyModifiedPropertiesWithoutUndo();
            var controllerSo = new SerializedObject(controller);
            SerializedProperty entries = controllerSo.FindProperty("terrainMaps");
            entries.arraySize = maps.Length;
            for (int i = 0; i < maps.Length; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("sceneName").stringValue = maps[i].sceneName;
                entry.FindPropertyRelative("texture").objectReferenceValue = maps[i].texture;
                entry.FindPropertyRelative("worldBounds").rectValue = maps[i].worldBounds;
            }
            controllerSo.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        return "APPLIED: baked MainScene/HideoutScene terrain and connected one RawImage. Backup: " + backup;
    }

    static WorldMinimapController.TerrainMap BakeHub(string sceneName, string backup)
    {
        string scenePath = "Assets/ProjectOverburst/00_Scenes/" + sceneName + ".unity";
        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        Scene active = SceneManager.GetActiveScene();
        Texture2D texture = null;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            Physics.SyncTransforms();
            int groundMask = LayerMask.GetMask("Ground");
            var grounds = new HashSet<Collider>();
            var obstacles = new HashSet<Collider>();
            Bounds bounds = default;
            bool found = false;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || !collider.gameObject.activeInHierarchy || collider.isTrigger
                    || collider.GetComponentInParent<PlayerActorRuntime>() != null
                    || collider.GetComponentInParent<EnemyRank>() != null
                    || collider is CharacterController || IsEditorOnly(collider.transform)) continue;
                if (collider.attachedRigidbody != null && !collider.attachedRigidbody.isKinematic) continue;
                if ((groundMask & (1 << collider.gameObject.layer)) != 0)
                {
                    grounds.Add(collider);
                    if (!found) { bounds = collider.bounds; found = true; }
                    else bounds.Encapsulate(collider.bounds);
                }
                else obstacles.Add(collider);
            }
            if (!found || bounds.size.x < 1f || bounds.size.z < 1f)
                throw new InvalidOperationException(sceneName + " has no usable Ground collider footprint.");
            float margin = Mathf.Max(2f, Mathf.Max(bounds.size.x, bounds.size.z) / 512f * 2f);
            Rect footprint = new Rect(bounds.min.x - margin, bounds.min.z - margin,
                bounds.size.x + margin * 2f, bounds.size.z + margin * 2f);
            float sample = Mathf.Max(.5f, Mathf.Max(footprint.width, footprint.height) / 512f);
            int width = Mathf.Clamp(Mathf.CeilToInt(footprint.width / sample), 2, 512);
            int height = Mathf.Clamp(Mathf.CeilToInt(footprint.height / sample), 2, 512);
            var cells = new byte[width * height];
            var hits = new RaycastHit[64];
            var overlaps = new Collider[64];
            float castY = bounds.max.y + 20f, castDistance = bounds.size.y + 40f;
            int walkableCount = 0;
            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                // A transparent outer texel prevents Clamp from smearing terrain outside the map.
                if (x == 0 || z == 0 || x == width - 1 || z == height - 1) continue;
                var position = new Vector3(footprint.xMin + (x + .5f) * footprint.width / width,
                    castY, footprint.yMin + (z + .5f) * footprint.height / height);
                int count = Physics.RaycastNonAlloc(position, Vector3.down, hits, castDistance, groundMask, QueryTriggerInteraction.Ignore);
                if (count == hits.Length) throw new InvalidOperationException("Ground ray buffer saturated; bake aborted.");
                int nearest = -1;
                float distance = float.PositiveInfinity;
                for (int j = 0; j < count; j++)
                    if (grounds.Contains(hits[j].collider) && hits[j].distance < distance)
                    { nearest = j; distance = hits[j].distance; }
                if (nearest < 0) continue;
                RaycastHit hit = hits[nearest];
                bool blocked = hit.normal.y < .65f;
                if (!blocked)
                {
                    count = Physics.OverlapBoxNonAlloc(hit.point + Vector3.up * .9f,
                        new Vector3(.2f, .55f, .2f), overlaps, Quaternion.identity, ~groundMask, QueryTriggerInteraction.Ignore);
                    if (count == overlaps.Length) throw new InvalidOperationException("Obstacle buffer saturated; bake aborted.");
                    for (int j = 0; j < count; j++)
                        if (obstacles.Contains(overlaps[j])) { blocked = true; break; }
                }
                cells[z * width + x] = blocked ? MinimapTerrainPixels.Obstacle : MinimapTerrainPixels.Ground;
                if (!blocked) walkableCount++;
            }
            if (walkableCount == 0) throw new InvalidOperationException(sceneName + " produced no visible ground.");
            // Encode needs a CPU copy; runtime textures use Create() with readability discarded instead.
            texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(MinimapTerrainPixels.Build(cells, width, height));
            texture.Apply(false, false);
            string path = MapRoot + "/" + sceneName + ".png";
            Backup(path, backup);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            Debug.Log("[MinimapTerrain] " + sceneName + ": " + width + "x" + height + ", visible ground=" + walkableCount);
            return new WorldMinimapController.TerrainMap
                { sceneName = sceneName, texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path), worldBounds = footprint };
        }
        finally
        {
            if (texture != null) Object.DestroyImmediate(texture);
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
        }
    }

    static bool IsEditorOnly(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
            if (current.CompareTag("EditorOnly")) return true;
        return false;
    }

    static void Backup(string path, string destination)
    {
        foreach (string source in new[] { path, path + ".meta" })
            if (File.Exists(source))
            {
                string target = Path.Combine(destination, source);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target, false);
            }
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        EnsureFolder(path.Substring(0, slash));
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
