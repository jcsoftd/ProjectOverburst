using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// GOAL A2 검증: Gameplay 18-action 계약 + 기존 Player/UI 보존 + 파사드 이름 조회 계약.
// Play Mode나 Player 빌드를 사용하지 않는다.
// F8 DebugValidation 예외와 개발 검증 입력 주입 예외는 A2 validator가 목록으로 검사한다.
public static class OverburstGameplayInputValidator
{
    public const string AssetPath = "Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";
    public const string MenuPath = "OVERBURST/Codex/Validate/Input/Validate GOAL A1 Input";

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        Debug.Log(ValidateAndReport());
    }

    public static string ValidateAndReport()
    {
        List<string> errors = new List<string>();
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetPath);
        if (asset == null)
            throw new InvalidOperationException("Input Action 에셋을 찾을 수 없다: " + AssetPath);

        InputActionMap playerMap = asset.FindActionMap("Player");
        InputActionMap uiMap = asset.FindActionMap("UI");
        InputActionMap gameplayMap = asset.FindActionMap("Gameplay");
        InputActionMap debugMap = asset.FindActionMap("DebugValidation");

        RequireMap(errors, playerMap, "Player");
        RequireMap(errors, uiMap, "UI");
        RequireMap(errors, gameplayMap, "Gameplay");
        RequireMap(errors, debugMap, "DebugValidation");

        // 기존 Player/UI 보존.
        if (playerMap != null)
        {
            foreach (string name in new[] { "Move", "Look", "Attack", "Interact", "Crouch", "Jump", "Previous", "Next", "Sprint" })
                RequireAction(errors, playerMap, name);
            InputAction playerInteract = playerMap.FindAction("Interact");
            if (playerInteract != null)
            {
                Require(errors, HasBinding(playerInteract, "<Keyboard>/e"), "Player/Interact의 <Keyboard>/e 바인딩이 사라졌다.");
                Require(errors, ContainsText(playerInteract.interactions, "Hold"), "Player/Interact의 Hold가 바뀌었다.");
            }
        }
        if (uiMap != null)
        {
            foreach (string name in new[] { "Navigate", "Submit", "Cancel", "Point", "Click" })
                RequireAction(errors, uiMap, name);
        }

        // DebugValidation은 분리 경계만 둔다. 임의 디버그 키 금지.
        if (debugMap != null)
            Require(errors, debugMap.actions.Count == 0, "DebugValidation map에 임의 action이 있다: " + debugMap.actions.Count);

        if (gameplayMap != null)
        {
            RequireTypedAction(errors, gameplayMap, "Move", InputActionType.Value, "Vector2");
            RequireTypedAction(errors, gameplayMap, "Look", InputActionType.Value, "Vector2");
            RequireTypedAction(errors, gameplayMap, "Attack", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "Interact", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "Evade", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "CombatMode", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "Inventory", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "Cancel", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "Jump", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "WalkToggle", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "Aim", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "CameraRotate", InputActionType.Value, "Axis");
            RequireTypedAction(errors, gameplayMap, "Zoom", InputActionType.Value, "Vector2");
            RequireTypedAction(errors, gameplayMap, "QuickSlot4", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "QuickSlot5", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "QuickSlot6", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "QuickSlot7", InputActionType.Button, "Button");
            RequireTypedAction(errors, gameplayMap, "LootModeCycle", InputActionType.Button, "Button");

            RequireBinding(errors, gameplayMap, "Move", "<Keyboard>/w");
            RequireBinding(errors, gameplayMap, "Move", "<Keyboard>/upArrow");
            RequireBinding(errors, gameplayMap, "Move", "<Gamepad>/leftStick");
            RequireBinding(errors, gameplayMap, "Look", "<Pointer>/delta");
            RequireBinding(errors, gameplayMap, "Look", "<Gamepad>/rightStick");
            RequireBinding(errors, gameplayMap, "Attack", "<Mouse>/leftButton");
            RequireBinding(errors, gameplayMap, "Attack", "<Gamepad>/buttonWest");
            RequireBinding(errors, gameplayMap, "Jump", "<Keyboard>/space");
            RequireBinding(errors, gameplayMap, "Jump", "<Gamepad>/buttonSouth");
            RequireBinding(errors, gameplayMap, "Evade", "<Keyboard>/leftShift");
            RequireBinding(errors, gameplayMap, "Evade", "<Keyboard>/rightShift");
            RequireBinding(errors, gameplayMap, "CombatMode", "<Keyboard>/x");
            RequireBinding(errors, gameplayMap, "Inventory", "<Keyboard>/tab");
            RequireBinding(errors, gameplayMap, "Cancel", "<Keyboard>/escape");
            RequireBinding(errors, gameplayMap, "WalkToggle", "<Keyboard>/c");
            RequireBinding(errors, gameplayMap, "Aim", "<Mouse>/rightButton");
            RequireBinding(errors, gameplayMap, "CameraRotate", "<Keyboard>/q");
            RequireBinding(errors, gameplayMap, "CameraRotate", "<Keyboard>/e");
            RequireBinding(errors, gameplayMap, "Zoom", "<Mouse>/scroll");
            RequireBinding(errors, gameplayMap, "QuickSlot4", "<Keyboard>/4");
            RequireBinding(errors, gameplayMap, "QuickSlot4", "<Keyboard>/numpad4");
            RequireBinding(errors, gameplayMap, "QuickSlot5", "<Keyboard>/5");
            RequireBinding(errors, gameplayMap, "QuickSlot5", "<Keyboard>/numpad5");
            RequireBinding(errors, gameplayMap, "QuickSlot6", "<Keyboard>/6");
            RequireBinding(errors, gameplayMap, "QuickSlot6", "<Keyboard>/numpad6");
            RequireBinding(errors, gameplayMap, "QuickSlot7", "<Keyboard>/7");
            RequireBinding(errors, gameplayMap, "QuickSlot7", "<Keyboard>/numpad7");
            RequireBinding(errors, gameplayMap, "LootModeCycle", "<Keyboard>/capsLock");

            // Interact는 F press 1회이며 Hold가 아니다. 기존 E+Hold를 복사하지 않는다.
            InputAction interact = gameplayMap.FindAction("Interact");
            if (interact != null)
            {
                Require(errors, HasBinding(interact, "<Keyboard>/f"), "Gameplay/Interact에 <Keyboard>/f 바인딩이 없다.");
                Require(errors, !HasBinding(interact, "<Keyboard>/e"), "Gameplay/Interact가 기존 E를 복사했다.");
                Require(errors, BindingHasInteraction(interact, "<Keyboard>/f", "Press"), "Gameplay/Interact/F에 Press가 없다.");
                Require(errors, !AnyInteraction(interact, "Hold"), "Gameplay/Interact에 Hold가 있다.");
            }

            RequirePressBinding(errors, gameplayMap, "Evade", "<Keyboard>/leftShift");
            RequirePressBinding(errors, gameplayMap, "Evade", "<Keyboard>/rightShift");
            RequirePressBinding(errors, gameplayMap, "CombatMode", "<Keyboard>/x");
            RequirePressBinding(errors, gameplayMap, "Inventory", "<Keyboard>/tab");
            RequirePressBinding(errors, gameplayMap, "Cancel", "<Keyboard>/escape");
            RequirePressBinding(errors, gameplayMap, "QuickSlot4", "<Keyboard>/4");
            RequirePressBinding(errors, gameplayMap, "QuickSlot4", "<Keyboard>/numpad4");
            RequirePressBinding(errors, gameplayMap, "QuickSlot5", "<Keyboard>/5");
            RequirePressBinding(errors, gameplayMap, "QuickSlot5", "<Keyboard>/numpad5");
            RequirePressBinding(errors, gameplayMap, "QuickSlot6", "<Keyboard>/6");
            RequirePressBinding(errors, gameplayMap, "QuickSlot6", "<Keyboard>/numpad6");
            RequirePressBinding(errors, gameplayMap, "QuickSlot7", "<Keyboard>/7");
            RequirePressBinding(errors, gameplayMap, "QuickSlot7", "<Keyboard>/numpad7");
            RequirePressBinding(errors, gameplayMap, "LootModeCycle", "<Keyboard>/capsLock");

            InputAction aim = gameplayMap.FindAction("Aim");
            if (aim != null)
                Require(errors, BindingHasInteraction(aim, "<Mouse>/rightButton", "Hold"), "Gameplay/Aim에 Hold가 없다.");

            RequireCompositeGroups(errors, gameplayMap, "Move", "2DVector", "Keyboard&Mouse");
            RequireCompositeGroups(errors, gameplayMap, "CameraRotate", "1DAxis", "Keyboard&Mouse");
        }

        // 파사드가 요구 action을 이름으로 찾을 수 있는지 계약 수준에서 확인한다.
        // (Edit Mode 부작용 없이: 에셋 측 FindAction + 파사드 공개 API 리플렉션.)
        if (gameplayMap != null)
        {
            foreach (string name in PlayerInputFacade.RequiredGameplayActions)
                Require(errors, gameplayMap.FindAction(name) != null, "Facade 요구 action을 이름으로 찾을 수 없다: Gameplay/" + name);
        }
        Type facadeType = typeof(PlayerInputFacade);
        Require(errors, facadeType.GetMethod("TryGetGameplayAction") != null, "PlayerInputFacade.TryGetGameplayAction이 없다.");
        Require(errors, facadeType.GetMethod("TryGetUiAction") != null, "PlayerInputFacade.TryGetUiAction이 없다.");
        Require(errors, facadeType.GetProperty("MoveValue") != null, "PlayerInputFacade.MoveValue가 없다.");
        Require(errors, facadeType.GetProperty("LastInputDevice") != null, "PlayerInputFacade.LastInputDevice가 없다.");
        Require(errors, facadeType.GetMethod("EnableGameplay") != null, "PlayerInputFacade.EnableGameplay가 없다.");
        Require(errors, facadeType.GetMethod("DisableGameplay") != null, "PlayerInputFacade.DisableGameplay가 없다.");
        Require(errors, facadeType.GetMethod("EnableUi") != null, "PlayerInputFacade.EnableUi가 없다.");
        Require(errors, facadeType.GetMethod("DisableUi") != null, "PlayerInputFacade.DisableUi가 없다.");
        Require(errors, facadeType.GetMethod("EnableDebugValidation") != null, "PlayerInputFacade.EnableDebugValidation이 없다.");
        Require(errors, facadeType.GetMethod("DisableDebugValidation") != null, "PlayerInputFacade.DisableDebugValidation이 없다.");
        Require(errors, facadeType.GetProperty("Current") != null, "PlayerInputFacade.Current가 없다.");
        Require(errors, facadeType.GetProperty("PointerPosition") != null, "PlayerInputFacade.PointerPosition이 없다.");
        Require(errors, facadeType.GetProperty("UiSubmitPressedThisFrame") != null, "PlayerInputFacade.UiSubmitPressedThisFrame이 없다.");
        Require(errors, facadeType.GetProperty("UiCancelPressedThisFrame") != null, "PlayerInputFacade.UiCancelPressedThisFrame이 없다.");
        Require(errors, facadeType.GetProperty("QuickSlot4PressedThisFrame") != null, "PlayerInputFacade.QuickSlot4PressedThisFrame이 없다.");
        Require(errors, facadeType.GetProperty("QuickSlot5PressedThisFrame") != null, "PlayerInputFacade.QuickSlot5PressedThisFrame이 없다.");
        Require(errors, facadeType.GetProperty("QuickSlot6PressedThisFrame") != null, "PlayerInputFacade.QuickSlot6PressedThisFrame이 없다.");
        Require(errors, facadeType.GetProperty("QuickSlot7PressedThisFrame") != null, "PlayerInputFacade.QuickSlot7PressedThisFrame이 없다.");
        Require(errors, facadeType.GetProperty("LootModeCyclePressedThisFrame") != null, "PlayerInputFacade.LootModeCyclePressedThisFrame이 없다.");

        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGameplayInputValidator] FAIL\n- " + string.Join("\n- ", errors.ToArray()));
        return "[OverburstGameplayInputValidator] PASS\n- Gameplay 18 action 계약, Player/UI 보존, Facade 이름 조회 확인";
    }

    private static void RequireMap(List<string> errors, InputActionMap map, string name)
    {
        Require(errors, map != null, "Action Map이 없다: " + name);
    }

    private static void RequireAction(List<string> errors, InputActionMap map, string name)
    {
        Require(errors, map.FindAction(name) != null, "Action이 없다: " + map.name + "/" + name);
    }

    private static void RequireTypedAction(List<string> errors, InputActionMap map, string name, InputActionType type, string expectedControlType)
    {
        InputAction action = map.FindAction(name);
        if (action == null)
        {
            errors.Add("Action이 없다: " + map.name + "/" + name);
            return;
        }
        Require(errors, action.type == type, map.name + "/" + name + " type 불일치: " + action.type + " (기대 " + type + ")");
        Require(errors, string.Equals(action.expectedControlType, expectedControlType, StringComparison.OrdinalIgnoreCase),
            map.name + "/" + name + " controlType 불일치: " + action.expectedControlType + " (기대 " + expectedControlType + ")");
    }

    private static void RequireBinding(List<string> errors, InputActionMap map, string actionName, string path)
    {
        InputAction action = map.FindAction(actionName);
        if (action == null)
        {
            errors.Add("Action이 없다: " + map.name + "/" + actionName);
            return;
        }
        Require(errors, HasBinding(action, path), "바인딩이 없다: " + map.name + "/" + actionName + " " + path);
    }

    private static void RequirePressBinding(List<string> errors, InputActionMap map, string actionName, string path)
    {
        InputAction action = map.FindAction(actionName);
        if (action == null)
        {
            errors.Add("Action이 없다: " + map.name + "/" + actionName);
            return;
        }
        Require(errors, BindingHasInteraction(action, path, "Press"), "Press 바인딩이 없다: " + map.name + "/" + actionName + " " + path);
    }

    private static bool HasBinding(InputAction action, string path)
    {
        foreach (InputBinding binding in action.bindings)
        {
            if (string.Equals(binding.path, path, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void RequireCompositeGroups(List<string> errors, InputActionMap map, string actionName, string compositeType, string groups)
    {
        InputAction action = map.FindAction(actionName);
        if (action == null)
        {
            errors.Add("Action이 없다: " + map.name + "/" + actionName);
            return;
        }
        bool found = false;
        foreach (InputBinding binding in action.bindings)
        {
            if (!binding.isComposite)
                continue;
            if (!string.Equals(binding.path, compositeType, StringComparison.OrdinalIgnoreCase))
                continue;
            found = true;
            Require(errors, ContainsText(binding.groups, groups),
                "합성 root 그룹이 없다: " + map.name + "/" + actionName + " " + compositeType + " (기대 " + groups + ")");
        }
        Require(errors, found, "합성 바인딩이 없다: " + map.name + "/" + actionName + " " + compositeType);
    }

    private static bool BindingHasInteraction(InputAction action, string path, string interaction)
    {
        foreach (InputBinding binding in action.bindings)
        {
            if (!string.Equals(binding.path, path, StringComparison.OrdinalIgnoreCase))
                continue;
            if (ContainsText(binding.interactions, interaction))
                return true;
        }
        return false;
    }

    private static bool AnyInteraction(InputAction action, string interaction)
    {
        if (ContainsText(action.interactions, interaction))
            return true;
        foreach (InputBinding binding in action.bindings)
        {
            if (ContainsText(binding.interactions, interaction))
                return true;
        }
        return false;
    }

    private static bool ContainsText(string text, string value)
    {
        return !string.IsNullOrEmpty(text)
            && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void Require(List<string> errors, bool condition, string message)
    {
        if (!condition)
            errors.Add(message);
    }
}
