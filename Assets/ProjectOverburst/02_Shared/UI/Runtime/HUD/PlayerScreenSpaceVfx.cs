using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(10002)]
[DisallowMultipleComponent]
public sealed class PlayerScreenSpaceVfx : MonoBehaviour
{
    public const string ResourcePath = "Combat/VFX/PF_PlayerScreenSpaceVfx";
    public const float DamageDuration = .38f, HealDuration = .75f;
    public const float LowHealthEnter = .30f, LowHealthExit = .33f;
    public static readonly Color HealingColor = new Color(.18f, 1f, .28f, 1f);
    static PlayerScreenSpaceVfx instance;
    [SerializeField] ParticleSystem damage, healing, lowHealth;
    readonly ParticleSystem.Particle[] particle = new ParticleSystem.Particle[1];
    PlayerContext context;
    CombatHealth health;
    Camera outputCamera;
    float damageStart = -100f, healStart = -100f, smallHealStart = -100f;
    bool lowHealthActive;
    public bool DamageVisible { get; private set; }
    public bool HealingVisible { get; private set; }
    public bool LowHealthVisible { get; private set; }
    public Camera OutputCamera => outputCamera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() => instance = null;

    public static void Ensure()
    {
        if (instance != null || !Application.isPlaying) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        var owner = PlayerContext.GetOrCreate();
        if (prefab != null && owner != null) Instantiate(prefab, owner.transform);
    }
    public static bool Handles(CombatHealth target)
        => instance != null && instance.CanPresent && instance.health == target;
    public static bool TryDamage()
    {
        Ensure();
        if (instance == null || !instance.CanPresent) return false;
        if (OverburstGameSettings.HitEffectScale > 0f)
        {
            instance.damageStart = OverburstGameClock.UnscaledTime;
            Restart(instance.damage);
        }
        return true;
    }
    public static bool TryHealing()
    {
        Ensure();
        if (instance == null || !instance.CanPresent) return false;
        instance.Healed(instance.health, instance.health.MaxHp);
        return true;
    }
    bool CanPresent => isActiveAndEnabled && damage != null && healing != null && lowHealth != null
        && health != null && health.isActiveAndEnabled && !health.IsDead && health.CurrentHp > 0f
        && outputCamera != null && outputCamera.isActiveAndEnabled
        && (WorldSessionState.Phase == WorldPhase.Hideout || WorldSessionState.Phase == WorldPhase.Run)
        && (PersistentSceneFlow.Instance == null || !PersistentSceneFlow.Instance.IsSwitching);

    void Awake() { instance = this; Clear(); }
    void OnEnable()
    {
        context = PlayerContext.GetOrCreate();
        if (context != null) { context.CurrentActorChanged += Bind; Bind(context.CurrentActor); }
        WorldSessionState.Changed += WorldChanged;
        RenderPipelineManager.beginCameraRendering += BeforeCamera;
        RenderPipelineManager.endCameraRendering += AfterCamera;
    }
    void Bind(PlayerActorRuntime actor)
    {
        if (health != null) { health.OnHealed -= Healed; health.OnReset -= ResetHealth; }
        Clear(); health = actor != null ? actor.Health : null;
        if (health != null) { health.OnHealed += Healed; health.OnReset += ResetHealth; }
    }
    void Healed(CombatHealth target, float amount)
    {
        if (target != health || !CanPresent || amount <= 0f) return;
        float now = OverburstGameClock.UnscaledTime;
        // Continuous regeneration and frequent small lifesteal retain a mild edge.
        if (amount >= health.MaxHp * .01f) healStart = now;
        else smallHealStart = now;
        if (healing.particleCount == 0) Restart(healing);
    }
    void ResetHealth(CombatHealth target) => Clear();
    void WorldChanged(WorldPhase phase) => Clear();
    static void Restart(ParticleSystem effect)
    {
        if (effect == null) return;
        effect.Simulate(.001f, false, true, false);
        effect.Pause(false);
    }
    void LateUpdate()
    {
        var rig = QuarterViewCamera.ActiveInstance;
        var next = rig != null ? (rig.CinemachineRig != null ? rig.CinemachineRig.OutputCamera : rig.GetComponent<Camera>()) : null;
        if (outputCamera != next) { Clear(); outputCamera = next; }
        var actor = context != null ? context.CurrentActor : null;
        if (health != (actor != null ? actor.Health : null)) Bind(actor);
        if (!CanPresent) { Clear(); return; }
        if (lowHealthActive ? health.NormalizedHp > LowHealthExit : health.NormalizedHp <= LowHealthEnter)
            lowHealthActive = !lowHealthActive;
        float now = OverburstGameClock.UnscaledTime;
        float hit = Fade(now - damageStart, DamageDuration) * OverburstGameSettings.HitEffectScale;
        float heal = Mathf.Max(.60f * Fade(now - healStart, HealDuration), .18f * Fade(now - smallHealStart, HealDuration));
        DamageVisible = hit > 0f;
        HealingVisible = !DamageVisible && heal > 0f;
        LowHealthVisible = !DamageVisible && !HealingVisible && lowHealthActive && OverburstGameSettings.HitEffectScale > 0f;
        Present(damage, DamageVisible ? hit : 0f, true);
        Present(healing, HealingVisible ? heal : 0f, false);
        Present(lowHealth, LowHealthVisible ? .23f * OverburstGameSettings.HitEffectScale : 0f, false);
    }
    static float Fade(float elapsed, float duration)
        => elapsed < 0f || elapsed >= duration ? 0f : 1f - Mathf.SmoothStep(0f, 1f, elapsed / duration);
    void Present(ParticleSystem effect, float alpha, bool animate)
    {
        if (alpha <= 0f) { effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); return; }
        if (effect.particleCount == 0) Restart(effect);
        if (animate && OverburstGameClock.UnscaledDeltaTime > 0f)
        { effect.Simulate(OverburstGameClock.UnscaledDeltaTime, false, false, false); effect.Pause(false); }
        int count = effect.GetParticles(particle);
        if (count > 0)
        {
            Color tint = effect == healing ? HealingColor : Color.white;
            tint.a = Mathf.Clamp01(alpha);
            particle[0].startColor = tint; effect.SetParticles(particle, count);
        }
    }
    void BeforeCamera(ScriptableRenderContext renderContext, Camera camera)
    {
        bool visible = camera == outputCamera && CanPresent && !OverburstTimeEffectArbiter.IsPaused;
        if (visible)
        {
            transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
            float depth = camera.nearClipPlane + .05f;
            float height = camera.orthographic ? 2f * camera.orthographicSize : 2f * depth * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
            Fit(damage, depth, height, camera.aspect); Fit(healing, depth, height, camera.aspect); Fit(lowHealth, depth, height, camera.aspect);
        }
        SetRenderer(damage, visible && DamageVisible); SetRenderer(healing, visible && HealingVisible); SetRenderer(lowHealth, visible && LowHealthVisible);
    }
    static void Fit(ParticleSystem effect, float depth, float height, float aspect)
    { effect.transform.localPosition = Vector3.forward * depth; effect.transform.localScale = new Vector3(height * aspect, height, 1f); }
    static void SetRenderer(ParticleSystem effect, bool visible)
    { if (effect != null) effect.GetComponent<ParticleSystemRenderer>().enabled = visible; }
    void AfterCamera(ScriptableRenderContext renderContext, Camera camera)
    { SetRenderer(damage, false); SetRenderer(healing, false); SetRenderer(lowHealth, false); }
    void Clear()
    {
        damageStart = healStart = smallHealStart = -100f; lowHealthActive = false;
        DamageVisible = HealingVisible = LowHealthVisible = false;
        StopAndHide(damage); StopAndHide(healing); StopAndHide(lowHealth);
    }
    static void StopAndHide(ParticleSystem effect)
    { if (effect != null) { effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); SetRenderer(effect, false); } }
    void OnDisable()
    {
        if (context != null) context.CurrentActorChanged -= Bind;
        if (health != null) { health.OnHealed -= Healed; health.OnReset -= ResetHealth; }
        WorldSessionState.Changed -= WorldChanged;
        RenderPipelineManager.beginCameraRendering -= BeforeCamera; RenderPipelineManager.endCameraRendering -= AfterCamera;
        Clear(); health = null; context = null;
    }
    void OnDestroy() { if (instance == this) instance = null; }
}
