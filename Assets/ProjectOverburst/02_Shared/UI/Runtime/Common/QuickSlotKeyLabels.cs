using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 2026-10-01 퀵슬롯 1~10에 지금 걸려 있는 키를 짧은 글자로 돌려준다(HUD 칸 모서리, 장비창 물약 칸, 물약 번호 고르기, 우클릭 등록 메뉴).
/// 설정 > 조작에서 키를 바꾸거나(OverburstGameSettings.Changed, 바인딩 재해석) 입력 에셋이 새로 만들어지면 다시 읽는다.
/// 한 번 만든 문자열을 돌려주므로 매 프레임 불러도 새로 할당하지 않는다. 어느 바인딩을 읽는지는 설정 창 키 행과 같은 규칙이다.
/// </summary>
public static class QuickSlotKeyLabels
{
    private const int SlotCount = InventoryQuickSlotBindingController.SlotCount;
    private static readonly string[] DefaultLabels = { "", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private static readonly string[] shortLabels = new string[SlotCount + 1];
    private static readonly string[] numberedLabels = new string[SlotCount + 1];
    private static InputActionAsset cachedAsset;
    private static bool dirty = true;
    private static bool subscribed;
    private static bool allDefault = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        InputSystem.onActionChange -= HandleActionChange;
        cachedAsset = null;
        dirty = true;
        subscribed = false;
        allDefault = true;
        Array.Clear(shortLabels, 0, shortLabels.Length);
        Array.Clear(numberedLabels, 0, numberedLabels.Length);
    }

    /// <summary>칸 모서리에 쓰는 짧은 키 글자("1", "Q", "Shift", "M4"). 키가 없으면 빈 글자.</summary>
    public static string Short(int key)
    {
        if (key < 1 || key > SlotCount) return string.Empty;
        Refresh();
        return shortLabels[key];
    }

    /// <summary>문장 안에 쓰는 이름. 숫자 키는 "3번", 그 밖의 키는 "Q 키".</summary>
    public static string Numbered(int key)
    {
        if (key < 1 || key > SlotCount) return string.Empty;
        Refresh();
        return numberedLabels[key];
    }

    /// <summary>퀵슬롯 열 칸이 모두 기본 숫자 키(1~9·0)인지. 안내 문구를 고를 때 쓴다.</summary>
    public static bool AllDefault
    {
        get { Refresh(); return allDefault; }
    }

    private static void HandleActionChange(object target, InputActionChange change)
    {
        if (change == InputActionChange.BoundControlsChanged) dirty = true;
    }

    private static void MarkDirty() => dirty = true;

    private static void Refresh()
    {
        if (!subscribed)
        {
            subscribed = true;
            OverburstGameSettings.Changed += MarkDirty;
            InputSystem.onActionChange += HandleActionChange;
        }

        PlayerInputFacade facade = PlayerInputFacade.Current;
        InputActionAsset asset = facade != null ? facade.RuntimeAsset : null;
        if (!dirty && ReferenceEquals(asset, cachedAsset) && shortLabels[1] != null) return;

        dirty = false;
        cachedAsset = asset;
        allDefault = true;
        for (int key = 1; key <= SlotCount; key++)
        {
            string label = DefaultLabels[key];
            if (asset != null)
            {
                InputAction action = asset.FindAction(PlayerInputFacade.GameplayMapName + "/QuickSlot" + key);
                int index = OverburstKeyBindingRow.ResolveBindingIndex(action, null);
                label = index >= 0 ? OverburstKeyBindingRow.ShortName(action.bindings[index].effectivePath) : string.Empty;
            }
            if (!string.Equals(shortLabels[key], label, StringComparison.Ordinal))
            {
                shortLabels[key] = label;
                numberedLabels[key] = label.Length == 1 && char.IsDigit(label[0]) ? label + "번"
                    : label.Length > 0 ? label + " 키" : "키 없음";
            }
            if (label != DefaultLabels[key]) allDefault = false;
        }
    }
}
