using UnityEngine;

[DisallowMultipleComponent]
public sealed class SurfaceOverride : MonoBehaviour
{
    [SerializeField] private SurfaceProfile profile;

    public SurfaceProfile Profile => profile;

    public void Configure(SurfaceProfile configuredProfile) => profile = configuredProfile;
}
