using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 2026-10-01 설정 > 조작의 한 줄. 게임 입력 맵(Gameplay)의 한 동작에서 키보드·마우스 첫 바인딩 하나를 보이고 바꾼다.
/// 이동처럼 합성 입력이면 compositePart(Up·Down·Left·Right)의 첫 키보드 바인딩을 쓴다.
/// </summary>
public sealed class OverburstKeyBindingRow : MonoBehaviour
{
    public string actionName;
    public string compositePart;
    public string mirrorUiAction; // 장비창(C)은 UI 맵에도 같은 키가 있어 함께 바꾼다.
    public Text label;
    public Text keyText;
    public Button keyButton;

    public InputAction FindAction(InputActionAsset asset)
        => asset != null ? asset.FindAction(PlayerInputFacade.GameplayMapName + "/" + actionName) : null;

    public int ResolveBindingIndex(InputAction action) => ResolveBindingIndex(action, compositePart);

    // HUD 퀵슬롯 글자(QuickSlotKeyLabels)도 같은 규칙으로 바인딩을 고른다. 설정 창에 보이는 키와 HUD 글자가 어긋나지 않게 한다.
    public static int ResolveBindingIndex(InputAction action, string compositePart)
    {
        if (action == null) return -1;
        var bindings = action.bindings;
        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            if (binding.isComposite) continue;
            bool partMatches = string.IsNullOrEmpty(compositePart)
                ? !binding.isPartOfComposite
                : binding.isPartOfComposite && string.Equals(binding.name, compositePart, System.StringComparison.OrdinalIgnoreCase);
            if (!partMatches) continue;
            string path = binding.path ?? string.Empty;
            if (path.StartsWith("<Keyboard>") || path.StartsWith("<Mouse>")) return i;
        }
        return -1;
    }

    public string CurrentPath(InputActionAsset asset)
    {
        var action = FindAction(asset);
        int index = ResolveBindingIndex(action);
        return index >= 0 ? action.bindings[index].effectivePath : null;
    }

    public void Refresh(InputActionAsset asset)
    {
        string path = CurrentPath(asset);
        keyText.text = string.IsNullOrEmpty(path) ? "—" : DisplayName(path);
    }

    private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
    {
        { "<Mouse>/leftButton", "마우스 왼쪽" }, { "<Mouse>/rightButton", "마우스 오른쪽" },
        { "<Mouse>/middleButton", "마우스 휠 클릭" }, { "<Mouse>/backButton", "마우스 뒤로" }, { "<Mouse>/forwardButton", "마우스 앞으로" },
        { "<Keyboard>/leftShift", "왼쪽 Shift" }, { "<Keyboard>/rightShift", "오른쪽 Shift" },
        { "<Keyboard>/leftCtrl", "왼쪽 Ctrl" }, { "<Keyboard>/rightCtrl", "오른쪽 Ctrl" },
        { "<Keyboard>/leftAlt", "왼쪽 Alt" }, { "<Keyboard>/rightAlt", "오른쪽 Alt" },
        { "<Keyboard>/space", "Space" }, { "<Keyboard>/tab", "Tab" }, { "<Keyboard>/capsLock", "Caps Lock" },
        { "<Keyboard>/escape", "ESC" }, { "<Keyboard>/enter", "Enter" }, { "<Keyboard>/backspace", "Backspace" },
        { "<Keyboard>/upArrow", "↑" }, { "<Keyboard>/downArrow", "↓" }, { "<Keyboard>/leftArrow", "←" }, { "<Keyboard>/rightArrow", "→" },
        { "<Keyboard>/backquote", "`" },
    };

    public static string DisplayName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "—";
        if (Names.TryGetValue(path, out string name)) return name;
        if (path.StartsWith("<Keyboard>/numpad")) return "숫자판 " + path.Substring("<Keyboard>/numpad".Length).ToUpperInvariant();
        string readable = InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
        return readable.Length == 1 ? readable.ToUpperInvariant() : readable;
    }

    // HUD 칸 모서리처럼 좁은 곳에 쓰는 짧은 이름. 설정 창은 위의 DisplayName(긴 이름)을 쓴다.
    private static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>
    {
        { "<Mouse>/leftButton", "M1" }, { "<Mouse>/rightButton", "M2" }, { "<Mouse>/middleButton", "M3" },
        { "<Mouse>/backButton", "M4" }, { "<Mouse>/forwardButton", "M5" },
        { "<Keyboard>/leftShift", "Shift" }, { "<Keyboard>/rightShift", "Shift" },
        { "<Keyboard>/leftCtrl", "Ctrl" }, { "<Keyboard>/rightCtrl", "Ctrl" },
        { "<Keyboard>/leftAlt", "Alt" }, { "<Keyboard>/rightAlt", "Alt" },
        { "<Keyboard>/space", "Space" }, { "<Keyboard>/tab", "Tab" }, { "<Keyboard>/capsLock", "Caps" },
        { "<Keyboard>/escape", "Esc" }, { "<Keyboard>/enter", "Enter" }, { "<Keyboard>/backspace", "Back" },
        { "<Keyboard>/insert", "Ins" }, { "<Keyboard>/delete", "Del" }, { "<Keyboard>/home", "Home" }, { "<Keyboard>/end", "End" },
        { "<Keyboard>/pageUp", "PgUp" }, { "<Keyboard>/pageDown", "PgDn" },
        { "<Keyboard>/upArrow", "↑" }, { "<Keyboard>/downArrow", "↓" }, { "<Keyboard>/leftArrow", "←" }, { "<Keyboard>/rightArrow", "→" },
        { "<Keyboard>/backquote", "`" }, { "<Keyboard>/minus", "-" }, { "<Keyboard>/equals", "=" },
        { "<Keyboard>/leftBracket", "[" }, { "<Keyboard>/rightBracket", "]" }, { "<Keyboard>/backslash", "\\" },
        { "<Keyboard>/semicolon", ";" }, { "<Keyboard>/quote", "'" }, { "<Keyboard>/comma", "," },
        { "<Keyboard>/period", "." }, { "<Keyboard>/slash", "/" },
        { "<Keyboard>/numpadEnter", "NEnt" }, { "<Keyboard>/numpadPlus", "N+" }, { "<Keyboard>/numpadMinus", "N-" },
        { "<Keyboard>/numpadMultiply", "N*" }, { "<Keyboard>/numpadDivide", "N/" }, { "<Keyboard>/numpadPeriod", "N." },
        { "<Keyboard>/numpadEquals", "N=" },
    };

    public static string ShortName(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        if (ShortNames.TryGetValue(path, out string name)) return name;
        const string KeyboardPrefix = "<Keyboard>/";
        if (path.StartsWith(KeyboardPrefix))
        {
            string key = path.Substring(KeyboardPrefix.Length);
            if (key.Length == 7 && key.StartsWith("numpad") && char.IsDigit(key[6])) return "N" + key[6];
            if (key.Length == 1) return key.ToUpperInvariant();
            if (key.Length <= 3 && key[0] == 'f' && int.TryParse(key.Substring(1), out _)) return key.ToUpperInvariant();
        }
        return DisplayName(path);
    }
}
