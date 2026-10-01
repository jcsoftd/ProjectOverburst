using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

// 2026-10-01: 상단 대상 HUD(일반·정예)와 보스 HUD를 머리 위 정예 HP바(PF_EnemyHpBar_Elite_Tier)와 같은
// 네임플레이트 재료로 다시 짠다. 시안은 개인파일/코덱스산출/UI/20261001_TargetHudProposal(v8 확정, 보스 연출 3종).
// - 좌표는 시안 캔버스 단위(원본 텍스처 1px, 위에서 아래로 +)이고 HUD 루트 배율 0.55로 1920x1080 화면에 맞춘다.
// - 두 프리팹의 기존 컴포넌트·GUID는 그대로 두고 하위 구성만 다시 만든다. 씬은 열거나 저장하지 않는다.
// - RepresentativeBossBuilder.EnsureHud가 찾는 이름(HealthBar·Phase·GroggyBar/GroggyFill·BossState)은 유지한다.
public static class EnemyTargetHudRpg11Builder
{
    private const string HudPrefab = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstHUD_Rpg11.prefab";
    private const string BossPrefab = "Assets/ProjectOverburst/Resources/UI/HUD/PF_EnemyBossHud.prefab";
    private const string VendorNameplate = "Assets/ThirdParty/RPG and MMO UI 11/Textures/HUD/Unit Frames/Nameplate/";
    private const string ArtFolder = "Assets/ProjectOverburst/05_Art/UI/HUD/TargetHud";
    private const string ElementIconFolder = "Assets/ProjectOverburst/05_Art/UI/Icons/Elements/";
    private const string FontAssetPath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD SDF.asset";
    // 2026-10-01: 기존 NotoSerifKR SDF는 가변 글꼴 기본값(굵기 200)으로 구워졌고 여백 3px라 그림자가 거의 안 그려졌다.
    // 시안과 같은 SemiBold(600)로 고정·한글 완성형 부분집합으로 만든 TTF(fontTools instancer)를 여백 9의 동적 SDF로 쓴다.
    private const string FontFilePath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD.ttf";
    private const string FallbackFontAssetPath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght SDF.asset";
    private const string SoftShadowMaterialPath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR SDF - HUD Soft Shadow.mat";
    private const string NameShadowMaterialPath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR SDF - HUD Name Shadow.mat";

    private const float RootScale = 0.55f;
    private const float BarLeft = 98f;
    private const float BarTop = 50f;
    private const float BarHeight = 52f;
    private const float DiamondSize = 148f;
    private const float DiamondCx = BarLeft - 24f;
    private const float DiamondCy = BarTop + 26f;
    private const float FxSize = DiamondSize * 2f; // 셰이더 _Extent 2 = 마름모 반지름의 2배까지 그린다
    private const float NormalWidth = 640f;
    private const float EliteWidth = 820f;
    private const float BossWidth = 1180f;
    private const float FrameExtra = 110f;
    private const float StatusSpacing = 54f;
    private const int StatusCells = 6;

    private static readonly Color Cream = new Color32(237, 230, 212, 255);
    private static readonly Color Gold = new Color32(246, 214, 150, 255);
    private static readonly Color FillRed = new Color(1f, 0.24f, 0.24f, 1f);
    private static readonly Color TrailOrange = new Color(0.89f, 0.43f, 0.17f, 1f);
    private static readonly Color PercentColor = new Color32(226, 218, 200, 235);
    private static readonly Color Metal = new Color32(73, 67, 60, 255);
    private static readonly Color GroggyAmber = new Color(1f, 0.74f, 0.22f, 1f);
    private static readonly Color StateColor = new Color(1f, 0.78f, 0.3f, 1f);

    private sealed class Art
    {
        public Sprite barBackground, barBorders, barFill, levelFrame, phaseGem, statusCell, statusSweep;
        public Sprite fillNormal, fillElite, fillBoss;
        public Sprite fire, ice, electric;

        // 막대 폭마다 끝 사선을 원래 크기로 둔 채움 그림(테두리·배경 9분할 끝과 같은 위치에서 끝난다).
        public Sprite FillFor(float width) => width >= BossWidth ? fillBoss : width >= EliteWidth ? fillElite : fillNormal;
        public TMP_FontAsset font;
        public Material softShadow, nameShadow;
    }

    private sealed class Frame
    {
        public RectTransform bar, groggyBar, statusRow, phaseGems, phaseTicks, phaseTickTemplate;
        public Image fill, trail, groggyFill, fxBack, fxFront, phaseGemTemplate;
        public TextMeshProUGUI name, level, hp, percent, phase, state;
        public EnemyTargetStatusRow.Cell[] cells;
    }

    [MenuItem("Tools/Project VTP/UI/상단 대상·보스 HUD 다시 만들기 (RPG UI 11)")]
    public static void BuildFromMenu()
    {
        Debug.Log("[EnemyTargetHudRpg11Builder]\n" + Build());
    }

    [MenuItem("Tools/Project VTP/UI/상단 대상 HUD만 다시 만들기 (RPG UI 11)")]
    public static void BuildTargetFromMenu()
    {
        Debug.Log("[EnemyTargetHudRpg11Builder]\n" + BuildTargetOnly());
    }

    public static string Build()
    {
        return Run(true, true);
    }

    // 대상 HUD(일반·정예)만: PF_OverburstHUD_Rpg11 하나만 저장한다.
    public static string BuildTargetOnly()
    {
        return Run(true, false);
    }

    // 보스 HUD만: PF_EnemyBossHud 하나만 저장한다.
    public static string BuildBossOnly()
    {
        return Run(false, true);
    }

    private static string Run(bool target, bool boss)
    {
        var log = new List<string>();
        EnsureArt(log);
        Art art = LoadArt();
        if (target) BuildTarget(art, log);
        if (boss) BuildBoss(art, log);
        AssetDatabase.SaveAssets();
        Validate(log, target, boss);
        return string.Join("\n", log);
    }

    // ---------- 자산 ----------

    private static void EnsureArt(List<string> log)
    {
        EnsureFolder(ArtFolder);
        CopyWithBorder(VendorNameplate + "Nameplate_Background.png", ArtFolder + "/TX_TargetHud_BarBackground.png", new Vector4(24f, 0f, 44f, 0f), log);
        CopyWithBorder(VendorNameplate + "Nameplate_Borders.png", ArtFolder + "/TX_TargetHud_BarBorders.png", new Vector4(24f, 0f, 44f, 0f), log);
        WriteGenerated(ArtFolder + "/TX_TargetHud_PhaseGem.png", GeneratePhaseGem(64), log);
        foreach (float w in new[] { NormalWidth, EliteWidth, BossWidth })
            WriteGenerated(FillPath(w), GenerateStretchedFill(VendorNameplate + "Nameplate_Fill.png", Mathf.RoundToInt(w) - 8, 24, 30), log);
        WriteGenerated(ArtFolder + "/TX_TargetHud_StatusCell.png", GenerateStatusCell(64, false), log);
        WriteGenerated(ArtFolder + "/TX_TargetHud_StatusSweep.png", GenerateStatusCell(64, true), log);
        EnsureFontMaterials(log);
    }

    private static Art LoadArt()
    {
        var art = new Art
        {
            barBackground = LoadSprite(ArtFolder + "/TX_TargetHud_BarBackground.png"),
            barBorders = LoadSprite(ArtFolder + "/TX_TargetHud_BarBorders.png"),
            barFill = LoadSprite(VendorNameplate + "Nameplate_Fill.png"),
            fillNormal = LoadSprite(FillPath(NormalWidth)),
            fillElite = LoadSprite(FillPath(EliteWidth)),
            fillBoss = LoadSprite(FillPath(BossWidth)),
            levelFrame = LoadSprite(VendorNameplate + "Nameplate_LevelFrame.png"),
            phaseGem = LoadSprite(ArtFolder + "/TX_TargetHud_PhaseGem.png"),
            statusCell = LoadSprite(ArtFolder + "/TX_TargetHud_StatusCell.png"),
            statusSweep = LoadSprite(ArtFolder + "/TX_TargetHud_StatusSweep.png"),
            fire = LoadSprite(ElementIconFolder + "Icon_Element_Fire.png"),
            ice = LoadSprite(ElementIconFolder + "Icon_Element_Ice.png"),
            electric = LoadSprite(ElementIconFolder + "Icon_Element_Electric.png"),
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath),
            softShadow = AssetDatabase.LoadAssetAtPath<Material>(SoftShadowMaterialPath),
            nameShadow = AssetDatabase.LoadAssetAtPath<Material>(NameShadowMaterialPath)
        };
        if (art.font == null) throw new InvalidOperationException("글꼴 자산이 없다: " + FontAssetPath);
        return art;
    }

    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) throw new InvalidOperationException("스프라이트를 불러오지 못했다: " + path);
        return sprite;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // 공급사 원본(ThirdParty)은 건드리지 않고, 9분할 여백을 준 사본을 프로젝트 쪽에 둔다.
    private static void CopyWithBorder(string source, string target, Vector4 border, List<string> log)
    {
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(target) == null)
        {
            if (!AssetDatabase.CopyAsset(source, target)) throw new InvalidOperationException("복사 실패: " + source);
            log.Add("사본 " + Path.GetFileName(target));
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(target);
        if (importer.spriteBorder != border || importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }
    }

    private static void WriteGenerated(string assetPath, Texture2D texture, List<string> log)
    {
        byte[] png = texture.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(texture);
        string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), assetPath);
        bool same = File.Exists(full) && File.ReadAllBytes(full).SequenceEqual(png);
        if (!same)
        {
            File.WriteAllBytes(full, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            log.Add("생성 " + Path.GetFileName(assetPath));
        }

        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled || importer.wrapMode != TextureWrapMode.Clamp)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }

    private static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    private static string FillPath(float width) => ArtFolder + "/TX_TargetHud_BarFill_" + Mathf.RoundToInt(width) + ".png";

    // 공급사 채움 그림(508폭)을 3분할로 늘린다: 왼쪽 끝·오른쪽 사선 끝은 원래 픽셀 그대로, 가운데만 가로로 늘린다.
    // Filled(가로) 이미지는 9분할이 안 되므로, 폭마다 미리 늘린 그림을 둬야 100%에서 끝 사선이 테두리와 맞는다.
    private static Texture2D GenerateStretchedFill(string sourcePath, int width, int left, int right)
    {
        var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        src.LoadImage(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Application.dataPath), sourcePath)));
        int sw = src.width, h = src.height;
        var tex = new Texture2D(width, h, TextureFormat.RGBA32, false);
        var pixels = new Color[width * h];
        int midSrc = sw - left - right, midDst = width - left - right;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < width; x++)
            {
                Color c;
                if (x < left) c = src.GetPixel(x, y);
                else if (x >= width - right) c = src.GetPixel(sw - (width - x), y);
                else c = src.GetPixelBilinear((left + (x - left + 0.5f) * midSrc / midDst) / sw, (y + 0.5f) / h);
                pixels[y * width + x] = c;
            }
        tex.SetPixels(pixels);
        tex.Apply();
        UnityEngine.Object.DestroyImmediate(src);
        return tex;
    }

    // 보스 남은 단계 보석(레벨틀 안쪽에 얹는다). 시안: 반 대각 0.25, 위쪽 작은 반사광.
    private static Texture2D GeneratePhaseGem(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float c = size * 0.5f, half = size * 0.25f;
        var body = new Color(236f / 255f, 108f / 255f, 48f / 255f, 1f);
        var shine = new Color(1f, 196f / 255f, 140f / 255f, 1f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - c, dy = y + 0.5f - c;
                float bodyA = 1f - Smooth(half - 0.8f, half + 0.8f, Mathf.Abs(dx) + Mathf.Abs(dy));
                float hx = dx / (half * 0.45f), hy = (dy - half * 0.35f) / (half * 0.34f); // 위쪽(텍스처 y+)
                float shineA = (1f - Smooth(0.85f, 1.1f, Mathf.Abs(hx) + Mathf.Abs(hy))) * 0.78f;
                Color col = Color.Lerp(body, shine, shineA);
                col.a = bodyA;
                pixels[y * size + x] = col;
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // 원소 상태 칸: 어두운 둥근 사각형 + 네임플레이트 테두리와 같은 금속색 선. sweepMask면 덮개용 흰 안쪽 모양.
    private static Texture2D GenerateStatusCell(int size, bool sweepMask)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        float radius = size * 7f / 64f, border = 3f, inset = sweepMask ? 3f : 0f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float lo = inset, hi = size - inset;
                float qx = Mathf.Max(Mathf.Max(lo + radius - px, px - (hi - radius)), 0f);
                float qy = Mathf.Max(Mathf.Max(lo + radius - py, py - (hi - radius)), 0f);
                float outside = Mathf.Sqrt(qx * qx + qy * qy) - radius; // 둥근 사각형 바깥 거리(음수 = 안쪽)
                float edgeDist = Mathf.Min(Mathf.Min(px - lo, hi - px), Mathf.Min(py - lo, hi - py));
                float a = 1f - Smooth(-0.6f, 0.6f, outside);
                Color col;
                if (sweepMask)
                    col = new Color(1f, 1f, 1f, a);
                else
                {
                    bool rim = edgeDist < border || outside > -border;
                    col = rim ? new Color(Metal.r, Metal.g, Metal.b, a) : new Color(20f / 255f, 16f / 255f, 14f / 255f, a * 240f / 255f);
                }
                pixels[y * size + x] = col;
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // TMP 글자 그림자 재질. 기본: 화면 기준 약 1px 아래 부드러운 그림자 + 아주 옅은 외곽.
    // 이름: 넓고 진한 그림자(밝은 바닥 위에서도 읽히게, 2026-10-01 사용자 요청 '이름 쪽 그림자 조금 더').
    private static void EnsureFontMaterials(List<string> log)
    {
        var font = EnsureHudFont(log);
        EnsureTmpMaterial(font, SoftShadowMaterialPath, 0.8f, -0.5f, 0.1f, 0.5f, 0f, 0.08f, 0.35f, log);
        EnsureTmpMaterial(font, NameShadowMaterialPath, 0.95f, -0.75f, 0.35f, 0.85f, 0f, 0.1f, 0.5f, log);
    }

    private static TMP_FontAsset EnsureHudFont(List<string> log)
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null) return existing;

        var ttf = AssetDatabase.LoadAssetAtPath<Font>(FontFilePath);
        if (ttf == null) throw new InvalidOperationException("HUD 글꼴 파일이 없다: " + FontFilePath);
        var asset = TMP_FontAsset.CreateFontAsset(ttf, 64, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 2048, 2048,
            AtlasPopulationMode.Dynamic, true);
        asset.name = Path.GetFileNameWithoutExtension(FontAssetPath);
        AssetDatabase.CreateAsset(asset, FontAssetPath);
        asset.atlasTexture.name = asset.name + " Atlas";
        AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
        asset.material.name = asset.name + " Material";
        AssetDatabase.AddObjectToAsset(asset.material, asset);
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FallbackFontAssetPath);
        if (fallback != null) asset.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
        using (var so = new SerializedObject(asset))
        {
            SerializedProperty clear = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null) clear.boolValue = true; // 빌드 때 동적 글리프를 비운다(Pretendard SDF와 같은 설정)
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        log.Add("HUD 글꼴 자산 " + asset.name);
        return asset;
    }

    private static void EnsureTmpMaterial(TMP_FontAsset font, string path, float underlayAlpha, float offsetY,
        float dilate, float softness, float faceDilate, float outlineWidth, float outlineAlpha, List<string> log)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(font.material) { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(material, path);
            log.Add("글자 재질 " + material.name);
        }

        material.shader = font.material.shader;
        material.CopyPropertiesFromMaterial(font.material);
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor("_UnderlayColor", new Color(0f, 0f, 0f, underlayAlpha));
        material.SetFloat("_UnderlayOffsetX", 0f);
        material.SetFloat("_UnderlayOffsetY", offsetY);
        material.SetFloat("_UnderlayDilate", dilate);
        material.SetFloat("_UnderlaySoftness", softness);
        material.SetFloat("_FaceDilate", faceDilate);
        material.SetFloat("_OutlineWidth", outlineWidth);
        material.SetColor("_OutlineColor", new Color(0f, 0f, 0f, outlineAlpha));
        EditorUtility.SetDirty(material);
    }

    // ---------- 프리팹 ----------

    private static void BuildTarget(Art art, List<string> log)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(HudPrefab);
        try
        {
            var slots = prefabRoot.GetComponentsInChildren<EnemyTargetHpSlotUI>(true);
            if (slots.Length != 1) throw new InvalidOperationException("HUD 프리팹의 EnemyTargetHpSlotUI 수가 1이 아니다: " + slots.Length);
            EnemyTargetHpSlotUI slot = slots[0];
            GameObject go = slot.gameObject;
            var rt = (RectTransform)go.transform;
            ClearChildren(rt, "ElementalStatusStrip");
            Transform oldStrip = rt.Find("ElementalStatusStrip");
            if (oldStrip != null) oldStrip.gameObject.SetActive(false); // 아이콘 원본만 남긴다(표시는 StatusRow)
            var oldPanel = go.GetComponent<Image>();
            if (oldPanel != null) UnityEngine.Object.DestroyImmediate(oldPanel);
            var group = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            SetRoot(rt, EliteWidth, 200f);
            Frame frame = BuildFrame(rt, EliteWidth, false, art);

            var strip = go.GetComponent<ElementalStatusIconStrip>();
            if (strip == null) throw new InvalidOperationException("대상 HUD의 ElementalStatusIconStrip이 없다.");
            var row = frame.statusRow.gameObject.AddComponent<EnemyTargetStatusRow>();
            row.Configure(strip, frame.cells, StatusSpacing);

            using (var so = new SerializedObject(slot))
            {
                Set(so, "root", go);
                Set(so, "canvasGroup", group);
                Set(so, "nameText", frame.name);
                Set(so, "levelText", frame.level);
                Set(so, "fillImage", frame.fill);
                Set(so, "trailImage", frame.trail);
                Set(so, "hpText", frame.hp);
                Set(so, "percentText", frame.percent);
                Set(so, "statusRow", row);
                Set(so, "elementalStatusIcons", strip);
                Set(so, "frameRoot", rt);
                Set(so, "barRoot", frame.bar);
                Set(so, "normalFillSprite", art.fillNormal);
                Set(so, "eliteFillSprite", art.fillElite);
                so.FindProperty("normalBarWidth").floatValue = NormalWidth;
                so.FindProperty("eliteBarWidth").floatValue = EliteWidth;
                so.FindProperty("frameExtraWidth").floatValue = FrameExtra;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, HudPrefab);
            log.Add("대상 HUD 재구성: " + HudPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    private static void BuildBoss(Art art, List<string> log)
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(BossPrefab);
        try
        {
            ConfigureBoss(prefabRoot, art);
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, BossPrefab);
            log.Add("보스 HUD 재구성: " + BossPrefab);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
    }

    // 확인 캡처용: 저장하지 않는 보스 HUD 인스턴스(미리보기 씬)에 같은 구성을 입힌다. 프리팹 자산은 바꾸지 않는다.
    public static void ApplyBossLayoutToInstance(GameObject bossHudInstance)
    {
        var log = new List<string>();
        EnsureArt(log);
        ConfigureBoss(bossHudInstance, LoadArt());
    }

    private static void ConfigureBoss(GameObject prefabRoot, Art art)
    {
        {
            var view = prefabRoot.GetComponent<EnemyBossHudView>();
            var panel = prefabRoot.transform.Find("BossPanel") as RectTransform;
            if (view == null || panel == null) throw new InvalidOperationException("보스 HUD 구조(EnemyBossHudView/BossPanel)를 찾지 못했다.");
            ClearChildren(panel, null);
            foreach (var effect in panel.GetComponents<BaseMeshEffect>()) UnityEngine.Object.DestroyImmediate(effect);
            var oldPanel = panel.GetComponent<Image>();
            if (oldPanel != null) UnityEngine.Object.DestroyImmediate(oldPanel);
            foreach (var old in panel.GetComponents<HudBossEmblemFx>()) UnityEngine.Object.DestroyImmediate(old);

            SetRoot(panel, BossWidth, 220f);
            Frame frame = BuildFrame(panel, BossWidth, true, art);

            var iconSource = frame.statusRow.gameObject.AddComponent<ElementalStatusIconStrip>();
            using (var so = new SerializedObject(iconSource))
            {
                Set(so, "fireIcon", art.fire);
                Set(so, "iceIcon", art.ice);
                Set(so, "electricIcon", art.electric);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var row = frame.statusRow.gameObject.AddComponent<EnemyTargetStatusRow>();
            row.Configure(iconSource, frame.cells, StatusSpacing);
            var fx = panel.gameObject.AddComponent<HudBossEmblemFx>();
            fx.Configure(frame.fxBack, frame.fxFront);

            using (var so = new SerializedObject(view))
            {
                var group = prefabRoot.GetComponent<CanvasGroup>() ?? prefabRoot.AddComponent<CanvasGroup>();
                Set(so, "canvasGroup", group);
                Set(so, "bossNameText", frame.name);
                Set(so, "phaseText", frame.phase);
                Set(so, "healthText", frame.hp);
                Set(so, "healthFill", frame.fill);
                Set(so, "groggyFill", frame.groggyFill);
                Set(so, "stateText", frame.state);
                Set(so, "levelText", frame.level);
                Set(so, "percentText", frame.percent);
                Set(so, "trailFill", frame.trail);
                Set(so, "phaseGemRoot", frame.phaseGems);
                Set(so, "phaseGemTemplate", frame.phaseGemTemplate);
                Set(so, "phaseTickRoot", frame.phaseTicks);
                Set(so, "phaseTickTemplate", frame.phaseTickTemplate);
                Set(so, "statusRow", row);
                Set(so, "emblemFx", fx);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static void SetRoot(RectTransform rt, float width, float height)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -34f);
        rt.sizeDelta = new Vector2(width + FrameExtra, height);
        rt.localScale = Vector3.one * RootScale;
        rt.localRotation = Quaternion.identity;
    }

    private static void ClearChildren(RectTransform parent, string keep)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (keep != null && child.name == keep) continue;
            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }

    private static Frame BuildFrame(RectTransform parent, float width, bool boss, Art art)
    {
        var f = new Frame();
        if (boss) f.fxBack = CenteredImage("FxBack", parent, DiamondCx, DiamondCy, FxSize, null, Color.white);

        f.bar = TopLeft(boss ? "HealthBar" : "Bar", parent, BarLeft, BarTop, width, BarHeight, new Vector2(0f, 1f));
        AddImage(Stretch("Background", f.bar, Vector2.zero, Vector2.zero), art.barBackground, Color.white, Image.Type.Sliced);
        f.trail = AddFilled(Stretch("Trail", f.bar, new Vector2(2f, 3f), new Vector2(-6f, -6f)), art.FillFor(width), TrailOrange);
        f.fill = AddFilled(Stretch("Fill", f.bar, new Vector2(2f, 3f), new Vector2(-6f, -6f)), art.FillFor(width), FillRed);
        if (boss)
        {
            f.phaseTicks = (RectTransform)Stretch("PhaseTicks", f.bar, new Vector2(2f, 3f), new Vector2(-6f, -6f)).transform;
            f.phaseTickTemplate = Rect("Tick", f.phaseTicks, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, -2f));
            AddImage(f.phaseTickTemplate.gameObject, null, new Color32(8, 6, 5, 255), Image.Type.Simple);
            var line = Rect("Line", f.phaseTickTemplate, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(1f, -2f));
            AddImage(line.gameObject, null, new Color32(214, 196, 164, 90), Image.Type.Simple);
            f.phaseTickTemplate.gameObject.SetActive(false);
        }
        AddImage(Stretch("Borders", f.bar, new Vector2(-7f, -10f), new Vector2(11f, 12f)), art.barBorders, Color.white, Image.Type.Sliced);
        var hpRect = Rect(boss ? "HealthText" : "HpText", f.bar, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(64f, -1f), new Vector2(520f, 0f));
        f.hp = AddText(hpRect.gameObject, art, art.softShadow, 26f, Cream, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, "0 / 0");
        var pctRect = Rect("PercentText", f.bar, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(-46f, -1f), new Vector2(160f, 0f));
        f.percent = AddText(pctRect.gameObject, art, art.softShadow, 22f, PercentColor, TextAlignmentOptions.MidlineRight, FontStyles.Normal, "100%");

        if (boss)
        {
            // 그로기 막대: 왼쪽은 마름모 밑에서 시작해 마름모 오른쪽 아래 변에서 딱 나오고(마름모가 위에 그려진다),
            // 오른쪽은 막대 아래 테두리선이 끝나는 자리(막대 끝 -10)에서 같이 끝난다.
            f.groggyBar = TopLeft("GroggyBar", parent, DiamondCx, BarTop + 58f, BarLeft + width - 10f - DiamondCx, 8f, new Vector2(0f, 1f));
            AddImage(f.groggyBar.gameObject, null, new Color(0.06f, 0.05f, 0.045f, 0.85f), Image.Type.Simple);
            f.groggyFill = AddFilled(Stretch("GroggyFill", f.groggyBar, new Vector2(1f, 1f), new Vector2(-1f, -1f)), art.barFill, GroggyAmber);
            f.groggyFill.fillAmount = 0f;
            f.groggyBar.gameObject.SetActive(false);
        }
        else
        {
            // 2026-10-01 사용자 결정: 정예 마름모 주변 빛은 뺐다(등급은 이름 옆 '엘리트'와 막대 폭으로 구분).
        }

        CenteredImage("Diamond", parent, DiamondCx, DiamondCy, DiamondSize, art.levelFrame, Color.white);
        var levelRect = CenteredRect("LevelText", parent, DiamondCx, DiamondCy - 1f, new Vector2(130f, 90f));
        f.level = AddText(levelRect.gameObject, art, art.softShadow, 46f, boss ? Gold : Cream, TextAlignmentOptions.Center, FontStyles.Normal, "1");
        if (boss) f.fxFront = CenteredImage("FxFront", parent, DiamondCx, DiamondCy, FxSize, null, Color.white);

        var nameRect = TopLeft(boss ? "BossName" : "NameText", parent, BarLeft + 26f, BarTop - 66f, width, 60f, new Vector2(0f, 1f));
        f.name = AddText(nameRect.gameObject, art, art.nameShadow, boss ? 38f : 32f, Cream, TextAlignmentOptions.BottomLeft, FontStyles.Normal, boss ? "BOSS" : "Enemy");
        f.name.characterSpacing = 5f;

        if (boss)
        {
            var phaseRect = TopLeft("Phase", parent, BarLeft, BarTop - 66f, 300f, 60f, new Vector2(0f, 1f));
            f.phase = AddText(phaseRect.gameObject, art, art.softShadow, 20f, Cream, TextAlignmentOptions.BottomLeft, FontStyles.Normal, "PHASE 1 / 1");
            f.phase.gameObject.SetActive(false); // 단계는 보석으로 보인다. RepresentativeBossBuilder가 찾는 이름이라 남겨 둔다.
            // 상태 문구(그로기·포효): RepresentativeBossBuilder.EnsureHud와 같은 규칙(윗줄 가운데 35~65%, 아래 가운데 정렬)으로 둬서
            // 두 빌더가 서로 위치를 바꾸지 않게 한다.
            var phaseRt = (RectTransform)f.phase.transform;
            var stateRect = Rect("BossState", parent, new Vector2(0.35f, 1f), new Vector2(0.65f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, phaseRt.anchoredPosition.y), new Vector2(0f, phaseRt.sizeDelta.y));
            f.state = AddText(stateRect.gameObject, art, art.nameShadow, 24f, StateColor, TextAlignmentOptions.Bottom, FontStyles.Normal, string.Empty);

            f.phaseGems = Rect("PhaseGems", parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(BarLeft + width - 36f, -(BarTop - 44f)), new Vector2(200f, 36f));
            var gem = Rect("GemTemplate", f.phaseGems, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(36f, 36f));
            f.phaseGemTemplate = AddImage(gem.gameObject, art.levelFrame, Color.white, Image.Type.Simple);
            AddImage(Stretch("Lit", gem, Vector2.zero, Vector2.zero), art.phaseGem, Color.white, Image.Type.Simple);
            gem.gameObject.SetActive(false);
        }

        f.statusRow = TopLeft("StatusRow", parent, BarLeft + 26f, BarTop + (boss ? 72f : 70f), StatusSpacing * StatusCells, 46f, new Vector2(0f, 1f));
        f.cells = new EnemyTargetStatusRow.Cell[StatusCells];
        for (int i = 0; i < StatusCells; i++)
        {
            var cellRoot = Rect("Cell " + i, f.statusRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * StatusSpacing, 0f), new Vector2(46f, 46f));
            AddImage(Stretch("Frame", cellRoot, Vector2.zero, Vector2.zero), art.statusCell, Color.white, Image.Type.Simple);
            var icon = AddImage(Stretch("Icon", cellRoot, new Vector2(5f, 5f), new Vector2(-5f, -5f)), art.fire, Color.white, Image.Type.Simple);
            icon.preserveAspect = true;
            var sweep = AddImage(Stretch("Sweep", cellRoot, Vector2.zero, Vector2.zero), art.statusSweep, new Color(0f, 0f, 0f, 0.59f), Image.Type.Filled);
            sweep.fillMethod = Image.FillMethod.Radial360;
            sweep.fillOrigin = (int)Image.Origin360.Top;
            sweep.fillClockwise = true;
            sweep.fillAmount = 0f;
            var stackRect = Rect("Stack", cellRoot, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-1f, -2f), new Vector2(32f, 26f));
            var stack = AddText(stackRect.gameObject, art, art.nameShadow, 20f, Color.white, TextAlignmentOptions.BottomRight, FontStyles.Normal, string.Empty);
            f.cells[i] = new EnemyTargetStatusRow.Cell { root = cellRoot, icon = icon, sweep = sweep, stack = stack };
            cellRoot.gameObject.SetActive(false);
        }

        return f;
    }

    // ---------- 작은 도우미 ----------

    private static RectTransform Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = position;
        rt.sizeDelta = size;
        rt.localScale = Vector3.one;
        return rt;
    }

    // x,y = 부모 왼쪽 위 기준 사각형 왼쪽 위 모서리(시안 좌표, 아래로 +).
    private static RectTransform TopLeft(string name, Transform parent, float x, float y, float w, float h, Vector2 pivot)
    {
        return Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), pivot,
            new Vector2(x + pivot.x * w, -(y + (1f - pivot.y) * h)), new Vector2(w, h));
    }

    private static RectTransform CenteredRect(string name, Transform parent, float cx, float cy, Vector2 size)
    {
        return Rect(name, parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(cx, -cy), size);
    }

    private static Image CenteredImage(string name, Transform parent, float cx, float cy, float size, Sprite sprite, Color color)
    {
        return AddImage(CenteredRect(name, parent, cx, cy, new Vector2(size, size)).gameObject, sprite, color, Image.Type.Simple);
    }

    private static GameObject Stretch(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rt = Rect(name, parent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
        return rt.gameObject;
    }

    private static Image AddImage(GameObject go, Sprite sprite, Color color, Image.Type type)
    {
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.type = type;
        image.raycastTarget = false;
        return image;
    }

    private static Image AddFilled(GameObject go, Sprite sprite, Color color)
    {
        var image = AddImage(go, sprite, color, Image.Type.Filled);
        image.fillMethod = Image.FillMethod.Horizontal;
        image.fillOrigin = (int)Image.OriginHorizontal.Left;
        image.fillAmount = 1f;
        return image;
    }

    private static TextMeshProUGUI AddText(GameObject go, Art art, Material material, float size, Color color,
        TextAlignmentOptions alignment, FontStyles style, string text)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = art.font;
        if (material != null) tmp.fontSharedMaterial = material;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.fontStyle = style;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    private static void Set(SerializedObject so, string property, UnityEngine.Object value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p == null) throw new InvalidOperationException("직렬화 필드가 없다: " + so.targetObject.GetType().Name + "." + property);
        p.objectReferenceValue = value;
    }

    // ---------- 검사 ----------

    private static void Validate(List<string> log, bool target, bool boss)
    {
        if (target) CheckPrefab(HudPrefab, root =>
        {
            var slot = root.GetComponentInChildren<EnemyTargetHpSlotUI>(true);
            RequireRefs(slot, "root", "canvasGroup", "nameText", "levelText", "fillImage", "trailImage", "hpText", "percentText",
                "statusRow", "elementalStatusIcons", "frameRoot", "barRoot", "normalFillSprite", "eliteFillSprite");
        }, log);
        if (boss) CheckPrefab(BossPrefab, root =>
        {
            var view = root.GetComponent<EnemyBossHudView>();
            RequireRefs(view, "canvasGroup", "bossNameText", "phaseText", "healthText", "healthFill", "groggyFill", "stateText",
                "levelText", "percentText", "trailFill", "phaseGemRoot", "phaseGemTemplate", "phaseTickRoot", "phaseTickTemplate",
                "statusRow", "emblemFx");
            var health = root.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == "HealthBar");
            var phase = root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "Phase");
            if (health == null || phase == null || health.parent.Find("GroggyBar") == null || phase.transform.parent.Find("BossState") == null)
                throw new InvalidOperationException("RepresentativeBossBuilder가 찾는 보스 HUD 이름이 빠졌다.");
        }, log);
        if (boss && Resources.Load<Shader>("Shaders/HudBossEmblemFxUI") == null)
            throw new InvalidOperationException("셰이더 Resources/Shaders/HudBossEmblemFxUI를 불러오지 못했다.");
        log.Add("검사 PASS: 직렬화 참조·Missing Script 0" + (boss ? "·셰이더 로드" : string.Empty));
    }

    private static void CheckPrefab(string path, Action<GameObject> check, List<string> log)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int missing = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (missing > 0) throw new InvalidOperationException(path + " Missing Script " + missing);
            check(root);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void RequireRefs(UnityEngine.Object target, params string[] names)
    {
        if (target == null) throw new InvalidOperationException("검사 대상 컴포넌트가 없다.");
        using (var so = new SerializedObject(target))
            foreach (string n in names)
            {
                SerializedProperty p = so.FindProperty(n);
                if (p == null || p.objectReferenceValue == null)
                    throw new InvalidOperationException(target.GetType().Name + "." + n + " 참조가 비었다.");
            }
    }
}
