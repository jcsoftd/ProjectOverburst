using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>Local, unsaved prefab and real item-data check for the approved item tooltip.</summary>
public static class OverburstTooltipHybridValidation
{
    private const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstTooltip_Rpg11.prefab";
    private const string OutputFolder = "개인파일/코덱스산출/UI/20260923_TooltipMaxMarks/Captures";
    private static readonly Regex SpriteMark = new Regex(@"<sprite index=[0-3] tint=0>", RegexOptions.Compiled);

    [MenuItem("OVERBURST/UI/Validate Approved Item Tooltip E+C")]
    public static void Menu() => Debug.Log("[ApprovedTooltip] " + Run());

    public static string Run()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab || !prefab.GetComponent<OverburstUITooltipView>() ||
            !prefab.GetComponent<OverburstTooltipHybridSkin>())
            throw new InvalidOperationException("Approved tooltip prefab or component is missing.");
        if (prefab.transform.Find("Approved Top Glow")?.GetComponent<Image>()?.sprite == null)
            throw new InvalidOperationException("Approved glow sprite is missing.");
        if (prefab.GetComponentsInChildren<Transform>(true).Any(x =>
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(x.gameObject) != 0))
            throw new InvalidOperationException("Tooltip prefab has a missing script.");
        foreach (string frame in new[] { "Approved Outer Frame", "Approved Dark Inset", "Approved Inner Frame", "Approved Badge Fill/Badge Frame" })
            foreach (string edge in new[] { "Top", "Right", "Bottom", "Left" })
                if (!prefab.transform.Find(frame + "/" + edge))
                    throw new InvalidOperationException("Incomplete four-sided frame: " + frame + "/" + edge);

        string[] weaponPaths = AssetDatabase.FindAssets("t:WeaponItemData")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();
        string[] flaskPaths = AssetDatabase.FindAssets("t:FlaskItemData")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();
        if (weaponPaths.Length < 2 || flaskPaths.Length < 12)
            throw new InvalidOperationException("Representative item assets are missing.");
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", OutputFolder));
        Directory.CreateDirectory(output);
        var randomState = Random.state;
        try
        {
            int checkedItems = 0;
            foreach (string path in weaponPaths.Concat(flaskPaths))
            {
                BaseItemData data = AssetDatabase.LoadAssetAtPath<BaseItemData>(path);
                ItemGrade[] grades = data is FlaskItemData
                    ? new[] { ItemGrade.Common, ItemGrade.Uncommon, ItemGrade.Rare, ItemGrade.Epic,
                        ItemGrade.Legendary, ItemGrade.Artifact, ItemGrade.Mythic }
                    : new[] { ItemGrade.Common, ItemGrade.Uncommon, ItemGrade.Rare, ItemGrade.Epic,
                        ItemGrade.Legendary, ItemGrade.Artifact, ItemGrade.Mythic, ItemGrade.Cursed };
                foreach (ItemGrade rarity in grades)
                {
                    Random.InitState(1260 + checkedItems);
                    bool referenceWeapon = path == weaponPaths[0] && rarity == ItemGrade.Rare;
                    ItemData item = referenceWeapon
                        ? new ItemData(data, 18, rarity, 1, WeaponElement.Ice)
                        : new ItemData(data, 18, rarity);
                    string name = Path.GetFileNameWithoutExtension(path) + "-" + rarity;
                    string capture = referenceWeapon ? "weapon-E-C.png"
                        : path.EndsWith("Flask_Life.asset", StringComparison.Ordinal) && rarity == ItemGrade.Epic ? "flask-E-C.png"
                        : null;
                    VerifyAndRender(prefab, item, name, capture == null ? null : Path.Combine(output, capture));
                    checkedItems++;
                }
            }
            WeaponItemData cursedWeapon = weaponPaths.Select(AssetDatabase.LoadAssetAtPath<WeaponItemData>)
                .First(x => x.name == "OHS01_FleurDeLys");
            Random.InitState(510267);
            ItemData crowded = new ItemData(cursedWeapon, 18, ItemGrade.Cursed);
            int crowdedMax = crowded.weaponGradeStats.Max(x => x.TotalStarCount);
            if (crowdedMax < 12)
                throw new InvalidOperationException("Cursed weapon sample no longer exercises 12 marks in one row: " + crowdedMax);
            VerifyAndRender(prefab, crowded, "cursed-weapon-12-marks", Path.Combine(output, "weapon-cursed-12.png"));
            VerifyAndRender(prefab, crowded, "synthetic-17-mark-boundary", Path.Combine(output, "weapon-layout-17-boundary.png"), 17);
            return "PASS " + checkedItems + " real item/grade cases, real cursed 12-mark row and synthetic 17-mark boundary, fixed 19px diamonds, prefab scripts/frame/glow, 4 captures: " + output;
        }
        finally { Random.state = randomState; }
    }

    private static void VerifyAndRender(GameObject prefab, ItemData item, string context, string capturePath,
        int syntheticDamageMarks = 0)
    {
        const int layer = 30;
        int width = capturePath == null ? 1000 : 520;
        int height = capturePath == null ? 1100
            : Path.GetFileName(capturePath).Contains("cursed") ||
              Path.GetFileName(capturePath).Contains("boundary") ? 900
            : Path.GetFileName(capturePath).StartsWith("weapon", StringComparison.Ordinal) ? 720 : 580;
        GameObject cameraObject = new GameObject("Approved tooltip check camera");
        GameObject canvasObject = new GameObject("Approved tooltip check canvas", typeof(RectTransform));
        GameObject instance = null;
        RenderTexture target = null;
        Texture2D pixels = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            cameraObject.layer = canvasObject.layer = layer;
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = height * .5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.067f, .075f, .082f, 1f);
            camera.cullingMask = 1 << layer;
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            instance = Object.Instantiate(prefab, canvasObject.transform, false);
            foreach (Transform child in instance.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            RectTransform panel = instance.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
            panel.anchoredPosition = Vector2.zero;
            OverburstUITooltipView view = instance.GetComponent<OverburstUITooltipView>();
            view.Present(item);
            int originalDamageMarks = 0;
            if (syntheticDamageMarks > 0)
            {
                originalDamageMarks = WeaponGradeStatRoller.GetRoll(item.weaponGradeStats,
                    WeaponGradeStatType.Damage).TotalStarCount;
                string[] lines = SimpleItemTooltipBuilder.Build(item).Replace("\r", "").Split('\n');
                int damageLine = Array.FindIndex(lines, line =>
                    Regex.Replace(line, "<[^>]+>", "").TrimStart().StartsWith("데미지 ", StringComparison.Ordinal));
                if (damageLine < 0) throw new InvalidOperationException("Boundary fixture has no damage row.");
                lines[damageLine] = "데미지 24 " + string.Concat(Enumerable.Repeat("<color=#F2F2F2>★</color>", syntheticDamageMarks));
                if (!instance.GetComponent<OverburstTooltipHybridSkin>().TryPresent(item, null, lines, view.Body))
                    throw new InvalidOperationException("Boundary fixture did not render.");
            }
            Canvas.ForceUpdateCanvases();
            Image glow = instance.transform.Find("Approved Top Glow").GetComponent<Image>();
            if (glow.color.a > .2f)
                throw new InvalidOperationException(context + ": top gradient is stronger than approved.");
            RectTransform badge = instance.transform.Find("Approved Badge Fill") as RectTransform;
            if (badge.sizeDelta.x > 42f)
                throw new InvalidOperationException(context + ": rarity badge is wider than approved.");
            if (view.Body.enabled || !instance.transform.Find("Approved Content/Primary Heading").gameObject.activeSelf)
                throw new InvalidOperationException(context + ": structured layout did not activate.");
            RectTransform rows = instance.transform.Find("Approved Content/Stat Rows") as RectTransform;
            int activeRows = 0;
            int displayedMarks = 0;
            foreach (Transform row in rows)
            {
                if (!row.gameObject.activeSelf) continue;
                activeRows++;
                TextMeshProUGUI label = row.Find("Label").GetComponent<TextMeshProUGUI>();
                TextMeshProUGUI value = row.Find("Value").GetComponent<TextMeshProUGUI>();
                if (string.IsNullOrWhiteSpace(label.text) || string.IsNullOrWhiteSpace(value.text))
                    throw new InvalidOperationException(context + ": empty visible stat row.");
                TextMeshProUGUI marks = row.Find("Marks").GetComponent<TextMeshProUGUI>();
                if (Mathf.Abs(value.rectTransform.anchoredPosition.x - 70f) > .01f ||
                    Mathf.Abs(value.rectTransform.sizeDelta.x - 160f) > .01f)
                    throw new InvalidOperationException(context + ": stat value column did not move left.");
                if (Mathf.Abs(marks.rectTransform.anchoredPosition.x - 244f) > .01f ||
                    Mathf.Abs(marks.rectTransform.sizeDelta.x - 136f) > .01f ||
                    Mathf.Abs(marks.fontSize - 19f) > .01f || marks.text.Contains("<size="))
                    throw new InvalidOperationException(context + ": quality diamond size or position changed.");
                int markCount = SpriteMark.Matches(marks.text).Count;
                displayedMarks += markCount;
                int expectedLines = Mathf.Max(1, Mathf.CeilToInt(markCount / 6f));
                int actualLines = marks.text.Count(c => c == '\n') + 1;
                if (actualLines != expectedLines ||
                    marks.GetPreferredValues(marks.text, 1000f, 0f).x > 136.1f)
                    throw new InvalidOperationException(context + ": quality diamonds exceed six fixed-size marks per line.");
                float columnGap = marks.rectTransform.anchoredPosition.x -
                    (value.rectTransform.anchoredPosition.x + value.rectTransform.sizeDelta.x);
                if (columnGap < 10f)
                    throw new InvalidOperationException(context + ": stat value and quality columns have no gap.");
                float valueWidth = value.GetPreferredValues(value.text, 1000f, 0f).x;
                if (valueWidth > value.rectTransform.rect.width + 1f)
                    throw new InvalidOperationException(context + ": value exceeds its column: " + value.text);
                float labelWidth = label.GetPreferredValues(label.text, 1000f, 0f).x;
                float valueStart = value.rectTransform.anchoredPosition.x + value.rectTransform.rect.width - valueWidth;
                if (labelWidth + 8f > valueStart)
                    throw new InvalidOperationException(context + ": label and value overlap: " + label.text + " / " + value.text);
            }
            int expectedMarks = item.baseData is FlaskItemData
                ? item.flaskState.rolls.Sum(x => x.stars.Count)
                : item.weaponGradeStats.Sum(x => x.TotalStarCount) - originalDamageMarks + syntheticDamageMarks;
            if (displayedMarks != expectedMarks)
                throw new InvalidOperationException(context + ": quality mark count changed: " + displayedMarks + " != " + expectedMarks);
            if (activeRows < 4 || panel.sizeDelta.y > height - 32f)
                throw new InvalidOperationException(context + ": stat rows or panel height invalid: " + activeRows + ", " + panel.sizeDelta.y);
            foreach (TextMeshProUGUI text in instance.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (!text.gameObject.activeInHierarchy || string.IsNullOrEmpty(text.text)) continue;
                text.ForceMeshUpdate();
                if (text.isTextOverflowing)
                    throw new InvalidOperationException(context + ": text overflow in " + text.name + " (" + text.text + ")");
            }
            if (capturePath == null) return;
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(capturePath, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (pixels) Object.DestroyImmediate(pixels);
            Camera camera = cameraObject.GetComponent<Camera>();
            if (camera) camera.targetTexture = null;
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (instance) Object.DestroyImmediate(instance);
            Object.DestroyImmediate(canvasObject);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
