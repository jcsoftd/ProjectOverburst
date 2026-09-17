using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WorldItemPickup : MonoBehaviour // 월드 아이템
{
    private static readonly HashSet<WorldItemPickup> activePickups = new HashSet<WorldItemPickup>(); // 활성 픽업 레지스트리
    private static int registryRevision; // 레지스트리 변경 번호

    [Header("Item")]
    [SerializeField] private BaseItemData itemDataAsset;
    [SerializeField] private int itemLevel = 1;
    [SerializeField] private ItemGrade itemGrade = ItemGrade.Common;
    [SerializeField] private int stackCount = 1;

    [Header("Pickup")]
    [SerializeField] private PlayerInventory targetInventory;
    [SerializeField] private Transform player;

    [Header("Drop")]
    [SerializeField] private bool useScriptedDrop = true;

    [Header("VFX")]
    [SerializeField] private PickupGradeVfxSet gradeVfxSet;
    [SerializeField] private Transform gradeVfxAnchor;

    private ItemData runtimeItem; // 런타임 아이템
    private bool pickedUp; // 획득 상태
    private GameObject spawnedGradeVfx; // 등급 VFX
    private Collider[] pickupColliders; // 충돌체 캐시
    private WorldItemDropMotion dropMotion; // 비물리 드랍 연출

    public static event Action RegistryChanged; // 활성 목록 변경 알림

    public static int RegistryRevision => registryRevision;

    private void Awake()
    {
        ResolveReferences(); // 인벤토리/플레이어 참조 확보
        BuildRuntimeItemIfNeeded(); // 에셋 기반 테스트 아이템 생성
        ConfigurePickupColliders(); // 플레이어를 막지 않도록 충돌 설정

        if (runtimeItem != null)
            BeginScriptedDrop(); // 비물리 드랍 시작

        RefreshGradeVfx(); // 등급 VFX 연결
    }

    private void OnEnable()
    {
        if (dropMotion != null)
        {
            dropMotion.Landed -= HandleDropLanded;
            dropMotion.Landed += HandleDropLanded;
        }

        if (activePickups.Add(this))
            NotifyRegistryChanged(); // 활성 등록
    }

    private void OnDisable()
    {
        UnbindDropMotion();
        if (activePickups.Remove(this))
            NotifyRegistryChanged(); // 비활성 해제
    }

    public ItemData RuntimeItem
    {
        get { return runtimeItem; }
    }

    public bool CanPickup
    {
        get
        {
            return !pickedUp
                && runtimeItem != null
                && runtimeItem.HasValidBaseData
                && WeaponContentPolicy.IsAllowedRuntimeItem(runtimeItem)
                && (dropMotion == null || dropMotion.IsLanded);
        }
    }

    public string DisplayName
    {
        get
        {
            if (runtimeItem == null || string.IsNullOrEmpty(runtimeItem.itemName))
                return "Item";

            return runtimeItem.itemName;
        }
    }

    public ItemGrade Grade
    {
        get { return runtimeItem != null ? runtimeItem.grade : itemGrade; }
    }

    public int StackCount
    {
        get { return runtimeItem != null ? runtimeItem.stackCount : stackCount; }
    }

    public void Initialize(ItemData item, PlayerInventory inventory, Transform playerTransform)
    {
        pickedUp = false; // 재사용 초기화
        runtimeItem = WeaponContentPolicy.IsAllowedRuntimeItem(item) ? item : null; // 드랍된 런타임 ItemData 연결
        targetInventory = inventory; // 획득 대상 인벤토리 연결
        player = playerTransform; // 플레이어 충돌 무시용 참조
        ConfigurePickupColliders(); // 충돌/트리거 정책 적용
        BeginScriptedDrop(); // 자연스러운 비물리 낙하 시작
        RefreshGradeVfx(); // 등급 이펙트 생성
        EnsureInitializedComponentRegistration(); // 비활성 컴포넌트 재사용 등록
        NotifyRegistryChanged(); // 런타임 데이터 갱신
    }

    public void Initialize(ItemData item, PlayerInventory inventory, Transform playerTransform, PickupGradeVfxSet pickupGradeVfxSet)
    {
        pickedUp = false;
        runtimeItem = WeaponContentPolicy.IsAllowedRuntimeItem(item) ? item : null;
        targetInventory = inventory;
        player = playerTransform;
        gradeVfxSet = pickupGradeVfxSet;
        ConfigurePickupColliders();
        BeginScriptedDrop();
        RefreshGradeVfx();
        EnsureInitializedComponentRegistration();
        NotifyRegistryChanged(); // 런타임 데이터 갱신
    }

    public bool TryPickup(PlayerInventory inventory)
    {
        if (pickedUp || inventory == null || runtimeItem == null || !WeaponContentPolicy.IsAllowedRuntimeItem(runtimeItem))
            return false; // 중복 획득/무효 인벤토리 차단

        if (!inventory.AddItem(runtimeItem))
            return false; // 인벤토리 추가 실패 시 월드 유지

        InventoryUI.NotifyWorldPickupAdded(runtimeItem); // 닫힌 인벤토리 신규 표시
        pickedUp = true; // 중복 획득 방지
        Destroy(gameObject); // 획득 성공 시 VFX 포함 제거
        return true;
    }

    public static void CopyActivePickups(List<WorldItemPickup> target)
    {
        if (target == null)
            return;

        target.Clear();
        int removedCount = activePickups.RemoveWhere(pickup => pickup == null); // 파괴 참조 정리
        foreach (WorldItemPickup pickup in activePickups)
        {
            if (pickup != null)
                target.Add(pickup);
        }

        if (removedCount > 0)
            NotifyRegistryChanged();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry()
    {
        activePickups.Clear();
        registryRevision = 0;
        RegistryChanged = null;
    }

    private static void NotifyRegistryChanged()
    {
        registryRevision++;
        RegistryChanged?.Invoke();
    }

    public bool TryPickup()
    {
        ResolveReferences();
        return TryPickup(targetInventory);
    }

    public void PlaceAuthored(Vector3 position, Scene targetScene)
    {
        if (targetScene.IsValid() && targetScene.isLoaded)
        {
            if (gameObject.scene != targetScene)
                SceneManager.MoveGameObjectToScene(gameObject, targetScene);

            if (spawnedGradeVfx != null && spawnedGradeVfx.transform.root == spawnedGradeVfx.transform
                && spawnedGradeVfx.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(spawnedGradeVfx, targetScene);
            }
        }

        if (dropMotion != null)
            dropMotion.SettleImmediately();

        transform.position = position; // 테스트 배치물은 드랍 산개 없이 지정 위치에 고정
    }

    private void BuildRuntimeItemIfNeeded()
    {
        if (runtimeItem != null || itemDataAsset == null)
            return; // 이미 런타임 아이템 있음

        if (!WeaponContentPolicy.IsAllowedItemData(itemDataAsset))
            return;

        ItemGrade grade = itemDataAsset is BagItemData bagData ? bagData.defaultGrade : itemGrade;
        runtimeItem = new ItemData(itemDataAsset, itemLevel, grade, stackCount); // 씬 배치용
    }

    private void RefreshGradeVfx()
    {
        if (runtimeItem == null || gradeVfxSet == null || spawnedGradeVfx != null)
            return; // VFX 생성 조건 확인

        GameObject prefab = gradeVfxSet.GetPrefab(runtimeItem.grade); // 등급 prefab
        if (prefab == null)
            return; // 등급 매핑 없음

        Transform anchor = gradeVfxAnchor != null ? gradeVfxAnchor : transform; // 시각 효과 기준점
        spawnedGradeVfx = VfxPrefabFactory.SpawnFollowing(prefab, anchor); // 아이템을 따라가는 등급 VFX
    }

    private void ResolveReferences()
    {
        if (targetInventory == null)
            targetInventory = FindFirstObjectByType<PlayerInventory>();

        if (player != null)
            return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        player = playerObject != null ? playerObject.transform : null;

    }

    private void ConfigurePickupColliders()
    {
        pickupColliders = GetComponentsInChildren<Collider>();

        for (int i = 0; i < pickupColliders.Length; i++)
        {
            if (pickupColliders[i] != null)
                pickupColliders[i].enabled = false; // 일반 아이템은 거리 기반 획득이므로 물리 충돌 제거
        }

        RemoveLegacyRigidbody();
    }

    private void BeginScriptedDrop()
    {
        if (!useScriptedDrop || runtimeItem == null)
            return;

        dropMotion = GetComponent<WorldItemDropMotion>();
        if (dropMotion == null)
            dropMotion = gameObject.AddComponent<WorldItemDropMotion>();

        dropMotion.Landed -= HandleDropLanded;
        dropMotion.Landed += HandleDropLanded;
        dropMotion.Begin();
    }

    private void HandleDropLanded()
    {
        NotifyRegistryChanged(); // 착지 후 유효 멤버십 갱신
    }

    private void EnsureInitializedComponentRegistration()
    {
        if (runtimeItem == null || enabled)
            return;

        enabled = true; // ItemData 준비 후 활성 레지스트리 등록
    }

    private void UnbindDropMotion()
    {
        if (dropMotion == null)
            return;

        dropMotion.Landed -= HandleDropLanded;
    }

    private void RemoveLegacyRigidbody()
    {
        Rigidbody legacyRigidbody = GetComponent<Rigidbody>();
        if (legacyRigidbody == null)
            return;

        legacyRigidbody.linearVelocity = Vector3.zero;
        legacyRigidbody.angularVelocity = Vector3.zero;
        legacyRigidbody.useGravity = false;
        legacyRigidbody.isKinematic = true;
        legacyRigidbody.detectCollisions = false;
        Destroy(legacyRigidbody);
    }
}
