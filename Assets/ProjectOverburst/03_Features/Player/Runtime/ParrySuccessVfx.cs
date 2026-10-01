using UnityEngine;

// 나를 때릴 패링 가능 공격이 있을 때 발밑에 준비 링을 표시한다.
// 성공 순간의 플레어는 ParryFeedbackService가 한 번만 재생한다.
[DisallowMultipleComponent]
public sealed class ParrySuccessVfx : MonoBehaviour
{
    private const int RingSegments = 64;
    private const float ReadyRingRadius = .85f;
    private const float ReadyFadeSpeed = 12f;
    private LineRenderer ring;
    private float readyWeight;

    private void Awake()
    {
        var ringObject = new GameObject("Parry ready ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = Vector3.up * .06f;
        ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = Resources.Load<Material>("Feel/MAT_OverburstFeelParticles");
        ring.useWorldSpace = false;
        ring.alignment = LineAlignment.View;
        ring.positionCount = RingSegments + 1;
        ring.numCapVertices = 2;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        for (int i = 0; i <= RingSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / RingSegments;
            ring.SetPosition(i, new Vector3(Mathf.Sin(angle) * ReadyRingRadius, 0f,
                Mathf.Cos(angle) * ReadyRingRadius));
        }
        ring.enabled = false;
    }

    private void Update()
    {
        if (ring == null) return;
        bool ready = EnemyStrongAttackWarning.ActiveThreatSignalCount > 0;
        readyWeight = Mathf.MoveTowards(readyWeight, ready ? 1f : 0f, ReadyFadeSpeed * Time.unscaledDeltaTime);
        if (readyWeight <= .001f)
        {
            if (ring.enabled) ring.enabled = false;
            return;
        }
        ring.enabled = true;
        float pulse = .72f + .28f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 5f);
        float alpha = readyWeight * pulse;
        Color color = new Color(alpha, .82f * alpha, .38f * alpha, alpha);
        ring.startColor = color;
        ring.endColor = color;
        ring.widthMultiplier = .045f + .015f * pulse;
    }

    private void OnDisable()
    {
        readyWeight = 0f;
        if (ring != null) ring.enabled = false;
    }
}
