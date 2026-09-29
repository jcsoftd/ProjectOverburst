using System.Collections;
using UnityEngine;

// One player-owned emitter renders all enemies in a multi-parry without spawning per-hit objects.
[DisallowMultipleComponent]
public sealed class ParrySuccessVfx : MonoBehaviour
{
    private const int RingSegments = 64;
    private ParticleSystem sparks;
    private LineRenderer ring;
    private Coroutine ringRoutine;
    private Camera cachedCamera;

    private void Awake()
    {
        var root = new GameObject("Parry success visual");
        root.transform.SetParent(transform, false);
        var ringMaterial = Resources.Load<Material>("Feel/MAT_OverburstFeelParticles");
        var sparkMaterial = Resources.Load<Material>("Combat/VFX/MAT_OverburstParrySparks");

        sparks = root.AddComponent<ParticleSystem>();
        var main = sparks.main;
        main.playOnAwake = false;
        main.loop = false;
        main.duration = .35f;
        main.startLifetime = .26f;
        main.startSpeed = 0f;
        main.startSize = .10f;
        main.startColor = Color.white;
        main.maxParticles = 256;
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

        var ringObject = new GameObject("Parry success wave");
        ringObject.transform.SetParent(root.transform, false);
        ringObject.transform.localPosition = Vector3.up * .08f;
        ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = ringMaterial;
        ring.useWorldSpace = false;
        ring.alignment = LineAlignment.View;
        ring.positionCount = RingSegments + 1;
        ring.numCapVertices = 2;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        ring.enabled = false;
    }

    public void EmitEnemy(EnemyActor enemy)
    {
        if (sparks == null || enemy == null) return;
        CombatTarget target = enemy.GetComponent<CombatTarget>();
        Vector3 center = target != null ? target.CurrentVolume.Center
            : enemy.transform.position + Vector3.up;
        float radius = target != null ? target.CurrentVolume.Radius : .45f;
        EmitAt(CameraFacingSurface(center, radius + .12f), 14, 1.6f);
    }

    public void Pulse()
    {
        if (sparks == null || ring == null) return;
        EmitAt(CameraFacingSurface(transform.position + Vector3.up, .48f), 10, 1.1f);
        if (ringRoutine != null) StopCoroutine(ringRoutine);
        ringRoutine = StartCoroutine(AnimateRing());
    }

    private void EmitAt(Vector3 position, int count, float speed)
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
                startSize = i % 5 == 0 ? .11f : .065f,
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

    private IEnumerator AnimateRing()
    {
        const float duration = .24f;
        float elapsed = 0f;
        ring.enabled = true;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float radius = Mathf.Lerp(.30f, 2.20f, 1f - (1f - t) * (1f - t));
            float alpha = (1f - t) * (1f - t);
            Color color = new Color(alpha, .88f * alpha, .52f * alpha, alpha);
            ring.startColor = color;
            ring.endColor = color;
            ring.widthMultiplier = Mathf.Lerp(.075f, .015f, t);
            for (int i = 0; i <= RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(i, new Vector3(Mathf.Sin(angle) * radius, 0f,
                    Mathf.Cos(angle) * radius));
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        ring.enabled = false;
        ringRoutine = null;
    }

    private void OnDisable()
    {
        if (ringRoutine != null) StopCoroutine(ringRoutine);
        ringRoutine = null;
        if (ring != null) ring.enabled = false;
        if (sparks != null) sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
