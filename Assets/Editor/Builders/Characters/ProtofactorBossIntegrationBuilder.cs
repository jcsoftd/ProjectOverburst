using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ProtofactorBossIntegrationBuilder
{
    public const string HudPrefabPath =
        "Assets/ProjectOverburst/Resources/UI/HUD/PF_EnemyBossHud.prefab";
    public const string PersistentScenePath =
        "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";

    private const string HudObjectName = "EnemyBossHud";
    private const string LogPath =
        "Logs/ProtofactorBossIntegrationBuilder.log";

    [MenuItem(
        "OVERBURST/Codex/Setup/Characters/Build Protofactor Boss Integration")]
    public static void BuildFromMenu()
    {
        Debug.Log(BuildAndValidate());
    }

    public static void RunOnceFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = BuildAndValidate();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(LogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string BuildAndValidate()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        EnsureFolder("Assets/ProjectOverburst/Resources/UI/HUD");
        BuildHudPrefab();
        DungeonRunSceneAuthoringBuilder.BuildAndValidate();
        ConfigurePersistentScene();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        string validation = ValidateOrThrow();
        return "[ProtofactorBossIntegrationBuilder] PASS\n" + validation;
    }

    public static string ValidateOrThrow()
    {
        GameObject hudPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        Require(hudPrefab != null, "Boss HUD prefab 누락");
        EnemyBossHudView prefabView =
            hudPrefab.GetComponent<EnemyBossHudView>();
        Require(prefabView != null, "Boss HUD view 누락");
        ValidateHudReferences(prefabView, "Boss HUD prefab");

        Scene scene = EditorSceneManager.OpenScene(
            PersistentScenePath,
            OpenSceneMode.Single);
        EnemyBossHudView[] views =
            FindComponentsInScene<EnemyBossHudView>(scene);
        Require(views.Length == 1,
            "PersistentScene Boss HUD 수=" + views.Length);
        ValidateHudReferences(views[0], "PersistentScene Boss HUD");

        string dungeonValidation =
            DungeonRunSceneValidator.ValidateOrThrow();
        return "BossHudPrefab=1\n"
            + "PersistentBossHud=1\n"
            + "BossRewardHook=1\n"
            + "BossRoomClearBridge=1\n"
            + "BossExitLockBridge=1\n"
            + dungeonValidation;
    }

    private static void BuildHudPrefab()
    {
        GameObject root = new(
            "PF_EnemyBossHud",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup),
            typeof(EnemyBossHudView));
        try
        {
            root.layer = LayerMask.NameToLayer("UI");
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 45;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode =
                CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            RectTransform panel = CreateRect(
                root.transform,
                "BossPanel",
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -30f),
                new Vector2(760f, 104f));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = new Color(0.035f, 0.035f, 0.05f, 0.94f);
            panelImage.raycastTarget = false;
            Shadow shadow = panel.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
            shadow.effectDistance = new Vector2(0f, -5f);

            RectTransform accent = CreateStretchRect(
                panel,
                "Accent",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0f, -3f),
                new Vector2(0f, 0f));
            Image accentImage = accent.gameObject.AddComponent<Image>();
            accentImage.color = new Color(0.92f, 0.65f, 0.2f, 1f);
            accentImage.raycastTarget = false;

            TMP_Text bossName = CreateText(
                panel,
                "BossName",
                new Vector2(0f, 1f),
                new Vector2(0.7f, 1f),
                new Vector2(22f, -38f),
                new Vector2(-8f, -8f),
                26f,
                TextAlignmentOptions.BottomLeft,
                FontStyles.Bold,
                new Color(1f, 0.91f, 0.72f, 1f),
                "BOSS");
            TMP_Text phase = CreateText(
                panel,
                "Phase",
                new Vector2(0.7f, 1f),
                new Vector2(1f, 1f),
                new Vector2(8f, -38f),
                new Vector2(-22f, -8f),
                17f,
                TextAlignmentOptions.BottomRight,
                FontStyles.Normal,
                new Color(0.82f, 0.84f, 0.9f, 1f),
                "PHASE 1 / 3");

            phase.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght SDF.asset");
            if (phase.font == null)
                throw new InvalidOperationException("Boss phase Korean font was not found.");

            RectTransform barBackground = CreateStretchRect(
                panel,
                "HealthBar",
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(22f, 17f),
                new Vector2(-22f, 39f));
            Image barBackgroundImage =
                barBackground.gameObject.AddComponent<Image>();
            barBackgroundImage.color =
                new Color(0.12f, 0.12f, 0.15f, 1f);
            barBackgroundImage.raycastTarget = false;

            RectTransform fillRect = CreateStretchRect(
                barBackground,
                "Fill",
                Vector2.zero,
                Vector2.one,
                new Vector2(3f, 3f),
                new Vector2(-3f, -3f));
            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = new Color(0.78f, 0.12f, 0.11f, 1f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = 1f;
            fill.raycastTarget = false;

            TMP_Text health = CreateText(
                barBackground,
                "HealthText",
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 0f),
                new Vector2(-8f, 0f),
                14f,
                TextAlignmentOptions.Center,
                FontStyles.Bold,
                Color.white,
                "100 / 100");

            EnemyBossHudView view =
                root.GetComponent<EnemyBossHudView>();
            view.ConfigureAuthoring(
                canvasGroup,
                bossName,
                phase,
                health,
                fill);

            GameObject saved =
                PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            Require(saved != null, "Boss HUD prefab 저장 실패");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ConfigurePersistentScene()
    {
        Scene scene = EditorSceneManager.OpenScene(
            PersistentScenePath,
            OpenSceneMode.Single);
        GameObject hudPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        Require(hudPrefab != null, "Boss HUD prefab 로드 실패");

        List<GameObject> existing = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name == HudObjectName
                || roots[i].GetComponent<EnemyBossHudView>() != null)
            {
                existing.Add(roots[i]);
            }
        }

        for (int i = 0; i < existing.Count; i++)
            UnityEngine.Object.DestroyImmediate(existing[i]);

        GameObject instance =
            PrefabUtility.InstantiatePrefab(hudPrefab, scene) as GameObject;
        Require(instance != null, "PersistentScene Boss HUD 생성 실패");
        instance.name = HudObjectName;
        EditorSceneManager.MarkSceneDirty(scene);
        Require(
            EditorSceneManager.SaveScene(scene, PersistentScenePath),
            "PersistentScene Boss HUD 저장 실패");
    }

    private static RectTransform CreateRect(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        GameObject child = new(objectName, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        return rect;
    }

    private static RectTransform CreateStretchRect(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        RectTransform rect = CreateRect(
            parent,
            objectName,
            anchorMin,
            anchorMax,
            Vector2.one * 0.5f,
            Vector2.zero,
            Vector2.zero);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        return rect;
    }

    private static TMP_Text CreateText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        float fontSize,
        TextAlignmentOptions alignment,
        FontStyles fontStyle,
        Color color,
        string text)
    {
        RectTransform rect = CreateStretchRect(
            parent,
            objectName,
            anchorMin,
            anchorMax,
            offsetMin,
            offsetMax);
        TextMeshProUGUI label =
            rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        return label;
    }

    private static void ValidateHudReferences(
        EnemyBossHudView view,
        string context)
    {
        SerializedObject serialized = new(view);
        Require(
            serialized.FindProperty("canvasGroup").objectReferenceValue
                != null,
            context + " CanvasGroup 누락");
        Require(
            serialized.FindProperty("bossNameText").objectReferenceValue
                != null,
            context + " BossName 누락");
        Require(
            serialized.FindProperty("phaseText").objectReferenceValue
                != null,
            context + " PhaseText 누락");
        Require(
            serialized.FindProperty("healthText").objectReferenceValue
                != null,
            context + " HealthText 누락");
        Require(
            serialized.FindProperty("healthFill").objectReferenceValue
                != null,
            context + " HealthFill 누락");
    }

    private static T[] FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        List<T> results = new();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            results.AddRange(roots[i].GetComponentsInChildren<T>(true));
        return results.ToArray();
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent =
            Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        Require(!string.IsNullOrWhiteSpace(parent),
            "폴더 부모 경로 누락: " + folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
