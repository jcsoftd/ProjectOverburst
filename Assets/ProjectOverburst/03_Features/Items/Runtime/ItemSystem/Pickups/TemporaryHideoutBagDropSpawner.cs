using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class TemporaryHideoutBagDropSpawner : MonoBehaviour
{
#if UNITY_EDITOR
    private const string HideoutSceneName = "HideoutScene";
    private const string BagAssetFolder = "Assets/ProjectOverburst/03_Features/Items/Data/Items/Bags";
    private const string PickupGradeVfxSetPath = "Assets/ProjectOverburst/03_Features/Items/VFX/Data/PickupGradeVfxSet.asset";
    private const float SpawnDistance = 3.05f;
    private const float SpawnSpacing = 0.58f;
    private const float SpawnHeight = 0.38f;
    private const int MaxSpawnGradeLevel = 7;

    private static bool spawnedThisPlaySession;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<TemporaryHideoutBagDropSpawner>() != null)
            return;

        GameObject spawnerObject = new GameObject("[TEMP] Hideout Bag Grade Drop Spawner");
        spawnerObject.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(spawnerObject);
        spawnerObject.AddComponent<TemporaryHideoutBagDropSpawner>();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        TrySpawnForLoadedHideout();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsHideoutScene(scene))
            StartCoroutine(SpawnAfterSceneSettles());
    }

    private void TrySpawnForLoadedHideout()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (IsHideoutScene(SceneManager.GetSceneAt(i)))
            {
                StartCoroutine(SpawnAfterSceneSettles());
                return;
            }
        }
    }

    private IEnumerator SpawnAfterSceneSettles()
    {
        PersistentSceneFlow flow = PersistentSceneFlow.Instance;
        while (flow != null
            && (flow.IsSwitching || flow.CurrentSubSceneName != HideoutSceneName))
        {
            yield return null;
        }

        yield return null; // 최종 파티 Transform 반영

        if (spawnedThisPlaySession || !IsHideoutLoaded())
            yield break;

        SpawnGradeBags();
    }

    private void SpawnGradeBags()
    {
        PlayerInventory inventory = FindFirstObjectByType<PlayerInventory>();
        Transform player = ResolvePlayerTransform(inventory);
        PickupGradeVfxSet gradeVfxSet = AssetDatabase.LoadAssetAtPath<PickupGradeVfxSet>(PickupGradeVfxSetPath);
        Vector3 origin = ResolveSpawnOrigin(player);
        Vector3 right = ResolveFlatDirection(player, Vector3.right, true);

        for (int gradeLevel = 1; gradeLevel <= MaxSpawnGradeLevel; gradeLevel++)
        {
            BagItemData bagData = LoadBagData(gradeLevel);
            if (bagData == null || !ItemGradeAvailabilityPolicy.IsEnabled(bagData.defaultGrade))
                continue;

            ItemData item = new ItemData(bagData, Mathf.Max(1, bagData.level), bagData.defaultGrade, 1);
            item.EnsureRuntimeState();
            item.EnsureAcquisitionOrder();

            float centeredIndex = gradeLevel - 1 - (MaxSpawnGradeLevel - 1) * 0.5f;
            Vector3 position = origin + right * centeredIndex * SpawnSpacing;
            WorldItemPickup pickup = WorldItemDropFactory.CreateWorldPickupFromExistingItem(item, position, inventory, player, gradeVfxSet);
            if (pickup != null)
            {
                Scene hideoutScene = SceneManager.GetSceneByName(HideoutSceneName);
                pickup.PlaceAuthored(position, hideoutScene);
                pickup.name = string.Format("[TEMP] HideoutBag_Grade{0}_{1}", gradeLevel, bagData.defaultGrade);
            }
        }

        spawnedThisPlaySession = true;
        Debug.Log("[TemporaryHideoutBagDropSpawner] 하이드아웃에 1~7등급 가방 테스트 드랍을 생성했습니다.");
    }

    private static BagItemData LoadBagData(int gradeLevel)
    {
        string path = string.Format("{0}/Bag_Grade{1}.asset", BagAssetFolder, gradeLevel);
        return AssetDatabase.LoadAssetAtPath<BagItemData>(path);
    }

    private static Transform ResolvePlayerTransform(PlayerInventory inventory)
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
            return playerObject.transform;

        return inventory != null ? inventory.transform : null;
    }

    private static Vector3 ResolveSpawnOrigin(Transform player)
    {
        Vector3 position = player != null ? player.position : Vector3.zero;
        Vector3 forward = ResolveFlatDirection(player, Vector3.forward, false);
        return position + forward * SpawnDistance + Vector3.up * SpawnHeight;
    }

    private static Vector3 ResolveFlatDirection(Transform player, Vector3 fallback, bool right)
    {
        Vector3 direction = player != null ? (right ? player.right : player.forward) : fallback;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = fallback;

        return direction.normalized;
    }

    private static bool IsHideoutLoaded()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            if (IsHideoutScene(SceneManager.GetSceneAt(i)))
                return true;
        }

        return false;
    }

    private static bool IsHideoutScene(Scene scene)
    {
        return scene.IsValid() && scene.isLoaded && scene.name == HideoutSceneName;
    }
#endif
}
