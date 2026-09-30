using System;
using TMPro;
using UnityEngine;

// 2026-09-30: 피해 숫자 종류별 연출. 움직임·크기·투명도 곡선은 FEEL(MMFloatingText)이 맡고,
// 글자별 정점 연출·장식은 DamageNumberFx, 글자 재질은 FloatingFeedbackTextStyle이 맡는다.
// DamageNumberStyleSettings.Enabled를 끄면 이전 연출 경로를 그대로 쓴다(되돌리기 스위치).
public enum DamageNumberKind { Normal, Critical, DamageOverTime, Discharge, PlayerHit, Heal }

public enum DamageNumberMaterialStyle
{
    Normal, Critical, DamageOverTime, Fire, Ice, Electric, Dark, Light, PlayerHit, Heal
}

public enum DamageNumberFxKind { None, Critical, Ember, Shatter, Shock, Sink, Radiance, PlayerHit }

public static class DamageNumberStyleSettings
{
    public static bool Enabled { get; private set; } = true;
    public static event Action<bool> Changed;

    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;
        Enabled = enabled;
        Changed?.Invoke(enabled);
    }

    public static void Toggle() => SetEnabled(!Enabled);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        Enabled = true;
        Changed = null;
    }
}

public readonly struct DamageNumberStyleRequest
{
    public readonly DamageNumberKind Kind;
    public readonly WeaponElement Element;
    public readonly bool IsCritical;
    public readonly int ChainCount;

    public DamageNumberStyleRequest(DamageNumberKind kind, WeaponElement element, bool isCritical, int chainCount = 0)
    {
        Kind = kind;
        Element = element;
        IsCritical = isCritical;
        ChainCount = chainCount;
    }
}

public sealed class DamageNumberMotion
{
    public float Lifetime;
    public float Rise;
    public float LateralRange;
    public float FontScale = 1f;
    public AnimationCurve Vertical;
    public AnimationCurve Scale;
    public AnimationCurve Opacity;
}

public static class DamageNumberStyles
{
    // 원소 방출 판정: 원소 방출 피해는 약공·강공 표시 없이 Elemental만 달고 온다(ElementDischargeBatch,
    // MeleeHeavyDischargeExecutor, ShatterWaveScheduler, UpperElementCombatUtility). 원소가 붙은 일반 타격은 Weak/Heavy가 같이 붙는다.
    public static DamageNumberStyleRequest Classify(in DamageInfo info, int electricChainCount)
    {
        bool elemental = (info.playerAttackKind & PlayerAttackKind.Elemental) != 0;
        bool weaponSwing = (info.playerAttackKind & (PlayerAttackKind.Weak | PlayerAttackKind.Heavy)) != 0;
        if (info.isDamageOverTime)
            return new DamageNumberStyleRequest(DamageNumberKind.DamageOverTime, info.element, false);
        if (elemental && !weaponSwing && HasDischargeStyle(info.element))
            return new DamageNumberStyleRequest(DamageNumberKind.Discharge, info.element, info.isCritical,
                info.element == WeaponElement.Electric ? electricChainCount : 0);
        return new DamageNumberStyleRequest(info.isCritical ? DamageNumberKind.Critical : DamageNumberKind.Normal,
            info.element, info.isCritical);
    }

    public static bool HasDischargeStyle(WeaponElement element) =>
        element == WeaponElement.Fire || element == WeaponElement.Ice || element == WeaponElement.Electric
        || element == WeaponElement.Dark || element == WeaponElement.Light;

    public static string DischargeName(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return "연소";
            case WeaponElement.Ice: return "쇄빙";
            case WeaponElement.Electric: return "감전";
            case WeaponElement.Dark: return "잠식";
            case WeaponElement.Light: return "광휘";
            default: return string.Empty;
        }
    }

    // Label on a smaller first line, number below. Formats are cached so a popup does not build strings.
    // 감전 연쇄는 첫 타에만 "감전"을 쓰고 이어지는 타는 ×N만 붙인다(튕길 때마다 이름이 겹쳐 쌓이지 않게).
    private static readonly string[] DischargeFormats = new string[16];
    private static readonly string[] DischargeCriticalFormats = new string[16];
    private const string ChainFormat = "<size=55%>×{1:0}</size>\n{0:0}";
    private const string ChainCriticalFormat = "<size=55%>×{1:0}</size>\n{0:0}!";

    public static string DischargeFormat(WeaponElement element, bool critical, int chainCount)
    {
        if (element == WeaponElement.Electric && chainCount >= 2)
            return critical ? ChainCriticalFormat : ChainFormat;
        int index = Mathf.Clamp((int)element, 0, DischargeFormats.Length - 1);
        string[] cache = critical ? DischargeCriticalFormats : DischargeFormats;
        return cache[index] ??= "<size=55%><cspace=1>" + DischargeName(element) + "</cspace></size>\n{0:0}" + (critical ? "!" : string.Empty);
    }

    public static DamageNumberMaterialStyle MaterialFor(in DamageNumberStyleRequest request)
    {
        switch (request.Kind)
        {
            case DamageNumberKind.Critical: return DamageNumberMaterialStyle.Critical;
            case DamageNumberKind.DamageOverTime: return DamageNumberMaterialStyle.DamageOverTime;
            case DamageNumberKind.PlayerHit: return DamageNumberMaterialStyle.PlayerHit;
            case DamageNumberKind.Heal: return DamageNumberMaterialStyle.Heal;
            case DamageNumberKind.Discharge:
                switch (request.Element)
                {
                    case WeaponElement.Fire: return DamageNumberMaterialStyle.Fire;
                    case WeaponElement.Ice: return DamageNumberMaterialStyle.Ice;
                    case WeaponElement.Electric: return DamageNumberMaterialStyle.Electric;
                    case WeaponElement.Dark: return DamageNumberMaterialStyle.Dark;
                    case WeaponElement.Light: return DamageNumberMaterialStyle.Light;
                }
                break;
        }
        return DamageNumberMaterialStyle.Normal;
    }

    public static DamageNumberFxKind FxFor(in DamageNumberStyleRequest request)
    {
        switch (request.Kind)
        {
            case DamageNumberKind.Critical: return DamageNumberFxKind.Critical;
            case DamageNumberKind.PlayerHit: return DamageNumberFxKind.PlayerHit;
            case DamageNumberKind.Discharge:
                switch (request.Element)
                {
                    case WeaponElement.Fire: return DamageNumberFxKind.Ember;
                    case WeaponElement.Ice: return DamageNumberFxKind.Shatter;
                    case WeaponElement.Electric: return DamageNumberFxKind.Shock;
                    case WeaponElement.Dark: return DamageNumberFxKind.Sink;
                    case WeaponElement.Light: return DamageNumberFxKind.Radiance;
                }
                break;
        }
        return DamageNumberFxKind.None;
    }

    // Top/bottom vertex colors. The text color stays white so the gradient shows as authored.
    // 2026-09-30 v2: 사용자 지적("카툰풍·캐주얼") 뒤로 채도를 낮추고 위아래 차이를 줄였다. 어두운 판타지 UI 색에 맞춘다.
    public static bool TryGetGradient(in DamageNumberStyleRequest request, out VertexGradient gradient)
    {
        Color top, bottom;
        switch (request.Kind)
        {
            case DamageNumberKind.Critical: top = Hex(0xF6DC97); bottom = Hex(0xDDA24A); break;
            case DamageNumberKind.PlayerHit: top = Hex(0xEE8172); bottom = Hex(0xB52A20); break;
            case DamageNumberKind.Heal: top = Hex(0xBDEAC4); bottom = Hex(0x5DAE6E); break;
            case DamageNumberKind.Discharge:
                switch (request.Element)
                {
                    case WeaponElement.Fire: top = Hex(0xF4B585); bottom = Hex(0xCC5B2E); break;
                    case WeaponElement.Ice: top = Hex(0xEEF6FA); bottom = Hex(0xA7CBDC); break;
                    case WeaponElement.Electric: top = Hex(0xE5DCFA); bottom = Hex(0xA48CDD); break;
                    case WeaponElement.Dark: top = Hex(0xC6A8E0); bottom = Hex(0x7B55A2); break;
                    case WeaponElement.Light: top = Hex(0xF6EDCF); bottom = Hex(0xD4B56A); break;
                    default: gradient = default; return false;
                }
                break;
            default:
                gradient = default;
                return false;
        }
        gradient = new VertexGradient(top, top, bottom, bottom);
        return true;
    }

    // 일반 타격 글자색(흰색 대신 살짝 따뜻한 상아색)
    public static readonly Color NormalColor = new Color(.93f, .9f, .84f, 1f);

    // Damage over time keeps one flat element color so a stream of small ticks reads as one source.
    public static Color DamageOverTimeColor(WeaponElement element)
    {
        switch (element)
        {
            case WeaponElement.Fire: return Hex(0xE09460);
            case WeaponElement.Electric: return Hex(0xB6A2E0);
            case WeaponElement.Dark: return Hex(0xA884C6);
            case WeaponElement.Ice: return Hex(0xA8CFE0);
            case WeaponElement.Light: return Hex(0xE2CC8E);
            default: return new Color(.8f, .78f, .74f, 1f);
        }
    }

    public static DamageNumberMotion MotionFor(in DamageNumberStyleRequest request)
    {
        switch (request.Kind)
        {
            case DamageNumberKind.DamageOverTime: return DamageOverTimeMotion;
            case DamageNumberKind.PlayerHit: return PlayerHitMotion;
            case DamageNumberKind.Heal: return HealMotion;
            case DamageNumberKind.Discharge:
                switch (request.Element)
                {
                    case WeaponElement.Fire: return FireMotion;
                    case WeaponElement.Ice: return IceMotion;
                    case WeaponElement.Electric: return ElectricMotion;
                    case WeaponElement.Dark: return DarkMotion;
                    case WeaponElement.Light: return LightMotion;
                }
                break;
        }
        return null; // Normal and critical keep the FEEL preset chosen in the debug panel.
    }

    private static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

    private static AnimationCurve Curve(params float[] timeValue)
    {
        var keys = new Keyframe[timeValue.Length / 2];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new Keyframe(timeValue[i * 2], timeValue[i * 2 + 1]);
        var curve = new AnimationCurve(keys);
        for (int i = 0; i < keys.Length; i++)
            curve.SmoothTangents(i, 0f);
        return curve;
    }

    // 2026-09-30 v2(카툰풍 지적 뒤): 튀어나오는 크기를 1.2~1.3배 안으로 줄이고 되튐을 없앴다.
    // 지속 피해: 틱마다 작게, 빠르게 솟았다 사라진다. 다음 틱이 올라올 때 이전 숫자는 이미 위에 있어 주르륵 이어진다.
    // 2026-10-01: 사용자 요청으로 크기를 조금 키웠다(일반 숫자의 0.66배 -> 0.8배).
    private static readonly DamageNumberMotion DamageOverTimeMotion = new DamageNumberMotion
    {
        Lifetime = .62f, Rise = 44f, LateralRange = 9f, FontScale = .8f,
        Vertical = Curve(0f, 0f, .18f, .62f, .5f, .88f, 1f, 1f),
        Scale = Curve(0f, .8f, .1f, 1.04f, .22f, 1f, 1f, .95f),
        Opacity = Curve(0f, .9f, .5f, .9f, 1f, 0f),
    };

    // 연소: 크게 솟는다. 불씨는 DamageNumberFx.
    private static readonly DamageNumberMotion FireMotion = new DamageNumberMotion
    {
        Lifetime = .86f, Rise = 90f, LateralRange = 8f, FontScale = 1.18f,
        Vertical = Curve(0f, 0f, .12f, .6f, .36f, .86f, 1f, 1f),
        Scale = Curve(0f, .7f, .1f, 1.28f, .26f, 1f, 1f, .97f),
        Opacity = Curve(0f, 1f, .55f, 1f, 1f, 0f),
    };

    // 쇄빙: 올라와 딱 멈춘 뒤(0.12~0.34) 갈라지면서 조각이 조금 떨어진다.
    private static readonly DamageNumberMotion IceMotion = new DamageNumberMotion
    {
        Lifetime = .92f, Rise = 36f, LateralRange = 0f, FontScale = 1.18f,
        Vertical = new AnimationCurve(new Keyframe(0f, 0f, 9f, 9f), new Keyframe(.12f, 1f, 0f, 0f),
            new Keyframe(.34f, 1f, 0f, 0f), new Keyframe(1f, .62f, -.6f, 0f)),
        Scale = new AnimationCurve(new Keyframe(0f, .75f), new Keyframe(.08f, 1.22f), new Keyframe(.16f, 1f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f)),
        Opacity = Curve(0f, 1f, .66f, 1f, 1f, 0f),
    };

    // 감전: 빠르게 오르고 떨림은 정점 연출이 맡는다.
    private static readonly DamageNumberMotion ElectricMotion = new DamageNumberMotion
    {
        Lifetime = .8f, Rise = 56f, LateralRange = 6f, FontScale = 1.12f,
        Vertical = Curve(0f, 0f, .16f, .66f, .44f, .9f, 1f, 1f),
        Scale = Curve(0f, .75f, .09f, 1.2f, .2f, 1f, 1f, .98f),
        Opacity = Curve(0f, 1f, .56f, 1f, 1f, 0f),
    };

    // 잠식: 살짝 떠올랐다가 위가 아니라 아래로 가라앉는다.
    private static readonly DamageNumberMotion DarkMotion = new DamageNumberMotion
    {
        Lifetime = .96f, Rise = -42f, LateralRange = 0f, FontScale = 1.12f,
        Vertical = Curve(0f, 0f, .1f, -.3f, .28f, .05f, 1f, 1f),
        Scale = Curve(0f, .75f, .1f, 1.18f, .24f, 1f, 1f, 1f),
        Opacity = Curve(0f, 1f, .6f, 1f, 1f, 0f),
    };

    // 광휘: 부드럽게 천천히 떠오른다. 빛줄기는 DamageNumberFx.
    private static readonly DamageNumberMotion LightMotion = new DamageNumberMotion
    {
        Lifetime = 1.05f, Rise = 64f, LateralRange = 0f, FontScale = 1.12f,
        Vertical = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
        Scale = Curve(0f, .85f, .16f, 1.1f, .42f, 1f, 1f, 1.01f),
        Opacity = Curve(0f, 1f, .62f, 1f, 1f, 0f),
    };

    private static readonly DamageNumberMotion PlayerHitMotion = new DamageNumberMotion
    {
        Lifetime = .72f, Rise = 32f, LateralRange = 0f, FontScale = 1.05f,
        Vertical = Curve(0f, 0f, .16f, .7f, .44f, .9f, 1f, 1f),
        Scale = Curve(0f, .8f, .08f, 1.18f, .2f, 1f, 1f, 1f),
        Opacity = Curve(0f, 1f, .5f, 1f, 1f, 0f),
    };

    private static readonly DamageNumberMotion HealMotion = new DamageNumberMotion
    {
        Lifetime = 1f, Rise = 48f, LateralRange = 0f, FontScale = 1f,
        Vertical = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
        Scale = Curve(0f, .9f, .14f, 1.04f, .3f, 1f, 1f, 1f),
        Opacity = Curve(0f, 1f, .6f, 1f, 1f, 0f),
    };
}
