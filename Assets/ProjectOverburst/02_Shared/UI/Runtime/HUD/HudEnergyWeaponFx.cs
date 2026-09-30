using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// 2026-09-30: 에너지 바에 대검 원소 칼날 이펙트(Weapon Effects 2)를 입힌다.
// 게이지를 칼날로 삼아 화면 밖 전용 무대에서 파티클을 돌리고, 전용 카메라가 투명 배경 렌더 텍스처에 그린 것을
// 바 위(테두리 위·글자 아래)에 RawImage로 얹는다. 찬 길이는 무대의 칼날 길이 축 스케일로 따라간다.
// 원소별 조정은 사용자 확인을 마친 시안(개인파일/코덱스산출/UI/20260930_HudResourceBar/weaponfx_example)을 그대로 옮겼다.
//   불: 연료층 끔, 불꽃 흐름을 게이지 끝 쪽으로, 방출 구간을 찬 길이에 맞춤, 세기 0.85(하한 50%)
//   얼음: 서리 조각·가루 입자 끄고 위로 피어오르는 냉기만
//   번개: 분홍 파편 끔, 아크를 찬 길이에 맞추고 약 2.3배 느리게, 세기 0.7
//   어둠: 붉은 파편을 적고 어둡게, 검은 연기 비중을 크게
//   빛: 무기 원본 그대로
internal sealed class HudEnergyWeaponFx
{
    private const int StageLayer = 30;                                   // 이름 없는 레이어. 전용 카메라만 본다.
    private static readonly Vector3 StageOrigin = new Vector3(0f, -8000f, 0f);
    private const string CompositeShaderPath = "Shaders/HudFxCompositeUI";
    private const float BladeWidth = .12f;                               // 대검 칼날 폭(월드) = 바 높이
    private const float Above = 1.6f, Below = 1f;                        // 바 높이 단위 세로 여유
    private const float Side = .04f;                                     // 바 길이 단위 좌우 여유(흐려지며 사라짐)
    private const int MaxTextureWidth = 1024;

    private readonly Image fill;
    private readonly RawImage view;
    private readonly Material viewMaterial;
    private readonly GameObject stage;
    private readonly Transform blade, length;
    private readonly Camera camera;
    private readonly UniversalAdditionalCameraData cameraData;
    private readonly float barLength;
    private RenderTexture texture;
    private float nextSizeCheckAt;

    private WeaponElement element = WeaponElement.None;
    private WeaponEffects2Playback playback;
    private MeleeWeaponElementFx.ElementTuning tuning;
    private readonly List<Material> copies = new List<Material>();
    private readonly List<ParticleSystem> rated = new List<ParticleSystem>();
    private readonly List<float> baseRates = new List<float>();
    private Light[] lights = System.Array.Empty<Light>();
    private WeaponElectricBladeArc[] arcs = System.Array.Empty<WeaponElectricBladeArc>();
    private ParticleSystem fireCore;
    private ParticleSystem.Particle[] fireBuffer = System.Array.Empty<ParticleSystem.Particle>();
    private float shownLength = -1f, shownEnergy = -1f;
    private bool running = true;
    // 게이지가 0이 되면(방출 등) 크기를 그대로 둔 채 빠르게 투명해지며 사라진다. 1 = 보임, 0 = 사라짐.
    private const float FadeOutSeconds = .22f;
    private float presence;

    private HudEnergyWeaponFx(Image fill, RawImage view, Material viewMaterial, GameObject stage, Transform blade,
        Transform length, Camera camera, UniversalAdditionalCameraData cameraData, float barLength)
    {
        this.fill = fill; this.view = view; this.viewMaterial = viewMaterial; this.stage = stage;
        this.blade = blade; this.length = length; this.camera = camera; this.cameraData = cameraData; this.barLength = barLength;
    }

    // fromRightOnScreen: 화면에서 채움이 오른쪽에서 시작하면 true(렌더 결과를 좌우로 뒤집는다).
    internal static HudEnergyWeaponFx Create(Image fill, bool fromRightOnScreen)
    {
        Shader shader = Resources.Load<Shader>(CompositeShaderPath);
        var profile = Resources.Load<GreatswordElementFxProfile>(GreatswordElementFxProfile.ResourcePath);
        var fillRect = (RectTransform)fill.transform;
        float w = fillRect.rect.width, h = fillRect.rect.height;
        if (shader == null || profile == null || w < 1f || h < 1f) return null;

        var viewMaterial = new Material(shader) { name = "HudEnergyWeaponFx Composite (Runtime)" };
        viewMaterial.SetFloat("_EdgeFade", Side / (1f + Side * 2f));
        // 세로 캡처 = 아래 여유 1칸 + 바 1칸 + 위 여유 1.6칸. 바 위 여유의 위쪽 70%, 아래 여유의 아래쪽 45%에서 사라지게 한다.
        float total = Below + 1f + Above;
        viewMaterial.SetFloat("_EdgeFadeTop", Above * .7f / total);
        viewMaterial.SetFloat("_EdgeFadeBottom", Below * .45f / total);
        var layer = new GameObject("ResourceBarWeaponFx", typeof(RectTransform));
        var rect = (RectTransform)layer.transform;
        rect.SetParent(fill.transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-w * Side, -h * Below);
        rect.offsetMax = new Vector2(w * Side, h * Above);
        // Fill 자식이면 바 테두리 그림에 가려지므로 바 아래로 옮겨 테두리 위·글자 아래에 둔다.
        Transform bar = fill.transform.parent;
        if (bar != null)
        {
            rect.SetParent(bar, true);
            Transform text = bar.Find("Text Group");
            if (text != null) rect.SetSiblingIndex(text.GetSiblingIndex());
            else rect.SetAsLastSibling();
        }
        var view = layer.AddComponent<RawImage>();
        view.raycastTarget = false;
        view.material = viewMaterial;
        view.uvRect = fromRightOnScreen ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        view.enabled = false;

        float barLength = BladeWidth * w / h;
        var stage = new GameObject("HudEnergyWeaponFxStage");
        Object.DontDestroyOnLoad(stage);
        stage.layer = StageLayer;
        stage.transform.position = StageOrigin;
        // 칼날 축: local Z = 길이 -> 월드 +X, local X = 폭 -> 월드 +Y (시안 렌더와 같은 배치).
        var blade = new GameObject("Blade").transform;
        blade.gameObject.layer = StageLayer;
        blade.SetParent(stage.transform, false);
        blade.localRotation = Quaternion.LookRotation(Vector3.right, Vector3.forward);
        var length = new GameObject("FilledLength").transform;
        length.gameObject.layer = StageLayer;
        length.SetParent(blade, false);

        var cameraObject = new GameObject("Camera");
        cameraObject.layer = StageLayer;
        cameraObject.transform.SetParent(stage.transform, false);
        float viewWidth = barLength * (1f + Side * 2f), viewHeight = BladeWidth * (1f + Above + Below);
        cameraObject.transform.localPosition = new Vector3(barLength * .5f, (Above - Below) * .5f * BladeWidth, -4f);
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = viewHeight * .5f;
        camera.aspect = viewWidth / viewHeight;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 20f;
        camera.cullingMask = 1 << StageLayer;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.allowHDR = false;                // HUD에는 블룸이 없다: 시안과 같은 LDR 결과
        camera.allowMSAA = false;
        camera.useOcclusionCulling = false;
        camera.enabled = false;
        var cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.renderPostProcessing = false;
        cameraData.renderShadows = false;
        cameraData.antialiasing = AntialiasingMode.None;
        // ME2 입자 셰이더(ParticleDepth·ParticleNoise 등)는 SG_DepthFade 하위 그래프로 장면 깊이를 읽는다.
        // 끄면 다른 카메라의 깊이를 읽어 냉기 위쪽에 일직선 경계가 생긴다(2026-09-30 최적화 후 회귀). 반드시 켠다.
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        // 불투명 색은 빛 원소 굴절만 쓰므로 Build에서 원소별로 켠다.
        cameraData.requiresColorOption = CameraOverrideOption.Off;
        // 후처리를 안 쓰므로 볼륨 레이어를 비워 둔다.
        cameraData.volumeLayerMask = 0;

        var fx = new HudEnergyWeaponFx(fill, view, viewMaterial, stage, blade, length, camera, cameraData, barLength);
        fx.EnsureTexture();
        return fx;
    }

    // filled = 화면에 보이는 부드럽게 한 채움, target = 실제 에너지 비율.
    internal void Tick(WeaponElement nextElement, float filled, float target, bool visible)
    {
        if (nextElement != element) Build(nextElement);
        // 실제 에너지가 비면 줄어드는 채움을 따라 0까지 작아지지 않고, 지금 크기 그대로 빠르게 투명해진다.
        bool emptied = target <= .001f;
        if (!visible || playback == null) presence = 0f;
        else if (!emptied) presence = filled > .001f ? 1f : 0f;
        else if (presence > 0f) presence = Mathf.Max(0f, presence - Time.unscaledDeltaTime / FadeOutSeconds);
        bool show = presence > 0f;
        view.enabled = show;
        camera.enabled = show;
        if (view.color.a != presence) view.color = new Color(1f, 1f, 1f, presence);
        if (playback == null) return;
        SetRunning(show);
        if (!show || emptied) return;   // 사라지는 동안은 크기·세기를 마지막 값으로 고정
        if (Time.unscaledTime >= nextSizeCheckAt) { nextSizeCheckAt = Time.unscaledTime + 1f; EnsureTexture(); }

        if (Mathf.Abs(filled - shownLength) > .003f)
        {
            shownLength = filled;
            length.localScale = new Vector3(1f, 1f, Mathf.Max(.02f, filled));
            FitFire(filled);
        }
        // 게이지 값 -> 굵기·입자 크기·방출량. 하한 덕에 낮은 충전에서도 가늘게 남는다.
        float gain = element == WeaponElement.Electric ? .7f : element == WeaponElement.Fire ? .85f : 1f;
        float floor = element == WeaponElement.Fire ? .5f : .25f;
        float energy = gain * Mathf.Lerp(floor, 1f, Mathf.Clamp01(filled));
        if (Mathf.Abs(energy - shownEnergy) <= .01f) return;
        float previousEnergy = shownEnergy;
        shownEnergy = energy;
        playback.SetBladeWidthMultiplier(energy);
        playback.SetTrailWidthMultiplier(energy);
        DisableLights();   // 두 호출이 무기 설정대로 조명을 다시 켠다
        GrowLiveFlames(previousEnergy, energy);
        float strength = Strength(energy);
        for (int i = 0; i < rated.Count; i++)
        {
            if (rated[i] == null) continue;
            var emission = rated[i].emission;
            emission.rateOverTimeMultiplier = baseRates[i] * strength;
        }
    }

    // 무기 설정은 굵기가 바뀌면 새로 나올 불꽃 크기만 바꾼다. 불꽃이 몇 장 안 되는 큰 입자라
    // 그대로 두면 게이지가 오를 때마다 큰 불꽃이 새로 피어나는 것처럼 보인다: 떠 있는 불꽃도 같은 비율로 키운다.
    private void GrowLiveFlames(float previousEnergy, float energy)
    {
        if (fireCore == null || previousEnergy <= 0f) return;
        float ratio = Mathf.Clamp01(energy) / Mathf.Max(.0001f, Mathf.Clamp01(previousEnergy));
        if (Mathf.Approximately(ratio, 1f)) return;
        int capacity = Mathf.Min(fireCore.main.maxParticles, 256);
        if (fireBuffer.Length < capacity) fireBuffer = new ParticleSystem.Particle[capacity];
        int count = fireCore.GetParticles(fireBuffer);
        if (count == 0) return;
        for (int i = 0; i < count; i++) fireBuffer[i].startSize *= ratio;
        fireCore.SetParticles(fireBuffer, count);
    }

    // 게이지가 비었거나 HUD가 꺼진 동안은 그리기뿐 아니라 파티클 계산과 아크 갱신도 멈춘다(상태는 보존).
    private void SetRunning(bool value)
    {
        if (running == value) return;
        running = value;
        foreach (var p in playback.Particles)
        {
            if (p == null || !p.gameObject.activeInHierarchy) continue;
            if (value) p.Play(false);
            else p.Pause(false);
        }
        foreach (var arc in arcs) if (arc != null) arc.SetVisible(value);
    }

    // 무기 프리팹의 점광원과 밝기 스크립트는 조명을 안 받는 HUD 파티클에 쓸모가 없다.
    private void DisableLights()
    {
        foreach (var light in lights) if (light != null) light.enabled = false;
    }

    internal void Dispose()
    {
        ClearPlayback();
        if (texture != null) { texture.Release(); Object.Destroy(texture); }
        if (stage != null) Object.Destroy(stage);
        if (view != null) Object.Destroy(view.gameObject);
        if (viewMaterial != null) Object.Destroy(viewMaterial);
    }

    private void EnsureTexture()
    {
        var fillRect = (RectTransform)fill.transform;
        float scale = fill.canvas != null ? fill.canvas.scaleFactor : 1f;
        int width = Mathf.Clamp(Mathf.RoundToInt(fillRect.rect.width * (1f + Side * 2f) * scale), 64, MaxTextureWidth);
        int height = Mathf.Max(16, Mathf.RoundToInt(width * (1f + Above + Below) / ((fillRect.rect.width / Mathf.Max(1f, fillRect.rect.height)) * (1f + Side * 2f))));
        if (texture != null && texture.width == width && texture.height == height) return;
        if (texture != null) { camera.targetTexture = null; texture.Release(); Object.Destroy(texture); }
        texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "HudEnergyWeaponFx" };
        texture.Create();
        camera.targetTexture = texture;
        view.texture = texture;
    }

    private void ClearPlayback()
    {
        playback?.Dispose();
        playback = null;
        foreach (var copy in copies) if (copy != null) Object.Destroy(copy);
        copies.Clear(); rated.Clear(); baseRates.Clear();
        lights = System.Array.Empty<Light>();
        arcs = System.Array.Empty<WeaponElectricBladeArc>();
        fireCore = null;
        fireBuffer = System.Array.Empty<ParticleSystem.Particle>();
        shownLength = shownEnergy = -1f;
    }

    private void Build(WeaponElement next)
    {
        ClearPlayback();
        element = next;
        var profile = Resources.Load<GreatswordElementFxProfile>(GreatswordElementFxProfile.ResourcePath);
        GameObject source = profile != null && OverburstElementRules.IsActive(next) ? profile.BladeFor(next) : null;
        if (source == null) return;
        tuning = profile.TuningFor(next);

        // 찬 길이 1(=바 전체)을 기준으로 만들고, 실제 찬 길이는 FilledLength의 z 스케일로 준다.
        length.localScale = Vector3.one;
        Vector2 place = Placement(next);
        float span = barLength * place.y;
        float z = barLength * place.x + span * .5f;
        float trailSpan = barLength * tuning.trailLength;
        float trailZ = barLength * tuning.trailCenter + trailSpan * .5f;
        var container = new GameObject("WeaponEffects2Blade");
        container.SetActive(false);   // 생성자가 배치·스케일을 다 준 뒤 켠다
        container.layer = StageLayer;
        container.transform.SetParent(length, false);
        playback = new WeaponEffects2Playback(source, container.transform, new Vector3(0f, 0f, z),
            new Vector3(tuning.bladeThickness, tuning.bladeThickness, span / WeaponEffects2Playback.AuthoredBladeLength), false,
            tuning.trailDensity, tuning.trailSpread, tuning.trailParticleSize, tuning.trailParticleLifetime,
            new Vector3(0f, 0f, trailZ),
            new Vector3(tuning.trailScale, tuning.trailScale, trailSpan / WeaponEffects2Playback.AuthoredBladeLength),
            next == WeaponElement.Fire || next == WeaponElement.Dark || next == WeaponElement.Light);
        foreach (var t in container.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = StageLayer;
        cameraData.requiresColorOption = next == WeaponElement.Light ? CameraOverrideOption.On : CameraOverrideOption.Off;
        // 어둠의 검은 연기와 얼음 냉기는 알파대로 덮는다. 나머지는 가산으로 얹어야 가산·굴절 입자의 불투명 알파가 검은 판으로 보이지 않는다.
        viewMaterial.SetFloat("_AlphaWeight", next == WeaponElement.Dark || next == WeaponElement.Ice ? 1f : 0f);
        view.SetMaterialDirty();   // 마스크 아래 복사 재질도 새 값으로 다시 만들게 한다
        lights = container.GetComponentsInChildren<Light>(true);
        foreach (var script in container.GetComponentsInChildren<MeshEffects2.ME2_Light>(true)) script.enabled = false;
        arcs = container.GetComponentsInChildren<WeaponElectricBladeArc>(true);

        playback.SetBladeWidthMultiplier(1f);
        playback.SetTrailWidthMultiplier(1f);
        playback.SetEnergy(1f);
        DisableLights();
        Customize();
        playback.PlayContinuously();
        running = true;
    }

    // (중심, 길이) 칼날 비율. 원소 묶음마다 실제로 덮는 구간이 달라 시안 렌더에서 재어 맞춘 값. 빛은 무기 설정 그대로.
    private Vector2 Placement(WeaponElement e)
    {
        switch (e)
        {
            case WeaponElement.Fire: return new Vector2(.08f, 1.58f);
            case WeaponElement.Ice: return new Vector2(.45f, 1.1f);
            case WeaponElement.Electric: return new Vector2(.64f, 1.06f);
            case WeaponElement.Dark: return new Vector2(.38f, .72f);
            default: return new Vector2(tuning.bladeCenter, tuning.bladeLength);
        }
    }

    private float Strength(float energy)
    {
        // MeleeWeaponElementFx.ShowElement와 같은 원소별 세기 곡선.
        if (element == WeaponElement.Electric) return energy;
        if (element == WeaponElement.Fire || element == WeaponElement.Dark || element == WeaponElement.Light)
            return energy * (1f + tuning.idleStrength * (1f - energy));
        return Mathf.Lerp(tuning.idleStrength, 1f, energy);
    }

    private void Customize()
    {
        foreach (var p in playback.Particles)
        {
            var r = p.GetComponent<ParticleSystemRenderer>();
            string mat = r != null && r.sharedMaterial != null ? r.sharedMaterial.name : string.Empty;
            float rate = 1f;
            switch (element)
            {
                case WeaponElement.Fire:
                    if (mat == "FireFuel") { p.gameObject.SetActive(false); continue; }   // 끝에 고이던 보라색 연료 불꽃
                    if (mat == "Fire")
                    {
                        // 원래 초당 2장·수명 2.5~3초라 화면에 5장 안팎: 틈이 생기며 불이 꺼졌다 켜진다.
                        // 2.2배로 늘려 늘 겹치게 하고, 가산으로 겹친 만큼 밝기를 0.75배로 낮춘다.
                        rate = 2.2f;
                        Brighten(r, .75f);
                        // 방출 위치가 무작위라 한쪽이 비는 순간 불이 꺼져 보인다: 선 위를 일정 속도로 돌며 내보내
                        // 게이지 전체에 나이가 다른 불꽃이 늘 고르게 깔리게 한다(수명 동안 약 1.2바퀴).
                        var edge = p.shape;
                        edge.radiusMode = ParticleSystemShapeMultiModeValue.Loop;
                        edge.radiusSpread = 0f;
                        edge.radiusSpeed = 1.2f / Mathf.Max(.5f, p.main.startLifetime.constantMax);
                        // 늘인 빌보드는 속도 방향을 따른다: 부호만 바꿔 불꽃이 게이지 끝 쪽으로 흐르게.
                        var velocity = p.velocityOverLifetime; velocity.xMultiplier = -velocity.xMultiplier;
                        TrimFlameIntro(p);
                        fireCore = p;
                    }
                    break;
                case WeaponElement.Ice:
                    if (p.name == "Icicles" || p.name == "Icicles2" || mat == "IceParticles") { p.gameObject.SetActive(false); continue; }
                    if (mat == "IceFog")
                    {
                        // 남기는 것은 게이지에서 위로 피어오르는 냉기뿐.
                        rate = 2f;
                        Fade(r, 2.4f);
                        var main = p.main; main.startSizeMultiplier *= 1.3f;
                        var velocity = p.velocityOverLifetime;
                        velocity.enabled = true;
                        velocity.space = ParticleSystemSimulationSpace.World;
                        // 세 축은 같은 커브 모드여야 한다(y가 두 상수라 x·z도 두 상수 0..0).
                        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
                        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
                        velocity.y = new ParticleSystem.MinMaxCurve(.09f, .16f);
                    }
                    break;
                case WeaponElement.Electric:
                    if (mat == "Effect9_Electric_LightingParticlesLine2") { p.gameObject.SetActive(false); continue; } // 바 안 분홍 점선
                    { var main = p.main; main.simulationSpeed *= .55f; }
                    break;
                case WeaponElement.Dark:
                    // 붉은 파편은 적고 어둡게, 검은 연기 비중은 크게.
                    if (mat == "Smoke") { rate = .75f; Fade(r, .9f); }
                    else if (mat == "Effect10_Dark_Particles") { rate = .7f; Tint(r, new Color(.35f, .02f, .04f), .75f); }
                    else if (mat == "SmokeTrails") Fade(r, .75f);
                    break;
            }
            var emission = p.emission;
            emission.rateOverTimeMultiplier *= rate;
            rated.Add(p);
            baseRates.Add(emission.rateOverTimeMultiplier);
        }

        if (element != WeaponElement.Electric) return;
        foreach (var chain in playback.Root.GetComponentsInChildren<ChainElectricityLiteVfxController>(true))
        {
            // 아크는 자기 변환의 local z -0.5..0.5에 놓인다: 바 전체(찬 길이 1) 가운데에 두고 96%로 늘린다.
            Transform t = chain.transform;
            float current = (t.TransformPoint(0f, 0f, .5f) - t.TransformPoint(0f, 0f, -.5f)).magnitude;
            t.position = blade.TransformPoint(0f, 0f, barLength * .5f);
            Vector3 s = t.localScale; s.z *= barLength * .96f / Mathf.Max(.0001f, current); t.localScale = s;
            chain.SetAuraTiming(.1f, .6f);
        }
        foreach (var arc in playback.Root.GetComponentsInChildren<WeaponElectricBladeArc>(true))
        {
            // Configure는 체인 참조도 바꾸므로, 같은 오브젝트(또는 자식)의 체인을 찾았을 때만 간격을 늘린다.
            var chain = arc.GetComponentInChildren<ChainElectricityLiteVfxController>(true);
            if (chain != null) arc.Configure(chain, .5f);
        }
    }

    // 불은 Local 스케일이라 칼날 길이 스케일을 무시한다: 방출 구간(파티클 local X = 칼날 -Z)을 찬 길이에 직접 맞춘다.
    private void FitFire(float filled)
    {
        if (fireCore == null) return;
        float filledLength = barLength * Mathf.Max(.02f, filled);
        var shape = fireCore.shape;
        float rootZ = blade.InverseTransformPoint(fireCore.transform.position).z;
        float a = Mathf.Min(.08f * barLength, .3f * filledLength);
        float b = Mathf.Max(a + .1f * filledLength, filledLength - .05f * barLength);
        Vector3 position = shape.position; position.x = rootZ - (a + b) * .5f; shape.position = position;
        shape.radius = (b - a) * .5f;
    }

    // 불꽃 한 장은 수명의 41%에 걸쳐 나타나고 49%부터 사라지며 70% 크기에서 자라, 몇 장 안 되는 불꽃이
    // 번갈아 꺼졌다 켜지는 것처럼 보인다. 12%에 나타나 85%까지 유지하고, 88% 크기에서 시작하게 바꾼다.
    private static void TrimFlameIntro(ParticleSystem p)
    {
        var color = p.colorOverLifetime;
        if (color.enabled && color.color.mode == ParticleSystemGradientMode.Gradient && color.color.gradient != null)
        {
            Gradient source = color.color.gradient;
            var held = new Gradient { mode = source.mode };
            held.SetKeys(source.colorKeys, new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .12f),
                new GradientAlphaKey(1f, .85f), new GradientAlphaKey(0f, 1f)
            });
            color.color = new ParticleSystem.MinMaxGradient(held);
        }
        var size = p.sizeOverLifetime;
        if (size.enabled && size.size.mode == ParticleSystemCurveMode.Curve)
            size.size = new ParticleSystem.MinMaxCurve(size.sizeMultiplier, AnimationCurve.Linear(0f, .88f, 1f, 1f));
    }

    private void Brighten(ParticleSystemRenderer r, float scale)
    {
        Material copy = Copy(r);
        if (copy == null) return;
        Color c = copy.GetColor("_Color"); copy.SetColor("_Color", new Color(c.r * scale, c.g * scale, c.b * scale, c.a));
    }

    private void Fade(ParticleSystemRenderer r, float alpha)
    {
        Material copy = Copy(r);
        if (copy == null) return;
        Color c = copy.GetColor("_Color"); c.a *= alpha; copy.SetColor("_Color", c);
    }

    private void Tint(ParticleSystemRenderer r, Color rgb, float alpha)
    {
        Material copy = Copy(r);
        if (copy == null) return;
        Color c = copy.GetColor("_Color"); copy.SetColor("_Color", new Color(rgb.r, rgb.g, rgb.b, c.a * alpha));
    }

    private Material Copy(ParticleSystemRenderer r)
    {
        if (r == null || r.sharedMaterial == null || !r.sharedMaterial.HasProperty("_Color")) return null;
        var copy = new Material(r.sharedMaterial) { name = r.sharedMaterial.name + " (HUD)" };
        copies.Add(copy);
        r.sharedMaterial = copy;
        return copy;
    }
}
