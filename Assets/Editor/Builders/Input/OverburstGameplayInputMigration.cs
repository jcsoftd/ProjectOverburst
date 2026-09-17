using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// GOAL A2: Gameplay에 QuickSlot4~7(숫자열+numpad 4~7)과 LootModeCycle(CapsLock)을 멱등 추가한다.
// - Player/UI map과 기존 action/binding을 삭제·이름변경하지 않는다.
// - Gameplay/DebugValidation map만 멱등으로 추가한다.
// - .inputactions JSON을 직접 편집하지 않는다.
public static class OverburstGameplayInputMigration
{
    public const string AssetPath = "Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";
    public const string GameplayMapName = "Gameplay";
    public const string DebugValidationMapName = "DebugValidation";
    public const string MenuPath = "OVERBURST/Codex/Setup/Input/Apply GOAL A1 Input Migration";

    private const string KeyboardMouseGroup = "Keyboard&Mouse";
    private const string GamepadGroup = "Gamepad";
    private const string PressInteraction = "Press";
    private const string HoldInteraction = "Hold";

    [MenuItem(MenuPath)]
    public static void MigrateFromMenu()
    {
        Debug.Log(MigrateAndReport());
    }

    public static string MigrateAndReport()
    {
        // 실패한 실행의 메모리 부분 변경을 버리고 디스크 원본으로 초기화한다.
        AssetDatabase.ImportAsset(
            AssetPath,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        InputActionAsset diskAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
        Require(diskAsset != null, "Input Action 에셋을 찾을 수 없다: " + AssetPath);

        // 기존 map 보호: Player/UI가 없으면 진행하지 않는다.
        Require(diskAsset.FindActionMap("Player") != null, "기존 Player map이 없어 마이그레이션을 중단한다.");
        Require(diskAsset.FindActionMap("UI") != null, "기존 UI map이 없어 마이그레이션을 중단한다.");

        // Unity 작업 디렉터리에 의존하지 않는 정규화 절대 경로 하나를 계산한다.
        string assetFilePath = ResolveAssetFilePath();
        byte[] originalBytes = File.ReadAllBytes(assetFilePath);

        // 실제 asset이 아니라 복제본에만 변경한다. 예외가 나면 실제 asset은 손대지 않는다.
        InputActionAsset working = UnityEngine.Object.Instantiate(diskAsset);
        try
        {
            bool changed = false;
            changed |= EnsureDebugValidationMap(working);
            changed |= EnsureGameplayMap(working);

            if (!changed)
            {
                string noChangeReport = OverburstGameplayInputValidator.ValidateAndReport();
                return "[OverburstGameplayInputMigration] NO_CHANGE"
                    + "\n- Player/UI map 보존, Gameplay/DebugValidation 멱등 확인"
                    + "\n- asset: " + AssetPath
                    + "\n" + noChangeReport;
            }

            // Input System API로 생성한 전체 자산 직렬화를 UTF-8 BOM 없이 저장한다.
            File.WriteAllText(assetFilePath, working.ToJson(), new UTF8Encoding(false));
            try
            {
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
                string appliedReport = OverburstGameplayInputValidator.ValidateAndReport();
                return "[OverburstGameplayInputMigration] APPLIED"
                    + "\n- Player/UI map 보존, Gameplay/DebugValidation 멱등 확인"
                    + "\n- asset: " + AssetPath
                    + "\n" + appliedReport;
            }
            catch (Exception)
            {
                File.WriteAllBytes(assetFilePath, originalBytes);
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }
        finally
        {
            if (working != null)
                UnityEngine.Object.DestroyImmediate(working);
        }
    }

    private static bool EnsureDebugValidationMap(InputActionAsset asset)
    {
        // 분리 경계만 만든다. 검증 입력 계약이 확정되지 않았으므로 임의 키를 추가하지 않는다.
        if (asset.FindActionMap(DebugValidationMapName) != null)
            return false;
        asset.AddActionMap(new InputActionMap(DebugValidationMapName));
        return true;
    }

    private static bool EnsureGameplayMap(InputActionAsset asset)
    {
        bool changed = false;
        InputActionMap map = asset.FindActionMap(GameplayMapName);
        if (map == null)
        {
            map = new InputActionMap(GameplayMapName);
            asset.AddActionMap(map);
            changed = true;
        }

        changed |= EnsureMove(map);
        changed |= EnsureLook(map);
        changed |= EnsureButtonWithBindings(map, "Attack", new BindingSpec[]
        {
            new BindingSpec("<Mouse>/leftButton", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Gamepad>/buttonWest", string.Empty, GamepadGroup),
        });
        changed |= EnsureButtonWithBindings(map, "Interact", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/f", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "Evade", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/leftShift", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Keyboard>/rightShift", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "CombatMode", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/x", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "Inventory", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/tab", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "Cancel", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/escape", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "Jump", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/space", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Gamepad>/buttonSouth", string.Empty, GamepadGroup),
        });
        changed |= EnsureButtonWithBindings(map, "WalkToggle", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/c", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "Aim", new BindingSpec[]
        {
            new BindingSpec("<Mouse>/rightButton", HoldInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureCameraRotate(map);
        changed |= EnsureZoom(map);
        // A2: 소비품 4~7 기존 감각(숫자열+numpad, 이번 프레임 눌림, 4>5>6>7 우선은 소비자 else-if로 보존)과
        // CapsLock 루팅 모드 전환을 보존한다.
        changed |= EnsureButtonWithBindings(map, "QuickSlot4", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/4", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Keyboard>/numpad4", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "QuickSlot5", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/5", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Keyboard>/numpad5", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "QuickSlot6", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/6", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Keyboard>/numpad6", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "QuickSlot7", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/7", PressInteraction, KeyboardMouseGroup),
            new BindingSpec("<Keyboard>/numpad7", PressInteraction, KeyboardMouseGroup),
        });
        changed |= EnsureButtonWithBindings(map, "LootModeCycle", new BindingSpec[]
        {
            new BindingSpec("<Keyboard>/capsLock", PressInteraction, KeyboardMouseGroup),
        });
        return changed;
    }

    private static bool EnsureMove(InputActionMap map)
    {
        bool changed = false;
        InputAction action = map.FindAction("Move");
        if (action == null)
        {
            action = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
            changed = true;
        }
        // WASD/방향키 2DVector 합성 + Gamepad left stick. 확인된 gamepad 바인딩만 보존한다.
        changed |= EnsureComposite(action, "2DVector", new CompositePart[]
        {
            new CompositePart("Up", "<Keyboard>/w", KeyboardMouseGroup),
            new CompositePart("Up", "<Keyboard>/upArrow", KeyboardMouseGroup),
            new CompositePart("Down", "<Keyboard>/s", KeyboardMouseGroup),
            new CompositePart("Down", "<Keyboard>/downArrow", KeyboardMouseGroup),
            new CompositePart("Left", "<Keyboard>/a", KeyboardMouseGroup),
            new CompositePart("Left", "<Keyboard>/leftArrow", KeyboardMouseGroup),
            new CompositePart("Right", "<Keyboard>/d", KeyboardMouseGroup),
            new CompositePart("Right", "<Keyboard>/rightArrow", KeyboardMouseGroup),
        });
        changed |= EnsureSimpleBinding(action, "<Gamepad>/leftStick", string.Empty, GamepadGroup);
        return changed;
    }

    private static bool EnsureLook(InputActionMap map)
    {
        bool changed = false;
        InputAction action = map.FindAction("Look");
        if (action == null)
        {
            action = map.AddAction("Look", InputActionType.Value, expectedControlLayout: "Vector2");
            changed = true;
        }
        changed |= EnsureSimpleBinding(action, "<Pointer>/delta", string.Empty, KeyboardMouseGroup);
        changed |= EnsureSimpleBinding(action, "<Gamepad>/rightStick", string.Empty, GamepadGroup);
        return changed;
    }

    private static bool EnsureCameraRotate(InputActionMap map)
    {
        bool changed = false;
        InputAction action = map.FindAction("CameraRotate");
        if (action == null)
        {
            action = map.AddAction("CameraRotate", InputActionType.Value, expectedControlLayout: "Axis");
            changed = true;
        }
        changed |= EnsureComposite(action, "1DAxis", new CompositePart[]
        {
            new CompositePart("Negative", "<Keyboard>/q", KeyboardMouseGroup),
            new CompositePart("Positive", "<Keyboard>/e", KeyboardMouseGroup),
        });
        return changed;
    }

    private static bool EnsureZoom(InputActionMap map)
    {
        bool changed = false;
        InputAction action = map.FindAction("Zoom");
        if (action == null)
        {
            action = map.AddAction("Zoom", InputActionType.Value, expectedControlLayout: "Vector2");
            changed = true;
        }
        changed |= EnsureSimpleBinding(action, "<Mouse>/scroll", string.Empty, KeyboardMouseGroup);
        return changed;
    }

    private static bool EnsureButtonWithBindings(InputActionMap map, string actionName, BindingSpec[] bindings)
    {
        bool changed = false;
        InputAction action = map.FindAction(actionName);
        if (action == null)
        {
            action = map.AddAction(actionName, InputActionType.Button, expectedControlLayout: "Button");
            changed = true;
        }
        foreach (BindingSpec spec in bindings)
            changed |= EnsureSimpleBinding(action, spec.Path, spec.Interactions, spec.Groups);
        return changed;
    }

    private static bool EnsureSimpleBinding(InputAction action, string path, string interactions, string groups)
    {
        foreach (InputBinding existing in action.bindings)
        {
            if (existing.isComposite || existing.isPartOfComposite)
                continue;
            if (string.Equals(existing.path, path, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        action.AddBinding(path, interactions, null, groups);
        return true;
    }

    private static bool EnsureComposite(InputAction action, string compositeType, CompositePart[] parts)
    {
        string[] rootGroups = parts
            .Select(part => part.Groups)
            .Where(group => !string.IsNullOrEmpty(group))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string rootGroupsText = string.Join(";", rootGroups);
        bool compositeExists = action.bindings.Any(b =>
            b.isComposite && string.Equals(b.path, compositeType, StringComparison.OrdinalIgnoreCase));
        if (!compositeExists)
        {
            int rootIndex = action.bindings.Count;
            var syntax = action.AddCompositeBinding(compositeType);
            foreach (CompositePart part in parts)
                syntax = syntax.With(part.Name, part.Path);
            action.ChangeBinding(rootIndex).WithGroups(rootGroupsText);
            return true;
        }
        // 합성이 있으면 부분·root 그룹 누락을 loud failure로 보고한다. 수동 JSON 훼손 외에는 도달하지 않는다.
        foreach (CompositePart part in parts)
        {
            bool partExists = action.bindings.Any(b =>
                b.isPartOfComposite
                && string.Equals(b.name, part.Name, StringComparison.Ordinal)
                && string.Equals(b.path, part.Path, StringComparison.OrdinalIgnoreCase));
            Require(partExists, "Gameplay/" + action.name + " 합성 부분 누락: " + part.Name + " " + part.Path);
        }
        InputBinding root = action.bindings.First(b =>
            b.isComposite && string.Equals(b.path, compositeType, StringComparison.OrdinalIgnoreCase));
        foreach (string group in rootGroups)
            Require(ContainsText(root.groups, group), "Gameplay/" + action.name + " 합성 root 그룹 누락: " + group);
        return false;
    }

    private static bool ContainsText(string text, string value)
    {
        return !string.IsNullOrEmpty(text)
            && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string ResolveAssetFilePath()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string filePath = Path.GetFullPath(Path.Combine(projectRoot, AssetPath));
        string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Require(
            filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase),
            "에셋 파일 경로가 프로젝트 루트 하위가 아니다: " + filePath);
        return filePath;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class BindingSpec
    {
        public readonly string Path;
        public readonly string Interactions;
        public readonly string Groups;

        public BindingSpec(string path, string interactions, string groups)
        {
            Path = path;
            Interactions = interactions;
            Groups = groups;
        }
    }

    private sealed class CompositePart
    {
        public readonly string Name;
        public readonly string Path;
        public readonly string Groups;

        public CompositePart(string name, string path, string groups)
        {
            Name = name;
            Path = path;
            Groups = groups;
        }
    }
}
