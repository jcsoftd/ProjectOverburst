using UnityEngine;

public class PlayerStarterLoadout : MonoBehaviour // 시작 지급
{
    [Header("References")]
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerEquipment playerEquipment;

    [Header("Starter Weapon")]
    [SerializeField] private WeaponItemData starterWeaponItem;
    [SerializeField] private Transform starterWeaponRootSource;
    [SerializeField] private ItemGrade starterWeaponGrade = ItemGrade.Common;
    [SerializeField] private bool grantOnStart = true;
    [SerializeField] private bool autoEquipStarterWeapon = true;

    [Header("Test Bags")]
    [SerializeField] private bool grantTestBagsOnStart;
    [SerializeField] private BagItemData[] testBagItems;

    public ItemData StarterWeaponItem { get; private set; } // 지급 무기

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        if (grantOnStart)
            GrantStarterWeapon();

        if (grantTestBagsOnStart)
            GrantTestBags();
    }

    [ContextMenu("Grant Starter Weapon")]
    public void GrantStarterWeapon()
    {
        ResolveReferences();

        if (inventory == null)
            return;

        WeaponItemData weaponData = GetStarterWeaponData(); // 지급 데이터

        if (weaponData == null || weaponData.weaponRootPrefab == null
            || !ItemGradeAvailabilityPolicy.IsEnabled(starterWeaponGrade))
            return;

        StarterWeaponItem = new ItemData(weaponData, 1, starterWeaponGrade); // 런타임 아이템

        if (!inventory.AddItem(StarterWeaponItem))
            return;

        if (autoEquipStarterWeapon && playerEquipment != null && playerEquipment.EquipWeaponItem(StarterWeaponItem))
            inventory.RemoveItem(StarterWeaponItem);
    }

    [ContextMenu("Grant Test Bags")]
    public void GrantTestBags()
    {
        ResolveReferences();

        if (inventory == null || testBagItems == null)
            return;

        for (int i = 0; i < testBagItems.Length; i++)
        {
            BagItemData bagData = testBagItems[i]; // 테스트 가방

            if (bagData == null || !ItemGradeAvailabilityPolicy.IsEnabled(bagData.defaultGrade))
                continue;

            ItemData bagItem = new ItemData(bagData, Mathf.Max(1, bagData.level), bagData.defaultGrade); // 가방 아이템
            inventory.AddItem(bagItem);
        }
    }

    private void ResolveReferences()
    {
        if (inventory == null)
            inventory = PlayerAccountInventoryService.FindSharedInventory();

        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();

        if (playerEquipment == null)
            playerEquipment = GetComponent<PlayerEquipment>();
    }

    private WeaponItemData GetStarterWeaponData()
    {
        if (starterWeaponItem != null
            && starterWeaponItem.weaponRootPrefab != null
            && WeaponContentPolicy.IsActiveWeapon(starterWeaponItem))
            return starterWeaponItem;

        return null;
    }
}
