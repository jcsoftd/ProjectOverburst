using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MeleeTooltipFoundationValidationUtility
{
    private const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const string StarSpriteSetResourcePath = "UI/Tooltip/WeaponGradeStarSpriteSet";

    [MenuItem("OVERBURST/Codex/Validate/UI/Validate Authored Melee Tooltip")]
    public static void ValidateFromMenu()
    {
        ValidateOrThrow();
        Debug.Log("[MeleeTooltipValidation] Authored melee tooltip validation passed.");
    }

    public static void RunFromCommandLine()
    {
        try
        {
            ValidateOrThrow();
            Debug.Log("[MeleeTooltipValidation] PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateOrThrow()
    {
        Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
        TooltipManager manager = FindSceneTooltipManager(scene);
        if (manager == null)
            throw new InvalidOperationException("PersistentScene TooltipManager를 찾을 수 없습니다.");

        SerializedObject serialized = new SerializedObject(manager);
        ValidateGallery(manager, serialized);
        GameObject panel = RequireObject<GameObject>(serialized, "tooltipPanel");
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        if (panelRect == null || !panel.activeSelf)
            throw new InvalidOperationException("정식 TooltipPanel이 활성 상태로 저장되지 않았습니다.");
        if (panelRect.anchorMin.x < 0.999f || panelRect.anchorMax.x < 0.999f || panelRect.anchoredPosition.x <= 0f)
            throw new InvalidOperationException("TooltipPanel이 캔버스 오른쪽 바깥에 배치되지 않았습니다.");

        RequireObject<TextMeshProUGUI>(serialized, "nameText");
        RectTransform header = RequireObject<RectTransform>(serialized, "weaponHeaderRoot");
        TextMeshProUGUI weaponName = RequireObject<TextMeshProUGUI>(serialized, "weaponNameText");
        RequireObject<TextMeshProUGUI>(serialized, "weaponSubtitleText");
        RequireObject<Image>(serialized, "weaponGradeTagImage");
        RequireObject<TextMeshProUGUI>(serialized, "weaponGradeTagText");
        RequireObject<TextMeshProUGUI>(serialized, "basicStatsText");
        WeaponMeleeStatListView meleeView = RequireObject<WeaponMeleeStatListView>(serialized, "meleeStatListView");
        RequireObject<TextMeshProUGUI>(serialized, "weaponStatsText");
        RequireObject<TextMeshProUGUI>(serialized, "gemStatsText");
        RequireObject<TextMeshProUGUI>(serialized, "priceText");
        RequireObject<GameObject>(serialized, "dividerBasic");
        RequireObject<GameObject>(serialized, "dividerWeapon");
        RequireObject<GameObject>(serialized, "dividerGem");
        RequireObject<GameObject>(serialized, "dividerPrice");
        RequireObject<RectTransform>(serialized, "gemSlotRoot");
        ValidateObjectArray(serialized, "gemSlotImages", 4);
        ValidateObjectArray(serialized, "gemSlotOutlineEffects", 4);

        if (!meleeView.HasAuthoredView)
            throw new InvalidOperationException("WeaponMeleeStatListView 정식 참조가 완성되지 않았습니다.");
        Transform[] rows = meleeView.GetComponentsInChildren<Transform>(true)
            .Where(transform => transform.name.StartsWith("MeleeStatRow_", StringComparison.Ordinal))
            .ToArray();
        if (rows.Length != WeaponMeleeStatListView.AuthoredRowCount)
            throw new InvalidOperationException("정식 밀리 능력치 행은 7개여야 합니다. count=" + rows.Length);

        WeaponGradeStarStrip[] strips = meleeView.GetComponentsInChildren<WeaponGradeStarStrip>(true);
        if (strips.Length != WeaponMeleeStatListView.AuthoredRowCount)
            throw new InvalidOperationException("정식 별 표시 줄은 7개여야 합니다. count=" + strips.Length);
        for (int i = 0; i < strips.Length; i++)
        {
            if (!strips[i].HasAuthoredView)
                throw new InvalidOperationException("별 표시 정식 참조가 누락되었습니다: " + strips[i].name);
            int starRootCount = strips[i].transform.Cast<Transform>()
                .Count(child => child.name.StartsWith("GradeStar_", StringComparison.Ordinal));
            if (starRootCount != WeaponGradeStarStrip.AuthoredStarCapacity)
                throw new InvalidOperationException("별 오브젝트 풀은 줄마다 25개여야 합니다. count=" + starRootCount);
        }

        WeaponGradeStarSpriteSet spriteSet = Resources.Load<WeaponGradeStarSpriteSet>(StarSpriteSetResourcePath);
        if (spriteSet == null)
            throw new InvalidOperationException("WeaponGradeStarSpriteSet을 Resources에서 불러오지 못했습니다.");
        Image previewStar = strips.SelectMany(strip => strip.GetComponentsInChildren<Image>(true))
            .FirstOrDefault(image => image.name == "ImageStar" && image.gameObject.activeSelf && image.sprite != null);
        if (previewStar == null)
            throw new InvalidOperationException("씬 미리보기에서 활성 별 Sprite를 찾지 못했습니다.");

        if (!header.gameObject.activeSelf || string.IsNullOrWhiteSpace(weaponName.text))
            throw new InvalidOperationException("씬 미리보기 무기 헤더 또는 무기 이름이 비어 있습니다.");
        if (weaponName.overflowMode != TextOverflowModes.Overflow)
            throw new InvalidOperationException("무기 이름은 TMP Overflow 모드를 사용해야 합니다.");
        Canvas.ForceUpdateCanvases();
        weaponName.ForceMeshUpdate(true, true);
        if (weaponName.textInfo.characterCount <= 0
            || weaponName.textInfo.meshInfo == null
            || weaponName.textInfo.meshInfo.Length == 0
            || weaponName.textInfo.meshInfo[0].vertexCount <= 0)
        {
            throw new InvalidOperationException("무기 이름 TMP 메시가 생성되지 않았습니다.");
        }

        ValidateSocketPolicy();
    }

    private static void ValidateGallery(TooltipManager manager, SerializedObject serialized)
    {
        TooltipAuthoredView[] views = manager.EditorAuthoredViews;
        int expectedCount = Enum.GetValues(typeof(TooltipAuthoredViewKind)).Length;
        if (views == null || views.Length != expectedCount)
            throw new InvalidOperationException("정식 Tooltip 전시관은 6종이어야 합니다.");

        bool[] found = new bool[expectedCount];
        HashSet<string> galleryPositions = new HashSet<string>();
        for (int i = 0; i < views.Length; i++)
        {
            TooltipAuthoredView view = views[i];
            int kindIndex = view != null ? (int)view.Kind : -1;
            if (kindIndex < 0 || kindIndex >= expectedCount || found[kindIndex])
                throw new InvalidOperationException("Tooltip 전시관 종류가 누락되거나 중복되었습니다. index=" + i);
            if (!view.HasRequiredReferences || view.Panel == null || !view.Panel.activeSelf)
                throw new InvalidOperationException("Tooltip 전시관 정식 참조가 완성되지 않았습니다. kind=" + view.Kind);
            if (!view.WeaponHeaderRoot.gameObject.activeSelf || view.NameText.gameObject.activeSelf)
                throw new InvalidOperationException("Tooltip 전시관은 공통 헤더만 활성 상태여야 합니다. kind=" + view.Kind);
            if (FindFirstActiveChild(view.RectTransform) != view.WeaponHeaderRoot)
                throw new InvalidOperationException("Tooltip 공통 헤더가 첫 번째 활성 자식이 아닙니다. kind=" + view.Kind);
            if (string.IsNullOrWhiteSpace(view.WeaponNameText.text)
                || string.IsNullOrWhiteSpace(view.WeaponGradeTagText.text))
            {
                throw new InvalidOperationException("Tooltip 전시관 이름 또는 등급 태그가 비어 있습니다. kind=" + view.Kind);
            }
            if (!ColorsApproximatelyEqual(view.WeaponNameText.color, view.WeaponGradeTagImage.color))
                throw new InvalidOperationException("Tooltip 이름과 등급 태그 색상이 일치하지 않습니다. kind=" + view.Kind);

            Image panelBackground = view.Panel.GetComponent<Image>();
            if (panelBackground.sprite == null
                || panelBackground.sprite.name != "TooltipPanelRounded"
                || panelBackground.type != Image.Type.Sliced
                || Mathf.Abs(panelBackground.color.a - 0.92f) > 0.001f)
            {
                throw new InvalidOperationException("Tooltip 둥근 반투명 배경 설정이 일치하지 않습니다. kind=" + view.Kind);
            }

            RectTransform rect = view.RectTransform;
            if (rect.anchorMin.x < 0.999f || rect.anchorMax.x < 0.999f || rect.anchoredPosition.x <= 0f)
                throw new InvalidOperationException("Tooltip 전시 뷰가 Canvas 오른쪽 바깥에 없습니다. kind=" + view.Kind);
            if (Mathf.Abs(rect.pivot.x) > 0.001f || Mathf.Abs(rect.pivot.y - 1f) > 0.001f)
                throw new InvalidOperationException("Tooltip 전시 뷰 피벗은 좌측 상단이어야 합니다. kind=" + view.Kind);
            string positionKey = Mathf.RoundToInt(rect.anchoredPosition.x) + ":" + Mathf.RoundToInt(rect.anchoredPosition.y);
            if (!galleryPositions.Add(positionKey))
                throw new InvalidOperationException("Tooltip 전시 뷰 위치가 겹칩니다. kind=" + view.Kind);
            found[kindIndex] = true;
        }

        SerializedProperty pointerOffset = serialized.FindProperty("tooltipPointerOffset");
        if (pointerOffset == null
            || Mathf.Abs(pointerOffset.vector2Value.x - 20f) > 0.01f
            || Mathf.Abs(pointerOffset.vector2Value.y + 16f) > 0.01f)
        {
            throw new InvalidOperationException("Tooltip 마우스 오프셋은 (20, -16)이어야 합니다.");
        }
    }

    private static bool ColorsApproximatelyEqual(Color left, Color right)
    {
        return Mathf.Abs(left.r - right.r) <= 0.001f
            && Mathf.Abs(left.g - right.g) <= 0.001f
            && Mathf.Abs(left.b - right.b) <= 0.001f
            && Mathf.Abs(left.a - right.a) <= 0.001f;
    }

    private static RectTransform FindFirstActiveChild(RectTransform parent)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            RectTransform child = parent.GetChild(i) as RectTransform;
            if (child != null && child.gameObject.activeSelf)
                return child;
        }

        return null;
    }

    private static void ValidateSocketPolicy()
    {
        WeaponItemData weaponData = LoadMeleeWeapon();
        if (weaponData == null)
            throw new InvalidOperationException("검증용 활성 밀리 무기를 찾을 수 없습니다.");

        ItemData item = new ItemData(weaponData, 1, ItemGrade.Legendary);
        if (!item.TryEnsureWeaponComboGemLoadouts(out string error))
            throw new InvalidOperationException("콤보 보석 슬롯 생성 실패: " + error);

        int comboCount = item.GetWeaponComboGemLoadoutCount();
        for (int comboIndex = 0; comboIndex < comboCount; comboIndex++)
        {
            if (!item.TryGetWeaponComboAttackId(comboIndex, out string attackId))
                throw new InvalidOperationException("콤보 attackId 조회 실패: index=" + comboIndex);

            WeaponComboGemLoadout loadout = item.GetWeaponComboGemLoadout(attackId);
            if (loadout == null
                || loadout.SlotCount != WeaponComboGemSlotRules.SlotCapacity
                || loadout.UnlockedSlotCount != WeaponComboGemSlotRules.InitialUnlockedSlotCount)
            {
                throw new InvalidOperationException("콤보 보석 슬롯 계약 불일치: attackId=" + attackId);
            }
        }
    }

    private static T RequireObject<T>(SerializedObject serialized, string propertyName) where T : UnityEngine.Object
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        T value = property != null ? property.objectReferenceValue as T : null;
        if (value == null)
            throw new InvalidOperationException("TooltipManager 정식 참조 누락: " + propertyName);
        return value;
    }

    private static void ValidateObjectArray(SerializedObject serialized, string propertyName, int expectedCount)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null || !property.isArray || property.arraySize != expectedCount)
            throw new InvalidOperationException(propertyName + " 배열 크기가 올바르지 않습니다.");
        for (int i = 0; i < property.arraySize; i++)
        {
            if (property.GetArrayElementAtIndex(i).objectReferenceValue == null)
                throw new InvalidOperationException(propertyName + " 참조 누락 index=" + i);
        }
    }

    private static TooltipManager FindSceneTooltipManager(Scene scene)
    {
        TooltipManager[] managers = UnityEngine.Object.FindObjectsByType<TooltipManager>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < managers.Length; i++)
        {
            if (managers[i] != null && managers[i].gameObject.scene == scene)
                return managers[i];
        }
        return null;
    }

    private static WeaponItemData LoadMeleeWeapon()
    {
        string[] guids = AssetDatabase.FindAssets("t:WeaponItemData", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            WeaponItemData weapon = AssetDatabase.LoadAssetAtPath<WeaponItemData>(path);
            if (weapon != null
                && weapon.CombatFamily == WeaponCombatFamily.Melee
                && WeaponContentPolicy.IsActiveWeapon(weapon))
            {
                return weapon;
            }
        }
        return null;
    }
}
