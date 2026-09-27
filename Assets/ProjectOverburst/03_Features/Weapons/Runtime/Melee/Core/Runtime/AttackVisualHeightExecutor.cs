using UnityEngine;

// Shared by runtime and the combo preview; height never moves the collision capsule.
public sealed class AttackVisualHeightExecutor
{
    private Transform visualRoot;
    private Vector3 restPosition;
    private AnimationCurve heightCurve;

    public void Begin(Transform root, AnimationCurve curve)
    {
        Cancel();
        if (root == null || curve == null || curve.length == 0) return;
        visualRoot = root;
        restPosition = root.localPosition;
        heightCurve = curve;
    }

    public void Tick(float normalizedTime)
    {
        if (visualRoot == null || heightCurve == null) return;
        visualRoot.localPosition = restPosition + Vector3.up
            * Mathf.Max(0f, heightCurve.Evaluate(Mathf.Clamp01(normalizedTime)));
    }

    public void Cancel()
    {
        if (visualRoot != null) visualRoot.localPosition = restPosition;
        visualRoot = null;
        heightCurve = null;
    }
}
