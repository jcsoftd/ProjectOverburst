using UnityEngine;

// Keep imported root curves from rescaling/drifting an authored actor. Bone motion remains intact.
[DefaultExecutionOrder(800)]
public sealed class EnemyVisualRootGuard : MonoBehaviour
{
    [SerializeField] private Transform animatedRoot;
    private Vector3 position, scale;
    private Quaternion rotation;
    private bool captured;
    public void Configure(Transform root) { animatedRoot = root; captured = false; }
    private void OnEnable()
    {
        if (animatedRoot == null || captured) return;
        position = animatedRoot.localPosition; scale = animatedRoot.localScale; rotation = animatedRoot.localRotation; captured = true;
    }
    private void LateUpdate()
    {
        if (animatedRoot == null || !captured) return;
        animatedRoot.SetLocalPositionAndRotation(position, rotation); animatedRoot.localScale = scale;
    }
}
