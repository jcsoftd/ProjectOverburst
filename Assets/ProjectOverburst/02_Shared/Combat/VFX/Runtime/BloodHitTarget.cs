using UnityEngine;

[DisallowMultipleComponent]
public sealed class BloodHitTarget : MonoBehaviour
{
    [SerializeField] private BloodHitProfile profile;
    public BloodHitProfile Profile => profile;
}
