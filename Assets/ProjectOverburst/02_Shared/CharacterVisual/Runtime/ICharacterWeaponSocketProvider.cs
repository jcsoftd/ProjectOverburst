using UnityEngine;

public interface ICharacterWeaponSocketProvider
{
    Transform GetWeaponSocket(WeaponItemData weaponData);
    Transform GetNamedSocket(string socketName);
}
