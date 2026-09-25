using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Gameplay action(퀵슬롯 1~9·0 포함) + UI Point/Submit/Cancel/Equipment + Current 수명주기를 제공한다.
// 기존 공개 API(맵 enable/disable, TryGet, 값/눌림 API)는 유지한다.
[DisallowMultipleComponent]
public sealed class PlayerInputFacade : MonoBehaviour
{
    public const string AssetPath = "Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";
    public const string GameplayMapName = "Gameplay";
    public const string UiMapName = "UI";
    public const string DebugValidationMapName = "DebugValidation";

    // 한 플레이어 기준 현재 파사드. 씬 전환/도메인 리로드 뒤 stale을 남기지 않는다.
    public static PlayerInputFacade Current { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState() => Current = null;

    public static readonly string[] RequiredGameplayActions =
    {
        "Move",
        "Look",
        "Attack",
        "Interact",
        "Evade",
        "CombatMode",
        "Inventory",
        "Cancel",
        "Jump",
        "WalkToggle",
        "Aim",
        "Zoom",
        "ZoomReset",
        "QuickSlot1",
        "QuickSlot2",
        "QuickSlot3",
        "QuickSlot4",
        "QuickSlot5",
        "QuickSlot6",
        "QuickSlot7",
        "QuickSlot8",
        "QuickSlot9",
        "QuickSlot10",
        "Equipment",
        "LootModeCycle",
    };

    public static readonly string[] MajorUiActions =
    {
        "Navigate",
        "Submit",
        "Cancel",
        "Point",
        "Click",
    };

    [SerializeField] private InputActionAsset sourceAsset;
    [SerializeField] private bool autoEnableGameplay = true;
    [SerializeField] private bool autoEnableUi = true;
    [SerializeField] private bool autoEnableDebugValidation;

    [Header("Combat Input Buffer (unscaled seconds)")]
    [SerializeField, Min(0f)] private float attackBufferDuration = 0.15f;
    [SerializeField, Min(0f)] private float evadeBufferDuration = 0.15f;
    public float AttackBufferDuration => attackBufferDuration;
    public float EvadeBufferDuration => evadeBufferDuration;
    public PlayerCombatInputBuffer CombatInputs { get; private set; }

    private InputActionAsset runtimeAsset;
    private InputActionMap gameplayMap;
    private InputActionMap uiMap;
    private InputActionMap debugValidationMap;
    private readonly Dictionary<string, InputAction> gameplayActions = new Dictionary<string, InputAction>(StringComparer.Ordinal);
    private readonly Dictionary<string, InputAction> uiActions = new Dictionary<string, InputAction>(StringComparer.Ordinal);
    private bool subscribedToActionChange;
    private InputDevice lastDevice;

    public InputActionAsset SourceAsset => sourceAsset;
    public InputActionAsset RuntimeAsset => runtimeAsset;
    public InputActionMap GameplayMap => gameplayMap;
    public InputActionMap UiMap => uiMap;
    public InputActionMap DebugValidationMap => debugValidationMap;
    public bool IsInitialized => runtimeAsset != null && gameplayMap != null;

    public bool IsGameplayEnabled => gameplayMap != null && gameplayMap.enabled;
    public bool IsUiEnabled => uiMap != null && uiMap.enabled;
    public bool IsDebugValidationEnabled => debugValidationMap != null && debugValidationMap.enabled;

    // 마지막 입력 장치. 장치가 없거나 도메인 리로드 뒤에도 null을 반환할 뿐 예외를 내지 않는다.
    public InputDevice LastInputDevice => lastDevice;

    public string LastDeviceName
    {
        get
        {
            try
            {
                return lastDevice != null ? lastDevice.name : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    // 값 입력.
    public Vector2 MoveValue => ReadVector2("Move");
    public Vector2 LookValue => ReadVector2("Look");
    public Vector2 ZoomValue => ReadVector2("Zoom");
    public bool ZoomResetPressedThisFrame => WasPressedThisFrame("ZoomReset");

    // 버튼 의미: Pressed(이번 프레임 눌림), Held(유지), Released(이번 프레임 해제).
    public bool AttackPressedThisFrame => WasPressedThisFrame("Attack");
    public bool AttackHeld => IsPressed("Attack");
    public bool AttackReleasedThisFrame => WasReleasedThisFrame("Attack");

    public bool InteractPressedThisFrame => WasPressedThisFrame("Interact");
    public bool InteractHeld => IsPressed("Interact");
    public bool InteractReleasedThisFrame => WasReleasedThisFrame("Interact");

    public bool EvadePressedThisFrame => WasPressedThisFrame("Evade");
    public bool EvadeHeld => IsPressed("Evade");
    public bool EvadeReleasedThisFrame => WasReleasedThisFrame("Evade");

    public bool CombatModePressedThisFrame => WasPressedThisFrame("CombatMode");
    public bool CombatModeHeld => IsPressed("CombatMode");
    public bool CombatModeReleasedThisFrame => WasReleasedThisFrame("CombatMode");

    public bool InventoryPressedThisFrame => WasPressedThisFrame("Inventory");
    public bool InventoryHeld => IsPressed("Inventory");
    public bool InventoryReleasedThisFrame => WasReleasedThisFrame("Inventory");

    public bool CancelPressedThisFrame => WasPressedThisFrame("Cancel");
    public bool CancelHeld => IsPressed("Cancel");
    public bool CancelReleasedThisFrame => WasReleasedThisFrame("Cancel");

    public bool JumpPressedThisFrame => WasPressedThisFrame("Jump");
    public bool JumpHeld => IsPressed("Jump");
    public bool JumpReleasedThisFrame => WasReleasedThisFrame("Jump");

    public bool WalkTogglePressedThisFrame => WasPressedThisFrame("WalkToggle");
    public bool WalkToggleHeld => IsPressed("WalkToggle");
    public bool WalkToggleReleasedThisFrame => WasReleasedThisFrame("WalkToggle");

    public bool AimPressedThisFrame => WasPressedThisFrame("Aim");
    public bool AimHeld => IsPressed("Aim");
    public bool AimReleasedThisFrame => WasReleasedThisFrame("Aim");

    public bool QuickSlotPressedThisFrame(int key) => key >= 1 && key <= 10 && WasPressedThisFrame("QuickSlot" + key);
    public bool EquipmentPressedThisFrame => WasPressedThisFrame("Equipment") || WasUiPressedThisFrame("Equipment");

    public bool QuickSlot1PressedThisFrame => WasPressedThisFrame("QuickSlot1");
    public bool QuickSlot2PressedThisFrame => WasPressedThisFrame("QuickSlot2");
    public bool QuickSlot3PressedThisFrame => WasPressedThisFrame("QuickSlot3");

    public bool QuickSlot4PressedThisFrame => WasPressedThisFrame("QuickSlot4");
    public bool QuickSlot4Held => IsPressed("QuickSlot4");
    public bool QuickSlot4ReleasedThisFrame => WasReleasedThisFrame("QuickSlot4");

    public bool QuickSlot5PressedThisFrame => WasPressedThisFrame("QuickSlot5");
    public bool QuickSlot5Held => IsPressed("QuickSlot5");
    public bool QuickSlot5ReleasedThisFrame => WasReleasedThisFrame("QuickSlot5");

    public bool QuickSlot6PressedThisFrame => WasPressedThisFrame("QuickSlot6");
    public bool QuickSlot6Held => IsPressed("QuickSlot6");
    public bool QuickSlot6ReleasedThisFrame => WasReleasedThisFrame("QuickSlot6");

    public bool QuickSlot7PressedThisFrame => WasPressedThisFrame("QuickSlot7");
    public bool QuickSlot7Held => IsPressed("QuickSlot7");
    public bool QuickSlot7ReleasedThisFrame => WasReleasedThisFrame("QuickSlot7");

    public bool LootModeCyclePressedThisFrame => WasPressedThisFrame("LootModeCycle");
    public bool LootModeCycleHeld => IsPressed("LootModeCycle");
    public bool LootModeCycleReleasedThisFrame => WasReleasedThisFrame("LootModeCycle");

    // 포인터 위치와 팝업/닫기용 UI 입력. 기존 UI map의 Point/Submit/Cancel을 재사용한다.
    public Vector2 PointerPosition => ReadUiVector2("Point");

    public bool UiSubmitPressedThisFrame => WasUiPressedThisFrame("Submit");
    public bool UiSubmitHeld => IsUiPressed("Submit");
    public bool UiSubmitReleasedThisFrame => WasUiReleasedThisFrame("Submit");

    public bool UiCancelPressedThisFrame => WasUiPressedThisFrame("Cancel");
    public bool UiCancelHeld => IsUiPressed("Cancel");
    public bool UiCancelReleasedThisFrame => WasUiReleasedThisFrame("Cancel");

    public bool UiClickHeld => IsUiPressed("Click");

    private void Awake()
    {
        EnsureInitialized();
        if (Current != null && Current != this)
            Debug.LogWarning("[PlayerInputFacade] Current가 이미 있어 교체한다.", this);
        Current = this;
    }

    private void OnEnable()
    {
        EnsureInitialized();
        if (Current != null && Current != this)
            Debug.LogWarning("[PlayerInputFacade] Current가 이미 있어 교체한다.", this);
        Current = this;
        if (autoEnableGameplay)
            EnableGameplay();
        if (autoEnableUi)
            EnableUi();
        if (autoEnableDebugValidation)
            EnableDebugValidation();
        CombatInputs?.Dispose();
        CombatInputs = new PlayerCombatInputBuffer(this);
    }

    private void OnDisable()
    {
        CombatInputs?.Dispose();
        CombatInputs = null;
        DisableAllMaps();
        UnsubscribeActionChange();
        if (Current == this)
            Current = null;
    }

    private void OnDestroy()
    {
        CombatInputs?.Dispose();
        CombatInputs = null;
        DisableAllMaps();
        UnsubscribeActionChange();
        gameplayActions.Clear();
        uiActions.Clear();
        gameplayMap = null;
        uiMap = null;
        debugValidationMap = null;
        if (runtimeAsset != null)
        {
            Destroy(runtimeAsset);
            runtimeAsset = null;
        }
        lastDevice = null;
        if (Current == this)
            Current = null;
    }

    public void EnsureInitialized()
    {
        if (runtimeAsset != null && gameplayMap != null)
        {
            // OnDisable에서 해제한 장치 추적 구독을 재활성화 뒤에도 복구한다. 중복 구독은 플래그로 방지.
            SubscribeActionChange();
            return;
        }
        if (sourceAsset == null)
        {
            Debug.LogError("[PlayerInputFacade] SourceAsset이 비어 있다. InputSystem_Actions를 주입하라.", this);
            return;
        }

        // Recreate action-map state from the authoring JSON. Instantiate can retain a stale
        // resolved map index after the .inputactions asset is reimported during Editor Play.
        runtimeAsset = InputActionAsset.FromJson(sourceAsset.ToJson());
        runtimeAsset.name = sourceAsset.name + " (Runtime)";
        gameplayMap = runtimeAsset.FindActionMap(GameplayMapName);
        uiMap = runtimeAsset.FindActionMap(UiMapName);
        debugValidationMap = runtimeAsset.FindActionMap(DebugValidationMapName);
        if (gameplayMap == null)
            Debug.LogError("[PlayerInputFacade] Gameplay map을 찾을 수 없다.", this);
        if (uiMap == null)
            Debug.LogError("[PlayerInputFacade] UI map을 찾을 수 없다.", this);
        if (debugValidationMap == null)
            Debug.LogError("[PlayerInputFacade] DebugValidation map을 찾을 수 없다.", this);

        gameplayActions.Clear();
        uiActions.Clear();
        CacheActions(gameplayMap, RequiredGameplayActions, gameplayActions);
        if (uiMap != null)
            CacheActions(uiMap, MajorUiActions, uiActions);
        SubscribeActionChange();
    }

    public void EnableGameplay()
    {
        EnsureInitialized();
        if (gameplayMap != null && !gameplayMap.enabled)
            gameplayMap.Enable();
    }

    public void DisableGameplay()
    {
        CombatInputs?.Invalidate();
        if (gameplayMap != null && gameplayMap.enabled)
            gameplayMap.Disable();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CombatInputs?.Invalidate();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) CombatInputs?.Invalidate();
    }

    public void EnableUi()
    {
        EnsureInitialized();
        if (uiMap != null && !uiMap.enabled)
            uiMap.Enable();
    }

    public void DisableUi()
    {
        if (uiMap != null && uiMap.enabled)
            uiMap.Disable();
    }

    public void EnableDebugValidation()
    {
        EnsureInitialized();
        if (debugValidationMap != null && !debugValidationMap.enabled)
            debugValidationMap.Enable();
    }

    public void DisableDebugValidation()
    {
        if (debugValidationMap != null && debugValidationMap.enabled)
            debugValidationMap.Disable();
    }

    public bool TryGetGameplayAction(string actionName, out InputAction action)
    {
        action = null;
        if (string.IsNullOrEmpty(actionName))
            return false;
        EnsureInitialized();
        if (gameplayActions.TryGetValue(actionName, out action) && action != null)
            return true;
        if (gameplayMap != null)
        {
            action = gameplayMap.FindAction(actionName);
            if (action != null)
            {
                gameplayActions[actionName] = action;
                return true;
            }
        }
        return false;
    }

    public bool TryGetUiAction(string actionName, out InputAction action)
    {
        action = null;
        if (string.IsNullOrEmpty(actionName))
            return false;
        EnsureInitialized();
        if (uiActions.TryGetValue(actionName, out action) && action != null)
            return true;
        if (uiMap != null)
        {
            action = uiMap.FindAction(actionName);
            if (action != null)
            {
                uiActions[actionName] = action;
                return true;
            }
        }
        return false;
    }

    private Vector2 ReadVector2(string actionName)
    {
        try
        {
            if (TryGetGameplayAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.ReadValue<Vector2>();
        }
        catch (Exception)
        {
        }
        return Vector2.zero;
    }

    private bool IsPressed(string actionName)
    {
        try
        {
            if (TryGetGameplayAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.IsPressed();
        }
        catch (Exception)
        {
        }
        return false;
    }

    private bool WasPressedThisFrame(string actionName)
    {
        try
        {
            if (TryGetGameplayAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.WasPressedThisFrame();
        }
        catch (Exception)
        {
        }
        return false;
    }

    private bool WasReleasedThisFrame(string actionName)
    {
        try
        {
            if (TryGetGameplayAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.WasReleasedThisFrame();
        }
        catch (Exception)
        {
        }
        return false;
    }

    private Vector2 ReadUiVector2(string actionName)
    {
        try
        {
            if (TryGetUiAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.ReadValue<Vector2>();
        }
        catch (Exception)
        {
        }
        return Vector2.zero;
    }

    private bool IsUiPressed(string actionName)
    {
        try
        {
            if (TryGetUiAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.IsPressed();
        }
        catch (Exception)
        {
        }
        return false;
    }

    private bool WasUiPressedThisFrame(string actionName)
    {
        try
        {
            if (TryGetUiAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.WasPressedThisFrame();
        }
        catch (Exception)
        {
        }
        return false;
    }

    private bool WasUiReleasedThisFrame(string actionName)
    {
        try
        {
            if (TryGetUiAction(actionName, out InputAction action) && action != null && action.enabled)
                return action.WasReleasedThisFrame();
        }
        catch (Exception)
        {
        }
        return false;
    }

    private static void CacheActions(InputActionMap map, string[] names, Dictionary<string, InputAction> cache)
    {
        if (map == null || names == null || cache == null)
            return;
        foreach (string name in names)
        {
            InputAction action = map.FindAction(name);
            if (action != null)
                cache[name] = action;
            else
                Debug.LogError("[PlayerInputFacade] Action을 찾을 수 없다: " + map.name + "/" + name);
        }
    }

    private void DisableAllMaps()
    {
        try
        {
            if (gameplayMap != null && gameplayMap.enabled)
                gameplayMap.Disable();
            if (uiMap != null && uiMap.enabled)
                uiMap.Disable();
            if (debugValidationMap != null && debugValidationMap.enabled)
                debugValidationMap.Disable();
        }
        catch (Exception)
        {
        }
    }

    private void SubscribeActionChange()
    {
        if (subscribedToActionChange)
            return;
        try
        {
            InputSystem.onActionChange += HandleActionChange;
            subscribedToActionChange = true;
        }
        catch (Exception)
        {
            subscribedToActionChange = false;
        }
    }

    private void UnsubscribeActionChange()
    {
        if (!subscribedToActionChange)
            return;
        try
        {
            InputSystem.onActionChange -= HandleActionChange;
        }
        catch (Exception)
        {
        }
        subscribedToActionChange = false;
    }

    private void HandleActionChange(object actionOrMap, InputActionChange change)
    {
        if (change == InputActionChange.ActionMapDisabled && ReferenceEquals(actionOrMap, gameplayMap))
            CombatInputs?.Invalidate();
        if (change != InputActionChange.ActionPerformed
            && change != InputActionChange.ActionStarted
            && change != InputActionChange.ActionCanceled)
            return;
        if (!(actionOrMap is InputAction action))
            return;
        if (runtimeAsset == null || action.actionMap == null || action.actionMap.asset != runtimeAsset)
            return;
        try
        {
            InputControl activeControl = action.activeControl;
            if (activeControl != null && activeControl.device != null)
                lastDevice = activeControl.device;
        }
        catch (Exception)
        {
        }
    }
}
