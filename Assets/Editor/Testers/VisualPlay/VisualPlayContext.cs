#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Overburst.DebugTools;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

public sealed class VisualPlayUnavailable : Exception
{
    public VisualPlayUnavailable(string reason) : base(reason) { }
}

/// <summary>한 장면의 실제 배우·입력·임시 자원 소유권. UI 입력은 물리 장치에 남긴다.</summary>
public sealed class VisualPlayContext : IDisposable
{
    public readonly VisualPlayEntry Entry;
    readonly VisualPlayState state;
    readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    readonly List<EnemyActor> enemies = new List<EnemyActor>();
    Keyboard keyboard;
    Mouse mouse;
    Gamepad pad;
    InputSettings previousSettings, settings;
    InputActionMap gameplay, ui;
    ReadOnlyArray<InputDevice>? previousGameplay, previousUi;
    readonly Dictionary<InputActionMap, ReadOnlyArray<InputDevice>?> uiDeviceFilters = new Dictionary<InputActionMap, ReadOnlyArray<InputDevice>?>();
    Keyboard physicalKeyboard;
    Mouse physicalMouse;
    Gamepad physicalPad;
    KeyboardState keys;
    GamepadState stick;
    MouseState pointer;
    bool disposed;
    AccountGameplaySession accountOwner;
    static AccountGameplaySession fixtureOwner;
    static AccountSnapshot fixture;
    readonly HashSet<int> previousPickups = new HashSet<int>();
    Vector3 previousPosition;
    Quaternion previousRotation;
    float previousUnmodifiedMaxHp;
    public PlayerActorRuntime Actor { get { var actor = PlayerContext.Instance?.CurrentActor; return actor != null ? actor : null; } }
    public PlayerInventory Inventory => PlayerContext.Instance?.CurrentActorInventory;
    public MeleeRuntime Melee => Actor != null ? Actor.GetComponent<MeleeRuntime>() : null;
    public PlayerEvadeController Evade => Actor != null ? Actor.GetComponent<PlayerEvadeController>() : null;
    public Vector3 Origin { get; private set; }
    public EnemySpawnService SpawnService { get; private set; }
    public VisualPlayContext(VisualPlayEntry entry, VisualPlayState state) { Entry = entry; this.state = state; }
    public void Detail(string text) => state.Detail = text;
    public T Own<T>(T value) where T : UnityEngine.Object { if (value != null) owned.Add(value); return value; }
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public IEnumerator Prepare()
    {
        OverburstGameMenu.Instance?.Close();
        DebugTime.SetSpeed(1f);
        if (!WorldSessionState.IsHideout)
        {
            var driver = PersistentSceneFlow.Instance?.GetComponent<RunLifetimeDriver>();
            Require(driver != null, "하이드아웃으로 돌아갈 런 관리자가 없어요");
            driver.RequestAbandon();
            yield return Until(() => WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching, 45, "하이드아웃 복귀");
        }
        if (EnemyThemeTrialService.InArena) EnemyThemeTrialService.ToggleArena();
        EnemyThemeTrialService.Clear();
        yield return Wait(.35f);
        Require(Actor != null && Inventory != null, "실제 플레이어와 인벤토리가 필요해요");
        accountOwner = AccountGameplaySession.Current;
        ResetAccountFixture();
        previousPosition = Actor.transform.position; previousRotation = Actor.transform.rotation; previousUnmodifiedMaxHp = Actor.Health.UnmodifiedMaxHp;
        foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None)) previousPickups.Add(pickup.GetInstanceID());
        Actor.Health.ResetHealth();
        Actor.GetComponent<PlayerKnockdownController>()?.ResetReaction();
        Melee?.CancelCurrentAttackState(); Evade?.CancelForKnockdown();
        PlayerFlaskController.Current?.ClearEffects(); PlayerFlaskController.Current?.ResetCooldowns();
        CloseWindows();
        DragSlot.ClearDragState(); TooltipManager.Instance?.HideTooltip();
        PlayerCombatModeController.GetOrCreate().ExitCombatMode(PlayerCombatModeReason.System);
        Origin = Actor.transform.position;
        SetupInput();
        yield return Wait(.2f);
    }

    static void ResetAccountFixture()
    {
        var session = AccountGameplaySession.Current;
        Require(session != null && !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory), "시각 장면은 테스트 계정에서만 준비할 수 있어요");
        if (!ReferenceEquals(fixtureOwner, session)) { fixtureOwner = session; fixture = session.Read(); }
        var snapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<AccountSnapshot>(Newtonsoft.Json.JsonConvert.SerializeObject(fixture));
        Require(session.ExecuteState("visual-fixture-" + Guid.NewGuid().ToString("N"), state =>
        {
            foreach (var field in typeof(AccountSnapshot).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
                if (field.Name != "revision" && field.Name != "lastTransactionId") field.SetValue(state, field.GetValue(snapshot));
        }), "테스트 계정의 시작 상태를 준비하지 못했어요");
    }

    void SetupInput()
    {
        var input = PlayerInputFacade.Current;
        Require(input?.GameplayMap != null, "게임 입력이 준비되지 않았어요");
        gameplay = input.GameplayMap; ui = input.UiMap;
        previousGameplay = gameplay.devices; previousUi = ui?.devices;
        InputDevice[] physical = InputSystem.devices.ToArray();
        physicalKeyboard = Keyboard.current; physicalMouse = Mouse.current; physicalPad = Gamepad.current;
        previousSettings = InputSystem.settings;
        settings = UnityEngine.Object.Instantiate(previousSettings);
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = settings;
        keyboard = InputSystem.AddDevice<Keyboard>("VisualPlay Keyboard");
        mouse = InputSystem.AddDevice<Mouse>("VisualPlay Mouse");
        pad = InputSystem.AddDevice<Gamepad>("VisualPlay Gamepad");
        gameplay.devices = new InputDevice[] { keyboard, mouse, pad };
        if (ui != null) ui.devices = physical;
        foreach (var module in UnityEngine.Object.FindObjectsByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            foreach (var map in new[] { module.point?.action?.actionMap, module.leftClick?.action?.actionMap, module.move?.action?.actionMap }.Where(map => map != null && map != ui && map != gameplay).Distinct())
                if (!uiDeviceFilters.ContainsKey(map)) { uiDeviceFilters.Add(map, map.devices); map.devices = physical; }
        InputSystem.onBeforeUpdate += Send;
        InputSystem.onAfterUpdate += RestorePhysicalCurrent;
        Aim(Actor.transform.position + Vector3.forward * 3f);
    }
    void Send()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, keys);
        if (pad != null && pad.added) InputSystem.QueueStateEvent(pad, stick);
        if (mouse != null && mouse.added) InputSystem.QueueStateEvent(mouse, pointer);
    }
    void RestorePhysicalCurrent()
    {
        if (physicalKeyboard != null && physicalKeyboard.added) physicalKeyboard.MakeCurrent();
        if (physicalMouse != null && physicalMouse.added) physicalMouse.MakeCurrent();
        if (physicalPad != null && physicalPad.added) physicalPad.MakeCurrent();
    }
    public void Input(Vector2 move, params Key[] pressed)
    {
        keys = new KeyboardState(pressed); stick = new GamepadState { leftStick = move };
    }
    public void MouseButton(bool light, bool heavy)
    {
        pointer = pointer.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, light).WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Right, heavy);
    }
    public void Aim(Vector3 point)
    {
        if (Camera.main != null) { Vector3 screen = Camera.main.WorldToScreenPoint(point); pointer.position = new Vector2(screen.x, screen.y); }
    }
    public void Scroll(float value) { pointer.scroll = new Vector2(0, value); }
    public void ReleaseInput() { keys = new KeyboardState(); stick = new GamepadState(); MouseButton(false, false); Scroll(0); }
    public IEnumerator Wait(float seconds)
    {
        float remaining = seconds;
        while (remaining > 0f) { remaining -= Mathf.Min(Time.unscaledDeltaTime, .1f); yield return null; }
    }
    public IEnumerator Until(Func<bool> condition, float seconds, string label)
    {
        float remaining = seconds;
        while (!condition())
        {
            remaining -= Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (remaining <= 0) throw new TimeoutException(label + " 시간이 초과됐어요");
            yield return null;
        }
    }
    public IEnumerator Arena()
    {
        if (!EnemyThemeTrialService.InArena)
        {
            var result = EnemyThemeTrialService.ToggleArena(); Require(result.Success, result.Message);
            yield return Wait(.6f);
        }
        Origin = Actor.transform.position;
        Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(Actor.transform, out var spawn), "적 스폰 서비스가 없어요");
        SpawnService = spawn;
        foreach (var entry in EnemyThemeTrialService.Entries)
            Require(spawn.RegisterAdditionalCatalog(entry.Table.Catalog, out string reason), reason);
        Melee?.SetManualInputEnabled(true);
    }
    public EnemyActor Spawn(EnemyDefinition definition, Vector3 offset, bool active = false)
    {
        Require(SpawnService != null && definition != null, "정식 적 정의와 스폰 서비스가 필요해요");
        var point = Origin + offset;
        if (Physics.Raycast(point + Vector3.up * 4f, Vector3.down, out var floor, 9f, LayerMask.GetMask("Default", "Environment", "Ground"))) point = floor.point + Vector3.up * .035f;
        Require(SpawnService.TrySpawn(new EnemySpawnRequest(definition, point,
            Quaternion.LookRotation(-new Vector3(offset.x, 0, offset.z).normalized), Actor.transform), out var enemy), "적을 소환하지 못했어요");
        enemies.Add(enemy); enemy.AI.enabled = active;
        if (!active) enemy.Movement.StopMovement();
        return enemy;
    }
    public EnemyAbilityDefinition SingleWeak(EnemyActor enemy)
    {
        var definition = enemy.Definition;
        var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility)
            .FirstOrDefault(value => value != null && value.IsValid && !value.IsTelegraphedStrongAttack);
        if (ability == null) throw new VisualPlayUnavailable("이 개체는 별도 약공 행동을 제공하지 않아요");
        var set = Own(ScriptableObject.CreateInstance<EnemyAbilitySet>());
        var serialized = new UnityEditor.SerializedObject(set);
        serialized.FindProperty("abilitySetId").stringValue = "VisualPlaySingleWeak";
        serialized.FindProperty("abilities").arraySize = 1;
        serialized.FindProperty("abilities").GetArrayElementAtIndex(0).objectReferenceValue = ability;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        enemy.AbilityController.Configure(set, enemy.RuntimeStats.DamageMultiplier, 1);
        return ability;
    }
    public void SingleAbility(EnemyActor enemy, EnemyAbilityDefinition ability)
    {
        Require(ability != null && ability.IsValid && enemy.Definition.AbilitySet != null
            && Enumerable.Range(0, enemy.Definition.AbilitySet.Count).Select(enemy.Definition.AbilitySet.GetAbility).Contains(ability), "개체의 정식 행동이 아니에요");
        var set = Own(ScriptableObject.CreateInstance<EnemyAbilitySet>());
        var serialized = new UnityEditor.SerializedObject(set);
        serialized.FindProperty("abilitySetId").stringValue = "VisualPlaySingleAbility";
        serialized.FindProperty("abilities").arraySize = 1;
        serialized.FindProperty("abilities").GetArrayElementAtIndex(0).objectReferenceValue = ability;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        enemy.AbilityController.Configure(set, enemy.RuntimeStats.DamageMultiplier, 1);
        if (ability.IsTelegraphedStrongAttack) enemy.AbilityController.BeginStrongOnlyPass();
    }
    public EnemyAbilityDefinition SingleStrong(EnemyActor enemy)
    {
        var definition = enemy.Definition;
        var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).FirstOrDefault(value => value.IsMeleeStrongAttack && value.HitCount == 1);
        if (ability == null) throw new VisualPlayUnavailable("이 개체는 단발 근접 강공을 제공하지 않아요");
        var set = Own(ScriptableObject.CreateInstance<EnemyAbilitySet>());
        var serialized = new UnityEditor.SerializedObject(set);
        serialized.FindProperty("abilitySetId").stringValue = "VisualPlaySingleStrong";
        serialized.FindProperty("abilities").arraySize = 1;
        serialized.FindProperty("abilities").GetArrayElementAtIndex(0).objectReferenceValue = ability;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        enemy.AbilityController.Configure(set, enemy.RuntimeStats.DamageMultiplier, 1);
        enemy.AbilityController.BeginStrongOnlyPass();
        return ability;
    }
    public static EnemyDefinition[] Definitions => EnemyThemeTrialService.Entries.SelectMany(entry => entry.Table.Entries)
        .Select(entry => entry.definition).Where(definition => definition != null).Distinct().OrderBy(definition => definition.EnemyId).ToArray();
    public static BaseItemData[] Items => ItemPickupSpawner.CollectHideoutCatalog().OrderBy(item => UnityEditor.AssetDatabase.GetAssetPath(item)).ToArray();
    public ItemData Make(BaseItemData data, ItemGrade grade = ItemGrade.Common, int count = 1)
    {
        if (data is ElementGemItemData gem) grade = gem.fixedGrade;
        return new ItemData(data, 10, grade, count);
    }
    public void Equip(WeaponItemData definition)
    {
        Require(definition != null, "정식 무기가 없어요");
        var item = Make(definition);
        Require(Inventory.AddItem(item), "테스트 무기를 넣을 공간이 없어요");
        Require(Actor.Equipment.EquipWeaponItem(item), "무기를 장착하지 못했어요");
    }
    public void Gem(WeaponElement element)
    {
        if (Actor.Equipment.EquippedElementGem != null) Require(ElementGemEquipmentService.UnequipToInventory(), "보석 해제 실패");
        if (element == WeaponElement.None) return;
        var definition = Items.OfType<ElementGemItemData>().FirstOrDefault(item => item.element == element && item.fixedGrade == ItemGrade.Legendary);
        Require(definition != null, "정식 원소 보석이 없어요: " + element);
        var gem = Make(definition);
        Require(Inventory.AddItem(gem), "보석을 넣을 공간이 없어요");
        Require(ElementGemEquipmentService.EquipFromInventorySlot(Inventory.FindFirstMatchingItemIndex(gem)), "보석 장착 실패");
    }
    public WorldItemPickup Drop(BaseItemData definition, Vector3 offset, ItemGrade grade = ItemGrade.Common)
    {
        var pickup = WorldItemDropFactory.CreateWorldPickup(Make(definition, grade), Origin + offset, Inventory, Actor.transform);
        Require(pickup != null, "월드 아이템을 만들지 못했어요"); Own(pickup.gameObject); return pickup;
    }
    public void CloseWindows()
    {
        foreach (var inventory in UnityEngine.Object.FindObjectsByType<InventoryUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) inventory.SetVisible(false);
        UnityEngine.Object.FindFirstObjectByType<OverburstGameUI>()?.CloseEquipment();
        OverburstGameMenu.Instance?.Close();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        ReleaseInput(); InputSystem.onBeforeUpdate -= Send; InputSystem.onAfterUpdate -= RestorePhysicalCurrent;
        // Play 종료·재로딩에서 해제된 맵의 devices를 바꾸면 입력 상태가 다시 만들어질 수 있다.
        var input = PlayerInputFacade.Current;
        if (input != null && ReferenceEquals(input.GameplayMap, gameplay))
        {
            gameplay.devices = previousGameplay;
            if (ui != null && ReferenceEquals(input.UiMap, ui)) ui.devices = previousUi;
            foreach (var pair in uiDeviceFilters) if (pair.Key.asset != null) pair.Key.devices = pair.Value;
        }
        RestorePhysicalCurrent();
        if (settings != null && InputSystem.settings == settings) InputSystem.settings = previousSettings;
        foreach (var device in new InputDevice[] { keyboard, mouse, pad }) if (device != null && device.added) InputSystem.RemoveDevice(device);
        if (settings != null) UnityEngine.Object.Destroy(settings);
        bool ownsWorld = ReferenceEquals(accountOwner, AccountGameplaySession.Current) && accountOwner != null;
        if (ownsWorld) { Melee?.CancelCurrentAttackState(); Evade?.CancelForKnockdown(); Actor?.GetComponent<PlayerKnockdownController>()?.ResetReaction(); }
        foreach (var enemy in enemies) if (enemy != null && enemy.IsLeased) SpawnService?.Release(enemy);
        foreach (var value in owned.AsEnumerable().Reverse()) if (value != null) UnityEngine.Object.Destroy(value);
        if (!ownsWorld) return;
        DragSlot.ClearDragState(); TooltipManager.Instance?.HideTooltip();
        CloseWindows();
        foreach (var shop in UnityEngine.Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) shop.Close();
        foreach (var stash in UnityEngine.Object.FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) stash.Close();
        foreach (var portal in UnityEngine.Object.FindObjectsByType<MapDungeonPortal>(FindObjectsSortMode.None)) portal.ClosePanel();
        UnityEngine.Object.FindFirstObjectByType<OverburstRunUi>()?.Close();
        foreach (var pickup in UnityEngine.Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None))
            if (!previousPickups.Contains(pickup.GetInstanceID())) UnityEngine.Object.Destroy(pickup.gameObject);
        if (Actor != null && WorldSessionState.IsHideout && !EnemyThemeTrialService.InArena)
        { ActorTeleportUtility.TeleportSafely(Actor.transform, previousPosition, previousRotation); Actor.Health.SetUnmodifiedMaxHp(previousUnmodifiedMaxHp, true); }
    }
}
#endif
