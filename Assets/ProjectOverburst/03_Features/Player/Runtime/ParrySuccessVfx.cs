using UnityEngine;

// One player-owned emitter renders all enemies in a multi-parry without spawning per-hit objects.
// 2026-09-30: 성공 순간의 바닥 링은 패링 중심 파동(ParryFeedbackService)으로 대체했다.
// 링은 "지금 누르면 패링된다" 표시로 쓴다(나를 때릴 패링 가능 공격이 있을 때 발밑에 금색으로).
[DisallowMultipleComponent]
public sealed class ParrySuccessVfx : MonoBehaviour
{
    private const int RingSegments = 64;
    private const float ReadyRingRadius = .85f;
    private const float ReadyFadeSpeed = 12f;
    private ParticleSystem sparks;
    private LineRenderer ring;
    private Camera cachedCamera;
    private float readyWeight;

    private void Awake()
    {
        var root = new GameObject("Parry success visual");
        root.transform.SetParent(transform, false);
        var ringMaterial = Resources.Load<Material>("Feel/MAT_OverburstFeelParticles");
        var sparkMaterial = Resources.Load<Material>("Combat/VFX/MAT_OverburstParrySparks");

        sparks = root.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = .35f;
        main.startLifetime = .26f;
        main.startSpeed = 0f;
        main.startSize = .10f;
        main.startColor = Color.white;
        main.maxParticles = 512;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = .08f;
        var emission = sparks.emission;
        emission.rateOverTime = 0f;
        var shape = sparks.shape;
        shape.enabled = false;
        var fade = sparks.colorOverLifetime;
        fade.enabled = true;
        var fadeGradient = new Gradient();
        fadeGradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white * .5f, .5f),
                new GradientColorKey(Color.black, 1f) },
            new[] { new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(.5f, .5f), new GradientAlphaKey(0f, 1f) });
        fade.color = fadeGradient;
        var particleRenderer = root.GetComponent<ParticleSystemRenderer>();
        particleRenderer.sharedMaterial = sparkMaterial != null ? sparkMaterial : ringMaterial;
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = .4f;
        particleRenderer.lengthScale = 2.2f;
        particleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        particleRenderer.receiveShadows = false;
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var ringObject = new GameObject("Parry ready ring");
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localPosition = Vector3.up * .06f;
        ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = ringMaterial;
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

    // Small burst on each parried body; the big burst belongs to the parry center.
    public void EmitEnemy(EnemyActor enemy)
    {
        if (sparks == null || enemy == null) return;
        CombatTarget target = enemy.GetComponent<CombatTarget>();
        Vector3 center = target != null ? target.CurrentVolume.Center
            : enemy.transform.position + Vector3.up;
        float radius = target != null ? target.CurrentVolume.Radius : .45f;
        EmitAt(CameraFacingSurface(center, radius + .12f), 12, 1.6f, 1f);
    }

    public void EmitCenter(Vector3 center, int count)
    {
        if (sparks == null) return;
        EmitAt(CameraFacingSurface(center, .1f), Mathf.Clamp(count, 1, 96), 2.4f, 1.8f);
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
        Color color = new Color(1f * alpha, .82f * alpha, .38f * alpha, alpha);
        ring.startColor = color;
        ring.endColor = color;
        ring.widthMultiplier = .045f + .015f * pulse;
    }

    private void EmitAt(Vector3 position, int count, float speed, float sizeScale)
    {
        if (!sparks.isPlaying) sparks.Play();
        if (cachedCamera == null) cachedCamera = Camera.main;
        Vector3 right = cachedCamera != null ? cachedCamera.transform.right : Vector3.right;
        Vector3 up = cachedCamera != null ? cachedCamera.transform.up : Vector3.up;
        for (int i = 0; i < count; i++)
        {
            float angle = Mathf.PI * 2f * (i + .17f * (i % 3)) / count;
            Vector3 direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            var particle = new ParticleSystem.EmitParams
            {
                position = position + direction * .10f,
                velocity = direction * speed * (1.18f + (i % 4) * .18f),
                startLifetime = .14f + (i % 4) * .035f,
                startSize = (i % 5 == 0 ? .11f : .065f) * sizeScale,
                startColor = i % 3 == 0
                    ? new Color(1f, 1f, .92f, 1f)
                    : new Color(1f, .72f, .28f, 1f)
            };
            sparks.Emit(particle, 1);
        }
    }

    private Vector3 CameraFacingSurface(Vector3 center, float distance)
    {
        if (cachedCamera == null) cachedCamera = Camera.main;
        if (cachedCamera == null) return center;
        Vector3 towardCamera = cachedCamera.transform.position - center;
        towardCamera.y = 0f;
        return towardCamera.sqrMagnitude > .0001f
            ? center + towardCamera.normalized * distance : center;
    }

    private void OnDisable()
    {
        readyWeight = 0f;
        if (ring != null) ring.enabled = false;
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
