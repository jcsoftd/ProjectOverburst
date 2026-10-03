using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>대시 이동 중 실제 자세를 복사하고, 접지한 발밑에 짧은 먼지를 남긴다.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(PlayerEvadeController)), DefaultExecutionOrder(400)]
public sealed class PlayerDashVfx : MonoBehaviour
{
    [Header("대시 잔상")]
    [SerializeField] Material afterimageMaterial;
    [SerializeField] Color explorationTint = new Color(.65f, .82f, .91f, .14f);
    [SerializeField] Color combatTint = new Color(.64f, .84f, 1f, .22f);
    [SerializeField, Min(.03f)] float sampleInterval = .065f;
    [SerializeField, Min(.1f)] float minimumSpacing = .45f;
    [SerializeField, Range(.08f, .5f)] float afterimageLifetime = .22f;
    [Header("1번 자세 잔상 색상 비교")]
    [SerializeField, Range(0, 6)] int colorStyle;
    [SerializeField] bool fullEnergyHeavyAfterimage = true;
    [Header("발밑 먼지")]
    [SerializeField] Material dustMaterial;
    [SerializeField] Color dustTint = new Color(.53f, .48f, .39f, .18f);

    const int FrameCapacity = 4, PieceLimit = 24;
    static readonly int TintId = Shader.PropertyToID("_Tint");
    static readonly int AccentTintId = Shader.PropertyToID("_AccentTint");
    readonly Frame[] frames = new Frame[FrameCapacity];
    readonly List<Renderer> sources = new List<Renderer>(PieceLimit);
    readonly RaycastHit[] groundHits = new RaycastHit[12];
    MaterialPropertyBlock properties;
    readonly System.Random visualRandom = new System.Random();
    PlayerEvadeController evade;
    MeleeRuntime melee;
    OverburstElementEnergy energy;
    GameObject root;
    ParticleSystem dust;
    bool emitting, dustPaused;
    int nextFrame;
    float nextSample;
    Vector3 lastSamplePosition;
    Color activeTint, activeAccent;
    bool previousHeavy, emittingHeavy;

    sealed class Piece
    {
        public Transform transform;
        public MeshRenderer renderer;
        public MeshFilter filter;
        public Mesh baked;
        public int materialCount;
    }
    sealed class Frame
    {
        public readonly List<Piece> pieces = new List<Piece>();
        public float born;
        public Color tint, accent;
        public bool active;
    }

    public int CapturedAfterimageCount { get; private set; }
    public int EmittedDustCount { get; private set; }
    public int ColorStyle => colorStyle;
    public Color CurrentTint => activeTint;
    public Color CurrentAccent => activeAccent;
    public Color GroundDustTint => dustTint;
    public static readonly string[] ColorNames = {
        "기본 청회색", "흰색 + 밝은 노랑", "공용 백색", "공용 은회색", "공용 옅은 민트", "공용 옅은 라벤더", "장착 원소 반영"
    };
    public void SetColorStyle(int value)
    {
        Clear();
        colorStyle = Mathf.Clamp(value, 0, ColorNames.Length - 1);
    }
    Color ResolveTint(Color original)
    {
        if (colorStyle == 0) return original;
        Color color;
        if (colorStyle == 6)
        {
            var equipment = GetComponent<PlayerEquipment>();
            switch (equipment != null ? equipment.ActiveElement : WeaponElement.None)
            {
                case WeaponElement.Fire: color = new Color(1f, .3f, .1f); break;
                case WeaponElement.Ice: color = new Color(.42f, .87f, 1f); break;
                case WeaponElement.Electric: color = new Color(.59f, .53f, 1f); break;
                case WeaponElement.Dark: color = new Color(.42f, .13f, .25f); break;
                case WeaponElement.Light: color = new Color(1f, .91f, .56f); break;
                default: return original;
            }
        }
        else switch (colorStyle)
        {
            case 1: color = new Color(1f, .99f, .94f); break;
            case 2: color = new Color(.95f, .97f, 1f); break;
            case 3: color = new Color(.73f, .77f, .82f); break;
            case 4: color = new Color(.67f, .95f, .84f); break;
            default: color = new Color(.83f, .77f, 1f); break;
        }
        color.a = original.a;
        return color;
    }
    public int ActiveAfterimageCount
    {
        get { int count = 0; foreach (var frame in frames) if (frame != null && frame.active) count++; return count; }
    }
    public int PooledMeshCount
    {
        get { int count = 0; foreach (var frame in frames) if (frame != null) count += frame.pieces.Count; return count; }
    }

    void OnEnable()
    {
        if(properties==null)properties=new MaterialPropertyBlock();
        evade = GetComponent<PlayerEvadeController>();
        melee = GetComponent<MeleeRuntime>(); energy = GetComponent<OverburstElementEnergy>();
        evade.OnEvadeStarted += Started;
        evade.OnEvadeEnded += Ended;
    }
    void OnDisable()
    {
        if (evade != null) { evade.OnEvadeStarted -= Started; evade.OnEvadeEnded -= Ended; }
        Clear();
    }
    void Started(PlayerEvadeType type)
    {
        if (type != PlayerEvadeType.ExplorationDodge && type != PlayerEvadeType.CombatDodge) return;
        if (afterimageMaterial == null || dustMaterial == null) return;
        emittingHeavy = false;
        Begin(type == PlayerEvadeType.CombatDodge ? combatTint : explorationTint);
        EmitDust(4);
    }
    void Begin(Color tint)
    {
        EnsureRoot();
        if(dust!=null){dust.Play();dustPaused=false;}
        sources.Clear();
        var animator = GetComponentInChildren<Animator>();
        if (animator != null)
            foreach (var renderer in animator.GetComponentsInChildren<Renderer>())
            {
                if (sources.Count >= PieceLimit) break;
                if (!renderer.enabled || renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var skin = renderer as SkinnedMeshRenderer;
                var filter = renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>() : null;
                if ((skin != null && skin.sharedMesh != null) || (filter != null && filter.sharedMesh != null)) sources.Add(renderer);
            }
        activeTint = ResolveTint(tint);
        activeAccent = colorStyle == 1 ? new Color(1f, .92f, .38f, activeTint.a) : activeTint;
        lastSamplePosition = transform.position;
        nextSample = OverburstGameClock.UnscaledTime + sampleInterval;
        emitting = true;
    }
    void Ended(PlayerEvadeType type)
    {
        if (type != PlayerEvadeType.ExplorationDodge && type != PlayerEvadeType.CombatDodge) return;
        emitting = false;
        if (evade.LastEndWasCompleted) EmitDust(2); else Clear();
    }
    void LateUpdate()
    {
        float now = OverburstGameClock.UnscaledTime;
        bool heavy = melee != null && melee.IsHeavyAttackInProgress;
        if (heavy && energy == null) energy = GetComponent<OverburstElementEnergy>();
        if (heavy && !previousHeavy && fullEnergyHeavyAfterimage && energy != null && energy.Amount >= energy.Capacity - .001f)
        { emittingHeavy=true; Begin(combatTint); }
        if (!heavy && emittingHeavy) { emittingHeavy=false; emitting=false; }
        previousHeavy = heavy;
        bool paused = OverburstGameClock.UnscaledDeltaTime <= 0f || (emittingHeavy && MeleeRuntime.IsHeavyParryClockPaused);
        if (dust != null)
        {
            if (paused != dustPaused) { if (paused) dust.Pause(); else dust.Play(); dustPaused = paused; }
        }
        foreach (var frame in frames)
        {
            if (frame == null || !frame.active) continue;
            float age = Mathf.Clamp01((now - frame.born) / Mathf.Max(.01f, afterimageLifetime));
            if (age >= 1f) { Hide(frame); continue; }
            Color tint = frame.tint;
            tint.a *= (1f - age) * (1f - age);
            properties.SetColor(TintId, tint);
            properties.SetColor(AccentTintId, frame.accent);
            foreach (var piece in frame.pieces) if (piece.renderer.enabled) piece.renderer.SetPropertyBlock(properties);
        }
        if (!emitting || (!emittingHeavy && !evade.IsEvading) || now < nextSample || paused) return;
        if (!emittingHeavy && (transform.position - lastSamplePosition).sqrMagnitude < minimumSpacing * minimumSpacing) return;
        Capture(now);
        if(!emittingHeavy)EmitDust(2);
        lastSamplePosition = transform.position;
        nextSample = now + (emittingHeavy ? .12f : sampleInterval);
    }
    void EnsureRoot()
    {
        if (root != null) return;
        root = new GameObject("Player Dash VFX") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        var dustObject = new GameObject("Dash Ground Dust");
        dustObject.transform.SetParent(root.transform, false);
        dust = dustObject.AddComponent<ParticleSystem>();
        dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = dust.main;
        main.loop = true; main.playOnAwake = false; main.maxParticles = 48;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.useUnscaledTime = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .42f);
        main.startSize = new ParticleSystem.MinMaxCurve(.19f, .34f);
        main.startSpeed = 0f; main.startColor = dustTint; main.gravityModifier = -.04f;
        var emission = dust.emission; emission.enabled = false;
        var shape = dust.shape; shape.enabled = false;
        var size = dust.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, .6f, 1f, 1.7f));
        var color = dust.colorOverLifetime; color.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] {new GradientColorKey(Color.white, 0f),new GradientColorKey(Color.white,1f)},
            new[] {new GradientAlphaKey(0f,0f),new GradientAlphaKey(1f,.12f),new GradientAlphaKey(0f,1f)});
        color.color = new ParticleSystem.MinMaxGradient(gradient);
        var renderer = dust.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = dustMaterial; renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        dust.Play();
    }
    void Capture(float now)
    {
        var frame = frames[nextFrame] ?? (frames[nextFrame] = new Frame());
        nextFrame = (nextFrame + 1) % FrameCapacity;
        Hide(frame);
        int index = 0;
        foreach (var source in sources)
        {
            if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) continue;
            if (index == frame.pieces.Count)
            {
                var item = new GameObject("Dash Pose") { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
                item.transform.SetParent(root.transform, false);
                var piece = new Piece {transform=item.transform, filter=item.AddComponent<MeshFilter>(), renderer=item.AddComponent<MeshRenderer>(),
                    baked=new Mesh {name="Dash Pose Mesh", hideFlags=HideFlags.DontSave}};
                piece.baked.MarkDynamic();
                piece.renderer.shadowCastingMode = ShadowCastingMode.Off; piece.renderer.receiveShadows = false;
                piece.renderer.lightProbeUsage = LightProbeUsage.Off; piece.renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                piece.renderer.enabled = false;
                frame.pieces.Add(piece);
            }
            var target = frame.pieces[index++];
            var skin = source as SkinnedMeshRenderer;
            if (skin != null) { skin.BakeMesh(target.baked, false); target.filter.sharedMesh = target.baked; }
            else target.filter.sharedMesh = source.GetComponent<MeshFilter>().sharedMesh;
            var mesh = target.filter.sharedMesh;
            if (target.materialCount != mesh.subMeshCount)
            {
                var materials = new Material[mesh.subMeshCount];
                for (int i = 0; i < materials.Length; i++) materials[i] = afterimageMaterial;
                target.renderer.sharedMaterials = materials; target.materialCount = materials.Length;
            }
            target.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            target.transform.localScale = source.transform.lossyScale;
            target.renderer.enabled = true;
            properties.SetColor(TintId, activeTint);
            properties.SetColor(AccentTintId, activeAccent);
            target.renderer.SetPropertyBlock(properties);
        }
        frame.active = index > 0; frame.born = now; frame.tint = activeTint; frame.accent = activeAccent;
        if (frame.active) CapturedAfterimageCount++;
    }
    void EmitDust(int count)
    {
        if (dust == null || OverburstGameClock.UnscaledDeltaTime <= 0f) return;
        int hits = Physics.RaycastNonAlloc(transform.position + Vector3.up * .5f, Vector3.down, groundHits, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        bool grounded = false; RaycastHit ground = default;
        for (int i = 0; i < hits; i++)
        {
            var hit = groundHits[i];
            if (hit.collider.transform.IsChildOf(transform) || hit.normal.y < .5f) continue;
            if (!grounded || hit.distance < ground.distance) { grounded = true; ground = hit; }
        }
        if (!grounded) return;
        for (int i = 0; i < count; i++)
        {
            var emit = new ParticleSystem.EmitParams {
                position=ground.point + ground.normal*.035f + new Vector3(RandomValue(-.08f,.08f),0f,RandomValue(-.08f,.08f)),
                velocity=-evade.ActiveDirection*RandomValue(.3f,.7f) + ground.normal*RandomValue(.25f,.45f),
                rotation=RandomValue(0f,360f)};
            dust.Emit(emit,1); EmittedDustCount++;
        }
    }
    static void Hide(Frame frame)
    {
        frame.active = false;
        foreach (var piece in frame.pieces) piece.renderer.enabled = false;
    }
    void Clear()
    {
        emitting = false;
        emittingHeavy=false;previousHeavy=false;
        foreach (var frame in frames) if (frame != null) Hide(frame);
        if (dust != null) dust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dustPaused = false;
    }
    void OnDestroy()
    {
        foreach (var frame in frames) if (frame != null) foreach (var piece in frame.pieces) DestroyOwned(piece.baked);
        if (root != null) DestroyOwned(root);
    }
    static void DestroyOwned(Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
    float RandomValue(float minimum,float maximum)=>Mathf.Lerp(minimum,maximum,(float)visualRandom.NextDouble());
}
