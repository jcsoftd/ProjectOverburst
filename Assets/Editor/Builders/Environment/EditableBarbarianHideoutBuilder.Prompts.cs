using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static partial class EditableBarbarianHideoutBuilder
{
    public const string PromptPath = "Assets/ProjectOverburst/02_Shared/Interaction/Prefabs/PF_WorldInteractionKeyPrompt.prefab";

    static GameObject BuildServicePrompt(bool replace = false)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PromptPath);
        if (existing != null && !replace) return existing;
        EnsureFolder(Path.GetDirectoryName(PromptPath).Replace('\\', '/'));
        var fontPath = AssetDatabase.FindAssets("LiberationSans t:TMP_FontAsset")
            .Select(AssetDatabase.GUIDToAssetPath).Single(p => p.EndsWith("/LiberationSans SDF.asset", StringComparison.Ordinal));
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject("PF_WorldInteractionKeyPrompt");
            SceneManager.MoveGameObjectToScene(root, scene);
            var canvasRoot = new GameObject("Prompt Canvas", typeof(RectTransform), typeof(Canvas));
            canvasRoot.transform.SetParent(root.transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 40;
            var frame = new GameObject("Key Root", typeof(RectTransform)); frame.transform.SetParent(canvasRoot.transform, false);
            var rect = frame.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(40, 40);
            var border = new GameObject("Keycap Border", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            border.transform.SetParent(frame.transform, false);
            border.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
            var borderImage = border.GetComponent<Image>(); borderImage.color = new Color(.96f, .94f, .87f); borderImage.raycastTarget = false;
            var inner = new GameObject("Keycap", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            inner.transform.SetParent(frame.transform, false);
            inner.GetComponent<RectTransform>().sizeDelta = new Vector2(36, 36);
            var keycap = inner.GetComponent<Image>(); keycap.color = new Color(.08f, .09f, .10f, .96f); keycap.raycastTarget = false;
            var text = new GameObject("F", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            text.transform.SetParent(frame.transform, false);
            text.GetComponent<RectTransform>().sizeDelta = new Vector2(36, 36);
            var label = text.GetComponent<TextMeshProUGUI>(); label.font = font; label.text = "F";
            label.fontSize = 25; label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white; label.raycastTarget = false; label.enableAutoSizing = false;
            var view = root.AddComponent<WorldInteractionKeyPrompt>();
            var fields = new SerializedObject(view);
            fields.FindProperty("canvas").objectReferenceValue = canvas;
            fields.FindProperty("keycap").objectReferenceValue = keycap;
            fields.FindProperty("keyLabel").objectReferenceValue = label;
            fields.FindProperty("keyRoot").objectReferenceValue = rect;
            fields.ApplyModifiedPropertiesWithoutUndo();
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PromptPath);
            if (prefab == null) throw new IOException("World F keycap prefab save failed.");
            return prefab;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void ConfigureServicePrompts(Scene scene, bool recreate = false)
    {
        var prefab = BuildServicePrompt();
        var services = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
            .Where(c => c is StashInteractable || c is GeneralGoodsMerchantInteractable).ToArray();
        if (services.Length != 3) throw new InvalidOperationException("Both merchants and stash required.");
        foreach (var service in services)
        {
            var fields = new SerializedObject(service);
            var old = fields.FindProperty("promptRoot").objectReferenceValue as GameObject;
            if (recreate)
            {
                foreach (var child in service.transform.Cast<Transform>().Where(t => t.name == "PromptRoot").ToArray())
                    Object.DestroyImmediate(child.gameObject);
                old = null;
            }
            var modelTop = OverburstWorldHighlight.CollectModelRenderers(service.transform).Max(r => r.bounds.max.y);
            var height = service is StashInteractable ? modelTop + .55f : Mathf.Max(service.transform.position.y + 2.35f, modelTop + .35f);
            var position = service.transform.InverseTransformPoint(new Vector3(service.transform.position.x, height, service.transform.position.z));
            var prompt = old != null && old.GetComponent<WorldInteractionKeyPrompt>() != null ? old : null;
            if (prompt == null)
            {
                if (old != null) Object.DestroyImmediate(old);
                prompt = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            }
            prompt.name = "PromptRoot";
            prompt.transform.SetParent(service.transform, false);
            prompt.transform.localPosition = position;
            prompt.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(prompt.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(prompt);
            fields.FindProperty("promptRoot").objectReferenceValue = prompt;
            fields.FindProperty("promptText").objectReferenceValue = null;
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(service);
            SetKeycapPosition(prompt, position);
        }
    }

    static void SetKeycapPosition(GameObject prompt, Vector3 position)
    {
        // RectTransform persists its XY through anchored position, including nested prefab instances.
        var rect = prompt.GetComponent<RectTransform>();
        if (rect == null)
        {
            prompt.transform.localPosition = position;
            prompt.transform.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(prompt.transform);
            return;
        }
        var fields = new SerializedObject(rect);
        fields.FindProperty("m_AnchoredPosition").vector2Value = new Vector2(position.x, position.y);
        fields.ApplyModifiedPropertiesWithoutUndo();
        var local = rect.localPosition; local.z = position.z; rect.localPosition = local;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
    }

    public static string UseReadableOverlayKeycaps(string output)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output); RequireIdle();
        string backup = Path.Combine(output, "BeforeOverlay", PromptPath);
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(PromptPath)))
            throw new IOException("Exact keycap prefab backup required.");
        BuildServicePrompt(true);
        return RepairServicePromptPlacement(output, "BeforeOverlay");
    }

    public static string RepairServicePromptPlacement(string output, string backupFolder = "BeforePromptRefine", bool recreate = false)
    {
        output = IsolatedSavePlayGuard.ValidateDirectory(output); RequireIdle();
        if (SceneManager.GetSceneByPath(ScenePath).IsValid()) throw new InvalidOperationException("Preserve an already open Hideout.");
        string backup = Path.Combine(output, backupFolder, ScenePath);
        if (!File.Exists(backup) || !File.ReadAllBytes(backup).SequenceEqual(File.ReadAllBytes(ScenePath)))
            throw new IOException("Exact prompt refinement backup required.");
        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            if (recreate) ConfigureServicePrompts(scene, true);
            var services = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true))
                .Where(c => c is StashInteractable || c is GeneralGoodsMerchantInteractable).ToArray();
            foreach (var service in services)
            {
                var fields = new SerializedObject(service);
                var prompt = fields.FindProperty("promptRoot").objectReferenceValue as GameObject;
                if (prompt == null || prompt.GetComponent<WorldInteractionKeyPrompt>() == null) throw new InvalidOperationException("Authored keycap required.");
                var modelTop = OverburstWorldHighlight.CollectModelRenderers(service.transform).Max(r => r.bounds.max.y);
                var height = service is StashInteractable ? modelTop + .55f : Mathf.Max(service.transform.position.y + 2.35f, modelTop + .35f);
                SetKeycapPosition(prompt, service.transform.InverseTransformPoint(new Vector3(service.transform.position.x, height, service.transform.position.z)));
            }
            ValidateLoaded(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new IOException("Prompt placement save failed.");
            return "All service keycaps placed above model bounds.";
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
