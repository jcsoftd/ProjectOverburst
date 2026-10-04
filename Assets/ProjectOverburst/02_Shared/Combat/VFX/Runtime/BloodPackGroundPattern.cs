using UnityEngine;
using UnityEngine.Rendering.Universal;

// Atlas frames advance on each projector, so tinted materials can remain shared across marks.
[DisallowMultipleComponent]
public sealed class BloodPackGroundPattern : MonoBehaviour
{
    public int variant;
    [Min(1)] public int columns = 1, rows = 1, frames = 1;
    [Min(.01f)] public float spreadSeconds = .48f;
    public void Apply(DecalProjector projector, float age)
    {
        int cols = Mathf.Max(1, columns), rowCount = Mathf.Max(1, rows);
        int last = Mathf.Min(Mathf.Max(1, frames), cols * rowCount) - 1;
        int frame = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(age / spreadSeconds) * last), 0, last);
        Vector2 size = new Vector2(1f / cols, 1f / rowCount);
        projector.uvScale = size;
        projector.uvBias = new Vector2((frame % cols) * size.x, 1f - size.y - (frame / cols) * size.y);
    }
}
