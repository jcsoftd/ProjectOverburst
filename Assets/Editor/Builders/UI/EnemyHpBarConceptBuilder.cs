using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class EnemyHpBarConceptBuilder
{
    private const string PrefabFolder = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/HpBarConcepts";
    private const string SupplierPrefab = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstEnemyHealthBar_Rpg11.prefab";
    private const string PreviewOutput = "../../개인파일/코덱스산출/UI/20260923_HpBarConcept";

    private static readonly Color Health = new Color(0.78f, 0.145f, 0.155f, 1f);
    private static readonly Color Orange = new Color(0.89f, 0.43f, 0.17f, 1f);
    private static readonly Color MainText = new Color(0.94f, 0.87f, 0.76f, 1f);
    private static readonly Color MutedText = new Color(0.65f, 0.57f, 0.49f, 1f);

    [MenuItem("OVERBURST/UI/Build Monster HP Bar Concepts")]
    public static void BuildAndRender()
    {
        EnsureFolder(PrefabFolder);
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SupplierPrefab);
        Font font = AssetDatabase.LoadAssetAtPath<Font>(
            "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        TMP_FontAsset tmpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium SDF.asset");
        if (source == null || font == null || tmpFont == null)
            throw new InvalidOperationException("Supplier nameplate or project font is missing.");

        Image supplierBackground = source.GetComponent<Image>();
        Image supplierBorder = source.transform.Find("Border").GetComponent<Image>();
        Image supplierFill = source.transform.Find("Bar (Health)").GetComponent<Image>();
        if (supplierBackground.sprite == null || supplierBorder.sprite == null || supplierFill.sprite == null)
            throw new InvalidOperationException("Supplier nameplate sprites are missing.");

        Save(CreateCompact("PF_EnemyHpBar_Concept_Small", 74f, false,
            supplierBackground.sprite, supplierBorder.sprite, supplierFill.sprite, tmpFont), "Small");
        Save(CreateCompact("PF_EnemyHpBar_Concept_Medium", 138f, true,
            supplierBackground.sprite, supplierBorder.sprite, supplierFill.sprite, tmpFont), "Medium");
        Save(CreateElite(), "Elite");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, PreviewOutput));
        Directory.CreateDirectory(outputDir);
        RenderComparison(outputDir, font);
        RenderActualScale(outputDir, font);
        RenderCompactDetails(outputDir, font);
        Debug.Log("Enemy HP bar concept prefabs and previews: " + outputDir);
    }

    private static GameObject CreateCompact(string name, float width, bool showName,
        Sprite backgroundSprite, Sprite borderSprite, Sprite fillSprite, TMP_FontAsset font)
    {
        // The supplier's 516:52 nameplate keeps its bevels when uniformly reduced.
        float barHeight = width * 52f / 516f;
        float borderWidth = width * 534f / 516f;
        float borderHeight = barHeight * 74f / 52f;
        float fillWidth = width * 508f / 516f;
        float fillHeight = barHeight * 43f / 52f;
        float barY = showName ? -7f : 0f;
        GameObject root = NewRect(name, null,
            new Vector2(borderWidth + 4f, showName ? 34f : borderHeight + 3f), Vector2.zero);
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        Image background = NewImage("NameplateBackground", root.transform, new Vector2(width, barHeight),
            new Vector2(0f, barY), backgroundSprite, Color.white, Image.Type.Simple);
        Shadow shadow = background.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
        shadow.effectDistance = new Vector2(0f, -1.2f);
        Image trail = NewImage("OrangeDamageTrail", root.transform,
            new Vector2(fillWidth, fillHeight), new Vector2(0f, barY), fillSprite, Orange, Image.Type.Filled);
        Image fill = NewImage("CurrentHealth", root.transform,
            new Vector2(fillWidth, fillHeight), new Vector2(0f, barY), fillSprite, Health, Image.Type.Filled);
        NewImage("NameplateBorders", root.transform, new Vector2(borderWidth, borderHeight),
            new Vector2(0f, barY), borderSprite, Color.white, Image.Type.Simple);

        if (showName)
        {
            GameObject textObject = NewRect("Name", root.transform, new Vector2(width + 12f, 17f),
                new Vector2(0f, 9.5f));
            TextMeshProUGUI nameText = textObject.AddComponent<TextMeshProUGUI>();
            nameText.font = font;
            nameText.fontSize = 11.5f;
            nameText.fontStyle = FontStyles.Normal;
            nameText.alignment = TextAlignmentOptions.Center;
            nameText.color = MainText;
            nameText.raycastTarget = false;
            nameText.enableWordWrapping = false;
            nameText.text = "암굴 추적자";
        }

        EnemyHpBarConceptPreview preview = root.AddComponent<EnemyHpBarConceptPreview>();
        preview.Configure(group, fill, trail, false);
        return root;
    }

    private static GameObject CreateElite()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(SupplierPrefab);
        if (source == null)
            throw new InvalidOperationException("Supplier RPG/MMO11 nameplate is missing.");

        GameObject root = NewRect("PF_EnemyHpBar_Concept_Elite", null, new Vector2(226f, 48f), Vector2.zero);
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        GameObject vendor = (GameObject)PrefabUtility.InstantiatePrefab(source);
        vendor.transform.SetParent(root.transform, false);
        RectTransform vendorRect = vendor.GetComponent<RectTransform>();
        vendorRect.anchoredPosition = Vector2.zero;
        vendorRect.localScale = Vector3.one * 0.4f;
        Image fill = vendor.transform.Find("Bar (Health)").GetComponent<Image>();
        RectTransform fillRect = fill.rectTransform;
        Image trail = NewImage("OrangeDamageTrail", vendor.transform,
            fillRect.sizeDelta, fillRect.anchoredPosition, fill.sprite, Orange, Image.Type.Filled);
        RectTransform trailRect = trail.rectTransform;
        trailRect.anchorMin = fillRect.anchorMin;
        trailRect.anchorMax = fillRect.anchorMax;
        trailRect.pivot = fillRect.pivot;
        trailRect.localScale = fillRect.localScale;
        trailRect.localRotation = fillRect.localRotation;
        trail.transform.SetSiblingIndex(fill.transform.GetSiblingIndex());
        vendor.GetComponent<OverburstEnemyHealthBarView>().PresentTarget("암굴 변이 정예", "Elite", 1f);

        EnemyHpBarConceptPreview preview = root.AddComponent<EnemyHpBarConceptPreview>();
        preview.Configure(group, fill, trail, true);
        return root;
    }

    private static void Save(GameObject root, string tier)
    {
        try
        {
            string path = PrefabFolder + "/PF_EnemyHpBar_Concept_" + tier + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            if (!success)
                throw new InvalidOperationException("Could not save prefab: " + path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void RenderComparison(string outputDir, Font font)
    {
        Render(Path.Combine(outputDir, "01-comparison-zoom.png"), 1460, 660, (canvas) =>
        {
            NewText("Title", canvas, font, "몬스터 HP바 · 체급별 비교", 29, MainText,
                new Vector2(850f, 45f), new Vector2(0f, 274f));
            NewText("Subtitle", canvas, font, "기존 RPG/MMO11 프레임을 공유하는 시안  |  확대 1.7×", 16, MutedText,
                new Vector2(1000f, 28f), new Vector2(0f, 236f));
            NewImage("TopRule", canvas, new Vector2(1320f, 1f), new Vector2(0f, 205f),
                null, new Color(0.47f, 0.36f, 0.24f, 0.8f), Image.Type.Simple);

            float[] columns = { -350f, 40f, 430f };
            string[] headers = { "평상시", "피격 직후 · 64%", "0.2초 후 · 잔상" };
            for (int col = 0; col < 3; col++)
            {
                NewText("ColumnTitle", canvas, font, headers[col], 17, MainText,
                    new Vector2(365f, 29f), new Vector2(columns[col], 169f));
            }

            float[] rows = { 104f, -31f, -166f };
            string[] tiers = { "소형", "중형", "정예" };
            string[] sizes = { "74px · 막대만", "138px · 이름만", "206px · RPG형" };
            for (int row = 0; row < 3; row++)
            {
                NewImage("RowBand", canvas, new Vector2(1320f, 115f), new Vector2(0f, rows[row]),
                    null, new Color(0.15f, 0.115f, 0.105f, row == 2 ? 0.90f : 0.62f), Image.Type.Simple);
                NewText("Tier", canvas, font, tiers[row], 21, MainText,
                    new Vector2(130f, 30f), new Vector2(-626f, rows[row] + 13f));
                NewText("Size", canvas, font, sizes[row], 13, MutedText,
                    new Vector2(150f, 24f), new Vector2(-626f, rows[row] - 13f));
                for (int col = 0; col < 3; col++)
                {
                    if (row < 2 && col == 0)
                    {
                        NewText("Hidden", canvas, font, "표시 안 함", 14, MutedText,
                            new Vector2(220f, 26f), new Vector2(columns[col], rows[row]));
                        continue;
                    }

                    float hp = col == 0 ? 1f : 0.64f;
                    float trail = col == 0 ? 1f : col == 1 ? 1f : 0.79f;
                    AddConcept(canvas, tiers[row], new Vector2(columns[col], rows[row]), 1.7f, hp, trail);
                }
            }
            NewText("Footer", canvas, font,
                "주황 = 방금 잃은 체력의 잔상     소형·중형은 1.35초 뒤 사라짐     정예는 계속 표시",
                15, MainText, new Vector2(1310f, 30f), new Vector2(0f, -293f));
        });
    }

    private static void RenderActualScale(string outputDir, Font font)
    {
        Render(Path.Combine(outputDir, "02-actual-scale.png"), 1280, 500, (canvas) =>
        {
            NewText("Title", canvas, font, "기본 크기 1× · 피격 직후", 27, MainText,
                new Vector2(800f, 43f), new Vector2(0f, 203f));
            NewText("Subtitle", canvas, font, "세 프리팹을 같은 UI 단위로 비교", 15, MutedText,
                new Vector2(700f, 27f), new Vector2(0f, 169f));
            NewImage("TopRule", canvas, new Vector2(1000f, 1f), new Vector2(0f, 137f),
                null, new Color(0.47f, 0.36f, 0.24f, 0.8f), Image.Type.Simple);
            float[] rows = { 77f, -23f, -123f };
            string[] tiers = { "소형", "중형", "정예" };
            for (int i = 0; i < 3; i++)
            {
                NewImage("Row", canvas, new Vector2(1000f, 82f), new Vector2(0f, rows[i]),
                    null, new Color(0.15f, 0.115f, 0.105f, 0.75f), Image.Type.Simple);
                NewText("Tier", canvas, font, tiers[i], 18, MainText,
                    new Vector2(120f, 30f), new Vector2(-435f, rows[i]));
                AddConcept(canvas, tiers[i], new Vector2(75f, rows[i]), 1f, 0.64f, 1f);
            }
            NewText("Foot", canvas, font, "게임 내 거리·카메라 배율은 아직 적용 전 시안", 13, MutedText,
                new Vector2(950f, 26f), new Vector2(0f, -214f));
        });
    }

    private static void RenderCompactDetails(string outputDir, Font font)
    {
        Render(Path.Combine(outputDir, "03-compact-details.png"), 1300, 520, (canvas) =>
        {
            NewText("Title", canvas, font, "일반 몬스터 · 디테일 확대", 29, MainText,
                new Vector2(850f, 44f), new Vector2(0f, 207f));
            NewText("Subtitle", canvas, font, "기존 UI의 사선 끝·금속 테두리·체력 질감을 작은 막대에도 사용", 16, MutedText,
                new Vector2(1050f, 28f), new Vector2(0f, 168f));
            float[] x = { -308f, 308f };
            string[] tiers = { "소형", "중형" };
            string[] detail = { "이름·퍼센트·등급 표식 없음", "이름만 남기고 퍼센트·등급 표식 제거" };
            for (int i = 0; i < 2; i++)
            {
                NewImage("DetailPanel", canvas, new Vector2(575f, 310f), new Vector2(x[i], -26f),
                    null, new Color(0.15f, 0.115f, 0.105f, 0.9f), Image.Type.Simple);
                NewText("Tier", canvas, font, tiers[i], 22, MainText,
                    new Vector2(250f, 32f), new Vector2(x[i], 105f));
                NewText("Now", canvas, font, "피격 직후", 13, MutedText,
                    new Vector2(250f, 25f), new Vector2(x[i], 67f));
                AddConcept(canvas, tiers[i], new Vector2(x[i], 0f), 2.5f, 0.64f, 1f);
                NewText("Later", canvas, font, "주황 잔상 감소", 13, MutedText,
                    new Vector2(250f, 25f), new Vector2(x[i], -45f));
                AddConcept(canvas, tiers[i], new Vector2(x[i], -105f), 2.5f, 0.64f, 0.79f);
                NewText("Detail", canvas, font, detail[i], 14, MainText,
                    new Vector2(520f, 26f), new Vector2(x[i], -160f));
            }
            NewText("Foot", canvas, font, "비교를 위한 확대 화면 · 실제 크기는 02 이미지", 13, MutedText,
                new Vector2(900f, 24f), new Vector2(0f, -231f));
        });
    }

    private static void AddConcept(Transform canvas, string tier, Vector2 position, float scale, float hp, float trail)
    {
        string english = tier == "소형" ? "Small" : tier == "중형" ? "Medium" : "Elite";
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabFolder + "/PF_EnemyHpBar_Concept_" + english + ".prefab");
        GameObject instance = UnityEngine.Object.Instantiate(source, canvas, false);
        RectTransform rect = instance.GetComponent<RectTransform>();
        rect.anchoredPosition = position;
        rect.localScale = Vector3.one * scale;
        EnemyHpBarConceptPreview preview = instance.GetComponent<EnemyHpBarConceptPreview>();
        preview.SetEditorPreview(hp, trail, 1f);
        OverburstEnemyHealthBarView rpg = instance.GetComponentInChildren<OverburstEnemyHealthBarView>();
        if (rpg != null)
            rpg.PresentTarget("암굴 변이 정예", "Elite", hp);
    }

    private static void Render(string path, int width, int height, Action<Transform> populate)
    {
        GameObject cameraObject = new GameObject("HP concept preview camera");
        GameObject canvasObject = new GameObject("HP concept preview canvas");
        RenderTexture target = null;
        Texture2D pixels = null;
        RenderTexture previousActive = RenderTexture.active;
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = height * 0.5f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.065f, 0.052f, 0.052f, 1f);
            target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();
            populate(canvasObject.transform);

            Canvas.ForceUpdateCanvases();
            foreach (TextMeshProUGUI label in canvasObject.GetComponentsInChildren<TextMeshProUGUI>(true))
                label.ForceMeshUpdate(true, true);
            Canvas.ForceUpdateCanvases();
            camera.Render();
            camera.Render();
            RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            pixels.Apply();
            File.WriteAllBytes(path, pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previousActive;
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            UnityEngine.Object.DestroyImmediate(canvasObject);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }

    private static void EnsureFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static GameObject NewRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return go;
    }

    private static Image NewImage(string name, Transform parent, Vector2 size, Vector2 position,
        Sprite sprite, Color color, Image.Type type)
    {
        GameObject go = NewRect(name, parent, size, position);
        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.raycastTarget = false;
        if (type == Image.Type.Filled)
        {
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
        }
        return image;
    }

    private static Text NewText(string name, Transform parent, Font font, string value, int fontSize,
        Color color, Vector2 size, Vector2 position)
    {
        GameObject go = NewRect(name, parent, size, position);
        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.text = value;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }
}
