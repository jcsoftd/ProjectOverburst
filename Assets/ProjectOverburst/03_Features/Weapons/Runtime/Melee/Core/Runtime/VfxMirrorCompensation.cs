using UnityEngine;

public sealed class VfxMirrorCompensation : MonoBehaviour
{
    [SerializeField] private Transform[] counterMirroredTransforms;

    private Vector3[] baseLocalScales;

    public void Configure(Transform[] transforms)
    {
        counterMirroredTransforms = transforms;
        baseLocalScales = null;
    }

    public void Apply(bool mirrored)
    {
        EnsureScaleCache();
        for (int i = 0; i < counterMirroredTransforms.Length; i++)
        {
            Transform target = counterMirroredTransforms[i];
            if (target == null)
                continue;

            Vector3 scale = baseLocalScales[i];
            if (mirrored)
                scale.x = -scale.x;
            target.localScale = scale;
        }
    }

    private void Awake()
    {
        EnsureScaleCache();
    }

    private void EnsureScaleCache()
    {
        int count = counterMirroredTransforms != null
            ? counterMirroredTransforms.Length
            : 0;
        if (baseLocalScales != null && baseLocalScales.Length == count)
            return;

        baseLocalScales = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            baseLocalScales[i] = counterMirroredTransforms[i] != null
                ? counterMirroredTransforms[i].localScale
                : Vector3.one;
        }
    }
}
