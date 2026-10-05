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
        [Min(.25f)] public float detailSeconds = 1.5f;
        [Min(.25f)] public float riseSeconds = 1.6f;
        public string roarMotion = "Roar1";
        [Min(.25f)] public float revealSeconds = 1.35f;
        [Min(.1f)] public float returnSeconds = 1f;
        [Min(0f)] public float combatGraceSeconds = 1f;
        public string subtitle = "암굴의 포식자";
        public AudioClip roarClip;
        [Range(0f, 1f)] public float roarVolume = .75f;
    }

    public bool IsPlaying { get; private set; }
    public bool WasSkipped { get; private set; }
    public bool RoarStarted { get; private set; }
    public bool RoarAudioStarted { get; private set; }
    public bool ImpactStarted { get; private set; }
    public int ShotIndex { get; private set; }
    public float Elapsed { get; private set; }
    public float Duration => roarEnd + settings.revealSeconds + settings.returnSeconds;
    public CinemachineCamera ShotCamera => shotCamera;
    public float TitleAlpha => title != null ? title.alpha : 0f;
    public float RoarStart => settings.detailSeconds + settings.riseSeconds;
    public float RevealStart => Mathf.Lerp(RoarStart, roarEnd, .62f);
    public int HiddenCanvasCount => hiddenCanvases.Count;
    private float roarEnd;
    private Settings settings;
    private CrustaspikanEncounter encounter;
    private EnemyActor actor;
    private uint lease;
    private OverburstCinemachineCameraRig rig;
    private CinemachineCamera shotCamera;
    private GameObject presentation;
    private CanvasGroup title;
    private AudioSource voice;
    private bool voicePaused;
    private RectTransform topBar, bottomBar;
    private RectTransform titleRule;
    private Light keyLight, rimLight, mouthLight, arenaFill;
    private Renderer arenaFloor;
    private MaterialPropertyBlock previousFloorBlock;
    private float previousFillIntensity;
    private ParticleSystem groundBurst;
    private LineRenderer shockwave;
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
        var motion = owner.Brain.RuntimeMaterials.FindMotion(settings.roarMotion);
        if (motion?.IsPlayable != true) return false;
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
        WasSkipped = RoarStarted = RoarAudioStarted = ImpactStarted = voicePaused = false; Elapsed = 0f; ShotIndex = 0;
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
        keyLight = Light("Entrance Warm Key", Local(-.7f, 1.05f, 1.1f), new Color(1f, .68f, .38f), 6f);
        rimLight = Light("Entrance Cold Rim", Local(.55f, .95f, -.65f), new Color(.12f, .52f, .85f), 8f);
        mouthLight = Light("Entrance Mouth Glow", HeadPosition, new Color(1f, .18f, .035f), 0f);
        mouthLight.type = LightType.Point; mouthLight.range = height * .45f;
        BuildAtmosphere();

        var canvasObject = new GameObject("Crustaspikan Entrance Letterbox", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(presentation.transform, false);
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 30000;
        var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        topBar = Bar(canvasObject.transform, "Top Letterbox", true); bottomBar = Bar(canvasObject.transform, "Bottom Letterbox", false);
        var titleObject = new GameObject("Boss Title", typeof(RectTransform), typeof(CanvasGroup)); titleObject.transform.SetParent(canvasObject.transform, false);
        var titleRect = titleObject.GetComponent<RectTransform>(); titleRect.anchorMin = titleRect.anchorMax = new Vector2(.5f, .205f);
        titleRect.sizeDelta = new Vector2(1200, 180); title = titleObject.GetComponent<CanvasGroup>(); title.blocksRaycasts = false;
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>("UI/Fonts/DamageFloating/Pretendard_Medium SDF") ?? TMP_Settings.defaultFontAsset;
        Text(titleObject.transform, "CRUSTASPIKAN", 16, 76f, new Color(.75f, .82f, .84f), font, 18);
        Text(titleObject.transform, "크러스피칸", 76, 14f, new Color(1f, .87f, .56f), font, 9);
        Text(titleObject.transform, settings.subtitle, 22, -58f, new Color(.85f, .85f, .8f), font, 5);
        var rule = new GameObject("Title Rule", typeof(RectTransform), typeof(Image)); rule.transform.SetParent(titleObject.transform, false);
        titleRule = rule.GetComponent<RectTransform>(); titleRule.sizeDelta = new Vector2(0, 1); titleRule.anchoredPosition = new Vector2(0, -35);
        var line = rule.GetComponent<Image>(); line.color = new Color(.9f, .69f, .36f, .7f); line.raycastTarget = false;
        var hint = Text(canvasObject.transform, "SPACE  ·  건너뛰기", 17, 0, new Color(.7f, .72f, .73f), font, 1);
        hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(.93f, .055f); hint.rectTransform.sizeDelta = new Vector2(260, 35);
        title.alpha = 0f;
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
        groundBurst = Particles("Entrance Roar Dust", dustMaterial, false);
        var wave = new GameObject("Entrance Roar Shockwave"); wave.transform.SetParent(presentation.transform, false);
        wave.transform.position = basePosition + Vector3.up * .08f;
        shockwave = wave.AddComponent<LineRenderer>(); shockwave.useWorldSpace = false; shockwave.loop = true;
        shockwave.positionCount = 96; shockwave.sharedMaterial = TransparentMaterial("Crustaspikan Entrance Shockwave Material"); shockwave.enabled = false;
    }
    private Material TransparentMaterial(string label)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = label };
        material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = 3000; resources.Add(material); return material;
    }
    private ParticleSystem Particles(string label, Material material, bool looping)
    {
        var go = new GameObject(label); go.transform.SetParent(presentation.transform, false); go.transform.position = basePosition + Vector3.up * .12f;
        var system = go.AddComponent<ParticleSystem>(); system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main; main.playOnAwake = false; main.loop = looping; main.duration = 12;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = looping ? 60 : 100;
        main.startLifetime = new ParticleSystem.MinMaxCurve(looping ? 3f : .6f, looping ? 6f : 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(height * .02f, height * (looping ? .055f : .09f));
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
            if (voicePaused) voice.Pause(); else voice.UnPause();
        }
        Elapsed += Time.deltaTime;
        if (!RoarStarted && Elapsed >= RoarStart)
        {
            RoarStarted = actor.GetComponent<EnemyBossMaterialExecutor>().TryPlayMotion(settings.roarMotion);
            if (RoarStarted && settings.roarClip != null)
            { voice.PlayOneShot(settings.roarClip, settings.roarVolume); RoarAudioStarted = true; }
            if (!RoarStarted) { Debug.LogWarning("[Crustaspikan] 등장 포효를 시작할 수 없어 전투 시점으로 반환합니다."); Stop(false); return; }
        }
        if (!ImpactStarted && Elapsed >= Mathf.Lerp(RoarStart, roarEnd, .35f))
        { ImpactStarted = true; groundBurst.Play(); groundBurst.Emit(90); }
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
            position = Vector3.Lerp(Local(-1.12f, .11f, .82f), Local(-.76f, .19f, .97f), t);
            focus = ClawPosition; fov = Mathf.Lerp(46f, 38f, t); dutch = Mathf.Lerp(-5f, -2f, t);
        }
        else if (time < RoarStart)
        {
            ShotIndex = 1; float t = Ease((time - settings.detailSeconds) / settings.riseSeconds);
            position = Vector3.Lerp(Local(-.76f, .19f, .97f), Local(-.3f, .78f, 1.25f), t);
            focus = Vector3.Lerp(ClawPosition, smoothedHead, t); fov = Mathf.Lerp(38f, 44f, t); dutch = -2f * (1f - t);
        }
        else if (time < RevealStart)
        {
            ShotIndex = 2; float t = Mathf.Clamp01((time - RoarStart) / (roarEnd - RoarStart));
            position = Vector3.Lerp(Local(.16f, .57f, 1.05f), Local(-.08f, .61f, .9f), Ease(t / .62f)); focus = smoothedHead;
            float pulse = Mathf.Exp(-Mathf.Pow((t - .35f) / .11f, 2)) * OverburstGameSettings.CameraShakeScale;
            position += baseRotation * new Vector3(Mathf.Sin(time * 43f) * .75f, Mathf.Sin(time * 57f) * .5f, .7f) * pulse * height * .011f;
            fov = Mathf.Lerp(43f, 36f, Ease(t / .62f)) + pulse * 5f; dutch = Mathf.Sin(time * 39f) * pulse * 1.15f;
        }
        else
        {
            ShotIndex = returning > 0f ? 4 : 3; float t = Ease((time - RevealStart) / (roarEnd + settings.revealSeconds - RevealStart));
            position = Vector3.Lerp(Local(-.8f, .56f, 1.38f), Local(-.96f, .64f, 1.52f), t);
            focus = Local(0, .38f, 0); fov = Mathf.Lerp(51f, 46f, t);
        }
        shotCamera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(focus - position));
        var lens = shotCamera.Lens; lens.FieldOfView = fov; lens.Dutch = dutch; lens.ModeOverride = LensSettings.OverrideModes.Perspective;
        shotCamera.Lens = lens;
        float weight = Ease(time / .45f) * (1f - Ease(returning));
        cameraOverride = rig.Brain.SetCameraOverride(cameraOverride, 100, rig.VirtualCamera, shotCamera, weight, Time.deltaTime);
        float bars = Mathf.Min(Ease(time / .45f), 1f - Ease(returning));
        topBar.sizeDelta = bottomBar.sizeDelta = new Vector2(0, 130f * bars);
        float reveal = Ease((time - RevealStart - .15f) / .65f);
        title.alpha = reveal * (1f - Ease(returning));
        title.transform.localScale = Vector3.one * Mathf.Lerp(1.1f, 1f, reveal);
        titleRule.sizeDelta = new Vector2(640f * reveal, 1f);
        keyLight.intensity = Mathf.Lerp(1.5f, 6f, Ease(time / RoarStart)); rimLight.intensity = Mathf.Lerp(3f, 8f, Ease(time / RoarStart));
        mouthLight.transform.position = HeadPosition + baseRotation * Vector3.forward * .25f;
        float roarTime = Mathf.Clamp01((time - RoarStart) / (roarEnd - RoarStart));
        mouthLight.intensity = 5f * Mathf.Sin(roarTime * Mathf.PI);
        float waveAge = time - Mathf.Lerp(RoarStart, roarEnd, .35f);
        shockwave.enabled = waveAge >= 0f && waveAge < 2f;
        if (shockwave.enabled)
        {
            float radius = Mathf.Lerp(height * .55f, height * 2.2f, Ease(waveAge / 2f));
            for (int i = 0; i < 96; i++) { float a = i * Mathf.PI * 2 / 96; shockwave.SetPosition(i, new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius); }
            shockwave.widthMultiplier = Mathf.Lerp(.2f, .035f, waveAge / 2f);
            shockwave.startColor = shockwave.endColor = new Color(.75f, .52f, .27f, .35f * (1 - waveAge / 2f));
        }
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
            foreach (var pair in hiddenCanvases) if (pair.Key != null) pair.Key.enabled = pair.Value;
            hiddenCanvases.Clear();
            foreach (var pair in hiddenArenaRenderers) if (pair.Key != null) pair.Key.enabled = pair.Value;
            hiddenArenaRenderers.Clear();
            if (arenaFloor != null) arenaFloor.SetPropertyBlock(previousFloorBlock);
            arenaFloor = null; previousFloorBlock = null;
            if (arenaFill != null) arenaFill.intensity = previousFillIntensity;
            arenaFill = null;
            if (presentation != null) { presentation.SetActive(false); Destroy(presentation); }
            foreach (var resource in resources) if (resource != null) Destroy(resource);
            resources.Clear();
            presentation = null; shotCamera = null; title = null; voice = null;
            if (actor != null && actor.IsLeased && actor.LeaseVersion == lease) encounter.Brain?.FinishEntrance(cancelling ? 0f : settings.combatGraceSeconds);
        }
        finally { GameplayInputBlocker.Unblock(this); }
    }
    private void OnDisable() => Cancel();
    private void OnDestroy() => Cancel();
}
