using UnityEngine;

public struct MagicAimLine // 마법 조준선
{
    public Vector3 origin; // 시작점
    public Vector3 direction; // 방향
    public float range; // 사거리

    public Vector3 End
    {
        get { return origin + direction * Mathf.Max(0f, range); }
    }
}
