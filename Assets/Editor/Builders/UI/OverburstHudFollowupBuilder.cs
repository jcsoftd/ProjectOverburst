using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Targeted follow-up for the installed HUD. Does not rerun the original installer.</summary>
public static class OverburstHudFollowupBuilder
{
    private const string HudPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstHUD_Rpg11.prefab";
    private const string LoadingBarPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstLoadingBar_Rpg11.prefab";
    private const string VendorRoot = "Assets/ThirdParty/RPG and MMO UI 11/";

    [MenuItem("OVERBURST/UI/Finalize HUD, Objectives And Loading")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Exit Play Mode before editing the HUD.");
        // 2026-10-01 폐기: 현재 HUD(d222412 이후 자원 바·초상화·글꼴 배치)가 원본이다. 이 도구는 옛 바탕판·목표 배경을 다시 넣고
        // 위치·색·예시 문구를 처음 값으로 되돌린다(2026-10-01 04:33 사고). 다시 쓰지 않는다.
        EditorSceneSafety.RefuseRetired("Finalize HUD, Objectives And Loading", "The current HUD layout (after d222412: resource bars, portrait, fonts) is the source of truth. "
            + "This tool re-adds the old slot backplate and objective backdrop and resets positions, colors and sample texts.");
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.name != "PersistentScene" || scene.isDirty)
            throw new InvalidOperationException("Open a clean PersistentScene before applying the HUD follow-up.");

        // 2026-10-01: 이미 적용된 HUD는 그 뒤의 HUD 조정(자원 바 연출·초상화·글꼴·배치)이 원본이다. 다시 적용하면 위치·크기·색·예시 문구를
        // 처음 값으로 되돌리므로 적용 흔적이 모두 있으면 아무것도 쓰지 않는다.
        if (IsAlreadyApplied(scene))
        {
            Debug.Log("[OverburstHudFollowupBuilder] NO_CHANGE: follow-up already applied; HUD prefab layout and scene overrides are kept.");
            return;
        }

        UpdateHudPrefab();
        EnsureLoadingBarPrefab();
        UpdatePersistentScene(scene);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
    }

    private static bool IsAlreadyApplied(Scene scene)
    {
        GameObject hud = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        if (!hud || !hud.transform.Find("Action Bar/Slot Backplate") || !hud.transform.Find("Quest Tracker/Objective Backdrop"))
            return false;
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(LoadingBarPath))
            return false;
        var roots = scene.GetRootGameObjects();
        var game = roots.SelectMany(x => x.GetComponentsInChildren<OverburstGameUI>(true)).FirstOrDefault();
        var loading = roots.SelectMany(x => x.GetComponentsInChildren<LoadingScreenUI>(true)).FirstOrDefault();
        Transform tracker = game && game.hud ? game.hud.Find("Quest Tracker") : null;
        return tracker && tracker.GetComponent<OverburstObjectiveTracker>() && loading
            && loading.transform.Find("PF_OverburstLoadingBar_Rpg11");
    }

    private static void UpdateHudPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            RectTransform action = (RectTransform)root.transform.Find("Action Bar");
            RectTransform unit = (RectTransform)root.transform.Find("Action Bar Unit Frame");
            RectTransform tracker = (RectTransform)root.transform.Find("Quest Tracker");
            if (!action || !unit || !tracker)
                throw new InvalidOperationException("HUD source hierarchy differs from the approved prefab.");

            RectTransform backplate = action.Find("Slot Backplate") as RectTransform;
            if (!backplate)
            {
                var panel = new GameObject("Slot Backplate", typeof(RectTransform), typeof(Image));
                panel.layer = 5;
                panel.transform.SetParent(action, false);
                backplate = (RectTransform)panel.transform;
            }
            backplate.SetAsFirstSibling();
            backplate.anchorMin = backplate.anchorMax = new Vector2(.5f, 0f);
            backplate.pivot = new Vector2(.5f, 0f);
            backplate.anchoredPosition = new Vector2(0f, 20f);
            backplate.sizeDelta = new Vector2(1644f, 140f);
            Image panelImage = backplate.GetComponent<Image>();
            panelImage.sprite = null;
            panelImage.color = new Color(.075f, .064f, .058f, .98f);
            panelImage.raycastTarget = false;

            action.anchoredPosition = new Vector2(0f, 14f);
            unit.anchoredPosition = new Vector2(unit.anchoredPosition.x, 258f * .58f + 14f);
            SetBarText(unit, "Bar (Health)", "1,200 / 1,200 체력", TextAnchor.MiddleRight);
            SetBarText(unit, "Bar (Power)", "원소 에너지 65 / 100", TextAnchor.MiddleLeft);
            tracker.anchoredPosition = new Vector2(-34f, -320f);
            RectTransform backdrop = tracker.Find("Objective Backdrop") as RectTransform;
            if (!backdrop)
            {
                var panel = new GameObject("Objective Backdrop", typeof(RectTransform), typeof(Image));
                panel.layer = 5;
                panel.transform.SetParent(tracker, false);
                backdrop = (RectTransform)panel.transform;
            }
            backdrop.SetAsFirstSibling();
            backdrop.anchorMin = backdrop.anchorMax = backdrop.pivot = new Vector2(.5f, 1f);
            backdrop.anchoredPosition = Vector2.zero;
            backdrop.sizeDelta = new Vector2(460f, 180f);
            Image objectiveImage = backdrop.GetComponent<Image>();
            objectiveImage.sprite = null;
            objectiveImage.color = new Color(.05f, .035f, .03f, .78f);
            objectiveImage.raycastTarget = false;
            Text questTitle = tracker.Find("Header/Text").GetComponent<Text>();
            questTitle.fontSize = 34;
            questTitle.color = new Color(.93f, .90f, .83f, 1f);
            Text questBody = tracker.Find("Body/Quests Group/Quest (1)/Quest Name Text").GetComponent<Text>();
            questBody.fontSize = 30;
            questBody.color = new Color(.84f, .81f, .73f, 1f);
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SetBarText(RectTransform unit, string barName, string sample, TextAnchor alignment)
    {
        Transform group = unit.Find(barName + "/Text Group");
        group.Find("Label Text").GetComponent<Text>().text = "";
        Text value = group.Find("Percentage Text").GetComponent<Text>();
        value.text = sample;
        value.alignment = alignment;
        if (barName == "Bar (Power)")
            value.rectTransform.offsetMin = new Vector2(50f, 0f);
    }

    private static void EnsureLoadingBarPrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(LoadingBarPath))
            return;
        GameObject vendor = AssetDatabase.LoadAssetAtPath<GameObject>(VendorRoot + "Prefabs/Loading Overlay.prefab");
        if (!vendor || !vendor.transform.Find("Loading Bar"))
            throw new InvalidOperationException("Vendor loading bar is missing.");
        GameObject clone = Object.Instantiate(vendor.transform.Find("Loading Bar").gameObject);
        clone.name = "PF_OverburstLoadingBar_Rpg11";
        try
        {
            PrefabUtility.SaveAsPrefabAsset(clone, LoadingBarPath);
        }
        finally
        {
            Object.DestroyImmediate(clone);
        }
    }

    private static void UpdatePersistentScene(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var game = roots.SelectMany(x => x.GetComponentsInChildren<OverburstGameUI>(true)).FirstOrDefault();
        var loading = roots.SelectMany(x => x.GetComponentsInChildren<LoadingScreenUI>(true)).FirstOrDefault();
        if (!game || !loading)
            throw new InvalidOperationException("Installed game UI or loading screen is missing.");

        Transform hud = game.hud;
        RectTransform action = (RectTransform)hud.Find("Action Bar");
        RectTransform unit = (RectTransform)hud.Find("Action Bar Unit Frame");
        action.anchoredPosition = new Vector2(0f, 14f);
        unit.anchoredPosition = new Vector2(unit.anchoredPosition.x, 258f * .58f + 14f);
        SetBarText(unit, "Bar (Health)", "1,200 / 1,200 체력", TextAnchor.MiddleRight);
        SetBarText(unit, "Bar (Power)", "원소 에너지 65 / 100", TextAnchor.MiddleLeft);

        Transform xp = hud.Find("Action Bar/XP Bar");
        xp.gameObject.SetActive(true);
        RectTransform mask = (RectTransform)xp.Find("Fill Rect/Fill Mask");
        mask.sizeDelta = new Vector2(0f, mask.sizeDelta.y);

        Transform tracker = hud.Find("Quest Tracker");
        tracker.gameObject.SetActive(true);
        ((RectTransform)tracker).anchoredPosition = new Vector2(-34f, -320f);
        Text empty = tracker.Find("Body/Quests Group/Quest (1)/Quest Name Text").GetComponent<Text>();
        empty.text = "진행 중인 목표 없음";
        GameObject rows = tracker.Find("Body/Quests Group/Quest (1)/Objectives").gameObject;
        rows.SetActive(false);
        var view = tracker.GetComponent<OverburstObjectiveTracker>() ?? tracker.gameObject.AddComponent<OverburstObjectiveTracker>();
        view.Configure(tracker.Find("Header/Button (Toggle)").GetComponent<Toggle>(),
            tracker.Find("Body").gameObject, empty, rows,
            (RectTransform)tracker.Find("Objective Backdrop"));

        Canvas overlay = loading.GetComponent<Canvas>();
        if (!overlay)
            overlay = loading.gameObject.AddComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 200;
        if (!loading.GetComponent<GraphicRaycaster>())
            loading.gameObject.AddComponent<GraphicRaycaster>();
        loading.transform.SetAsLastSibling();
        Transform oldProgress = loading.transform.Find("ProgressBarRoot");
        if (oldProgress) oldProgress.gameObject.SetActive(false);
        foreach (string oldText in new[] { "TitleText", "StatusText", "HintText" })
        {
            Transform text = loading.transform.Find(oldText);
            if (text) text.gameObject.SetActive(false);
        }
        Transform bar = loading.transform.Find("PF_OverburstLoadingBar_Rpg11");
        if (!bar)
            bar = ((GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(LoadingBarPath), loading.transform)).transform;
        RectTransform barRect = (RectTransform)bar;
        barRect.anchorMin = barRect.anchorMax = new Vector2(.5f, 0f);
        barRect.pivot = new Vector2(.5f, .5f);
        barRect.anchoredPosition = new Vector2(0f, 98f);
        barRect.sizeDelta = new Vector2(3270f, 154f);
        barRect.localScale = Vector3.one * .45f;
        bar.SetAsLastSibling();

        RectTransform fillMask = (RectTransform)bar.Find("Fill Rect/Fill Mask");
        fillMask.sizeDelta = new Vector2(0f, fillMask.sizeDelta.y);
        Text percent = bar.Find("Text Pct").GetComponent<Text>();
        percent.text = "0%";
        SerializedObject screen = new SerializedObject(loading);
        screen.FindProperty("themedProgressMask").objectReferenceValue = fillMask;
        screen.FindProperty("themedPercentageText").objectReferenceValue = percent;
        screen.FindProperty("themedFillWidth").floatValue = 3194f;
        screen.ApplyModifiedPropertiesWithoutUndo();
        Image loadingBackground = loading.transform.Find("Background").GetComponent<Image>();
        Color backgroundColor = loadingBackground.color;
        backgroundColor.a = 1f;
        loadingBackground.color = backgroundColor;
        EditorUtility.SetDirty(loading);
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
