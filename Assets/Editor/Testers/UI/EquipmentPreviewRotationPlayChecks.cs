using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Uses the existing isolated Play owner and synthetic mouse; never writes the real account.
public static class EquipmentPreviewRotationPlayChecks
{
    static T Field<T>(string name) => (T)typeof(OverburstUICharacterPreview)
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    static void Check(bool value, string name) => AppearanceCustomizationPlayVerifier.Check(value, name);
    static IEnumerator Frames(int count) => AppearanceCustomizationPlayVerifier.Frames(count);

    public static IEnumerator Verify(string output)
    {
        var game = Object.FindFirstObjectByType<OverburstGameUI>();
        Check(game && !game.equipmentWindow.gameObject.activeSelf, "equipment starts closed");
        game.ToggleEquipment();
        try
        {
            yield return Frames(12);
            var preview = game.equipmentWindow.GetComponentInChildren<OverburstUICharacterPreview>();
            var target = preview.GetComponent<RawImage>();
            Check(preview && target && preview.Texture && OverburstUICharacterPreview.ActiveUsers == 1,
                "product equipment uses one isolated live portrait");
            var camera = Field<Camera>("previewCamera");
            var model = Field<GameObject>("model");
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            var size = camera.orthographicSize;
            var scale = model.transform.localScale;
            var modelRotation = model.transform.localRotation;
            var rect = target.rectTransform;
            var point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
            Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<OverburstUICharacterPreview>() == preview,
                "real equipment character region receives mouse drag");
            var rows = new List<object>();
            void Stable(string phase)
            {
                Check(Mathf.Abs(camera.orthographicSize - size) < .00001f, "equipment camera size stays fixed " + phase + " expected=" + size + " actual=" + camera.orthographicSize);
                Check(Vector3.Distance(camera.transform.position, position) < .0001f &&
                    Quaternion.Angle(camera.transform.rotation, rotation) < .001f, "equipment camera pose stays fixed " + phase + " distance=" + Vector3.Distance(camera.transform.position, position) + " angle=" + Quaternion.Angle(camera.transform.rotation, rotation));
                Check(Vector3.Distance(model.transform.localScale, scale) < .00001f, "equipment model scale stays fixed " + phase);
                rows.Add(new { phase, size = camera.orthographicSize, yaw = model.transform.localEulerAngles.y,
                    position = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z } });
            }
            yield return AppearanceCustomizationPlayVerifier.Capture("equipment-default");
            for (int direction = 1; direction >= -1; direction -= 2)
            {
                for (int i = 0; i < 24; i++)
                {
                    yield return AppearanceCustomizationPlayVerifier.Drag(rect, new Vector2(direction * 20, 0));
                    Stable("drag " + direction + "/" + i);
                    if (i == 0) Check(Quaternion.Angle(modelRotation, model.transform.localRotation) > 5f,
                        "actual equipment mouse rotates the model " + direction);
                    if (i % 6 == 5)
                    {
                        var yaw = model.transform.localRotation;
                        preview.RefreshFromCurrentActor();
                        Stable("appearance refresh " + direction + "/" + i);
                        Check(Quaternion.Angle(model.transform.localRotation, yaw) < .01f, "appearance refresh preserves manual yaw");
                        yield return AppearanceCustomizationPlayVerifier.Capture("equipment-yaw-" + direction + "-" + (i + 1));
                    }
                }
            }
            Check(Quaternion.Angle(model.transform.localRotation, modelRotation) < .1f, "both full rotations return to original yaw");
            for (int i = 0; i < 90; i++) { yield return null; Stable("idle " + i); }
            game.CloseEquipment();yield return Frames(4);
            Check(OverburstUICharacterPreview.ActiveUsers == 0 && !Field<GameObject>("rig") && !preview.Texture,
                "equipment close releases portrait rig and render texture");
            game.ToggleEquipment();yield return Frames(12);
            camera = Field<Camera>("previewCamera");model = Field<GameObject>("model");
            float reopenCameraOffset = Vector3.Distance(camera.transform.position, position);
            File.WriteAllText(Path.Combine(output, "equipment-reopen.json"), JsonConvert.SerializeObject(new
            {
                distance = reopenCameraOffset, angle = Quaternion.Angle(camera.transform.rotation, rotation),
                before = new[] { position.x, position.y, position.z },
                after = new[] { camera.transform.position.x, camera.transform.position.y, camera.transform.position.z },
                sizeBefore = size, sizeAfter = camera.orthographicSize
            }, Formatting.Indented));
            Stable("reopen");
            Check(model.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "reopened portrait retains equipped visible meshes");
            File.WriteAllText(Path.Combine(output, "equipment-rotation.json"), JsonConvert.SerializeObject(new
            {
                success = true, directions = 2, dragSteps = 48, idleFrames = 90, reopenCameraOffset, samples = rows,
                pointerPath = "InputSystem synthetic mouse through production EventSystem",
                realAccount = "untouched", playerExecutable = "NOT_RUN"
            }, Formatting.Indented));
        }
        finally { if (game) game.CloseEquipment(); }
        yield return Frames(4);
        Check(!GameplayInputBlocker.IsGameplayInputBlocked && OverburstUICharacterPreview.ActiveUsers == 0,
            "equipment verification returns gameplay input and preview ownership");
    }
}
