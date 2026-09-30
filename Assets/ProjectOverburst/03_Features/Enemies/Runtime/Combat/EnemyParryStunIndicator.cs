using UnityEngine;

// 패링 기절 동안 머리 위를 도는 별 4개와 옅은 원형 궤적. 고정 슬롯을 재사용해 기절마다 생성하지 않는다.
// 기절이 끝나거나 사망·비활성이면 0.15초에 걸쳐 사라진다.
[DefaultExecutionOrder(200)]
public sealed class EnemyParryStunIndicator : MonoBehaviour
{
    private const int Capacity = 24;
    private const int Stars = 4;
    private const int RingSegments = 28;
    private const float FadeSeconds = .15f;
    private const float TurnsPerSecond = .9f;

    private sealed class Slot
    {
        public GameObject root;
        public ParticleSystem stars;
        public LineRenderer ring;
        public EnemyMovementReaction reaction;
        public CombatTarget target;
        public EnemyActor actor;
        public float fadeStartedAt = -1f;
        public float phase;
        public bool active;
    }

    private static EnemyParryStunIndicator instance;
    private readonly Slot[] slots = new Slot[Capacity];
    private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[Stars];
    private Material starMaterial;
    private Material ringMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;

    public static void Show(EnemyActor enemy)
    {
        if (!Application.isPlaying || enemy == null) return;
        if (instance == null)
        {
            var host = new GameObject(nameof(EnemyParryStunIndicator));
            DontDestroyOnLoad(host);
            instance = host.AddComponent<EnemyParryStunIndicator>();
        }
        instance.Attach(enemy);
    }

    private void Awake()
    {
        var library = Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
        starMaterial = library != null ? library.ParryGlint : null;
        ringMaterial = Resources.Load<Material>("Feel/MAT_OverburstFeelParticles");
    }

    private void Attach(EnemyActor enemy)
    {
        Slot free = null;
        for (int i = 0; i < slots.Length; i++)
        {
            Slot slot = slots[i];
            if (slot != null && slot.active && slot.actor == enemy)
            {
                slot.fadeStartedAt = -1f; // 기절 연장: 같은 슬롯을 그대로 쓴다
                return;
            }
            if (free == null && (slot == null || !slot.active)) free = slot ?? (slots[i] = CreateSlot(i));
        }
        if (free == null) return; // 동시 기절 표시 상한. 판정·기절 자체에는 영향 없음
        free.actor = enemy;
        free.reaction = enemy.GetComponent<EnemyMovementReaction>();
        free.target = enemy.GetComponent<CombatTarget>();
        free.fadeStartedAt = -1f;
        free.phase = Random.value;
        free.active = true;
        free.root.SetActive(true);
        free.stars.Clear();
        free.stars.Play();
        free.stars.Emit(Stars);
    }

    private Slot CreateSlot(int index)
    {
        var root = new GameObject("Parry stun indicator " + index);
        root.transform.SetParent(transform, false);
        var stars = root.AddComponent<ParticleSystem>();
        stars.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = stars.main;
        main.playOnAwake = false;
        main.loop = true;
        main.startLifetime = 1000f;
        main.startSpeed = 0f;
        main.startSize = .24f;
        main.maxParticles = Stars;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = stars.emission;
        emission.rateOverTime = 0f;
        var shape = stars.shape;
        shape.enabled = false;
        var renderer = root.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = starMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        var ringObject = new GameObject("Orbit");
        ringObject.transform.SetParent(root.transform, false);
        var ring = ringObject.AddComponent<LineRenderer>();
        ring.sharedMaterial = ringMaterial;
        ring.useWorldSpace = true;
        ring.loop = true;
        ring.alignment = LineAlignment.View;
        ring.positionCount = RingSegments;
        ring.widthMultiplier = .025f;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        root.SetActive(false);
        return new Slot { root = root, stars = stars, ring = ring };
    }

    private void LateUpdate()
    {
        float now = Time.time;
        for (int s = 0; s < slots.Length; s++)
        {
            Slot slot = slots[s];
            if (slot == null || !slot.active) continue;
            bool alive = slot.actor != null && slot.actor.isActiveAndEnabled && slot.actor.IsLeased
                && slot.actor.Health != null && !slot.actor.Health.IsDead && slot.target != null;
            if (!alive) { Release(slot); continue; }
            if (slot.fadeStartedAt < 0f && (slot.reaction == null || !slot.reaction.IsParryStunned))
                slot.fadeStartedAt = now;
            float alpha = slot.fadeStartedAt < 0f ? 1f
                : 1f - Mathf.Clamp01((now - slot.fadeStartedAt) / FadeSeconds);
            if (alpha <= 0f) { Release(slot); continue; }

            CombatTargetVolume volume = slot.target.CurrentVolume;
            Vector3 head = volume.Center + Vector3.up * (volume.HalfHeight + .35f);
            float orbit = Mathf.Clamp(volume.Radius * .6f, .3f, .8f);
            float turn = (now * TurnsPerSecond + slot.phase) * Mathf.PI * 2f;

            int count = slot.stars.GetParticles(particles);
            for (int i = 0; i < count; i++)
            {
                float angle = turn + i * Mathf.PI * .5f;
                float twinkle = .8f + .2f * Mathf.Sin(now * 14f + i * 1.7f);
                particles[i].position = head + new Vector3(Mathf.Cos(angle) * orbit, 0f,
                    Mathf.Sin(angle) * orbit * .55f);
                particles[i].startSize = .24f * twinkle;
                particles[i].startColor = new Color(1f, .86f, .42f, alpha);
                particles[i].remainingLifetime = 1000f;
            }
            slot.stars.SetParticles(particles, count);

            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                slot.ring.SetPosition(i, head + new Vector3(Mathf.Cos(angle) * orbit, 0f,
                    Mathf.Sin(angle) * orbit * .55f));
            }
            Color ringColor = new Color(1f, .82f, .40f, .45f * alpha);
            slot.ring.startColor = ringColor;
            slot.ring.endColor = ringColor;
        }
    }

    private static void Release(Slot slot)
    {
        slot.active = false;
        slot.actor = null;
        slot.reaction = null;
        slot.target = null;
        slot.stars.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        slot.root.SetActive(false);
    }
}
