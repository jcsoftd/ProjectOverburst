using UnityEngine;
using UnityEngine.Rendering;

// One retained gathering effect per equipped weapon. Native elemental blade FX stay untouched.
public sealed class DashHeavyFocusPresentation : MonoBehaviour
{
    public const string MaterialResource = "Combat/VFX/DashHeavyFocus";
    public const string HeadMaterialResource = "Combat/VFX/DashHeavyFocusHead";
    public const string HeadMeshResource = "Combat/VFX/DashHeavyFocusHeadMesh";
    private const int LightCount = 24, PointCount = 14;
    private static Material focusMaterial, headMaterial;
    private static Mesh headMesh;
    private static AudioClip gatherClip, releaseClip;
    private static readonly int TintId = Shader.PropertyToID("_Tint"), GlintId = Shader.PropertyToID("_Glint");
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPrepared() { focusMaterial = headMaterial = null; headMesh = null; gatherClip = releaseClip = null; }
    public static void Prepare()
    {
        if (focusMaterial == null) focusMaterial = Resources.Load<Material>(MaterialResource);
        if (headMaterial == null) headMaterial = Resources.Load<Material>(HeadMaterialResource);
        if (headMesh == null) headMesh = Resources.Load<Mesh>(HeadMeshResource);
        if (gatherClip == null) gatherClip = CombatActionSfxService.ResolveNamedClip("DashHeavyGather");
        if (releaseClip == null) releaseClip = CombatActionSfxService.ResolveNamedClip("DashHeavyRelease");
        if (gatherClip != null && gatherClip.loadState == AudioDataLoadState.Unloaded) gatherClip.LoadAudioData();
        if (releaseClip != null && releaseClip.loadState == AudioDataLoadState.Unloaded) releaseClip.LoadAudioData();
    }
    public static void Prepare(PlayerEquipment owner) { Ensure(owner); }
    private PlayerEquipment equipment;
    private CombatHealth health;
    private Transform weaponRoot;
    private MeleeWeaponElementFx blade;
    private readonly LineRenderer[] wisps = new LineRenderer[LightCount];
    private readonly MeshRenderer[] heads = new MeshRenderer[LightCount];
    private readonly MaterialPropertyBlock[] blocks = new MaterialPropertyBlock[LightCount];
    private readonly Vector3[] origins = new Vector3[LightCount], bends = new Vector3[LightCount];
    private readonly bool[] spawned = new bool[LightCount];
    private readonly float[] arrivedAt = new float[LightCount];
    private const float ArrivalFadeDuration = .065f;
    private AudioSource gather, release;
    private Camera view;
    private QuarterViewCamera focusCamera;
    private HeavyFocusWindow window;
    private Color elementColor;
    private float sourceSeconds = -1f, focusStartedAt = -1f, gatherEndsAt;
    private string weaponId;
    private bool initialized;
    private bool ownsSlow;

    private static DashHeavyFocusPresentation Ensure(PlayerEquipment owner)
    {
        if (owner == null || owner.CurrentWeaponRoot == null) return null;
        var effect = owner.CurrentWeaponRoot.GetComponentInChildren<DashHeavyFocusPresentation>(true);
        if (effect != null) return effect;
        Prepare();
        if (focusMaterial == null || headMaterial == null || headMesh == null) return null;
        var root = new GameObject("HeavyBladeGathering"); root.transform.SetParent(owner.CurrentWeaponRoot, false);
        effect = root.AddComponent<DashHeavyFocusPresentation>();
        effect.equipment = owner; effect.health = owner.GetComponent<CombatHealth>();
        effect.weaponRoot = owner.CurrentWeaponRoot;
        effect.blade = effect.weaponRoot.GetComponentInChildren<MeleeWeaponElementFx>(true);
        for (int i = 0; i < LightCount; i++)
        {
            var line = new GameObject("GatherTail" + i).AddComponent<LineRenderer>(); line.transform.SetParent(root.transform, false);
            line.useWorldSpace = true; line.positionCount = PointCount; line.sharedMaterial = focusMaterial;
            line.widthMultiplier = .018f + .004f * (i % 3);
            line.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(.16f, .28f), new Keyframe(.70f, .65f), new Keyframe(.91f, .78f), new Keyframe(1, .35f));
            line.textureMode = LineTextureMode.Stretch; line.numCapVertices = 0; SetupRenderer(line); line.enabled = false;
            effect.wisps[i] = line;
            var head = new GameObject("GatherLight" + i, typeof(MeshFilter), typeof(MeshRenderer)); head.transform.SetParent(root.transform, false);
            head.GetComponent<MeshFilter>().sharedMesh = headMesh;
            var renderer = head.GetComponent<MeshRenderer>(); renderer.sharedMaterial = headMaterial; SetupRenderer(renderer); renderer.enabled = false;
            effect.heads[i] = renderer; effect.blocks[i] = new MaterialPropertyBlock();
        }
        effect.gather = effect.MakeVoice("Gather", .28f); effect.release = effect.MakeVoice("Release", .32f);
        effect.initialized = true; effect.enabled = false;
        return effect;
    }
    public static DashHeavyFocusPresentation Create(PlayerEquipment owner, WeaponElement element)
        => Create(owner, element, HeavyFocusWindow.Dash);
    public static DashHeavyFocusPresentation Create(PlayerEquipment owner, WeaponElement element, HeavyFocusWindow timing)
    {
        var effect = Ensure(owner); if (effect == null) return null;
        effect.Dispose(); effect.weaponId = owner.CurrentWeaponItem?.runtimeInstanceId;
        effect.elementColor = ColorFor(element); effect.window = timing; effect.sourceSeconds = effect.focusStartedAt = -1f;
        effect.view = Camera.main; effect.focusCamera = QuarterViewCamera.ActiveInstance;
        System.Array.Clear(effect.spawned, 0, effect.spawned.Length);
        for (int i = 0; i < LightCount; i++) effect.arrivedAt[i] = -1f;
        effect.enabled = true;
        return effect;
    }
    public const float MinimumEnergyFraction = .8f;
    public static bool CanBegin(PlayerEquipment owner, ElementGemAttackSnapshot attack)
    {
        var energy = owner != null ? owner.GetComponent<OverburstElementEnergy>() : null;
        return attack.HasValue && attack.IsCurrent && energy != null && energy.isActiveAndEnabled
            && energy.WeaponInstanceId == attack.WeaponId && energy.GemInstanceId == attack.GemId
            && energy.GemRevision == attack.GemRevision && energy.Element == attack.Element
            && OverburstElementRules.IsActive(attack.Element) && energy.Normalized >= MinimumEnergyFraction;
    }
    // Gathering lights use the original Soft02 neutral tint for every element.
    public static Color ColorFor(WeaponElement element) => new Color(1f, .985f, .955f);
    private static void SetupRenderer(Renderer renderer)
    {
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }
    private AudioSource MakeVoice(string voiceName, float volume)
    {
        var voice = new GameObject(voiceName).AddComponent<AudioSource>(); voice.transform.SetParent(transform, false);
        voice.playOnAwake = false; voice.volume = volume; voice.spatialBlend = .6f; voice.minDistance = 3f; voice.maxDistance = 24f;
        voice.dopplerLevel = 0f; voice.priority = 90; return voice;
    }
    public void Tick(float clipSeconds)
    {
        sourceSeconds = clipSeconds;
        if (!Application.isPlaying || !enabled) return;
        if (focusStartedAt == -1f && clipSeconds >= window.Start)
            focusStartedAt = clipSeconds < window.End ? OverburstGameClock.UnscaledTime : -2f;
        float scale = window.PulseScale(focusStartedAt >= 0f ? OverburstGameClock.UnscaledTime - focusStartedAt : -1f);
        if (scale < .99999f) ownsSlow |= OverburstTimeEffectArbiter.SetContinuous(this, OverburstTimeEffectKind.HeavyFocus, scale, .15f);
        else if (ownsSlow) { OverburstTimeEffectArbiter.ClearOwner(this); ownsSlow = false; }
        focusCamera?.SetHeavyFocusZoom(this, window.Zoom(clipSeconds));
    }
    public void PlayGather(float duration)
    {
        gather.clip = gatherClip; if (gather.clip == null) return;
        gatherEndsAt = OverburstGameClock.UnscaledTime + Mathf.Max(.03f, duration); gather.volume = .28f;
        gather.pitch = Mathf.Clamp(gather.clip.length / Mathf.Max(.03f, duration), .3f, 3f); gather.Play();
    }
    public void PlayRelease()
    {
        gather.Stop(); release.clip = releaseClip; if (release.clip != null) release.Play();
    }
    private void LateUpdate()
    {
        if (equipment == null || !equipment.isActiveAndEnabled || equipment.CurrentWeaponRoot != weaponRoot
            || equipment.CurrentWeaponItem?.runtimeInstanceId != weaponId || (health != null && health.IsDead)) { Dispose(); return; }
        SampleVisual(view);
        if (gather.isPlaying)
        {
            float remaining = gatherEndsAt - OverburstGameClock.UnscaledTime;
            gather.volume = .28f * Mathf.Clamp01(remaining / .025f); if (remaining <= 0f) gather.Stop();
        }
    }
    // Also used by native preview rendering: the production geometry is sampled after the animated pose.
    public void SampleVisual(Camera camera)
    {
        if (!initialized) return;
        bool shown = sourceSeconds >= window.Start;
        if (!shown || camera == null) { HideVisuals(); return; }
        Vector3 tip = equipment.CurrentWeaponTraceBinding?.WeaponTip?.position ?? transform.position, bottom = tip - transform.forward;
        if (blade != null) blade.TryGetBladeEndpoints(out bottom, out tip);
        Vector3 axis = (tip - bottom).normalized, right = Vector3.Cross(axis, Vector3.up).normalized;
        if (right.sqrMagnitude < .01f) right = Vector3.right;
        Vector3 up = Vector3.Cross(axis, right).normalized;
        float progress = window.Gather(sourceSeconds);
        float now = Application.isPlaying ? OverburstGameClock.UnscaledTime : sourceSeconds;
        for (int i = 0; i < LightCount; i++)
        {
            float seed = Mathf.Repeat(Mathf.Sin(i * 12.9898f + 2.7f) * 43758.5453f, 1f);
            float delay = .015f + .11f * seed, arrival = .87f + .125f * Mathf.Repeat(seed * 2.37f, .999f);
            float local = (progress - delay) / (arrival - delay);
            if (local >= 1f && arrivedAt[i] < 0f) arrivedAt[i] = now;
            float arrivalAge = arrivedAt[i] >= 0f ? Mathf.Clamp01((now - arrivedAt[i]) / ArrivalFadeDuration) : 0f;
            bool visible = local >= 0f && arrivalAge < 1f;
            wisps[i].enabled = heads[i].enabled = visible; if (!visible) continue;
            float angle = i * 2.39996323f + seed * .32f;
            Vector3 target = Vector3.Lerp(bottom, tip, .22f + .624f * i / (LightCount - 1f));
            if (!spawned[i])
            {
                origins[i] = target + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * (1f + .5f * seed) + axis * (seed - .5f) * .18f;
                origins[i].y = Mathf.Max(equipment.transform.position.y + .45f, origins[i].y);
                bends[i] = (right * Mathf.Sin(angle) + up * Mathf.Cos(angle)) * (.08f + .10f * seed); spawned[i] = true;
            }
            float tail = .060f + .014f * seed;
            // Reach the moving blade first, then collapse the remaining tail and fade at the blade.
            local = Mathf.Min(local, 1f + tail * Smooth(arrivalAge));
            for (int j = 0; j < PointCount; j++) wisps[i].SetPosition(j, Point(origins[i], target, bends[i], local - tail * (PointCount - 1 - j) / (PointCount - 1f)));
            float alpha = Smooth(local / .13f) * (1f - Smooth(arrivalAge));
            Color color = elementColor; color.a = alpha * .58f;
            wisps[i].startColor = new Color(color.r, color.g, color.b, 0f); wisps[i].endColor = color;
            float size = (.081f + .071f * seed) * (1f + .14f * Smooth((local - .85f) / .12f));
            // World size is independent of the supplier weapon's transform scale.
            var head = heads[i].transform; head.SetPositionAndRotation(Point(origins[i], target, bends[i], local), camera.transform.rotation);
            Vector3 parentScale = head.parent.lossyScale;
            head.localScale = new Vector3(size / Mathf.Max(.0001f, Mathf.Abs(parentScale.x)), size / Mathf.Max(.0001f, Mathf.Abs(parentScale.y)), size / Mathf.Max(.0001f, Mathf.Abs(parentScale.z)));
            color.a = alpha * (.60f + .24f * seed); blocks[i].SetColor(TintId, color);
            blocks[i].SetFloat(GlintId, (i % 3 == 0 ? .24f : 0f) * Smooth((local - .62f) / .25f)); heads[i].SetPropertyBlock(blocks[i]);
        }
    }
    private static float Smooth(float value) { value = Mathf.Clamp01(value); return value * value * (3f - 2f * value); }
    private static Vector3 Point(Vector3 origin, Vector3 target, Vector3 bend, float progress)
    {
        progress = Mathf.Clamp01(progress); return Vector3.Lerp(origin, target, progress * progress * progress) + bend * Mathf.Sin(Mathf.PI * progress) * .65f;
    }
    private void HideVisuals() { for (int i = 0; i < LightCount; i++) { if (wisps[i] != null) wisps[i].enabled = false; if (heads[i] != null) heads[i].enabled = false; } }
    private void ClearPresentation()
    {
        if (gather != null) gather.Stop(); if (release != null) release.Stop(); HideVisuals();
        if (Application.isPlaying) { if (ownsSlow) OverburstTimeEffectArbiter.ClearOwner(this); focusCamera?.ReleaseHeavyFocusZoom(this); }
        ownsSlow = false;
    }
    public void Dispose() { ClearPresentation(); enabled = false; }
    private void PauseAudio(bool paused)
    {
        if (!initialized) return;
        if (paused) { gather.Pause(); release.Pause(); } else { gather.UnPause(); release.UnPause(); }
    }
    private void OnEnable() { if (Application.isPlaying) OverburstTimeEffectArbiter.PauseChanged += PauseAudio; }
    private void OnDisable() { OverburstTimeEffectArbiter.PauseChanged -= PauseAudio; ClearPresentation(); }
    private void OnDestroy() { OverburstTimeEffectArbiter.PauseChanged -= PauseAudio; ClearPresentation(); }
}
