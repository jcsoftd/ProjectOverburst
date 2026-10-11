using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class MapOverlayVerifier
{
    const string KeyPrefix = "Overburst.MapOverlayVerifier.";
    static MapOverlayVerifier()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) RestoreInput(); };
        if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += RestoreInput;
    }
    static void RestoreInput()
    {
        int original = SessionState.GetInt(KeyPrefix + "input", 0), temporary = SessionState.GetInt(KeyPrefix + "temporary", 0);
        var before = EditorUtility.InstanceIDToObject(original) as InputSettings;
        var owned = EditorUtility.InstanceIDToObject(temporary) as InputSettings;
        if (owned && InputSystem.settings == owned && before) InputSystem.settings = before;
        if (owned && InputSystem.settings != owned) Object.DestroyImmediate(owned);
        SessionState.EraseInt(KeyPrefix + "input"); SessionState.EraseInt(KeyPrefix + "temporary");
    }
    static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    static IEnumerator Press(Keyboard keyboard, Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return Frames(3);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(5);
    }
    public static string ValidateAssets(string output)
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(MapOverlayBuilder.MinimapPath);
        var controller = root.GetComponent<WorldMinimapController>(); var overlay = controller.Overlay;
        if (!overlay || !overlay.IsReady || overlay.gameObject.activeSelf || overlay.outline.texture
            || overlay.markers.Atlas != controller.View.Markers.Atlas || overlay.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Any(g => g.raycastTarget))
            throw new Exception("Overlay asset contract failed");
        var panel = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath).GetComponentInChildren<OverburstSettingsPanel>(true);
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(PlayerInputFacade.AssetPath);
        if (!panel.mapOverlayOpacity || !panel.mapOverlayScale || panel.mapOverlayOpacity == panel.cameraShake) throw new Exception("Map settings missing");
        foreach (var action in MapOverlayBuilder.Actions)
            if (panel.keyRows.Count(k => k.actionName == action) != 1 || actions.FindAction("Gameplay/" + action) == null) throw new Exception("Input/row missing: " + action);
        if (actions.FindAction("Gameplay/Inventory").bindings[0].path != "<Keyboard>/tab" || actions.FindAction("Gameplay/Zoom").bindings[0].path != "<Mouse>/scroll")
            throw new Exception("Inventory/camera controls changed");
        Directory.CreateDirectory(output);
        string path = Path.Combine(output, "overlay-assets.json");
        File.WriteAllText(path, JsonConvert.SerializeObject(new { status = "PASS", input = MapOverlayBuilder.Actions, sharedAtlas = true,
            nonRaycast = true, settings = new[] { panel.mapOverlayOpacity.name, panel.mapOverlayScale.name },
            missingScripts = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) }, Formatting.Indented));
        return path;
    }
    public static IEnumerator VerifyPlay(RunWorldGate gate, string output, Action<bool, string> check)
    {
        var controller = WorldMinimapController.Instance; var map = controller.Exploration;
        var input = PlayerInputFacade.Current; var menu = OverburstGameMenu.Instance;
        float opacity = OverburstGameSettings.MapOverlayOpacity, scale = OverburstGameSettings.MapOverlayScale;
        var camera = QuarterViewCamera.ActiveInstance; float yaw = camera.CurrentYaw;
        var originalSettings = InputSystem.settings; var settings = Object.Instantiate(originalSettings);
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        SessionState.SetInt(KeyPrefix + "input", originalSettings.GetInstanceID()); SessionState.SetInt(KeyPrefix + "temporary", settings.GetInstanceID());
        InputSystem.settings = settings;
        var keyboard = InputSystem.AddDevice<Keyboard>("MapOverlayVerifierKeyboard");
        string bindings = input.RuntimeAsset.SaveBindingOverridesAsJson();
        try
        {
            int seen = map.RevealedCount, builds = controller.TerrainBuildCount;
            controller.SetOverlay(false); yield return Frames(4);
            yield return Press(keyboard, Key.M);
            check(controller.OverlayVisible && controller.IsVisible && !GameplayInputBlocker.IsGameplayInputBlocked && Time.timeScale > 0,
                "M opens shared map while combat and circular map remain active");
            check(controller.Overlay.outline.texture == map.OutlineTexture && map.RevealedCount == seen && controller.TerrainBuildCount == builds,
                "Opening shares the existing outline without more exploration or terrain generation");
            check(controller.Overlay.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.raycastTarget), "Overlay never captures pointer input");
            check(controller.Overlay.markers.MarkerCount == controller.View.Markers.MarkerCount, "Large map uses the same local enemy candidates");
            float circle = controller.ZoomRadius, startScale = OverburstGameSettings.MapOverlayScale;
            yield return Press(keyboard, Key.Equals);
            check(OverburstGameSettings.MapOverlayScale > startScale && controller.ZoomRadius == circle && map.RevealedCount == seen, "Dedicated zoom key changes only overlay scale");
            yield return Press(keyboard, Key.Minus);
            foreach (float angle in new[] { 0f, 45f, 90f })
            {
                camera.SetYaw(angle); yield return Frames(8);
                check(Mathf.Abs(Mathf.DeltaAngle(controller.Overlay.outline.rectTransform.localEulerAngles.z, controller.View.TerrainImage.rectTransform.localEulerAngles.z)) < .01f,
                    "Circle and overlay share camera yaw " + angle);
                float ratio = controller.Overlay.Radius / controller.OverlayRadius * controller.ZoomRadius / controller.View.Radius;
                for (int i = 0; i < controller.View.Markers.MarkerCount; i++)
                {
                    var a = controller.View.Markers.GetMarker(i); var b = controller.Overlay.markers.GetMarker(i);
                    check(a.Id == b.Id && (a.Position * ratio - b.Position).sqrMagnitude < .2f, "Shared marker projection at yaw " + angle + ": " + a.Id);
                }
            }
            camera.SetYaw(yaw); yield return Frames(8);
            string label = gate.Map.terrain + "-" + gate.Map.areaCount;
            ScreenCapture.CaptureScreenshot(Path.Combine(output, "overlay-" + label + ".png")); yield return Frames(4);
            yield return Press(keyboard, Key.Escape);
            check(!controller.OverlayRequested && !menu.gameObject.GetComponent<OverburstGameMenu>().ModalOpen && !OverburstGameMenu.IsOpen,
                "ESC closes only the open overlay");
            yield return Press(keyboard, Key.M); yield return Press(keyboard, Key.Tab);
            check(GameplayInputBlocker.IsGameplayInputBlocked && controller.OverlayRequested && !controller.OverlayVisible, "Inventory Tab takes priority and hides overlay");
            yield return Press(keyboard, Key.Escape);
            check(!GameplayInputBlocker.IsGameplayInputBlocked && controller.OverlayVisible && !OverburstGameMenu.IsOpen, "Inventory ESC restores overlay without opening pause menu");
            check(map.RevealedCount == seen && controller.TerrainBuildCount == builds, "Menu overlap retains fog and terrain");
            controller.SetOverlay(false);
            if (gate.Map.terrain == Overburst.Persistence.MapTerrainKind.Cave && gate.Map.areaCount == 4)
            {
                yield return Press(keyboard, Key.Escape); yield return Frames(12);
                check(OverburstGameMenu.IsOpen, "ESC opens menu after map is closed");
                menu.OpenSettings(); var panel = menu.settings; panel.tabs[2].isOn = true; yield return Frames(8);
                panel.mapOverlayOpacity.value = .6f; panel.mapOverlayScale.value = 1.5f;
                check(Mathf.Approximately(OverburstGameSettings.MapOverlayOpacity, .6f) && Mathf.Approximately(OverburstGameSettings.MapOverlayScale, 1.5f), "Existing settings sliders drive overlay values");
                var presentation = panel.GetComponent<OverburstSettingsGothicView>();
                presentation.SelectRow(panel.mapOverlayOpacity.GetComponentInParent<OverburstSettingsGothicRow>(), true); yield return Frames(6);
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "overlay-settings.png")); yield return Frames(4);
                panel.tabs[3].isOn = true; yield return Frames(6);
                var row = panel.keyRows.Single(r => r.actionName == "MapToggle");
                row.keyButton.onClick.Invoke(); yield return Frames(4);
                yield return Press(keyboard, Key.N); yield return Frames(12);
                check(row.CurrentPath(input.RuntimeAsset) == "<Keyboard>/n", "Map key rebind follows the existing interactive setting");
                menu.Close(); yield return Frames(10);
                yield return Press(keyboard, Key.M); check(!controller.OverlayRequested, "Old map key is released after rebinding");
                yield return Press(keyboard, Key.N); check(controller.OverlayVisible, "Rebound key opens overlay");
                check(Mathf.Approximately(controller.Overlay.outline.color.a, .6f), "Saved opacity is rendered");
                var saved = File.ReadAllText(OverburstGameSettings.FilePath);
                check(saved.Contains("mapOverlayOpacity") && saved.Contains("mapOverlayScale") && saved.Contains("bindingOverrides"), "Overlay values and rebind are saved in isolated settings");
            }
        }
        finally
        {
            try
            {
                if (menu && OverburstGameMenu.IsOpen) menu.Close();
                if (input && input.RuntimeAsset)
                {
                    input.RuntimeAsset.RemoveAllBindingOverrides(); input.RuntimeAsset.LoadBindingOverridesFromJson(bindings);
                    OverburstGameSettings.StoreBindingOverrides(input.RuntimeAsset);
                    OverburstGameSettings.MapOverlayOpacity = opacity; OverburstGameSettings.MapOverlayScale = scale; OverburstGameSettings.SaveIfDirty();
                }
                if (controller) controller.SetOverlay(false);
                if (camera) camera.SetYaw(yaw);
            }
            finally { if (keyboard.added) InputSystem.RemoveDevice(keyboard); RestoreInput(); }
        }
        yield return Frames(8);
    }
}
