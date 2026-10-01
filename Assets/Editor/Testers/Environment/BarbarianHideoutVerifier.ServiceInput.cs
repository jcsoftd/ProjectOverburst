using System;
using System.Collections;
using System.IO;
using System.Linq;
using HighlightPlus;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static partial class BarbarianHideoutVerifier
{
    static IEnumerator serviceInput;
    static int serviceFrame = -1;
    static bool ownsServiceInput;
    static MouseState serviceMouse;
    static KeyboardState serviceKeyboard;
    static Vector2 previousServicePointer;
    static bool capturePending;
    static Exception captureError;

    static void BeginServiceInput()
    {
        Check(Mouse.current != null && Keyboard.current != null, "Actual mouse and keyboard devices available");
        previousServicePointer = Mouse.current.position.ReadValue();
        serviceMouse = new MouseState { position = previousServicePointer };
        serviceKeyboard = new KeyboardState();
        ownsServiceInput = true;
        InputSystem.onBeforeUpdate -= QueueServiceInput;
        InputSystem.onBeforeUpdate += QueueServiceInput;
        serviceFrame = -1; serviceInput = VerifyServiceInput(); Phase = 20; Deadline();
    }
    static void QueueServiceInput()
    {
        if (!ownsServiceInput) return;
        if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, serviceMouse);
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, serviceKeyboard);
    }
    static void ReleaseServiceInput()
    {
        if (!ownsServiceInput) return;
        ownsServiceInput = false; InputSystem.onBeforeUpdate -= QueueServiceInput;
        if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = previousServicePointer });
        serviceInput = null;
    }
    static bool TickServiceInput()
    {
        if (Phase != 20) return false;
        if (serviceFrame == Time.frameCount) return true;
        serviceFrame = Time.frameCount;
        if (serviceInput == null) throw new InvalidOperationException("Service input routine was interrupted.");
        if (serviceInput.MoveNext()) return true;
        ReleaseServiceInput();
        SessionState.SetInt(Key + "interaction", SessionState.GetInt(Key + "interaction", 0) + 1);
        Phase = 2; return true;
    }
    static IEnumerator VerifyServiceInput()
    {
        int index = UnityEditor.SessionState.GetInt(Key + "interaction", 0);
        var target = Services()[index];
        for (int i = 0; i < 20; i++) yield return null;
        var component = target.InteractionComponent;
        var prompt = component.GetComponentInChildren<WorldInteractionKeyPrompt>(true);
        Check(prompt != null && prompt.HasUsableView && prompt.KeyLabel == "F", "Authored F keycap connected: " + component.name + " (view=" + (prompt != null && prompt.HasUsableView) + ", label=" + (prompt != null ? prompt.KeyLabel : "missing") + ")");
        var controller = Actor.GetComponent<PlayerInteractionController>();
        Check(controller != null && ReferenceEquals(controller.Current, target) && prompt.IsVisible, "Nearby service shows F keycap: " + component.name);
        var renderers = OverburstWorldHighlight.CollectModelRenderers(component.transform);
        if (component is GeneralGoodsMerchantInteractable)
            Check(prompt.transform.position.y > renderers.Max(r => r.bounds.max.y) + .15f, "NPC F keycap stays above animated model: " + component.name);
        var renderer = renderers.OrderByDescending(r => r.bounds.size.sqrMagnitude).First();
        var screen = Camera.main.WorldToScreenPoint(renderer.bounds.center);
        Check(screen.z > 0 && screen.x > 0 && screen.x < Screen.width && screen.y > 0 && screen.y < Screen.height, "Service model visible in actual game camera: " + component.name);
        serviceMouse = new MouseState { position = screen };
        for (int i = 0; i < 12; i++) yield return null;
        var highlight = Object.FindFirstObjectByType<OverburstWorldHighlightController>();
        Check(highlight != null && highlight.HoveredInteraction == target.InteractionTransform, "Actual pointer hover selects service: " + component.name);
        var effect = Resources.FindObjectsOfTypeAll<HighlightEffect>().Single(e => e.name == "__OverburstHighlight_InteractionHover");
        Check(effect.gameObject.activeInHierarchy && effect.includedObjectsCount > 0 && effect.outline > 0, "Service outline active with model renderers: " + component.name);
        capturePending = true; captureError = null;
        Actor.StartCoroutine(CaptureAtEndOfFrame(prompt, Path.Combine(Output, component.name + "_F_hover_" + Cycle + ".png")));
        while (capturePending) yield return null;
        if (captureError != null) throw captureError;
        serviceMouse = new MouseState { position = Vector2.zero };
        for (int i = 0; i < 8; i++) yield return null;
        Check(highlight.HoveredInteraction == null && !effect.gameObject.activeInHierarchy, "Hover exit clears outline: " + component.name);
        int before = controller.ExecutionCount;
        serviceKeyboard = new KeyboardState(UnityEngine.InputSystem.Key.F);
        for (int i = 0; i < 2; i++) yield return null;
        serviceKeyboard = new KeyboardState();
        for (int i = 0; i < 4; i++) yield return null;
        Check(controller.ExecutionCount == before + 1 && controller.LastResult == InteractionExecutionResult.Succeeded, "Actual F input opens service once: " + component.name);
        Check(!prompt.IsVisible, "Open service hides F keycap: " + component.name);
        if (component is StashInteractable)
        {
            var animator = component.transform.Find("Stash Visual").GetComponentInChildren<Animator>(true);
            Check(animator != null && animator.GetBool("Open"), "Storage UI drives chest Open parameter");
            float until = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < until) yield return null;
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Opened"), "Storage lid reaches and holds opened pose");
            SaveCamera(Camera.main, Path.Combine(Output, "storage_open_" + Cycle + ".png"));
        }
        foreach (var ui in Object.FindObjectsByType<StashUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ui.Close();
        foreach (var ui in Object.FindObjectsByType<ShopUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) ui.Close();
        for (int i = 0; i < 4; i++) yield return null;
        Check(prompt.IsVisible, "Closed service restores nearby F keycap: " + component.name);
        if (component is StashInteractable)
        {
            var animator = component.transform.Find("Stash Visual").GetComponentInChildren<Animator>(true);
            Check(!animator.GetBool("Open"), "Closed storage UI resets chest Open parameter");
            float until = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < until) yield return null;
            Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Closed"), "Storage lid returns to closed pose");
        }
        var spawn = Object.FindObjectsByType<HubReturnPoint>(FindObjectsSortMode.None).Single(p => p.ReturnPointId == "Default");
        ActorTeleportUtility.TeleportSafely(Actor.transform, spawn.transform.position, spawn.transform.rotation);
        for (int i = 0; i < 8; i++) yield return null;
        Check(!prompt.IsVisible, "Leaving service range hides F keycap: " + component.name);
    }

    static IEnumerator CaptureAtEndOfFrame(WorldInteractionKeyPrompt prompt, string path)
    {
        yield return new WaitForEndOfFrame();
        try { SaveServiceFrame(prompt, path); }
        catch (Exception error) { captureError = error; }
        finally { capturePending = false; }
    }

    static void SaveServiceFrame(WorldInteractionKeyPrompt prompt, string path)
    {
        var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            Check(texture != null && texture.width > 0, "Actual Game View frame captured with overlay UI");
            var centre = prompt.ScreenPosition; int white = 0, dark = 0;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            for (int y = (int)centre.y - 12; y <= (int)centre.y + 12; y++)
                for (int x = (int)centre.x - 12; x <= (int)centre.x + 12; x++)
                {
                    if (x < 0 || y < 0 || x >= texture.width || y >= texture.height) continue;
                    var color = texture.GetPixel(x, y);
                    if (color.r > .75f && color.g > .75f && color.b > .75f) white++;
                    if (color.r < .25f && color.g < .25f && color.b < .25f) dark++;
                }
            Check(white >= 12 && dark >= 100, "F glyph and keycap remain readable over characters (" + white + "/" + dark + ")");
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally { if (texture != null) Object.DestroyImmediate(texture); }
    }
}
