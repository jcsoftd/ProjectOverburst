using DuloGames.UI;
using UnityEngine;
using UnityEngine.UI;

// 2026-09-30: HUD 자원 바 연출(원소 에너지·체력). RPG11 Fill 위에 같은 모양의 가산 층(HudResourceBarUI 셰이더)을 얹는다.
// 에너지: 원소별 두 톤 색, 충전 단계(34% / 67% / 100%)마다 흐름·밝기·끝빛이 오르고 67%부터 끝 불꽃,
//        가득 찬 순간 번쩍 + 빛 쓸기 + 맥동, 빛 과충전(101~200)은 금빛 층(줄어들 때 깜빡임).
// 체력: 붉은 흐름이 늘 흐르고, 맞으면 끝이 번쩍, 회복하면 빛이 쓸고, 30% 이하에서 맥동(낮을수록 빠르게).
// RPG11 Fill은 Filled + 좌우 뒤집기(UIFlippable)라 같은 설정을 그대로 복사해 모양을 맞춘다.
[DisallowMultipleComponent]
public sealed class HudResourceBarFx : MonoBehaviour
{
    public enum BarKind { Energy, Health }

    private const string ShaderResourcePath = "Shaders/HudResourceBarUI";
    private const string EnergyFillPath = "Action Bar Unit Frame/Bar (Power)/Fill";
    private const string HealthFillPath = "Action Bar Unit Frame/Bar (Health)/Fill";

    private static readonly int ElementColorId = Shader.PropertyToID("_ElementColor");
    private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
    private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
    private static readonly int OverColorId = Shader.PropertyToID("_OverColor");
    private static readonly int UvRectId = Shader.PropertyToID("_UvRect");
    private static readonly int FillId = Shader.PropertyToID("_Fill");
    private static readonly int FromRightId = Shader.PropertyToID("_FromRight");
    private static readonly int ChargeId = Shader.PropertyToID("_Charge");
    private static readonly int FullId = Shader.PropertyToID("_Full");
    private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
    private static readonly int FlashId = Shader.PropertyToID("_Flash");
    private static readonly int ShinePosId = Shader.PropertyToID("_ShinePos");
    private static readonly int OverId = Shader.PropertyToID("_Over");
    private static readonly int DrainId = Shader.PropertyToID("_Drain");
    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int ColorAId = Shader.PropertyToID("_ColorA");
    private static readonly int ColorBId = Shader.PropertyToID("_ColorB");
    private static readonly int BarBandId = Shader.PropertyToID("_BarBand");
    private static readonly int BarAspectId = Shader.PropertyToID("_BarAspect");

    // 셰이더 원소 오라 층(불꽃·서리·전류·연기·빛): 무기 이펙트(HudEnergyWeaponFx)를 만들 수 없을 때만 쓰는 대체 연출.
    // 바보다 아래 0.5칸, 위 1.6칸 더 크게 잡는다(바 높이 단위).
    private const string AuraShaderPath = "Shaders/HudResourceBarAuraUI";
    private const float AuraBelow = .5f, AuraAbove = 1.6f;

    private const float TierLow = .34f, TierHigh = .67f;
    private const float ShineSeconds = .45f;
    private const float FlashDecayPerSecond = 3.2f;
    private const float HealthDangerRatio = .3f;
    private const float HealthFlowCharge = .45f;

    private BarKind kind;
    private Image fill;
    private Image fx;
    private Material material;
    private Image aura;
    private Material auraMaterial;
    private HudEnergyWeaponFx weaponFx;
    private Color originalFillColor;
    private OverburstElementEnergy energy;
    private CombatHealth health;
    private float nextResolveAt;
    private float shownFill;
    private float shownOver;
    private float lastAmount;
    private float lastTarget = -1f;
    private float flash;
    private float shineStartedAt = -1f;
    private int lastTier = -1;
    private WeaponElement lastElement = (WeaponElement)(-1);

    // HUD는 씬 전환 때 다시 만들어질 수 있다. 설치기는 찾은 HUD와 붙인 효과를 저장해 두고,
    // HUD가 없거나 파괴됐을 때만 1초 간격으로 다시 찾는다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallWatcher()
    {
        var host = new GameObject(nameof(HudResourceBarFx) + "Installer");
        DontDestroyOnLoad(host);
        host.AddComponent<Installer>();
    }

    private sealed class Installer : MonoBehaviour
    {
        private OverburstGameUI ui;
        private HudResourceBarFx energyFx, healthFx;
        private float nextCheckAt;

        private void Update()
        {
            // 파괴된 UnityEngine.Object는 == null이 참이라, 캐시가 살아 있으면 탐색도 Find도 하지 않는다.
            if (ui != null && ui.hud != null && energyFx != null && healthFx != null) return;
            if (Time.unscaledTime < nextCheckAt) return;
            nextCheckAt = Time.unscaledTime + 1f;
            if (ui == null || ui.hud == null)
            {
                energyFx = healthFx = null;
                ui = FindFirstObjectByType<OverburstGameUI>();
                if (ui == null || ui.hud == null) return;
            }
            if (energyFx == null) energyFx = Attach(ui.hud.Find(EnergyFillPath), BarKind.Energy);
            if (healthFx == null) healthFx = Attach(ui.hud.Find(HealthFillPath), BarKind.Health);
        }

        private static HudResourceBarFx Attach(Transform target, BarKind barKind)
        {
            if (target == null || target.GetComponent<Image>() == null) return null;
            HudResourceBarFx existing = target.GetComponent<HudResourceBarFx>();
            if (existing != null) return existing;
            HudResourceBarFx added = target.gameObject.AddComponent<HudResourceBarFx>();
            added.Init(barKind);
            return added;
        }
    }

    private void Init(BarKind barKind)
    {
        kind = barKind;
        fill = GetComponent<Image>();
        originalFillColor = fill.color;
        Shader shader = Resources.Load<Shader>(ShaderResourcePath);
        if (shader == null)
        {
            Debug.LogError("[HudResourceBarFx] Missing shader: " + ShaderResourcePath);
            enabled = false;
            return;
        }
        material = new Material(shader) { name = "HudResourceBarFx " + kind + " (Runtime)" };

        var layer = new GameObject("ResourceBarFx", typeof(RectTransform));
        layer.transform.SetParent(transform, false);
        var rect = (RectTransform)layer.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        fx = layer.AddComponent<Image>();
        fx.raycastTarget = false;
        fx.material = material;
        CopyShape();

        UIFlippable sourceFlip = GetComponent<UIFlippable>();
        if (sourceFlip != null)
        {
            UIFlippable flip = layer.AddComponent<UIFlippable>();
            flip.horizontal = sourceFlip.horizontal;
            flip.vertical = sourceFlip.vertical;
        }

        if (kind == BarKind.Health) ApplyHealthPalette();
        else
        {
            // 에너지 바 주변 원소 연출 = 대검 원소 칼날 이펙트(2026-09-30 사용자 확정). 만들 수 없을 때만 셰이더 오라로 대체.
            bool flippedHorizontal = sourceFlip != null && sourceFlip.horizontal;
            bool fromRight = fill.fillMethod == Image.FillMethod.Horizontal && fill.fillOrigin == (int)Image.OriginHorizontal.Right;
            weaponFx = HudEnergyWeaponFx.Create(fill, fromRight != flippedHorizontal);
            if (weaponFx == null) CreateAura(flippedHorizontal);
        }
    }

    private void CreateAura(bool flippedHorizontal)
    {
        Shader shader = Resources.Load<Shader>(AuraShaderPath);
        if (shader == null)
        {
            Debug.LogError("[HudResourceBarFx] Missing shader: " + AuraShaderPath);
            return;
        }
        auraMaterial = new Material(shader) { name = "HudResourceBarAura (Runtime)" };

        var fillRect = (RectTransform)transform;
        float h = fillRect.rect.height;
        float w = fillRect.rect.width;
        var layer = new GameObject("ResourceBarAura", typeof(RectTransform));
        var rect = (RectTransform)layer.transform;
        rect.SetParent(transform, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(0f, -h * AuraBelow);
        rect.offsetMax = new Vector2(0f, h * AuraAbove);
        // Fill 자식이면 바 테두리 그림에 가려지므로 바 아래로 옮겨 테두리 위·글자 아래에 둔다.
        Transform bar = transform.parent;
        if (bar != null)
        {
            rect.SetParent(bar, true);
            Transform text = bar.Find("Text Group");
            if (text != null) rect.SetSiblingIndex(text.GetSiblingIndex());
            else rect.SetAsLastSibling();
        }
        aura = layer.AddComponent<Image>();
        aura.raycastTarget = false;
        aura.material = auraMaterial;

        float total = AuraBelow + 1f + AuraAbove;
        auraMaterial.SetVector(BarBandId, new Vector4(AuraBelow / total, (AuraBelow + 1f) / total, 0f, 0f));
        auraMaterial.SetFloat(BarAspectId, h > .01f ? w / h : 10f);
        // 오라는 뒤집기 없는 사각형이라, 화면에서 보이는 채움 방향(기준 방향 XOR 좌우 뒤집기)을 쓴다.
        bool fromRight = fill.fillMethod == Image.FillMethod.Horizontal && fill.fillOrigin == (int)Image.OriginHorizontal.Right;
        auraMaterial.SetFloat(FromRightId, fromRight != flippedHorizontal ? 1f : 0f);
    }

    private void OnDestroy()
    {
        if (fill != null) fill.color = originalFillColor;
        if (material != null) Destroy(material);
        if (aura != null) Destroy(aura.gameObject);
        if (auraMaterial != null) Destroy(auraMaterial);
        weaponFx?.Dispose();
        weaponFx = null;
    }

    private void CopyShape()
    {
        fx.sprite = fill.sprite;
        fx.type = Image.Type.Filled;
        fx.fillMethod = fill.fillMethod;
        fx.fillOrigin = fill.fillOrigin;
        fx.fillClockwise = fill.fillClockwise;
        fx.preserveAspect = fill.preserveAspect;
        Vector4 uv = fill.sprite != null ? UnityEngine.Sprites.DataUtility.GetOuterUV(fill.sprite) : new Vector4(0f, 0f, 1f, 1f);
        material.SetVector(UvRectId, uv);
        bool fromRight = fill.fillMethod == Image.FillMethod.Horizontal && fill.fillOrigin == (int)Image.OriginHorizontal.Right;
        material.SetFloat(FromRightId, fromRight ? 1f : 0f);
    }

    // OverburstGameUI가 Update에서 원래 채움 값을 넣은 뒤, 여기서 부드럽게 한 값으로 덮어쓴다.
    private void LateUpdate()
    {
        if (fill == null || material == null || fx == null) return;
        if (fill.sprite != fx.sprite) CopyShape();
        ResolveSource();

        float dt = Time.unscaledDeltaTime;
        float target, charge, pulse, pulseSpeed = 5.2f, over = 0f;
        bool active, draining = false;

        if (kind == BarKind.Energy)
        {
            WeaponElement element = energy != null ? energy.Element : WeaponElement.None;
            target = energy != null ? energy.Normalized : 0f;
            if (element != lastElement)
            {
                lastElement = element;
                lastTier = ResolveTier(target);
                ApplyEnergyPalette(element);
            }
            int tier = ResolveTier(target);
            if (tier > lastTier && lastTier >= 0)
            {
                if (tier == 3) { flash = 1f; shineStartedAt = Time.unscaledTime; }
                else flash = Mathf.Max(flash, .35f);
            }
            lastTier = tier;
            float amount = energy != null ? energy.Amount : 0f;
            over = element == WeaponElement.Light && energy != null ? energy.OverchargeNormalized : 0f;
            draining = over > .001f && amount < lastAmount - .0001f;
            lastAmount = amount;
            charge = shownFill;
            pulse = tier == 3 ? 1f : 0f;
            active = element != WeaponElement.None;
        }
        else
        {
            target = health != null ? health.NormalizedHp : 0f;
            if (lastTarget >= 0f)
            {
                if (target < lastTarget - .005f) flash = Mathf.Max(flash, .45f);
                else if (target > lastTarget + .005f && shineStartedAt < 0f) shineStartedAt = Time.unscaledTime;
            }
            lastTarget = target;
            charge = HealthFlowCharge;
            float danger = target > 0f && target <= HealthDangerRatio ? 1f - target / HealthDangerRatio : 0f;
            pulse = target > 0f && target <= HealthDangerRatio ? 1f : 0f;
            pulseSpeed = Mathf.Lerp(4f, 10f, danger);
            active = health != null;
        }

        // 차오를 때는 부드럽게, 줄어들 때는 빠르게.
        float rate = target >= shownFill ? 10f : 22f;
        shownFill = Mathf.Lerp(shownFill, target, 1f - Mathf.Exp(-rate * dt));
        if (Mathf.Abs(shownFill - target) < .002f) shownFill = target;
        shownOver = Mathf.Lerp(shownOver, over, 1f - Mathf.Exp(-10f * dt));
        flash = Mathf.Max(0f, flash - FlashDecayPerSecond * dt);

        float shinePos = -1f;
        if (shineStartedAt >= 0f)
        {
            float t = (Time.unscaledTime - shineStartedAt) / ShineSeconds;
            if (t >= 1f) shineStartedAt = -1f;
            else shinePos = Mathf.Lerp(-.1f, 1.1f, t);
        }

        fill.fillAmount = shownFill;
        fx.enabled = active && shownFill > .001f;
        fx.fillAmount = shownFill;
        ApplyFrame(material, charge, pulse, pulseSpeed, shinePos, draining);
        // 마스크 아래에서는 UGUI가 스텐실용 복사 재질로 그리므로 그 재질에도 같은 값을 넣는다.
        Material rendering = fx.canvasRenderer.materialCount > 0 ? fx.canvasRenderer.GetMaterial() : null;
        if (rendering != null && rendering != material) ApplyFrame(rendering, charge, pulse, pulseSpeed, shinePos, draining);

        if (aura != null)
        {
            aura.enabled = active && shownFill > .001f;
            ApplyAura(auraMaterial, charge, pulse);
            Material auraRendering = aura.canvasRenderer.materialCount > 0 ? aura.canvasRenderer.GetMaterial() : null;
            if (auraRendering != null && auraRendering != auraMaterial) ApplyAura(auraRendering, charge, pulse);
        }
        weaponFx?.Tick(kind == BarKind.Energy ? lastElement : WeaponElement.None, shownFill, target, active);
    }

    private void ApplyAura(Material target, float charge, float pulse)
    {
        target.SetFloat(FillId, shownFill);
        target.SetFloat(ChargeId, charge);
        target.SetFloat(FullId, pulse);
        if (target == auraMaterial) return;
        target.SetFloat(ModeId, auraMaterial.GetFloat(ModeId));
        target.SetColor(ColorAId, auraMaterial.GetColor(ColorAId));
        target.SetColor(ColorBId, auraMaterial.GetColor(ColorBId));
        target.SetColor(CoreColorId, auraMaterial.GetColor(CoreColorId));
        target.SetVector(BarBandId, auraMaterial.GetVector(BarBandId));
        target.SetFloat(BarAspectId, auraMaterial.GetFloat(BarAspectId));
        target.SetFloat(FromRightId, auraMaterial.GetFloat(FromRightId));
    }

    private void ApplyFrame(Material target, float charge, float pulse, float pulseSpeed, float shinePos, bool draining)
    {
        target.SetFloat(FillId, shownFill);
        target.SetFloat(ChargeId, charge);
        target.SetFloat(FullId, pulse);
        target.SetFloat(PulseSpeedId, pulseSpeed);
        target.SetFloat(FlashId, flash);
        target.SetFloat(ShinePosId, shinePos);
        target.SetFloat(OverId, shownOver);
        target.SetFloat(DrainId, draining ? 1f : 0f);
        if (target == material) return;
        target.SetColor(ElementColorId, material.GetColor(ElementColorId));
        target.SetColor(AccentColorId, material.GetColor(AccentColorId));
        target.SetColor(CoreColorId, material.GetColor(CoreColorId));
        target.SetColor(OverColorId, material.GetColor(OverColorId));
        target.SetVector(UvRectId, material.GetVector(UvRectId));
        target.SetFloat(FromRightId, material.GetFloat(FromRightId));
    }

    private void ResolveSource()
    {
        bool valid = kind == BarKind.Energy ? energy != null && energy.isActiveAndEnabled : health != null;
        if (valid && Time.unscaledTime < nextResolveAt) return;
        nextResolveAt = Time.unscaledTime + .5f;
        PlayerContext context = PlayerContext.Instance;
        if (kind == BarKind.Health)
        {
            CombatHealth next = context != null ? context.CurrentActorHealth : null;
            if (next != health) lastTarget = -1f;
            health = next;
            return;
        }
        PlayerEquipment equipment = context != null ? context.CurrentActorEquipment : null;
        energy = equipment != null ? equipment.GetComponent<OverburstElementEnergy>() : null;
    }

    private static int ResolveTier(float normalized)
    {
        if (normalized >= .999f) return 3;
        if (normalized >= TierHigh) return 2;
        return normalized >= TierLow ? 1 : 0;
    }

    private void ApplyEnergyPalette(WeaponElement element)
    {
        ResolveEnergyPalette(element, out Color tint, out Color flowA, out Color flowB, out Color core);
        fill.color = element == WeaponElement.None ? originalFillColor : new Color(tint.r, tint.g, tint.b, originalFillColor.a);
        SetPalette(flowA, flowB, core);
        if (auraMaterial == null) return;
        auraMaterial.SetFloat(ModeId, AuraMode(element));
        auraMaterial.SetColor(ColorAId, flowA);
        auraMaterial.SetColor(ColorBId, flowB);
        auraMaterial.SetColor(CoreColorId, core);
    }

    // 셰이더 _Mode: 1 불꽃, 2 서리, 3 전류, 4 검은 연기, 5 빛줄기.
    private static float AuraMode(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return 1f;
            case WeaponElement.Ice: return 2f;
            case WeaponElement.Electric: return 3f;
            case WeaponElement.Dark: return 4f;
            case WeaponElement.Light: return 5f;
            default: return 0f;
        }
    }

    private void ApplyHealthPalette()
    {
        SetPalette(new Color(1f, .18f, .14f), new Color(1f, .42f, .25f), new Color(1f, .72f, .6f));
    }

    private void SetPalette(Color flowA, Color flowB, Color core)
    {
        material.SetColor(ElementColorId, flowA);
        material.SetColor(AccentColorId, flowB);
        material.SetColor(CoreColorId, core);
        material.SetColor(OverColorId, new Color(1f, .8f, .35f, 1f));
    }

    // 원소별 Fill 틴트 / 흐름 두 톤(A↔B) / 심(끝빛·불꽃·번쩍). 수치는 Play 확인 전 시안(2026-09-30 1차 조정).
    // 번개 = 보라↔파랑, 빛 = 상아·금에 옅은 파랑, 어둠 = 검정 바탕에 짙은 붉은 결.
    private static void ResolveEnergyPalette(WeaponElement element, out Color tint, out Color flowA, out Color flowB, out Color core)
    {
        switch (element)
        {
            case WeaponElement.Fire:
                tint = new Color(1f, .36f, .06f); flowA = new Color(1f, .36f, .06f); flowB = new Color(1f, .62f, .12f); core = new Color(1f, .85f, .45f); break;
            case WeaponElement.Ice:
                tint = new Color(.35f, .8f, 1f); flowA = new Color(.35f, .8f, 1f); flowB = new Color(.7f, .95f, 1f); core = new Color(.88f, .98f, 1f); break;
            case WeaponElement.Electric:
                tint = new Color(.45f, .42f, 1f); flowA = new Color(.58f, .3f, 1f); flowB = new Color(.3f, .62f, 1f); core = new Color(.75f, .9f, 1f); break;
            case WeaponElement.Dark:
                tint = new Color(.13f, .03f, .05f); flowA = new Color(.05f, 0f, .02f); flowB = new Color(.55f, .04f, .1f); core = new Color(.75f, .1f, .14f); break;
            case WeaponElement.Light:
                tint = new Color(1f, .95f, .8f); flowA = new Color(1f, .92f, .7f); flowB = new Color(.62f, .8f, 1f); core = new Color(.9f, .96f, 1f); break;
            default:
                tint = new Color(.24f, .7f, 1f); flowA = tint; flowB = tint; core = Color.white; break;
        }
    }
}
