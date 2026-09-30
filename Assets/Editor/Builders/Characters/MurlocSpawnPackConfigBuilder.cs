using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MurlocSpawnPackConfigBuilder // 멀록 pack 등록
{
    private const string HideoutScenePath =
        "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    private const string MurlocPrefabFolder = "Assets/ProjectOverburst/Resources/Enemies/Murloc/";
    private const string FishmanFallbackPath = "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster_FishmanTest.prefab";

    [MenuItem("OVERBURST/Codex/Setup/Enemies/Configure Murloc Spawn Packs")]
    public static void ConfigureFromMenu()
    {
        RunOnce();
    }

    public static void RunOnceFromCommandLine()
    {
        RunOnce();
    }

    private static void RunOnce()
    {
        Scene scene = EditorSceneManager.OpenScene(
            HideoutScenePath,
            OpenSceneMode.Single);
        Dictionary<string, GameObject> prefabs = LoadMurlocPrefabs();
        HideoutMonsterSpawnDebugController[] spawners =
            Object.FindObjectsByType<HideoutMonsterSpawnDebugController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        if (spawners.Length == 0)
        {
            throw new System.InvalidOperationException(
                "HideoutMonsterSpawnDebugController not found in HideoutScene.");
        }

        for (int i = 0; i < spawners.Length; i++)
        {
            SerializedObject serializedSpawner =
                new SerializedObject(spawners[i]);
            SerializedProperty spawnConfig =
                serializedSpawner.FindProperty("spawnConfig");
            ConfigureSharedSpawnConfig(spawnConfig, prefabs);
            serializedSpawner.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(spawners[i]);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            "[ProjectVTP] Hideout murloc spawn packs configured="
            + spawners.Length);
    }

    public static void ConfigureSharedSpawnConfig(SerializedProperty spawnConfig)
    {
        ConfigureSharedSpawnConfig(spawnConfig, LoadMurlocPrefabs());
    }

    private static void ConfigureSharedSpawnConfig(SerializedProperty spawnConfig, Dictionary<string, GameObject> prefabs)
    {
        if (spawnConfig == null)
            throw new System.ArgumentNullException(nameof(spawnConfig));

        SetObject(spawnConfig.FindPropertyRelative("enemyPrefab"), AssetDatabase.LoadAssetAtPath<GameObject>(FishmanFallbackPath));
        ConfigureEnemyPrefabFallback(spawnConfig, prefabs);
        ConfigureRespawnSequence(spawnConfig);
        ConfigureSpawnScaling(spawnConfig);
        ConfigureSpawnPacks(spawnConfig, prefabs);
        SetFloat(spawnConfig.FindPropertyRelative("detectionRange"), 10f);
        SetFloat(spawnConfig.FindPropertyRelative("stopDistance"), 1.5f);
    }

    private static Dictionary<string, GameObject> LoadMurlocPrefabs()
    {
        string[] ids =
        {
            "Murloc_Grunt",
            "Murloc_Scout",
            "Murloc_Spearling",
            "Murloc_Guard",
            "Murloc_Brute",
            "Murloc_Warlord"
        };

        Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        for (int i = 0; i < ids.Length; i++)
        {
            string path = MurlocPrefabFolder + "PF_StageMonster_" + ids[i] + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                prefabs[ids[i]] = prefab;
            else
                Debug.LogWarning("[ProjectVTP] Missing murloc prefab: " + path);
        }

        return prefabs;
    }

    private static void ConfigureEnemyPrefabFallback(SerializedProperty spawnConfig, Dictionary<string, GameObject> prefabs)
    {
        string[] ids =
        {
            "Murloc_Grunt",
            "Murloc_Scout",
            "Murloc_Spearling",
            "Murloc_Guard",
            "Murloc_Brute",
            "Murloc_Warlord"
        };

        float[] weights = { 34f, 22f, 18f, 14f, 6f, 1f };
        SerializedProperty enemyPrefabs = spawnConfig.FindPropertyRelative("enemyPrefabs");
        if (enemyPrefabs == null || !enemyPrefabs.isArray)
            return;

        enemyPrefabs.arraySize = ids.Length;
        for (int i = 0; i < ids.Length; i++)
        {
            SerializedProperty entry = enemyPrefabs.GetArrayElementAtIndex(i);
            SetObject(entry.FindPropertyRelative("prefab"), prefabs.TryGetValue(ids[i], out GameObject prefab) ? prefab : null);
            SetFloat(entry.FindPropertyRelative("weight"), weights[i]);
            SetInt(entry.FindPropertyRelative("minCount"), 1);
            SetInt(entry.FindPropertyRelative("maxCount"), 1);
            SetBool(entry.FindPropertyRelative("scaleWithAreaSize"), true);
        }
    }

    private static void ConfigureSpawnScaling(SerializedProperty spawnConfig)
    {
        SetBool(spawnConfig.FindPropertyRelative("scaleSpawnCountByAreaSize"), true);
        SetFloat(spawnConfig.FindPropertyRelative("referenceAreaSurface"), 900f);
        SetFloat(spawnConfig.FindPropertyRelative("minAreaSpawnMultiplier"), 0.65f);
        SetFloat(spawnConfig.FindPropertyRelative("maxAreaSpawnMultiplier"), 1.65f);
        SetFloat(spawnConfig.FindPropertyRelative("areaScalePower"), 0.5f);
        SetInt(spawnConfig.FindPropertyRelative("minEnemiesPerArea"), 1);
        SetInt(spawnConfig.FindPropertyRelative("maxEnemiesPerArea"), 10);
        SetInt(spawnConfig.FindPropertyRelative("maxEnemiesPerRespawnWave"), 8);
        SetInt(spawnConfig.FindPropertyRelative("maxEnemiesPerRespawnSequence"), 30);
        SetBool(spawnConfig.FindPropertyRelative("enableDebugLogs"), true);
    }

    private static void ConfigureRespawnSequence(SerializedProperty spawnConfig)
    {
        SetInt(spawnConfig.FindPropertyRelative("respawnWaveCount"), 6);
        SetFloat(spawnConfig.FindPropertyRelative("respawnWaveInterval"), 5f);
        SetInt(spawnConfig.FindPropertyRelative("respawnPacksPerWave"), 1);
        SetBool(spawnConfig.FindPropertyRelative("triggerRespawnOnFirstDeath"), true);
        SetBool(spawnConfig.FindPropertyRelative("allowMultipleRespawnSequences"), false);
    }

    private static void ConfigureSpawnPacks(SerializedProperty spawnConfig, Dictionary<string, GameObject> prefabs)
    {
        SerializedProperty packs = spawnConfig.FindPropertyRelative("spawnPacks");
        if (packs == null || !packs.isArray)
            return;

        packs.arraySize = 5;
        ConfigurePack(packs.GetArrayElementAtIndex(0), "Murloc_BasicMob", "기본 멀록 무리", 30f, false, prefabs,
            Entry("Murloc_Grunt", 2, 3, true),
            Entry("Murloc_Scout", 1, 2, true));
        ConfigurePack(packs.GetArrayElementAtIndex(1), "Murloc_ScoutPack", "멀록 정찰대", 20f, false, prefabs,
            Entry("Murloc_Scout", 3, 4, true),
            Entry("Murloc_Grunt", 0, 1, true));
        ConfigurePack(packs.GetArrayElementAtIndex(2), "Murloc_ShieldLine", "방패 전열", 15f, false, prefabs,
            Entry("Murloc_Guard", 1, 2, true),
            Entry("Murloc_Spearling", 1, 2, true),
            Entry("Murloc_Grunt", 1, 1, true));
        ConfigurePack(packs.GetArrayElementAtIndex(3), "Murloc_BruteRaid", "싸움꾼 돌격대", 6f, true, prefabs,
            Entry("Murloc_Brute", 1, 1, false),
            Entry("Murloc_Grunt", 1, 2, true),
            Entry("Murloc_Scout", 0, 1, true));
        ConfigurePack(packs.GetArrayElementAtIndex(4), "Murloc_WarlordEscort", "장군 호위대", 1f, true, prefabs,
            Entry("Murloc_Warlord", 1, 1, false),
            Entry("Murloc_Guard", 1, 1, true),
            Entry("Murloc_Spearling", 1, 2, true));
    }

    private static PackEntry Entry(string id, int minCount, int maxCount, bool scale)
    {
        return new PackEntry(id, minCount, maxCount, scale);
    }

    private static void ConfigurePack(SerializedProperty pack, string packId, string displayName, float weight, bool isElitePack, Dictionary<string, GameObject> prefabs, params PackEntry[] entries)
    {
        pack.FindPropertyRelative("packId").stringValue = packId;
        pack.FindPropertyRelative("displayName").stringValue = displayName;
        SetFloat(pack.FindPropertyRelative("weight"), weight);
        SetBool(pack.FindPropertyRelative("isElitePack"), isElitePack);

        SerializedProperty entryList = pack.FindPropertyRelative("entries");
        entryList.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            SerializedProperty entry = entryList.GetArrayElementAtIndex(i);
            SetObject(entry.FindPropertyRelative("prefab"), prefabs.TryGetValue(entries[i].Id, out GameObject prefab) ? prefab : null);
            SetFloat(entry.FindPropertyRelative("weight"), 1f);
            SetInt(entry.FindPropertyRelative("minCount"), entries[i].MinCount);
            SetInt(entry.FindPropertyRelative("maxCount"), entries[i].MaxCount);
            SetBool(entry.FindPropertyRelative("scaleWithAreaSize"), entries[i].ScaleWithAreaSize);
        }
    }

    private static void SetObject(SerializedProperty property, Object value)
    {
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetFloat(SerializedProperty property, float value)
    {
        if (property != null)
            property.floatValue = value;
    }

    private static void SetInt(SerializedProperty property, int value)
    {
        if (property != null)
            property.intValue = value;
    }

    private static void SetBool(SerializedProperty property, bool value)
    {
        if (property != null)
            property.boolValue = value;
    }

    private readonly struct PackEntry
    {
        public readonly string Id;
        public readonly int MinCount;
        public readonly int MaxCount;
        public readonly bool ScaleWithAreaSize;

        public PackEntry(string id, int minCount, int maxCount, bool scaleWithAreaSize)
        {
            Id = id;
            MinCount = minCount;
            MaxCount = maxCount;
            ScaleWithAreaSize = scaleWithAreaSize;
        }
    }
}
