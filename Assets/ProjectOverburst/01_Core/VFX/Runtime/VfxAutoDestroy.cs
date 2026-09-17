using UnityEngine;

public class VfxAutoDestroy : MonoBehaviour // VFX 자동 제거
{
    [SerializeField] private float lifetime = 1f;

    private void OnEnable()
    {
        if (lifetime > 0f)
            Destroy(gameObject, lifetime); // 수명 예약
    }
}
