#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Overburst.DebugTools;
using UnityEngine;

/// <summary>
/// 데미지 숫자 비교(90C 8.2). 옛 HUD 데미지 숫자 비교 패널(2026-10-01 삭제)의 버튼 14개·순환 버튼 4개를 선택 4줄 + 토글 1줄로 바꾸고,
/// A/B 비교 슬롯을 더했다. 슬롯은 Play 세션 동안만 유지한다(선택값 자체와 같은 기준).
/// </summary>
internal static class DamageNumberDebugModule
{
    private struct Slot
    {
        public bool Saved;
        public DamageNumberFeelPreset Preset;
        public DamageNumberFontChoice Font;
        public DamageNumberWeightChoice Weight;
        public DamageNumberSizeChoice Size;
        public bool Styled;
    }

    private static Slot a;
    private static Slot b;
    private static bool showingB;
    private static bool autoPreview = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        a = default;
        b = default;
        showingB = false;
        autoPreview = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DebugSection s = DebugRegistry.Section(DebugTabs.Presentation, "데미지 숫자", 10, "바꾸면 바로 미리보기");
        s.Choice("연출", () => DamageNumberPopup.SelectedPreset, DamageNumberPopup.SelectPreset, DamageNumberPopup.PresetLabel)
            .WithId("presentation.damage.preset")
            .AfterChange(AutoPreview)
            .Keywords("damage", "피해", "숫자");
        s.Choice("글꼴", () => DamageNumberPopup.SelectedFont, DamageNumberPopup.SelectFont, DamageNumberPopup.FontLabel)
            .WithId("presentation.damage.font")
            .AfterChange(AutoPreview);
        s.Choice("굵기", () => DamageNumberPopup.SelectedWeight, DamageNumberPopup.SelectWeight, DamageNumberPopup.WeightLabel)
            .WithId("presentation.damage.weight")
            .AfterChange(AutoPreview);
        s.Choice("크기", () => DamageNumberPopup.SelectedSize, DamageNumberPopup.SelectSize, DamageNumberPopup.SizeLabel)
            .WithId("presentation.damage.size")
            .AfterChange(AutoPreview);
        s.Toggle("종류별 연출", () => DamageNumberStyleSettings.Enabled, DamageNumberStyleSettings.SetEnabled)
            .WithId("presentation.damage.styled")
            .AfterChange(AutoPreview)
            .Tip("켜면 치명타·원소 방출·지속 피해를 종류별로 다르게 그린다. 끄면 이전 연출(되돌리기 스위치).");
        s.Buttons("비교 슬롯")
            .Add(() => a.Saved ? "A 다시 저장" : "A에 저장", () => Save(ref a, "A"))
            .Add(() => b.Saved ? "B 다시 저장" : "B에 저장", () => Save(ref b, "B"))
            .Add(() => showingB ? "A로 전환" : "B로 전환", Swap)
            .WithId("presentation.damage.slots")
            .Tip("두 조합을 저장해 두고 한 번에 바꿔 보며 비교한다.");
        s.Readout("슬롯", SlotText)
            .Lines(2)
            .WithId("presentation.damage.slotText");
        s.Button("다시 보기", Preview)
            .WithId("presentation.damage.preview");
        s.Toggle("변경 시 자동 미리보기", () => autoPreview, value => autoPreview = value)
            .WithId("presentation.damage.autoPreview")
            .NoPreset();
    }

    private static DebugResult Save(ref Slot slot, string name)
    {
        slot = Capture();
        return DebugResult.Ok($"{name} = {Describe(slot)}");
    }

    private static DebugResult Swap()
    {
        Slot target = showingB ? a : b;
        string name = showingB ? "A" : "B";
        if (!target.Saved)
            return DebugResult.Fail($"{name} 슬롯이 비어 있어요");
        Apply(target);
        showingB = !showingB;
        Preview();
        return DebugResult.Ok($"{name}: {Describe(target)}");
    }

    private static Slot Capture() => new Slot
    {
        Saved = true,
        Preset = DamageNumberPopup.SelectedPreset,
        Font = DamageNumberPopup.SelectedFont,
        Weight = DamageNumberPopup.SelectedWeight,
        Size = DamageNumberPopup.SelectedSize,
        Styled = DamageNumberStyleSettings.Enabled
    };

    private static void Apply(Slot slot)
    {
        DamageNumberPopup.SelectPreset(slot.Preset);
        DamageNumberPopup.SelectFont(slot.Font);
        DamageNumberPopup.SelectWeight(slot.Weight);
        DamageNumberPopup.SelectSize(slot.Size);
        DamageNumberStyleSettings.SetEnabled(slot.Styled);
    }

    private static string Describe(Slot slot)
        => slot.Saved
            ? $"{DamageNumberPopup.PresetLabel(slot.Preset)}·{DamageNumberPopup.FontLabel(slot.Font)}·{DamageNumberPopup.WeightLabel(slot.Weight)}·{DamageNumberPopup.SizeLabel(slot.Size)}·{(slot.Styled ? "종류별" : "기존")}"
            : "비어 있음";

    private static string SlotText() => $"A  {Describe(a)}\nB  {Describe(b)}";

    private static void AutoPreview()
    {
        if (autoPreview)
            Preview();
    }

    private static DebugResult Preview()
    {
        int shown = DamageNumberSpawner.PreviewSelectedPreset();
        return shown > 0 ? DebugResult.Ok($"{shown}개") : DebugResult.Fail("미리보기 불가(플레이어나 카메라가 없어요)");
    }
}
#endif
