using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Combat/Melee/Weapon Trace Binding")]
public sealed class WeaponTraceBinding : MonoBehaviour
{
    [SerializeField] private Transform weaponTip;

    public Transform WeaponTip => weaponTip;
    public bool IsValid => weaponTip != null;

    public void Configure(Transform tip)
    {
        weaponTip = tip;
    }
}
