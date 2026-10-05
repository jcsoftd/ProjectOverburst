using System;
using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// 기존 Brain의 출력 카메라에 이 연출의 override 한 개만 빌린다. 전투 카메라 설정은 그대로 둔다.
[DefaultExecutionOrder(500)]
public sealed class CrustaspikanEntranceCinematic : MonoBehaviour
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
        [Tooltip("출현 전 지면 이상 징후 길이")][Min(.25f)] public float detailSeconds = 1.3f;
        [Tooltip("출현 모션 시작부터 포효까지")][Min(.25f)] public float riseSeconds = 2.55f;
        public string arrivalMotion = "2HandsSmashAttack";
        [Range(.05f, .95f)] public float arrivalImpactNormalized = .425f;
        [Min(.1f)] public float emergenceSeconds = 1.1f;
        public string roarMotion = "Roar1";
        [Min(.25f)] public float revealSeconds = .35f;
        [Min(.1f)] public float returnSeconds = .75f;
        [Min(0f)] public float combatGraceSeconds = 1f;
        [HideInInspector] public string subtitle = "암굴의 포식자"; // 이전 저작 값을 보존한다. 소개 카드는 그리지 않는다.
        public AudioClip roarClip;
        public AudioClip rumbleClip;
        public AudioClip breachClip;
        public AudioClip impactClip;
        public AudioClip inhaleClip;
        [Range(0f, 1f)] public float roarVolume = .75f;
    }

    public bool IsPlaying { get; private set; }
    public bool WasSkipped { get; private set; }
    public bool RoarStarted { get; private set; }
    public bool RoarAudioStarted { get; private set; }
    public bool ImpactStarted { get; private set; }
    public bool ArrivalStarted { get; private set; }
    public bool RumbleAudioStarted { get; private set; }
    public bool ImpactAudioStarted { get; private set; }
    public bool VisualRestored { get; private set; }
    public bool FloorGeometryRestored { get; private set; }
    public float ImpactElapsed { get; private set; }
    public int ShotIndex { get; private set; }
    public float Elapsed { get; private set; }
    public float Duration => roarEnd + settings.revealSeconds + settings.returnSeconds;
    public CinemachineCamera ShotCamera => shotCamera;
    public float ArrivalImpactAt => settings.detailSeconds + arrivalLength * settings.arrivalImpactNormalized;
    public float RoarStart => Mathf.Max(settings.detailSeconds + settings.riseSeconds, ArrivalImpactAt + .22f);
    public float RevealStart => Mathf.Lerp(RoarStart, roarEnd, .62f);
    public int HiddenCanvasCount => hiddenCanvases.Count;
    public Vector3 VisualOffset => visualRoot != null ? visualRoot.localPosition - visualRestPosition : Vector3.zero;
    private float roarEnd, arrivalLength;
    private Settings settings;
    private CrustaspikanEncounter encounter;
    private EnemyActor actor;
    private uint lease;
    private OverburstCinemachineCameraRig rig;
    private CinemachineCamera shotCamera;
    private GameObject presentation;
    private AudioSource voice, rumble, impact, breath;
    private bool voicePaused, breachEmitted, roarPressureEmitted, visualCaptured, inhaleStarted;
    private RectTransform topBar, bottomBar;
    private Light keyLight, rimLight, mouthLight, arenaFill;
    private Renderer arenaFloor;
    private MeshFilter floorFilter;
    private Mesh previousFloorMesh, tornFloorMesh;
    private Vector3[] tornFloorVertices;
    private const int HoleSegments = 64;
    private MaterialPropertyBlock previousFloorBlock;
    private float previousFillIntensity;
    private ParticleSystem groundBurst;
    private Transform crater, visualRoot;
    private Vector3 visualRestPosition;
    private readonly List<LineRenderer> fractures = new List<LineRenderer>();
    private readonly List<Transform> debris = new List<Transform>();
    private readonly Dictionary<Renderer, bool> hiddenArenaRenderers = new Dictionary<Renderer, bool>();
    private readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();
    private Transform head, claw;
    private Vector3 smoothedHead, basePosition;
    private Quaternion baseRotation;
    private float height;
    private int cameraOverride = -1;
    private readonly Dictionary<Canvas, bool> hiddenCanvases = new Dictionary<Canvas, bool>();
    private Color previousBackground;
    private CameraClearFlags previousClearFlags;
    private bool cameraAppearanceCaptured;
    private static readonly Color Backdrop = new Color(.025f, .035f, .045f);

    public bool Play(CrustaspikanEncounter owner, PlayerActorRuntime player, QuarterViewCamera camera, Settings tuning)
    {
        if (IsPlaying || tuning == null || !tuning.enabled || owner.Brain == null
            || camera == null || !camera.UsesCinemachine || camera.CinemachineRig?.Brain == null) return false;
        encounter = owner; actor = owner.Brain.Actor; lease = actor.LeaseVersion; rig = camera.CinemachineRig; settings = tuning;
        var arrival = owner.Brain.RuntimeMaterials.FindMotion(settings.arrivalMotion);
        var motion = owner.Brain.RuntimeMaterials.FindMotion(settings.roarMotion);
        if (motion?.IsPlayable != true || arrival?.IsPlayable != true || actor.VisualRoot == null) return false;
        arrivalLength = arrival.runtime.length;
        roarEnd = RoarStart + motion.runtime.length;
        basePosition = actor.transform.position; baseRotation = actor.transform.rotation;
        var renderers = actor.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(basePosition + Vector3.up * 5, Vector3.one * 10);
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        height = Mathf.Max(2f, bounds.size.y);
        foreach (var bone in actor.GetComponentsInChildren<Transform>())
        {
            if (bone.name == "Crustaspikan_ Head") head = bone;
            if (bone.name == owner.Brain.RuntimeMaterials.boulderLeftHandBone) claw = bone;
        }
        smoothedHead = HeadPosition;
        WasSkipped = RoarStarted = RoarAudioStarted = ImpactStarted = voicePaused = false;
        ArrivalStarted = RumbleAudioStarted = ImpactAudioStarted = VisualRestored = FloorGeometryRestored = breachEmitted = roarPressureEmitted = inhaleStarted = false;
        Elapsed = 0f; ShotIndex = 0; ImpactElapsed = -1f;
        IsPlaying = true;
        try
        {
            player.GetComponent<MeleeRuntime>()?.CancelCurrentAction();
            owner.Brain.BeginEntrance();
            GameplayInputBlocker.Block(this);
            CaptureCanvas(owner.BossHud);
            foreach (var view in FindObjectsByType<OverburstGameUI>(FindObjectsSortMode.None)) CaptureCanvas(view.hud);
            foreach (var view in FindObjectsByType<PlayerHealthHud>(FindObjectsSortMode.None)) CaptureCanvas(view);
            foreach (var view in FindObjectsByType<MinimapView>(FindObjectsSortMode.None)) CaptureCanvas(view);
            foreach (var view in FindObjectsByType<OverburstObjectiveTracker>(FindObjectsSortMode.None)) CaptureCanvas(view);
            previousBackground = rig.OutputCamera.backgroundColor; previousClearFlags = rig.OutputCamera.clearFlags;
            cameraAppearanceCaptured = true;
            rig.OutputCamera.clearFlags = CameraClearFlags.SolidColor; rig.OutputCamera.backgroundColor = Backdrop;
            CaptureArena();
            BuildPresentation();
            visualRoot = actor.VisualRoot; visualRestPosition = visualRoot.localPosition; visualCaptured = true;
            ApplyEmergence(0f); smoothedHead = HeadPosition;
            ApplyShot(0f);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            Stop(false);
            return false;
        }
    }

    private Vector3 Local(float x, float y, float z) => basePosition + baseRotation * new Vector3(x, y, z) * height;
    private Vector3 HeadPosition => head != null ? head.position : Local(0f, .7f, .08f);
    private Vector3 ClawPosition => claw != null ? claw.position : Local(-.43f, .28f, .25f);
    private void CaptureCanvas(Component view)
    {
        var canvas = view != null ? view.GetComponentInParent<Canvas>()?.rootCanvas : null;
        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) return;
        // 별도 sorting을 쓰는 미니맵 자식 Canvas는 루트만 끄면 계속 그려질 수 있다.
        foreach (var nested in canvas.GetComponentsInChildren<Canvas>(true))
        {
            if (nested.renderMode == RenderMode.WorldSpace || hiddenCanvases.ContainsKey(nested)) continue;
            hiddenCanvases.Add(nested, nested.enabled); nested.enabled = false;
        }
    }
    private void CaptureArena()
    {
        foreach (var renderer in encounter.GetComponentsInChildren<Renderer>())
        {
            if (renderer.name == "1m Grid" || renderer.name == "5m Grid" || renderer.name == "Arena Rim")
            { hiddenArenaRenderers.Add(renderer, renderer.enabled); renderer.enabled = false; }
            if (renderer.name == "Arena Floor")
            {
                arenaFloor = renderer; previousFloorBlock = new MaterialPropertyBlock(); renderer.GetPropertyBlock(previousFloorBlock);
                floorFilter = renderer.GetComponent<MeshFilter>(); previousFloorMesh = floorFilter != null ? floorFilter.sharedMesh : null;
                var filmBlock = new MaterialPropertyBlock(); renderer.GetPropertyBlock(filmBlock);
                filmBlock.SetColor("_BaseColor", new Color(.025f, .031f, .045f)); renderer.SetPropertyBlock(filmBlock);
            }
        }
        foreach (var light in encounter.GetComponentsInChildren<Light>())
            if (light.name == "Arena Fill Light")
            { arenaFill = light; previousFillIntensity = light.intensity; light.intensity = .6f; break; }
    }

    private void BuildPresentation()
    {
        presentation = new GameObject("Crustaspikan Entrance Presentation"); presentation.transform.SetParent(transform, false);
        var cameraObject = new GameObject("Crustaspikan Entrance Cinemachine Shot"); cameraObject.transform.SetParent(presentation.transform, false);
        shotCamera = cameraObject.AddComponent<CinemachineCamera>();
        shotCamera.Priority = int.MinValue; // override를 반환하면 일반 카메라 선택에 관여하지 않는다.
        shotCamera.OutputChannel = rig.VirtualCamera.OutputChannel;
        shotCamera.Lens = rig.VirtualCamera.Lens;
        voice = presentation.AddComponent<AudioSource>(); voice.playOnAwake = false; voice.spatialBlend = 0f;
        rumble = presentation.AddComponent<AudioSource>(); rumble.playOnAwake = false; rumble.spatialBlend = 0f; rumble.pitch = .72f;
        impact = presentation.AddComponent<AudioSource>(); impact.playOnAwake = false; impact.spatialBlend = 0f; impact.pitch = .82f;
        breath = presentation.AddComponent<AudioSource>(); breath.playOnAwake = false; breath.spatialBlend = 0f;
        keyLight = Light("Entrance Warm Key", Local(-.7f, 1.05f, 1.1f), new Color(1f, .68f, .38f), 6f);
        rimLight = Light("Entrance Cold Rim", Local(.55f, .95f, -.65f), new Color(.12f, .52f, .85f), 8f);
        mouthLight = Light("Entrance Mouth Glow", HeadPosition, new Color(1f, .18f, .035f), 0f);
        mouthLight.type = LightType.Point; mouthLight.range = height * .45f;
        BuildAtmosphere(); BuildRupture();

        var canvasObject = new GameObject("Crustaspikan Entrance Letterbox", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(presentation.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        topBar = Bar(canvasObject.transform, "Top Letterbox", true); bottomBar = Bar(canvasObject.transform, "Bottom Letterbox", false);
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("UI/Fonts/DamageFloating/Pretendard_Medium SDF") ?? TMP_Settings.defaultFontAsset;
        var hint = Text(canvasObject.transform, "SPACE  ·  건너뛰기", 17, 0, new Color(.7f, .72f, .73f), font, 1);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(.93f, .055f); hint.rectTransform.sizeDelta = new Vector2(260, 35);
    }
    private Light Light(string label, Vector3 position, Color color, float intensity)
    {
        var go = new GameObject(label); go.transform.SetParent(presentation.transform, false); go.transform.position = position;
        go.transform.rotation = Quaternion.LookRotation(Local(0, .55f, 0) - position);
        var light = go.AddComponent<Light>(); light.type = LightType.Spot; light.color = color; light.intensity = intensity;
        light.range = height * 5; light.spotAngle = 90f; light.innerSpotAngle = 55f; light.shadows = LightShadows.None;
        return light;
    }
    private void BuildAtmosphere()
    {
        var texture = new Texture2D(32, 32, TextureFormat.RGBA32, false) { name = "Crustaspikan Entrance Soft Dust", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[32 * 32];
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float distance = new Vector2((x - 15.5f) / 15.5f, (y - 15.5f) / 15.5f).magnitude;
            pixels[y * 32 + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - distance), 2));
        }
        texture.SetPixels(pixels); texture.Apply(false, true); resources.Add(texture);
        var dustMaterial = TransparentMaterial("Crustaspikan Entrance Dust Material"); dustMaterial.SetTexture("_BaseMap", texture);
        Particles("Entrance Ground Haze", dustMaterial, true);
        groundBurst = Particles("Entrance Rupture Dust", dustMaterial, false);
    }
    private Material TransparentMaterial(string label)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = label };
        material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000; resources.Add(material); return material;
    }
    private void BuildRupture()
    {
        // 바닥 Collider와 본체 위치는 유지하고, 지면 위의 연출 메시만 파열시킨다.
        var material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Crustaspikan Entrance Rupture Material" };
        material.SetColor("_BaseColor", new Color(.016f, .012f, .009f)); resources.Add(material);
        var vertices = new Vector3[HoleSegments * 3]; var uv = new Vector2[vertices.Length]; var triangles = new int[HoleSegments * 12];
        for (int i = 0; i < HoleSegments; i++)
        {
            float a = i * Mathf.PI * 2f / HoleSegments; Vector3 radial = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            vertices[i] = radial * HoleRadius(i);
            vertices[i + HoleSegments] = vertices[i] + Vector3.down * height * .22f;
            vertices[i + HoleSegments * 2] = vertices[i] + radial * height * (.045f + .035f * Hash(i + 2));
            uv[i] = new Vector2(i / 8f, 1f); uv[i + HoleSegments] = new Vector2(i / 8f, 0f); uv[i + HoleSegments * 2] = new Vector2(i / 8f, 1.4f);
            int next = (i + 1) % HoleSegments, at = i * 12;
            triangles[at] = i; triangles[at + 1] = next; triangles[at + 2] = i + HoleSegments;
            triangles[at + 3] = next; triangles[at + 4] = next + HoleSegments; triangles[at + 5] = i + HoleSegments;
            triangles[at + 6] = i; triangles[at + 7] = i + HoleSegments * 2; triangles[at + 8] = next + HoleSegments * 2;
            triangles[at + 9] = i; triangles[at + 10] = next + HoleSegments * 2; triangles[at + 11] = next;
        }
        var mesh = new Mesh { name = "Crustaspikan Entrance Rupture Mesh", vertices = vertices, triangles = triangles, uv = uv };
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); resources.Add(mesh);
        var opening = new GameObject("Entrance Torn Ground", typeof(MeshFilter), typeof(MeshRenderer));
        opening.transform.SetParent(presentation.transform, false); opening.transform.position = basePosition + Vector3.up * .015f;
        opening.GetComponent<MeshFilter>().sharedMesh = mesh;
        opening.GetComponent<MeshRenderer>().sharedMaterial = encounter.Brain.RuntimeMaterials.boulderMaterial ?? material; crater = opening.transform;
        if (floorFilter != null)
        {
            tornFloorVertices = new Vector3[HoleSegments * 2]; var floorTriangles = new int[HoleSegments * 6];
            for (int i = 0; i < HoleSegments; i++)
            {
                float a = i * Mathf.PI * 2f / HoleSegments;
                Vector3 edge = encounter.ArenaCenter + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * encounter.Settings.arenaRadius;
                tornFloorVertices[i + HoleSegments] = floorFilter.transform.InverseTransformPoint(edge);
                int next = (i + 1) % HoleSegments, at = i * 6;
                floorTriangles[at] = i; floorTriangles[at + 1] = i + HoleSegments; floorTriangles[at + 2] = next + HoleSegments;
                floorTriangles[at + 3] = i; floorTriangles[at + 4] = next + HoleSegments; floorTriangles[at + 5] = next;
            }
            tornFloorMesh = new Mesh { name = "Crustaspikan Entrance Open Floor Mesh", vertices = tornFloorVertices, triangles = floorTriangles };
            resources.Add(tornFloorMesh); floorFilter.sharedMesh = tornFloorMesh;
        }
        for (int i = 0; i < 8; i++)
        {
            var fissure = new GameObject("Entrance Ground Fracture " + i); fissure.transform.SetParent(presentation.transform, false);
            fissure.transform.position = basePosition + Vector3.up * .018f;
            var line = fissure.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.positionCount = 5;
            line.sharedMaterial = material; line.startColor = line.endColor = Color.white;
            float angle = i * Mathf.PI * .25f + .1f * Mathf.Sin(i * 11f);
            for (int p = 0; p < 5; p++)
            {
                float a = angle + .12f * Mathf.Sin(p * 9.5f + i);
                line.SetPosition(p, new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * height * (.1f + p * (.12f + .035f * Hash(i + 7))));
            }
            line.startWidth = 1f; line.endWidth = .08f;
            fractures.Add(line);
        }
        var source = encounter.Brain.RuntimeMaterials;
        if (source.boulderMesh == null || source.boulderMaterial == null) return;
        float diameter = Mathf.Max(.01f, source.boulderMesh.bounds.size.magnitude);
        for (int i = 0; i < 22; i++)
        {
            var piece = new GameObject("Entrance Flying Stone " + i, typeof(MeshFilter), typeof(MeshRenderer));
            piece.transform.SetParent(presentation.transform, false);
            piece.GetComponent<MeshFilter>().sharedMesh = source.boulderMesh; piece.GetComponent<MeshRenderer>().sharedMaterial = source.boulderMaterial;
            float size = height * (.025f + .035f * Hash(i + 4));
            piece.transform.localScale = new Vector3(1.5f, .65f, 1f) * (size / diameter);
            debris.Add(piece.transform);
        }
    }
    private static float Hash(int seed) => Mathf.Repeat(Mathf.Sin(seed * 12.9898f) * 43758.5453f, 1f);
    private float HoleRadius(int index) => height * (.49f + .03f * Mathf.Sin(index * 7.13f));
    private void ApplyEmergence(float time)
    {
        if (!visualCaptured || visualRoot == null) return;
        float rise = Ease((time - settings.detailSeconds) / settings.emergenceSeconds);
        Vector3 worldOffset = Vector3.down * height * 1.12f * (1f - rise);
        visualRoot.localPosition = visualRestPosition + visualRoot.parent.InverseTransformVector(worldOffset);
        float fracture = Ease(time / settings.detailSeconds);
        if (crater != null) crater.localScale = Vector3.one * Mathf.Lerp(.015f, 1f, rise);
        foreach (var line in fractures) line.widthMultiplier = Mathf.Lerp(.004f, .036f, fracture);
        if (tornFloorMesh != null && floorFilter != null)
        {
            for (int i = 0; i < HoleSegments; i++)
            {
                float a = i * Mathf.PI * 2f / HoleSegments;
                Vector3 point = basePosition + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * HoleRadius(i) * Mathf.Lerp(.015f, 1f, rise);
                point.y = encounter.ArenaCenter.y; tornFloorVertices[i] = floorFilter.transform.InverseTransformPoint(point);
            }
            tornFloorMesh.vertices = tornFloorVertices; tornFloorMesh.RecalculateNormals(); tornFloorMesh.RecalculateBounds();
        }
        float age = Mathf.Max(0, time - settings.detailSeconds);
        for (int i = 0; i < debris.Count; i++)
        {
            float a = i * 2.39996f; Vector3 radial = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            float radius = height * (.25f + .22f * Hash(i + 10));
            float lift = time < settings.detailSeconds ? Mathf.Abs(Mathf.Sin(time * 23f + i)) * fracture * .09f
                : Mathf.Max(0f, age * (6f + 7f * Hash(i + 20)) - age * age * 6.5f);
            float outward = time < settings.detailSeconds ? 0f : Mathf.Min(age, 1.3f) * (2f + 3f * Hash(i + 30));
            debris[i].position = basePosition + radial * (radius + outward) + Vector3.up * (.07f + lift);
            debris[i].rotation = Quaternion.Euler(i * 39f + age * 130f, i * 71f + age * 89f, i * 23f + age * 97f);
        }
    }
    private ParticleSystem Particles(string label, Material material, bool looping)
    {
        var go = new GameObject(label); go.transform.SetParent(presentation.transform, false); go.transform.position = basePosition + Vector3.up * .12f;
        var system = go.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.playOnAwake = false; main.loop = looping; main.duration = 12;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = looping ? 70 : 220;
        main.startLifetime = new ParticleSystem.MinMaxCurve(looping ? 3f : .6f, looping ? 6f : 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(height * .035f, height * (looping ? .07f : .17f));
        main.startSpeed = new ParticleSystem.MinMaxCurve(looping ? .05f : 3f, looping ? .3f : 9f);
        main.startColor = new Color(.43f, .34f, .24f, looping ? .2f : .65f);
        var shape = system.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = looping ? 15f : 82f;
        shape.radius = height * (looping ? 1.3f : .6f); shape.rotation = new Vector3(-90f, 0, 0);
        var emission = system.emission; emission.rateOverTime = looping ? 12f : 0f;
        var lifetime = system.colorOverLifetime; lifetime.enabled = true;
        var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.8f, .12f), new GradientAlphaKey(0, 1) }); lifetime.color = gradient;
        var renderer = go.GetComponent<ParticleSystemRenderer>(); renderer.sharedMaterial = material;
        if (looping) system.Play(); return system;
    }
    private static RectTransform Bar(Transform parent, string label, bool top)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0, top ? 1 : 0); rect.anchorMax = new Vector2(1, top ? 1 : 0);
        rect.pivot = new Vector2(.5f, top ? 1 : 0); rect.sizeDelta = Vector2.zero;
        var image = go.GetComponent<Image>(); image.color = Color.black; image.raycastTarget = false; return rect;
    }
    private static TextMeshProUGUI Text(Transform parent, string content, float size, float y, Color color, TMP_FontAsset font, float spacing)
    {
        var go = new GameObject(content, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = font; text.text = content; text.fontSize = size; text.characterSpacing = spacing;
        text.color = color; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.rectTransform.sizeDelta = new Vector2(1200, size + 25); text.rectTransform.anchoredPosition = new Vector2(0, y); return text;
    }

    private void Update()
    {
        if (!IsPlaying) return;
        if (actor == null || !actor.IsLeased || actor.LeaseVersion != lease || encounter.Defeated || rig?.Brain == null)
        { Stop(false); return; }
        if (Time.timeScale > 0f && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        { Skip(); return; }
        if (voice != null && voicePaused != (Time.timeScale <= 0f))
        {
            voicePaused = Time.timeScale <= 0f;
            foreach (var source in new[] { voice, rumble, impact, breath })
                if (voicePaused) source.Pause(); else source.UnPause();
        }
        Elapsed += Time.deltaTime;
        ApplyEmergence(Elapsed);
        if (!RumbleAudioStarted && Elapsed >= .12f && settings.rumbleClip != null)
        { rumble.PlayOneShot(settings.rumbleClip, .42f); RumbleAudioStarted = true; }
        if (!ArrivalStarted && Elapsed >= settings.detailSeconds)
        {
            ArrivalStarted = actor.GetComponent<EnemyBossMaterialExecutor>().TryPlayMotion(settings.arrivalMotion);
            if (!ArrivalStarted) { Debug.LogWarning("[Crustaspikan] 출현 동작을 시작할 수 없어 전투로 반환합니다."); Stop(false); return; }
        }
        if (!breachEmitted && ArrivalStarted)
        {
            breachEmitted = true; groundBurst.Play(); groundBurst.Emit(150);
            if (settings.breachClip != null) impact.PlayOneShot(settings.breachClip, .5f);
        }
        var pose = actor.Animator.GetCurrentAnimatorStateInfo(0);
        if (!ImpactStarted && ArrivalStarted && pose.IsName("Material_" + settings.arrivalMotion) && pose.normalizedTime >= settings.arrivalImpactNormalized)
        {
            ImpactStarted = true; ImpactElapsed = Elapsed; groundBurst.Emit(120);
            if (settings.impactClip != null) { impact.PlayOneShot(settings.impactClip, .55f); ImpactAudioStarted = true; }
        }
        if (!RoarStarted && Elapsed >= RoarStart)
        {
            if (!ImpactStarted) { Debug.LogWarning("[Crustaspikan] 출현 모션이 접촉 시점에 도달하지 못해 전투로 반환합니다."); Stop(false); return; }
            actor.GetComponent<EnemyBossMaterialExecutor>().Cancel();
            RoarStarted = actor.GetComponent<EnemyBossMaterialExecutor>().TryPlayMotion(settings.roarMotion);
            if (!RoarStarted) { Debug.LogWarning("[Crustaspikan] 등장 포효를 시작할 수 없어 전투 시점으로 반환합니다."); Stop(false); return; }
        }
        if (!inhaleStarted && RoarStarted && settings.inhaleClip != null)
        { inhaleStarted = true; breath.PlayOneShot(settings.inhaleClip, .5f); }
        // 입을 벌리는 실제 포효 구간에 음성과 압력을 맞춘다. 준비 자세에서 먼저 소리내지 않는다.
        if (!RoarAudioStarted && RoarStarted && Elapsed >= Mathf.Lerp(RoarStart, roarEnd, .22f) && settings.roarClip != null)
        { breath.Stop(); voice.PlayOneShot(settings.roarClip, settings.roarVolume); RoarAudioStarted = true; }
        if (!roarPressureEmitted && Elapsed >= Mathf.Lerp(RoarStart, roarEnd, .35f))
        { roarPressureEmitted = true; groundBurst.Emit(90); }
        if (Elapsed >= Duration) Stop(false);
    }
    private void LateUpdate()
    {
        if (!IsPlaying) return;
        smoothedHead = Vector3.Lerp(smoothedHead, HeadPosition, 1f - Mathf.Exp(-8f * Time.deltaTime));
        ApplyShot(Elapsed);
    }
    private void ApplyShot(float time)
    {
        float returning = Mathf.Clamp01((time - roarEnd - settings.revealSeconds) / settings.returnSeconds);
        Vector3 position, focus; float fov, dutch = 0f;
        if (time < settings.detailSeconds)
        {
            ShotIndex = 0; float t = Ease(time / settings.detailSeconds);
            position = Vector3.Lerp(Local(-.28f, .11f, 1.45f), Local(-.3f, .10f, 1.38f), t);
            focus = Local(0f, .018f, .10f); fov = 58f; dutch = -1.5f;
            float tremor = t * t * OverburstGameSettings.CameraShakeScale;
            position += Vector3.up * Mathf.Sin(time * 31f) * tremor * .025f;
        }
        else if (!ImpactStarted)
        {
            ShotIndex = 1; float t = Ease((time - settings.detailSeconds) / (ArrivalImpactAt - settings.detailSeconds));
            position = Vector3.Lerp(Local(-.3f, .10f, 1.38f), Local(-.46f, .21f, 1.85f), Ease((time - settings.detailSeconds) / .9f));
            focus = Vector3.Lerp(Local(0, .05f, .1f), Local(0, .56f, .1f), Ease((time - settings.detailSeconds - .24f) / .9f));
            fov = Mathf.Lerp(58f, 63f, t); dutch = -3f * Mathf.Sin(t * Mathf.PI);
        }
        else if (time < RevealStart)
        {
            ShotIndex = 2; float t = Mathf.Clamp01((time - RoarStart) / (roarEnd - RoarStart));
            float recover = Ease((time - ImpactElapsed - .08f) / .6f);
            position = Vector3.Lerp(Local(.57f, .16f, 1.25f), Local(.28f, .47f, 1.02f), recover);
            focus = Vector3.Lerp(Local(0, .24f, .2f), smoothedHead, recover);
            float slam = Mathf.Exp(-Mathf.Max(0, time - ImpactElapsed) * 8f) * OverburstGameSettings.CameraShakeScale;
            float pulse = Mathf.Exp(-Mathf.Pow((t - .35f) / .11f, 2)) * OverburstGameSettings.CameraShakeScale;
            position += baseRotation * new Vector3(Mathf.Sin(time * 43f) * .8f, -1f + Mathf.Sin(time * 57f) * .5f, 1f) * (pulse + slam) * height * .018f;
            fov = Mathf.Lerp(64f, 46f, recover) + pulse * 7f; dutch = Mathf.Sin(time * 39f) * (pulse * 2f + slam * 4f);
        }
        else
        {
            ShotIndex = returning > 0f ? 4 : 3; float t = Ease((time - RevealStart) / (roarEnd + settings.revealSeconds - RevealStart));
            position = Vector3.Lerp(Local(-.08f, .22f, 1.48f), Local(-.18f, .34f, 1.95f), t);
            focus = Vector3.Lerp(smoothedHead, Local(0, .48f, 0), t); fov = Mathf.Lerp(62f, 57f, t);
            dutch = -2f * (1f - t);
        }
        shotCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
        var lens = shotCamera.Lens; lens.FieldOfView = fov; lens.Dutch = dutch; lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        shotCamera.Lens = lens;
        float weight = Ease(time / .45f) * (1f - Ease(returning));
        cameraOverride = rig.Brain.SetCameraOverride(cameraOverride, 100, rig.VirtualCamera, shotCamera, weight, Time.deltaTime);
        float bars = Mathf.Min(Ease(time / .45f), 1f - Ease(returning));
        topBar.sizeDelta = bottomBar.sizeDelta = new Vector2(0, 105f * bars);
        float exposed = Ease((time - settings.detailSeconds) / .7f);
        keyLight.intensity = Mathf.Lerp(.2f, 5f, exposed); rimLight.intensity = Mathf.Lerp(.5f, 9f, exposed);
        mouthLight.transform.position = HeadPosition + baseRotation * Vector3.forward * .25f;
        float roarTime = Mathf.Clamp01((time - RoarStart) / (roarEnd - RoarStart));
        mouthLight.intensity = 5f * Mathf.Sin(roarTime * Mathf.PI);
        // 저음은 출현 직전만 남기고 포효와 충돌음의 자리를 비운다.
        rumble.volume = Mathf.Lerp(.35f, 1f, Ease(time / settings.detailSeconds)) * (1f - Ease((time - settings.detailSeconds) / .5f));
        if (returning > 0f && rig.OutputCamera != null) rig.OutputCamera.backgroundColor = Color.Lerp(Backdrop, previousBackground, Ease(returning));
    }
    private static float Ease(float value) { float t = Mathf.Clamp01(value); return t * t * (3f - 2f * t); }
    public void Skip() { if (!IsPlaying) return; WasSkipped = true; Stop(false); }
    public void Cancel() => Stop(true);
    private void Stop(bool cancelling)
    {
        if (!IsPlaying && cameraOverride < 0 && presentation == null) return;
        IsPlaying = false;
        try
        {
            if (rig?.Brain != null && cameraOverride >= 0) rig.Brain.ReleaseCameraOverride(cameraOverride);
            cameraOverride = -1;
            if (cameraAppearanceCaptured && rig?.OutputCamera != null)
            { rig.OutputCamera.backgroundColor = previousBackground; rig.OutputCamera.clearFlags = previousClearFlags; }
            cameraAppearanceCaptured = false;
            if (visualCaptured && actor != null && actor.IsLeased && actor.LeaseVersion == lease && visualRoot != null)
            { visualRoot.localPosition = visualRestPosition; VisualRestored = true; }
            visualCaptured = false;
            foreach (var pair in hiddenCanvases) if (pair.Key != null) pair.Key.enabled = pair.Value;
            hiddenCanvases.Clear();
            foreach (var pair in hiddenArenaRenderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
            hiddenArenaRenderers.Clear();
            if (arenaFloor != null) arenaFloor.SetPropertyBlock(previousFloorBlock);
            arenaFloor = null; previousFloorBlock = null;
            if (floorFilter != null) { floorFilter.sharedMesh = previousFloorMesh; FloorGeometryRestored = true; }
            floorFilter = null; previousFloorMesh = tornFloorMesh = null; tornFloorVertices = null;
            if (arenaFill != null) arenaFill.intensity = previousFillIntensity;
            arenaFill = null;
            if (presentation != null) { presentation.SetActive(false); Destroy(presentation); }
            foreach (var resource in resources) if (resource != null) Destroy(resource);
            resources.Clear();
            presentation = null; shotCamera = null; voice = rumble = impact = breath = null;
            fractures.Clear(); debris.Clear(); crater = null;
            if (actor != null && actor.IsLeased && actor.LeaseVersion == lease) encounter.Brain?.FinishEntrance(cancelling ? 0f : settings.combatGraceSeconds);
        }
        finally { GameplayInputBlocker.Unblock(this); }
    }
    private void OnDisable() => Cancel();
    private void OnDestroy() => Cancel();
}
