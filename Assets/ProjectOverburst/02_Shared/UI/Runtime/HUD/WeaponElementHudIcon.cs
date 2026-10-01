using UnityEngine;

/// <summary>초상화 오른쪽 원형 아이콘을 현재 액터의 장착 무기에 연결한다.</summary>
[DisallowMultipleComponent]
public sealed class WeaponElementHudIcon : MonoBehaviour
{
    [SerializeField] private WeaponElementIconView view;
    private PlayerEquipment equipment;

    public WeaponElementIconView View => view;
    public void Configure(WeaponElementIconView icon) => view = icon;

    private void OnEnable() { RefreshActor(); RefreshIcon(); }
    private void LateUpdate() => RefreshActor();

    public void RefreshActor()
    {
        PlayerEquipment next = PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActorEquipment : null;
        if (ReferenceEquals(equipment, next)) return;
        Unsubscribe();
        equipment = next;
        if (equipment != null) equipment.WeaponSlotsChanged += RefreshIcon;
        RefreshIcon();
    }

    private void RefreshIcon() => view?.Present(equipment != null ? equipment.CurrentWeaponItem : null);

    private void OnDisable()
    {
        Unsubscribe();
        view?.Present(WeaponElement.None);
    }

    private void Unsubscribe()
    {
        if (equipment != null) equipment.WeaponSlotsChanged -= RefreshIcon;
        equipment = null;
    }
}
