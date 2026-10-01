using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HighlightPlus;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>실제 UI 입력과 획득 코어, URP 렌더 표현을 격리 계정에서 검증한다.</summary>
[InitializeOnLoad]
public static class OverburstHighlightPlusVerifier
{
    const string Key = "Overburst.HighlightPlusVerifier";
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly List<InputDevice> disabledDevices = new List<InputDevice>();
    static IEnumerator work;
    static int frame;
    static double deadline;
    static Mouse mouse;
    static Keyboard keyboard;
    static Vector2 pointerPosition;
    static string Output => SessionState.GetString(Key + ".output", "");
    public static string Status => SessionState.GetString(Key + ".status", "NOT_RUN");
    static OverburstHighlightPlusVerifier() { EditorApplication.playModeStateChanged += StateChanged; }

    public static void Run(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Editor must be idle.");
        if (SceneManager.GetActiveScene().name != "PersistentScene")
            throw new InvalidOperationException("PersistentScene required; the verifier does not reopen or save scenes.");
        Directory.CreateDirectory(output);
        SessionState.SetString(Key + ".output", output);
        SessionState.SetString(Key + ".status", "RUNNING");
        SessionState.SetBool(Key, true);
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "IsolatedAccount"));
    }

    static void StateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Key, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); frame = -1;
            SessionState.SetBool(Key + ".background", Application.runInBackground);
            Application.runInBackground = true;
            deadline = EditorApplication.timeSinceStartup + 150;
            work = Verify();
            Application.logMessageReceived += Log;
            EditorApplication.update += Tick;
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
            (work as IDisposable)?.Dispose(); work = null;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            foreach (InputDevice device in disabledDevices)
                if (device.added) InputSystem.EnableDevice(device);
            disabledDevices.Clear(); mouse = null; keyboard = null;
            Application.runInBackground = SessionState.GetBool(Key + ".background", false);
        }
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            Environment.SetEnvironmentVariable(IsolatedSavePlayGuard.Variable, null);
            SessionState.SetBool(Key, false);
            File.WriteAllText(Path.Combine(Output, "returned.json"), JsonConvert.SerializeObject(new {
                playing = EditorApplication.isPlaying,
                saveEnvironmentEmpty = string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),
                scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(i => new { SceneManager.GetSceneAt(i).path, SceneManager.GetSceneAt(i).isDirty })
            }, Formatting.Indented));
        }
    }

    static void Log(string message, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Play verification deadline");
            if (work.MoveNext()) return;
        }
        catch (Exception error) { errors.Add(error.ToString()); }
        SessionState.SetString(Key + ".status", errors.Count == 0 ? "PASS" : "FAIL");
        File.WriteAllText(Path.Combine(Output, "play-results.json"), JsonConvert.SerializeObject(new { status = Status, checks, errors }, Formatting.Indented));
        EditorApplication.update -= Tick;
        EditorApplication.ExitPlaymode();
    }
    static void Check(bool value, string detail)
    {
        checks.Add((value ? "PASS " : "FAIL ") + detail);
        if (!value) throw new InvalidOperationException(detail);
    }
    static void MouseStateAt(Vector2 position, bool held)
    {
        pointerPosition = position;
        InputSystem.QueueStateEvent(mouse, new MouseState { position = position, buttons = (ushort)(held ? 1 : 0) });
    }
    static void MouseRelease() => MouseStateAt(pointerPosition, false);
    static bool TryModelPoint(WorldItemPickup pickup, out Vector2 point)
    {
        var controller = Object.FindFirstObjectByType<OverburstWorldHighlightController>();
        var presenter = WorldItemNameplatePresenter.Active;
        var camera = Camera.main;
        foreach (Renderer renderer in pickup.GetComponentsInChildren<Renderer>())
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
            {
                Vector3 world = renderer.bounds.center + Vector3.Scale(renderer.bounds.extents, new Vector3(x * .7f, y * .7f, 0));
                Vector3 screen = camera.WorldToScreenPoint(world);
                point = screen;
                if (screen.z > 0 && camera.pixelRect.Contains(point)
                    && !controller.ResolvePointerUi(point, out _)
                    && presenter.ResolveModelPointerTarget(point) == pickup) return true;
            }
        }
        point = default;
        return false;
    }
    static WorldItemPickup Spawn(WeaponItemData data, PlayerActorRuntime actor, Vector3 offset)
    {
        var pickup = WorldItemDropFactory.CreateWorldPickup(new ItemData(data, 1, ItemGrade.Common), actor.transform.position + offset,
            PlayerAccountInventoryService.SharedInventory, actor.transform, null);
        pickup.PlaceAuthored(actor.transform.position + offset, actor.gameObject.scene);
        return pickup;
    }
    static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Output, name + ".png"));

    static IEnumerator Verify()
    {
        while (!AccountBootstrap.Attempted || (!AccountBootstrap.Ready && AccountBootstrap.Error == null)) yield return null;
        Check(AccountBootstrap.Ready && AccountBootstrap.SaveDirectory.StartsWith(Output), "Isolated account boot");
        for (int i = 0; i < 30; i++) yield return null;
        foreach (string name in new[] { "PC", "Mobile" })
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/ProjectOverburst/01_Core/Settings/" + name + "_Renderer.asset");
            Check(renderer != null && renderer.rendererFeatures.Count(x => x is HighlightPlusRenderPassFeature && x.isActive) == 1, name + " active renderer feature");
        }
        foreach (OverburstWorldHighlightStyle style in Enum.GetValues(typeof(OverburstWorldHighlightStyle)))
            Check(Resources.Load<HighlightProfile>(OverburstWorldHighlight.ProfileResource(style)) != null, style + " profile actual load");
        var actor = PlayerContext.Instance.CurrentActor;
        var controller = Object.FindFirstObjectByType<OverburstWorldHighlightController>();
        var presenter = WorldItemNameplatePresenter.Active;
        var core = PlayerPickupInteractor.ActiveCore;
        var state = actor.GetComponent<PlayerStateCoordinator>();
        Check(controller != null && presenter != null && core != null, "Runtime singleton installation");
        Check(controller.PlayerRendererCount > 0 && controller.PlayerRendererCount < 30, "Only active player model renderers included: " + controller.PlayerRendererCount);
        foreach (InputDevice device in InputSystem.devices.ToArray())
            if ((device is Mouse || device is Keyboard) && device.enabled) { InputSystem.DisableDevice(device); disabledDevices.Add(device); }
        mouse = InputSystem.AddDevice<Mouse>("Highlight verifier mouse");
        keyboard = InputSystem.AddDevice<Keyboard>("Highlight verifier keyboard");
        MouseStateAt(new Vector2(Screen.width * .5f, Screen.height * .5f), false);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!GameplayInputBlocker.IsGameplayInputBlocked, "Gameplay input is available");
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(weapon != null && actor.Equipment.EquipWeaponItem(new ItemData(weapon, 1, ItemGrade.Common)), "Usable melee equipped for attack leakage checks");
        PlayerCombatModeController.ExitSharedCombatMode(PlayerCombatModeReason.System);
        for (int i = 0; i < 10; i++) yield return null;
        var near = Spawn(weapon, actor, Camera.main.transform.right * (core.PickupRadius * .65f));
        for (int i = 0; i < 8; i++) yield return null;
        Check(TryModelPoint(near, out Vector2 point), "Near model point lies outside label and UI");
        MouseStateAt(point, false);
        for (int i = 0; i < 10; i++) yield return null;
        Check(presenter.HighlightedPickup == near, "Item model hover selects Highlight Plus target");
        Capture("item-hover");
        for (int i = 0; i < 4; i++) yield return null;
        var itemEffect = Resources.FindObjectsOfTypeAll<HighlightEffect>().Single(x => x.gameObject.scene.IsValid() && x.name == "__OverburstHighlight_ItemHover");
        itemEffect.enabled = false;
        Capture("item-hover-off"); for (int i = 0; i < 4; i++) yield return null;
        itemEffect.enabled = true; for (int i = 0; i < 4; i++) yield return null;
        int clicks = controller.ModelClickCount;
        int inventoryCount = PlayerAccountInventoryService.SharedInventory.Items.Count(x => x != null);
        MouseStateAt(point, true);
        for (int i = 0; i < 10; i++) yield return null;
        Check(near == null && controller.LastModelClickResult == WorldLootPickupRequestResult.Succeeded, "Actual model left click picks up nearby item");
        Check(PlayerAccountInventoryService.SharedInventory.Items.Count(x => x != null) == inventoryCount + 1, "Inventory gains exactly one item");
        Check(controller.ModelClickCount == clicks + 1 && !PlayerCombatModeController.IsSharedCombatModeActive()
            && !actor.PlayerKit.MeleeRuntime.IsAttackInProgress, "Holding pickup click triggers neither duplicate nor weak attack");
        MouseRelease(); for (int i = 0; i < 5; i++) yield return null;
        Check(!core.SuppressPrimaryAttackUntilRelease, "Pickup attack suppression releases");

        var busy = Spawn(weapon, actor, Camera.main.transform.right * (core.PickupRadius * .65f));
        for (int i = 0; i < 6; i++) yield return null;
        Check(TryModelPoint(busy, out point), "Busy fixture model can be selected");
        state.RequestAction(controller, PlayerActionState.Attack);
        MouseStateAt(point, true); for (int i = 0; i <5; i++) yield return null;
        Check(busy != null && busy.CanPickup && controller.LastModelClickResult == WorldLootPickupRequestResult.ActionBusy, "Active attack rejects model pickup");
        Check(core.SuppressPrimaryAttackUntilRelease && !PlayerCombatModeController.IsSharedCombatModeActive(), "Rejected item gesture does not become attack");
        MouseRelease(); state.ReleaseAction(controller); for (int i = 0; i < 6; i++) yield return null;
        GameplayInputBlocker.Block(controller);
        clicks = controller.ModelClickCount;
        MouseStateAt(point, true); for (int i = 0; i < 5; i++) yield return null;
        Check(controller.ModelClickCount == clicks && busy.CanPickup, "Blocked UI prevents world model pickup");
        MouseRelease(); GameplayInputBlocker.Unblock(controller); for (int i = 0; i < 6; i++) yield return null;
        // 같은 입력 프레임의 짧은 누름/해제도 획득 의도로 처리한다.
        clicks = controller.ModelClickCount;
        MouseStateAt(point, true); MouseRelease(); for (int i = 0; i < 6; i++) yield return null;
        Check(busy == null && controller.ModelClickCount == clicks + 1 && !PlayerCombatModeController.IsSharedCombatModeActive(), "Rapid model click picks up without attack");

        // 접근 코어는 기존 직선 이동이다. 캠프 울타리와 구분하여 열린 바닥에서 완료를 검증한다.
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Highlight approach floor fixture";
        floor.layer = LayerMask.NameToLayer("Ground");
        floor.transform.position = new Vector3(80, -.5f, 80);
        floor.transform.localScale = new Vector3(30, 1, 30);
        actor.CharacterController.enabled = false;
        actor.transform.position = new Vector3(80, .15f, 80);
        actor.CharacterController.enabled = true;
        for (int i = 0; i < 45; i++) yield return null;
        Vector3 right = Camera.main.transform.right; right.y = 0; right.Normalize();
        var distant = Spawn(weapon, actor, right * (core.PickupRadius + 3.5f));
        for (int i = 0; i < 8; i++) yield return null;
        Check(TryModelPoint(distant, out point), "Distant model point outside UI");
        MouseStateAt(point, true); for (int i = 0; i < 2; i++) yield return null;
        Check(core.PendingAutoMovePickup == distant, "Distant model click requests existing approach driver");
        MouseRelease();
        float expires = Time.unscaledTime + 8;
        while (distant != null && distant.CanPickup && Time.unscaledTime < expires) yield return null;
        Check(distant == null && !PlayerCombatModeController.IsSharedCombatModeActive(), "Distant approach completes pickup without combat transition; result=" + core.CurrentSnapshot.LastRequestResult);
        for (int i = 0; i < 8; i++) yield return null;
        distant = Spawn(weapon, actor, right * (core.PickupRadius + 3.5f));
        for (int i = 0; i < 8; i++) yield return null;
        Check(TryModelPoint(distant, out point), "Cancellation fixture model point");
        MouseStateAt(point, true); for (int i = 0; i < 2; i++) yield return null;
        MouseRelease(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.W));
        for (int i = 0; i < 5; i++) yield return null;
        Check(core.PendingAutoMovePickup == null && distant.CanPickup, "WASD cancels approach and preserves item");
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        Object.Destroy(distant.gameObject); for (int i = 0; i < 8; i++) yield return null;

        var stash = Object.FindFirstObjectByType<StashInteractable>();
        Check(stash != null, "Stash fixture exists in actual hideout");
        actor.CharacterController.enabled = false;
        actor.transform.position = stash.transform.position + Vector3.forward * 2;
        actor.CharacterController.enabled = true;
        for (int i = 0; i < 45; i++) yield return null;
        Object.Destroy(floor);
        Renderer stashRenderer = stash.GetComponentsInChildren<Renderer>().First(x => x.enabled && x is MeshRenderer);
        point = Camera.main.WorldToScreenPoint(stashRenderer.bounds.center);
        MouseStateAt(point, false); for (int i = 0; i < 10; i++) yield return null;
        Check(controller.HoveredInteraction == stash.transform, "Actual stash model hover");
        Capture("stash-hover"); for (int i = 0; i < 5; i++) yield return null;

        // 실제 플레이어와 주 카메라 사이에 임시 불투명 가림물을 배치한다.
        var playerBounds = new Bounds(actor.transform.position + Vector3.up, Vector3.one);
        foreach (Renderer r in OverburstWorldHighlight.CollectModelRenderers(actor.transform)) playerBounds.Encapsulate(r.bounds);
        var occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
        occluder.name = "Highlight occlusion fixture";
        occluder.layer = LayerMask.NameToLayer("Default");
        occluder.transform.position = Vector3.Lerp(playerBounds.center, Camera.main.transform.position, .23f) - Vector3.up * .25f;
        occluder.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        occluder.transform.localScale = new Vector3(playerBounds.size.x * 2.5f, playerBounds.size.y * .75f, .25f);
        Object.Destroy(occluder.GetComponent<Collider>());
        MouseStateAt(Vector2.zero, false);
        for (int i = 0; i < 20; i++) yield return null;
        var visuals = Resources.FindObjectsOfTypeAll<HighlightEffect>().Where(x => x.gameObject.scene.IsValid()).ToArray();
        var silhouette = visuals.Single(x => x.name == "__OverburstHighlight_PlayerOcclusion");
        Check(silhouette.seeThrough == SeeThroughMode.AlwaysWhenOccluded && silhouette.outline == 0
            && silhouette.includedObjectsCount == controller.PlayerRendererCount, "Player occlusion configuration and active renderers");
        Capture("player-occluded-on"); for (int i = 0; i < 8; i++) yield return null;
        silhouette.enabled = false;
        Capture("player-occluded-off"); for (int i = 0; i < 8; i++) yield return null;
        silhouette.enabled = true; Object.Destroy(occluder);
        for (int i = 0; i < 8; i++) yield return null;
        Capture("player-unoccluded"); for (int i = 0; i < 8; i++) yield return null;

        Vector2 empty = new Vector2(Screen.width * .7f, Screen.height * .6f);
        Check(!controller.ResolvePointerUi(empty, out _) && presenter.ResolveModelPointerTarget(empty) == null, "Empty world point");
        MouseStateAt(empty, true); for (int i = 0; i < 4; i++) yield return null;
        Check(PlayerCombatModeController.IsSharedCombatModeActive(), "Ordinary world click still starts combat");
        MouseRelease(); for (int i = 0; i < 10; i++) yield return null;
        Check(errors.Count == 0, "No runtime errors");
    }
}
