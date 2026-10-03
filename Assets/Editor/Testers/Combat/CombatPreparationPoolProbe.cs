#if UNITY_EDITOR
using UnityEngine;

// Transient owned validation objects only; never attached to authored assets.
public sealed class CombatPreparationPoolProbe : MonoBehaviour, ITransientVfxPlayback
{
    private void Awake() { _ = Random.value; }
    public void RestartVfx() { }
    public void StopAndClearVfx() { }
}
#endif
