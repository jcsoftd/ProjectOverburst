using System.IO;
using UnityEditor;
using UnityEngine;

public static class SlotVefectsFlipbookBakeUtility
{
    private const string SingleMenuPath = "OVERBURST/Codex/VFX/Bake Slot Vefects Flipbook/Electric";
    private const string FireMenuPath = "OVERBURST/Codex/VFX/Bake Slot Vefects Flipbook/Fire";
    private const string AllMenuPath = "OVERBURST/Codex/VFX/Bake Slot Vefects Flipbook/All Trail Types";
    private const string OutputFolder = "Assets/ProjectOverburst/Resources/UI/SlotVefects";
    private const int FrameSize = 256;
    private const int Columns = 8;
    private const int Rows = 8;
    private const int FrameCount = 60;
    private const int TrailPointCount = 64;
    private const int BakeLayer = 31;
    private const float TrailWidthPixels = 7f;
    private const float TrailCoreRadiusPixels = 1.25f;
    private const float TrailGlowRadiusPixels = 9.5f;
    private const float TrailLengthPixels = 380f;
    private const int FireTrailPointCount = 56;
    private const float FireCoreRadiusPixels = 1.55f;
    private const float FireGlowRadiusPixels = 12f;
    private const float FireTrailLengthPixels = 300f;
    private const int ThemedTrailPointCount = 58;
    private const float SquarePaddingPixels = 20f;
    private const string BakeAllRequestPath = "Temp/Codex/SlotVefectsBakeAll.request";
    private const string BakeAllRequestLogPath = "Logs/SlotVefectsFlipbookBake_Request.log";

    private static readonly SlotVefectsBakeDefinition[] BakeDefinitions =
    {
        new SlotVefectsBakeDefinition("Electric", SlotVefectsBakeStyle.Electric, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Electric.prefab", OutputFolder + "/SlotVefects_Electric_Flipbook.png"),
        new SlotVefectsBakeDefinition("Fire", SlotVefectsBakeStyle.Fire, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Fire.prefab", OutputFolder + "/SlotVefects_Fire_Flipbook.png"),
        new SlotVefectsBakeDefinition("Ice", SlotVefectsBakeStyle.Ice, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Ice.prefab", OutputFolder + "/SlotVefects_Ice_Flipbook.png"),
        new SlotVefectsBakeDefinition("Water", SlotVefectsBakeStyle.Water, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Water.prefab", OutputFolder + "/SlotVefects_Water_Flipbook.png"),
        new SlotVefectsBakeDefinition("Nature", SlotVefectsBakeStyle.Nature, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Nature.prefab", OutputFolder + "/SlotVefects_Nature_Flipbook.png"),
        new SlotVefectsBakeDefinition("Earth", SlotVefectsBakeStyle.Earth, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Earth.prefab", OutputFolder + "/SlotVefects_Earth_Flipbook.png"),
        new SlotVefectsBakeDefinition("Dark", SlotVefectsBakeStyle.Dark, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Dark.prefab", OutputFolder + "/SlotVefects_Dark_Flipbook.png"),
        new SlotVefectsBakeDefinition("Void", SlotVefectsBakeStyle.Void, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Void.prefab", OutputFolder + "/SlotVefects_Void_Flipbook.png"),
        new SlotVefectsBakeDefinition("Cosmos", SlotVefectsBakeStyle.Cosmos, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Cosmos.prefab", OutputFolder + "/SlotVefects_Cosmos_Flipbook.png"),
        new SlotVefectsBakeDefinition("Sound", SlotVefectsBakeStyle.Sound, "Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/VFX/Particles/VFX_Trail_Sound.prefab", OutputFolder + "/SlotVefects_Sound_Flipbook.png")
    };

    [MenuItem(SingleMenuPath)]
    public static void BakeFromMenu()
    {
        BakeFlipbookAsset();
    }

    [MenuItem(FireMenuPath)]
    public static void BakeFireFromMenu()
    {
        BakeFireFlipbookAsset();
    }

    [MenuItem(AllMenuPath)]
    public static void BakeAllFromMenu()
    {
        BakeAllFlipbookAssets();
    }

    static SlotVefectsFlipbookBakeUtility()
    {
        EditorApplication.delayCall += RunPendingBakeAllRequest;
    }

    private static void RunPendingBakeAllRequest()
    {
        if (Application.isBatchMode)
            return;

        string requestPath = Path.GetFullPath(BakeAllRequestPath);
        if (!File.Exists(requestPath))
            return;

        try
        {
            File.Delete(requestPath);
        }
        catch (IOException)
        {
            return;
        }

        AppendRequestLog("Start BakeAll");
        try
        {
            BakeAllFlipbookAssets();
            AppendRequestLog("Success BakeAll");
        }
        catch (System.Exception exception)
        {
            AppendRequestLog("Failed BakeAll: " + exception.GetType().Name + " " + exception.Message);
            Debug.LogException(exception);
        }
    }

    public static void RunOnceFromCommandLine()
    {
        try
        {
            BakeFlipbookAsset();
            EditorApplication.Exit(0);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void RunFireOnceFromCommandLine()
    {
        try
        {
            BakeFireFlipbookAsset();
            EditorApplication.Exit(0);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void RunAllOnceFromCommandLine()
    {
        try
        {
            BakeAllFlipbookAssets();
            EditorApplication.Exit(0);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void BakeFlipbookAsset()
    {
        BakeFlipbookAsset(BakeDefinitions[0]);
    }

    public static void BakeFireFlipbookAsset()
    {
        BakeFlipbookAsset(BakeDefinitions[1]);
    }

    public static void BakeAllFlipbookAssets()
    {
        int successCount = 0;
        int failureCount = 0;
        string failedLabels = string.Empty;
        for (int i = 0; i < BakeDefinitions.Length; i++)
        {
            try
            {
                BakeFlipbookAsset(BakeDefinitions[i]);
                successCount++;
            }
            catch (System.Exception exception)
            {
                failureCount++;
                failedLabels += (failedLabels.Length == 0 ? string.Empty : ", ") + BakeDefinitions[i].Label;
                Debug.LogError("[SlotVefectsFlipbookBake] failed " + BakeDefinitions[i].Label);
                Debug.LogException(exception);
            }
        }

        if (successCount == 0)
            throw new System.InvalidOperationException("No Slot Vefects flipbook was baked.");
        if (failureCount > 0)
            throw new System.InvalidOperationException("Some Slot Vefects flipbooks failed: " + failedLabels);

        Debug.Log("[SlotVefectsFlipbookBake] baked trail types=" + successCount + "/" + BakeDefinitions.Length);
    }

    private static void BakeFlipbookAsset(SlotVefectsBakeDefinition definition)
    {
        EnsureFolder(OutputFolder);

        Texture2D sheetTexture = null;

        try
        {
            sheetTexture = new Texture2D(FrameSize * Columns, FrameSize * Rows, TextureFormat.RGBA32, false);
            Fill(sheetTexture, Color.clear);

            Rect pathRect = Rect.MinMaxRect(
                SquarePaddingPixels,
                SquarePaddingPixels,
                FrameSize - SquarePaddingPixels,
                FrameSize - SquarePaddingPixels);

            int visiblePixels = 0;
            for (int frame = 0; frame < FrameCount; frame++)
            {
                float progress = frame / (float)FrameCount;
                visiblePixels += PaintFrame(definition, pathRect, progress, sheetTexture, frame);
            }

            sheetTexture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(definition.OutputPath), sheetTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(definition.OutputPath, ImportAssetOptions.ForceUpdate);
            ConfigureTextureImporter(definition.OutputPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[SlotVefectsFlipbookBake] baked " + definition.Label + " " + definition.OutputPath + " visiblePixels=" + visiblePixels);
        }
        finally
        {
            if (sheetTexture != null)
                Object.DestroyImmediate(sheetTexture);
        }
    }

    private static int PaintFrame(SlotVefectsBakeDefinition definition, Rect pathRect, float progress, Texture2D sheetTexture, int frame)
    {
        switch (definition.Style)
        {
            case SlotVefectsBakeStyle.Fire:
                return PaintFireFrame(pathRect, progress, sheetTexture, frame);
            case SlotVefectsBakeStyle.Ice:
            case SlotVefectsBakeStyle.Water:
            case SlotVefectsBakeStyle.Nature:
            case SlotVefectsBakeStyle.Earth:
            case SlotVefectsBakeStyle.Dark:
            case SlotVefectsBakeStyle.Void:
            case SlotVefectsBakeStyle.Cosmos:
            case SlotVefectsBakeStyle.Sound:
                return PaintThemedFrame(definition.Style, pathRect, progress, sheetTexture, frame);
            default:
                return PaintElectricFrame(pathRect, progress, sheetTexture, frame);
        }
    }

    private static int PaintElectricFrame(Rect pathRect, float progress, Texture2D sheetTexture, int frame)
    {
        Color32[] pixels = new Color32[FrameSize * FrameSize];
        float perimeter = (pathRect.width + pathRect.height) * 2f;
        float headDistance = progress * perimeter;
        float startDistance = headDistance - TrailLengthPixels; // 첫 프레임 꼬리 단절 방지
        float distanceRange = TrailLengthPixels;
        int pointCount = Mathf.Max(TrailPointCount, Mathf.CeilToInt(distanceRange / 6.5f) + 1);
        Vector2 previousPoint = Vector2.zero;
        bool hasPreviousPoint = false;

        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            float t = pointCount <= 1 ? 1f : pointIndex / (float)(pointCount - 1);
            float distance = Mathf.Lerp(startDistance, headDistance, t);
            Vector2 point = CreateElectricPathPoint(pathRect, distance, t, frame, pointIndex);

            if (hasPreviousPoint)
                PaintElectricSegment(pixels, previousPoint, point, t, frame, pointIndex);
            else
                PaintSoftDot(pixels, point, TrailCoreRadiusPixels, 0.45f);

            if (pointIndex > 2 && pointIndex < pointCount - 1 && ((pointIndex + frame * 3) % 4) == 0)
            {
                int seed = frame * 977 + pointIndex * 131;
                PaintElectricBranch(pixels, pathRect, distance, point, t, seed);
            }

            previousPoint = point;
            hasPreviousPoint = true;
        }

        Vector2 headPoint = GetSquarePoint(pathRect, headDistance);
        PaintSoftDot(pixels, headPoint, TrailGlowRadiusPixels * 0.62f, 0.2f);
        PaintSoftDot(pixels, headPoint, TrailGlowRadiusPixels * 0.18f, 0.42f);
        PaintSoftDot(pixels, headPoint, TrailCoreRadiusPixels * 0.95f, 1f);
        PaintElectricBranch(pixels, pathRect, headDistance, headPoint, 1f, frame * 1543 + 23);

        return CommitFrame(pixels, sheetTexture, frame);
    }

    private static float Hash01(int value)
    {
        unchecked
        {
            uint hash = (uint)value;
            hash ^= 2747636419u;
            hash *= 2654435769u;
            hash ^= hash >> 16;
            hash *= 2654435769u;
            hash ^= hash >> 16;
            return (hash & 0x00FFFFFF) / 16777215f;
        }
    }

    private static float HashSigned(int value)
    {
        return Hash01(value) * 2f - 1f;
    }

    private static Vector2 CreateElectricPathPoint(Rect pathRect, float distance, float trailT, int frame, int pointIndex)
    {
        Vector2 point = GetSquarePoint(pathRect, distance);
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);

        float jitter = Mathf.Lerp(6.5f, 2.2f, trailT) * HashSigned(frame * 157 + pointIndex * 71);
        float crawl = 1.7f * HashSigned(frame * 269 + pointIndex * 43);
        if (trailT > 0.93f)
        {
            float headBlend = Mathf.InverseLerp(1f, 0.93f, trailT);
            jitter *= headBlend;
            crawl *= headBlend;
        }

        return point + normal * jitter + tangent * crawl;
    }

    private static void PaintElectricSegment(Color32[] pixels, Vector2 from, Vector2 to, float trailT, int frame, int pointIndex)
    {
        float alpha = Mathf.SmoothStep(0.03f, 0.98f, trailT);
        alpha *= Mathf.Lerp(0.78f, 1.14f, Hash01(frame * 211 + pointIndex * 47));

        PaintSoftLine(pixels, from, to, TrailGlowRadiusPixels * 0.66f, alpha * 0.12f);
        PaintSoftLine(pixels, from, to, TrailGlowRadiusPixels * 0.3f, alpha * 0.32f);
        PaintSoftLine(pixels, from, to, TrailCoreRadiusPixels, alpha * 0.98f);

        if (((pointIndex + frame) % 6) == 0)
        {
            Vector2 direction = (to - from).normalized;
            Vector2 normal = GetPathNormal(direction);
            float offset = HashSigned(frame * 331 + pointIndex * 89) * 2.6f;
            PaintSoftLine(pixels, from + normal * offset, to + normal * offset, TrailCoreRadiusPixels * 0.72f, alpha * 0.5f);
        }

        if (((pointIndex + frame * 5) % 5) == 0)
        {
            Vector2 sparkPoint = Vector2.Lerp(from, to, Hash01(frame * 419 + pointIndex * 23));
            Vector2 direction = (to - from).normalized;
            Vector2 normal = GetPathNormal(direction);
            sparkPoint += normal * HashSigned(frame * 557 + pointIndex * 103) * 8.5f;
            PaintSoftDot(pixels, sparkPoint, Mathf.Lerp(1.6f, 2.7f, Hash01(frame * 613 + pointIndex * 29)), alpha * 0.58f);
        }
    }

    private static void PaintElectricBranch(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        float side = Hash01(seed + 3) > 0.42f ? 1f : -1f;
        Vector2 direction = (normal * side + tangent * HashSigned(seed + 17) * 0.55f).normalized;
        float length = Mathf.Lerp(9f, 24f, Hash01(seed + 31)) * Mathf.Lerp(0.65f, 1.2f, trailT);
        Vector2 bend = sourcePoint + direction * length * 0.52f + tangent * HashSigned(seed + 47) * 3.5f;
        Vector2 tip = sourcePoint + direction * length + tangent * HashSigned(seed + 61) * 4f;
        float alpha = Mathf.SmoothStep(0.08f, 1f, trailT) * Mathf.Lerp(0.42f, 0.76f, Hash01(seed + 79));

        PaintSoftLine(pixels, sourcePoint, bend, TrailGlowRadiusPixels * 0.24f, alpha * 0.24f);
        PaintSoftLine(pixels, bend, tip, TrailGlowRadiusPixels * 0.18f, alpha * 0.2f);
        PaintSoftLine(pixels, sourcePoint, bend, TrailCoreRadiusPixels * 0.9f, alpha);
        PaintSoftLine(pixels, bend, tip, TrailCoreRadiusPixels * 0.72f, alpha * 0.84f);
        PaintSoftDot(pixels, tip, 1.55f, alpha * 0.72f);
    }

    private static int PaintFireFrame(Rect pathRect, float progress, Texture2D sheetTexture, int frame)
    {
        Color32[] pixels = new Color32[FrameSize * FrameSize];
        float perimeter = (pathRect.width + pathRect.height) * 2f;
        float headDistance = progress * perimeter;
        float startDistance = headDistance - FireTrailLengthPixels;
        int pointCount = Mathf.Max(FireTrailPointCount, Mathf.CeilToInt(FireTrailLengthPixels / 6.8f) + 1);
        Vector2 previousPoint = Vector2.zero;
        bool hasPreviousPoint = false;

        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            float t = pointCount <= 1 ? 1f : pointIndex / (float)(pointCount - 1);
            float distance = Mathf.Lerp(startDistance, headDistance, t);
            Vector2 point = CreateFirePathPoint(pathRect, distance, t, frame, pointIndex);

            if (hasPreviousPoint)
                PaintFireSegment(pixels, previousPoint, point, t, frame, pointIndex);
            else
                PaintSoftDot(pixels, point, FireCoreRadiusPixels, 0.34f);

            if (pointIndex > 3 && pointIndex < pointCount - 2 && ((pointIndex + frame * 2) % 5) == 0)
            {
                int seed = frame * 827 + pointIndex * 149;
                PaintFireTongue(pixels, pathRect, distance, point, t, seed);
            }

            previousPoint = point;
            hasPreviousPoint = true;
        }

        Vector2 headPoint = GetSquarePoint(pathRect, headDistance);
        PaintSoftDot(pixels, headPoint, FireGlowRadiusPixels * 0.72f, 0.22f);
        PaintSoftDot(pixels, headPoint, FireGlowRadiusPixels * 0.28f, 0.44f);
        PaintSoftDot(pixels, headPoint, FireCoreRadiusPixels * 1.1f, 0.9f);
        PaintFireTongue(pixels, pathRect, headDistance, headPoint, 1f, frame * 1307 + 11);

        return CommitFrame(pixels, sheetTexture, frame);
    }

    private static int PaintThemedFrame(SlotVefectsBakeStyle style, Rect pathRect, float progress, Texture2D sheetTexture, int frame)
    {
        ThemedTrailSettings settings = ResolveThemedTrailSettings(style);
        Color32[] pixels = new Color32[FrameSize * FrameSize];
        float perimeter = (pathRect.width + pathRect.height) * 2f;
        float headDistance = progress * perimeter;
        float startDistance = headDistance - settings.TrailLength;
        int pointCount = Mathf.Max(settings.PointCount, Mathf.CeilToInt(settings.TrailLength / settings.PointSpacing) + 1);
        Vector2 previousPoint = Vector2.zero;
        bool hasPreviousPoint = false;

        for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
        {
            float t = pointCount <= 1 ? 1f : pointIndex / (float)(pointCount - 1);
            float distance = Mathf.Lerp(startDistance, headDistance, t);
            Vector2 point = CreateThemedPathPoint(style, settings, pathRect, distance, t, frame, pointIndex);

            if (hasPreviousPoint)
                PaintThemedSegment(style, settings, pixels, previousPoint, point, t, frame, pointIndex);
            else
                PaintSoftDot(pixels, point, settings.CoreRadius, 0.24f);

            PaintThemedAccent(style, settings, pixels, pathRect, distance, point, t, frame, pointIndex);
            previousPoint = point;
            hasPreviousPoint = true;
        }

        Vector2 headPoint = GetSquarePoint(pathRect, headDistance);
        PaintThemedHead(style, settings, pixels, pathRect, headDistance, headPoint, frame);
        return CommitFrame(pixels, sheetTexture, frame);
    }

    private static ThemedTrailSettings ResolveThemedTrailSettings(SlotVefectsBakeStyle style)
    {
        switch (style)
        {
            case SlotVefectsBakeStyle.Ice:
                return new ThemedTrailSettings(66, 1.1f, 8.2f, 330f, 5.7f, 4.2f, 0.96f);
            case SlotVefectsBakeStyle.Water:
                return new ThemedTrailSettings(60, 1.35f, 10f, 320f, 6.2f, 3.8f, 0.82f);
            case SlotVefectsBakeStyle.Nature:
                return new ThemedTrailSettings(58, 1.3f, 8.8f, 305f, 6.1f, 4.5f, 0.78f);
            case SlotVefectsBakeStyle.Earth:
                return new ThemedTrailSettings(54, 1.65f, 9.4f, 285f, 6.7f, 3.2f, 0.72f);
            case SlotVefectsBakeStyle.Dark:
                return new ThemedTrailSettings(52, 1.2f, 13.8f, 340f, 7.2f, 6.6f, 0.68f);
            case SlotVefectsBakeStyle.Void:
                return new ThemedTrailSettings(62, 1.0f, 7.6f, 350f, 5.8f, 6.1f, 0.86f);
            case SlotVefectsBakeStyle.Cosmos:
                return new ThemedTrailSettings(56, 1.05f, 9.2f, 315f, 6.3f, 5.2f, 0.84f);
            case SlotVefectsBakeStyle.Sound:
                return new ThemedTrailSettings(50, 1.0f, 8.8f, 300f, 7.0f, 2.2f, 0.74f);
            default:
                return new ThemedTrailSettings(ThemedTrailPointCount, 1.25f, 9f, 310f, 6.2f, 4f, 0.8f);
        }
    }

    private static Vector2 CreateThemedPathPoint(SlotVefectsBakeStyle style, ThemedTrailSettings settings, Rect pathRect, float distance, float trailT, int frame, int pointIndex)
    {
        Vector2 point = GetSquarePoint(pathRect, distance);
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        float wave = Mathf.Sin(distance * 0.055f + frame * 0.31f + pointIndex * 0.27f);
        float jitter = (wave * settings.Jitter + HashSigned(frame * 193 + pointIndex * 67) * settings.Jitter * 0.45f) * Mathf.Lerp(1f, 0.25f, trailT);
        float crawl = HashSigned(frame * 251 + pointIndex * 37) * Mathf.Lerp(1.4f, 0.2f, trailT);

        switch (style)
        {
            case SlotVefectsBakeStyle.Ice:
                jitter = Mathf.Round(jitter * 0.6f) / 0.6f;
                crawl *= 0.45f;
                break;
            case SlotVefectsBakeStyle.Water:
                jitter = Mathf.Sin(distance * 0.09f + frame * 0.25f) * settings.Jitter * Mathf.Lerp(0.85f, 0.2f, trailT);
                break;
            case SlotVefectsBakeStyle.Earth:
                jitter *= 0.55f;
                crawl *= 0.28f;
                break;
            case SlotVefectsBakeStyle.Dark:
                jitter *= 1.28f;
                crawl *= 0.8f;
                break;
            case SlotVefectsBakeStyle.Void:
                jitter *= Hash01(frame * 421 + pointIndex * 83) > 0.34f ? 1.1f : -0.45f;
                break;
            case SlotVefectsBakeStyle.Sound:
                jitter *= 0.35f;
                crawl *= 0.2f;
                break;
        }

        if (trailT > 0.93f)
        {
            float headBlend = Mathf.InverseLerp(1f, 0.93f, trailT);
            jitter *= headBlend;
            crawl *= headBlend;
        }

        return point + normal * jitter + tangent * crawl;
    }

    private static void PaintThemedSegment(SlotVefectsBakeStyle style, ThemedTrailSettings settings, Color32[] pixels, Vector2 from, Vector2 to, float trailT, int frame, int pointIndex)
    {
        float alpha = Mathf.SmoothStep(0.025f, 0.97f, trailT) * settings.Alpha;
        alpha *= Mathf.Lerp(0.72f, 1.08f, Hash01(frame * 227 + pointIndex * 61));

        if (style == SlotVefectsBakeStyle.Void && ((pointIndex + frame) % 5) == 0)
            alpha *= 0.2f;
        if (style == SlotVefectsBakeStyle.Sound && ((pointIndex + frame * 2) % 4) != 0)
            alpha *= 0.58f;

        switch (style)
        {
            case SlotVefectsBakeStyle.Water:
                PaintSoftLine(pixels, from, to, settings.GlowRadius * 0.72f, alpha * 0.12f);
                PaintSoftLine(pixels, from, to, settings.GlowRadius * 0.28f, alpha * 0.24f);
                PaintSoftLine(pixels, from, to, settings.CoreRadius, alpha * 0.58f);
                break;
            case SlotVefectsBakeStyle.Dark:
                PaintSoftLine(pixels, from, to, settings.GlowRadius, alpha * 0.09f);
                PaintSoftLine(pixels, from, to, settings.GlowRadius * 0.52f, alpha * 0.16f);
                PaintSoftLine(pixels, from, to, settings.CoreRadius, alpha * 0.32f);
                break;
            case SlotVefectsBakeStyle.Earth:
                PaintSoftLine(pixels, from, to, settings.GlowRadius * 0.45f, alpha * 0.11f);
                PaintSoftLine(pixels, from, to, settings.CoreRadius * 1.22f, alpha * 0.5f);
                break;
            case SlotVefectsBakeStyle.Sound:
                PaintSoftLine(pixels, from, to, settings.CoreRadius * 0.8f, alpha * 0.55f);
                break;
            default:
                PaintSoftLine(pixels, from, to, settings.GlowRadius * 0.58f, alpha * 0.1f);
                PaintSoftLine(pixels, from, to, settings.GlowRadius * 0.26f, alpha * 0.24f);
                PaintSoftLine(pixels, from, to, settings.CoreRadius, alpha * 0.72f);
                break;
        }
    }

    private static void PaintThemedAccent(SlotVefectsBakeStyle style, ThemedTrailSettings settings, Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int frame, int pointIndex)
    {
        int seed = frame * 997 + pointIndex * 137;
        switch (style)
        {
            case SlotVefectsBakeStyle.Ice:
                if ((pointIndex + frame) % 6 == 0)
                    PaintIceShard(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
            case SlotVefectsBakeStyle.Water:
                if ((pointIndex + frame * 2) % 7 == 0)
                    PaintRing(pixels, sourcePoint + GetPathNormal(GetSquareTangent(pathRect, distance)) * HashSigned(seed) * 5.5f, Mathf.Lerp(2.2f, 5.8f, Hash01(seed + 11)), 0.8f, Mathf.SmoothStep(0.06f, 1f, trailT) * 0.22f);
                break;
            case SlotVefectsBakeStyle.Nature:
                if ((pointIndex + frame) % 5 == 0)
                    PaintLeaf(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
            case SlotVefectsBakeStyle.Earth:
                if ((pointIndex + frame * 3) % 4 == 0)
                    PaintEarthChip(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
            case SlotVefectsBakeStyle.Dark:
                if ((pointIndex + frame) % 5 == 0)
                    PaintDarkWisp(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
            case SlotVefectsBakeStyle.Void:
                if ((pointIndex + frame * 3) % 4 == 0)
                    PaintVoidShard(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
            case SlotVefectsBakeStyle.Cosmos:
                if ((pointIndex + frame) % 4 == 0)
                    PaintStarSpark(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
            case SlotVefectsBakeStyle.Sound:
                if ((pointIndex + frame) % 6 == 0)
                    PaintSoundEcho(pixels, pathRect, distance, sourcePoint, trailT, seed, settings);
                break;
        }
    }

    private static void PaintThemedHead(SlotVefectsBakeStyle style, ThemedTrailSettings settings, Color32[] pixels, Rect pathRect, float headDistance, Vector2 headPoint, int frame)
    {
        PaintSoftDot(pixels, headPoint, settings.GlowRadius * 0.52f, 0.18f);
        PaintSoftDot(pixels, headPoint, settings.GlowRadius * 0.22f, 0.34f);
        PaintSoftDot(pixels, headPoint, settings.CoreRadius * 0.95f, 0.86f);

        switch (style)
        {
            case SlotVefectsBakeStyle.Ice:
                PaintIceShard(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 3, settings);
                break;
            case SlotVefectsBakeStyle.Water:
                PaintRing(pixels, headPoint, settings.GlowRadius * 0.38f, 1f, 0.36f);
                break;
            case SlotVefectsBakeStyle.Nature:
                PaintLeaf(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 5, settings);
                break;
            case SlotVefectsBakeStyle.Earth:
                PaintEarthChip(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 7, settings);
                break;
            case SlotVefectsBakeStyle.Dark:
                PaintDarkWisp(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 11, settings);
                break;
            case SlotVefectsBakeStyle.Void:
                PaintVoidShard(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 13, settings);
                break;
            case SlotVefectsBakeStyle.Cosmos:
                PaintStarSpark(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 17, settings);
                break;
            case SlotVefectsBakeStyle.Sound:
                PaintSoundEcho(pixels, pathRect, headDistance, headPoint, 1f, frame * 1511 + 19, settings);
                break;
        }
    }

    private static void PaintIceShard(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent) * (Hash01(seed + 3) > 0.5f ? 1f : -1f);
        float length = Mathf.Lerp(7f, 19f, Hash01(seed + 11)) * Mathf.Lerp(0.5f, 1f, trailT);
        Vector2 tip = sourcePoint + normal * length + tangent * HashSigned(seed + 17) * 3f;
        float alpha = Mathf.SmoothStep(0.08f, 1f, trailT) * 0.62f;

        PaintSoftLine(pixels, sourcePoint, tip, settings.CoreRadius * 0.75f, alpha);
        PaintSoftLine(pixels, sourcePoint + tangent * 2.5f, tip, settings.CoreRadius * 0.5f, alpha * 0.58f);
        PaintSoftLine(pixels, sourcePoint - tangent * 2.5f, tip, settings.CoreRadius * 0.5f, alpha * 0.58f);
    }

    private static void PaintLeaf(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent) * (Hash01(seed + 5) > 0.5f ? 1f : -1f);
        Vector2 center = sourcePoint + normal * Mathf.Lerp(3f, 9f, Hash01(seed + 13));
        float length = Mathf.Lerp(4.5f, 10f, Hash01(seed + 23)) * Mathf.Lerp(0.4f, 1f, trailT);
        float alpha = Mathf.SmoothStep(0.08f, 1f, trailT) * 0.5f;

        PaintSoftLine(pixels, center - tangent * length * 0.45f, center + tangent * length * 0.45f, settings.CoreRadius * 0.7f, alpha);
        PaintSoftLine(pixels, center, center + normal * length * 0.4f, settings.CoreRadius * 0.52f, alpha * 0.75f);
        PaintSoftDot(pixels, center, settings.CoreRadius * 0.95f, alpha * 0.62f);
    }

    private static void PaintEarthChip(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        Vector2 center = sourcePoint + normal * HashSigned(seed + 3) * 5.5f + tangent * HashSigned(seed + 7) * 3f;
        float radius = Mathf.Lerp(1.2f, 3.1f, Hash01(seed + 19));
        float alpha = Mathf.SmoothStep(0.05f, 1f, trailT) * Mathf.Lerp(0.28f, 0.58f, Hash01(seed + 29));

        PaintSoftDot(pixels, center, radius, alpha);
        PaintSoftDot(pixels, center + tangent * radius * 0.9f, radius * 0.58f, alpha * 0.65f);
    }

    private static void PaintDarkWisp(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent) * (Hash01(seed + 7) > 0.45f ? 1f : -1f);
        float length = Mathf.Lerp(9f, 24f, Hash01(seed + 17)) * Mathf.Lerp(0.55f, 1f, trailT);
        Vector2 bend = sourcePoint + normal * length * 0.45f + tangent * HashSigned(seed + 31) * 5f;
        Vector2 tip = sourcePoint + normal * length + tangent * HashSigned(seed + 47) * 8f;
        float alpha = Mathf.SmoothStep(0.08f, 1f, trailT) * 0.34f;

        PaintSoftLine(pixels, sourcePoint, bend, settings.GlowRadius * 0.32f, alpha * 0.5f);
        PaintSoftLine(pixels, bend, tip, settings.GlowRadius * 0.22f, alpha * 0.36f);
        PaintSoftDot(pixels, tip, settings.CoreRadius * 1.3f, alpha * 0.35f);
    }

    private static void PaintVoidShard(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent) * (Hash01(seed + 11) > 0.5f ? 1f : -1f);
        float length = Mathf.Lerp(8f, 21f, Hash01(seed + 19)) * Mathf.Lerp(0.55f, 1f, trailT);
        Vector2 start = sourcePoint + tangent * HashSigned(seed + 23) * 3f;
        Vector2 end = start + (normal * 0.85f + tangent * HashSigned(seed + 31) * 0.45f).normalized * length;
        float alpha = Mathf.SmoothStep(0.1f, 1f, trailT) * 0.68f;

        PaintSoftLine(pixels, start, end, settings.CoreRadius * 0.72f, alpha);
        PaintSoftDot(pixels, end, settings.CoreRadius * 0.9f, alpha * 0.55f);
    }

    private static void PaintStarSpark(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        Vector2 center = sourcePoint + normal * HashSigned(seed + 3) * 8f + tangent * HashSigned(seed + 5) * 5f;
        float radius = Mathf.Lerp(1.2f, 2.8f, Hash01(seed + 13));
        float alpha = Mathf.SmoothStep(0.06f, 1f, trailT) * Mathf.Lerp(0.42f, 0.82f, Hash01(seed + 29));

        PaintSoftDot(pixels, center, radius, alpha);
        PaintSoftLine(pixels, center - tangent * radius * 2.2f, center + tangent * radius * 2.2f, settings.CoreRadius * 0.38f, alpha * 0.58f);
        PaintSoftLine(pixels, center - normal * radius * 2.2f, center + normal * radius * 2.2f, settings.CoreRadius * 0.38f, alpha * 0.58f);
    }

    private static void PaintSoundEcho(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed, ThemedTrailSettings settings)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        float side = Hash01(seed + 3) > 0.5f ? 1f : -1f;
        float alpha = Mathf.SmoothStep(0.08f, 1f, trailT) * 0.36f;

        for (int i = 1; i <= 3; i++)
        {
            float offset = side * i * Mathf.Lerp(3.5f, 5.5f, Hash01(seed + i * 17));
            Vector2 from = sourcePoint - tangent * Mathf.Lerp(4f, 8f, Hash01(seed + i * 23)) + normal * offset;
            Vector2 to = sourcePoint + tangent * Mathf.Lerp(4f, 8f, Hash01(seed + i * 31)) + normal * offset;
            PaintSoftLine(pixels, from, to, settings.CoreRadius * 0.55f, alpha / i);
        }
    }

    private static void PaintRing(Color32[] pixels, Vector2 center, float radius, float width, float alpha)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - width - 1f));
        int maxX = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(center.x + radius + width + 1f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - width - 1f));
        int maxY = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(center.y + radius + width + 1f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - center.x;
                float dy = y + 0.5f - center.y;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                float delta = Mathf.Abs(distance - radius);
                if (delta > width + 1f)
                    continue;

                float coverage = 1f - Mathf.Clamp01((delta - width + 1f) * 0.5f);
                byte sourceAlpha = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * coverage * 255f), 0, 255);
                int index = y * FrameSize + x;
                if (sourceAlpha > pixels[index].a)
                    pixels[index] = new Color32(255, 255, 255, sourceAlpha);
            }
        }
    }

    private static int CommitFrame(Color32[] pixels, Texture2D sheetTexture, int frame)
    {
        int visiblePixels = CountVisiblePixels(pixels);
        int column = frame % Columns;
        int row = frame / Columns;
        int x = column * FrameSize;
        int y = (Rows - 1 - row) * FrameSize;
        sheetTexture.SetPixels32(x, y, FrameSize, FrameSize, pixels);
        return visiblePixels;
    }

    private static Vector2 CreateFirePathPoint(Rect pathRect, float distance, float trailT, int frame, int pointIndex)
    {
        Vector2 point = GetSquarePoint(pathRect, distance);
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        float wave = Mathf.Sin(distance * 0.075f + frame * 0.42f + pointIndex * 0.37f);
        float jitter = (wave * 3.2f + HashSigned(frame * 173 + pointIndex * 59) * 2.1f) * Mathf.Lerp(1f, 0.35f, trailT);
        float crawl = HashSigned(frame * 281 + pointIndex * 41) * 1.3f;
        if (trailT > 0.93f)
        {
            float headBlend = Mathf.InverseLerp(1f, 0.93f, trailT);
            jitter *= headBlend;
            crawl *= headBlend;
        }

        return point + normal * jitter + tangent * crawl;
    }

    private static void PaintFireSegment(Color32[] pixels, Vector2 from, Vector2 to, float trailT, int frame, int pointIndex)
    {
        float alpha = Mathf.SmoothStep(0.02f, 0.95f, trailT);
        alpha *= Mathf.Lerp(0.68f, 1.08f, Hash01(frame * 197 + pointIndex * 53));

        PaintSoftLine(pixels, from, to, FireGlowRadiusPixels * 0.85f, alpha * 0.1f);
        PaintSoftLine(pixels, from, to, FireGlowRadiusPixels * 0.38f, alpha * 0.26f);
        PaintSoftLine(pixels, from, to, FireCoreRadiusPixels, alpha * 0.72f);

        if (((pointIndex + frame) % 7) == 0)
        {
            Vector2 point = Vector2.Lerp(from, to, Hash01(frame * 467 + pointIndex * 31));
            PaintSoftDot(pixels, point, Mathf.Lerp(1.2f, 2.4f, Hash01(frame * 599 + pointIndex * 23)), alpha * 0.38f);
        }
    }

    private static void PaintFireTongue(Color32[] pixels, Rect pathRect, float distance, Vector2 sourcePoint, float trailT, int seed)
    {
        Vector2 tangent = GetSquareTangent(pathRect, distance);
        Vector2 normal = GetPathNormal(tangent);
        Vector2 direction = (normal + tangent * HashSigned(seed + 17) * 0.38f).normalized;
        float length = Mathf.Lerp(6f, 17f, Hash01(seed + 31)) * Mathf.Lerp(0.45f, 1f, trailT);
        Vector2 bend = sourcePoint + direction * length * 0.46f + tangent * HashSigned(seed + 47) * 2.7f;
        Vector2 tip = sourcePoint + direction * length + tangent * HashSigned(seed + 61) * 3.2f;
        float alpha = Mathf.SmoothStep(0.05f, 1f, trailT) * Mathf.Lerp(0.24f, 0.52f, Hash01(seed + 79));

        PaintSoftLine(pixels, sourcePoint, bend, FireGlowRadiusPixels * 0.28f, alpha * 0.25f);
        PaintSoftLine(pixels, bend, tip, FireGlowRadiusPixels * 0.18f, alpha * 0.18f);
        PaintSoftLine(pixels, sourcePoint, bend, FireCoreRadiusPixels * 0.82f, alpha);
        PaintSoftLine(pixels, bend, tip, FireCoreRadiusPixels * 0.56f, alpha * 0.62f);
        PaintSoftDot(pixels, tip, 1.1f, alpha * 0.5f);
    }

    private static void PaintSoftLine(Color32[] pixels, Vector2 from, Vector2 to, float radius, float alpha)
    {
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 1.35f));
        for (int step = 0; step <= steps; step++)
        {
            float t = step / (float)steps;
            Vector2 point = Vector2.Lerp(from, to, t);
            PaintSoftDot(pixels, point, radius, alpha);
        }
    }

    private static void PaintSoftDot(Color32[] pixels, Vector2 center, float radius, float alpha)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1f));
        int maxX = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(center.x + radius + 1f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1f));
        int maxY = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(center.y + radius + 1f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = x + 0.5f - center.x;
                float dy = y + 0.5f - center.y;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                if (distance > radius + 1f)
                    continue;

                float coverage = 1f - Mathf.Clamp01((distance - radius + 1f) * 0.5f);
                byte sourceAlpha = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * coverage * 255f), 0, 255);
                int index = y * FrameSize + x;
                if (sourceAlpha > pixels[index].a)
                    pixels[index] = new Color32(255, 255, 255, sourceAlpha);
            }
        }
    }

    private static void PrepareTrailRenderers(TrailRenderer[] trails)
    {
        Material bakeMaterial = CreateBakeTrailMaterial();
        for (int i = 0; i < trails.Length; i++)
        {
            TrailRenderer trail = trails[i];
            if (trail == null)
                continue;

            trail.enabled = i == 0;
            trail.emitting = false;
            trail.Clear();
            trail.widthMultiplier = TrailWidthPixels;
            trail.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            trail.colorGradient = CreateBakeTrailGradient();
            trail.numCornerVertices = 3;
            trail.numCapVertices = 3;
            trail.textureMode = LineTextureMode.Stretch;
            trail.sharedMaterial = bakeMaterial;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
        }
    }

    private static Material CreateBakeTrailMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        Material material = new Material(shader);
        material.hideFlags = HideFlags.HideAndDontSave;
        material.color = Color.white;
        return material;
    }

    private static Gradient CreateBakeTrailGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.1f, 0f),
                new GradientAlphaKey(0.88f, 0.58f),
                new GradientAlphaKey(0.02f, 1f)
            });
        return gradient;
    }

    private static void ApplyTrailFrame(TrailRenderer[] trails, Rect pathRect, float progress)
    {
        float perimeter = (pathRect.width + pathRect.height) * 2f;
        float headDistance = progress * perimeter;

        for (int trailIndex = 0; trailIndex < trails.Length; trailIndex++)
        {
            TrailRenderer trail = trails[trailIndex];
            if (trail == null)
                continue;

            trail.Clear();
            trail.emitting = true;

            for (int point = TrailPointCount - 1; point >= 0; point--)
            {
                float normalized = point / (float)(TrailPointCount - 1);
                float distance = headDistance - normalized * TrailLengthPixels;
                if (distance < 0f)
                    continue;

                Vector2 pointOnPath = GetSquarePoint(pathRect, distance);
                trail.AddPosition(new Vector3(pointOnPath.x, pointOnPath.y, 0f));
            }

            trail.emitting = false;
        }
    }

    private static int CaptureFrame(Camera camera, RenderTexture renderTexture, Texture2D frameTexture, Texture2D sheetTexture, int frame)
    {
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;
        frameTexture.ReadPixels(new Rect(0f, 0f, FrameSize, FrameSize), 0, 0);
        frameTexture.Apply(false, false);
        RenderTexture.active = previous;

        Color32[] pixels = frameTexture.GetPixels32();
        NormalizePixelsForGradeTint(pixels);
        int visiblePixels = CountVisiblePixels(pixels);
        int column = frame % Columns;
        int row = frame / Columns;
        int x = column * FrameSize;
        int y = (Rows - 1 - row) * FrameSize;
        sheetTexture.SetPixels32(x, y, FrameSize, FrameSize, pixels);
        return visiblePixels;
    }

    private static void NormalizePixelsForGradeTint(Color32[] pixels)
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            byte alpha = pixel.a;
            if (alpha == 0)
                continue;

            byte luminance = (byte)Mathf.Clamp((pixel.r * 54 + pixel.g * 183 + pixel.b * 19) >> 8, 0, 255);
            pixels[i] = new Color32(255, 255, 255, (byte)Mathf.Max(alpha, luminance));
        }
    }

    private static int CountVisiblePixels(Color32[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i].a > 4)
                count++;
        }

        return count;
    }

    private static Vector2 GetSquarePoint(Rect rect, float distance)
    {
        float width = rect.width;
        float height = rect.height;
        float perimeter = (width + height) * 2f;
        distance = Mathf.Repeat(distance, perimeter);

        if (distance < width)
            return new Vector2(rect.xMin + distance, rect.yMax);

        distance -= width;
        if (distance < height)
            return new Vector2(rect.xMax, rect.yMax - distance);

        distance -= height;
        if (distance < width)
            return new Vector2(rect.xMax - distance, rect.yMin);

        distance -= width;
        return new Vector2(rect.xMin, rect.yMin + distance);
    }

    private static Vector2 GetSquareTangent(Rect rect, float distance)
    {
        float width = rect.width;
        float height = rect.height;
        float perimeter = (width + height) * 2f;
        distance = Mathf.Repeat(distance, perimeter);

        if (distance < width)
            return Vector2.right;

        distance -= width;
        if (distance < height)
            return Vector2.down;

        distance -= height;
        if (distance < width)
            return Vector2.left;

        return Vector2.up;
    }

    private static Vector2 GetPathNormal(Vector2 tangent)
    {
        if (tangent.sqrMagnitude < 0.0001f)
            return Vector2.up;

        tangent.Normalize();
        return new Vector2(-tangent.y, tangent.x);
    }

    private static void ConfigureTextureImporter(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static void DisableAudio(GameObject instance)
    {
        AudioSource[] audioSources = instance.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < audioSources.Length; i++)
        {
            audioSources[i].Stop();
            audioSources[i].playOnAwake = false;
            audioSources[i].enabled = false;
        }
    }

    private static void Fill(Texture2D texture, Color color)
    {
        Color[] pixels = new Color[texture.width * texture.height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = color;

        texture.SetPixels(pixels);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);

            current = next;
        }
    }

    private static void AppendRequestLog(string message)
    {
        string logPath = Path.GetFullPath(BakeAllRequestLogPath);
        string directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.AppendAllText(logPath, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + System.Environment.NewLine);
    }

    private static void SetLayerRecursively(Transform target, int layer)
    {
        if (target == null)
            return;

        target.gameObject.layer = layer;
        for (int i = 0; i < target.childCount; i++)
            SetLayerRecursively(target.GetChild(i), layer);
    }

    private static void SetHideFlagsRecursive(Transform target, HideFlags hideFlags)
    {
        if (target == null)
            return;

        target.gameObject.hideFlags = hideFlags;
        for (int i = 0; i < target.childCount; i++)
            SetHideFlagsRecursive(target.GetChild(i), hideFlags);
    }

    private enum SlotVefectsBakeStyle
    {
        Electric,
        Fire,
        Ice,
        Water,
        Nature,
        Earth,
        Dark,
        Void,
        Cosmos,
        Sound
    }

    private readonly struct ThemedTrailSettings
    {
        public readonly int PointCount;
        public readonly float CoreRadius;
        public readonly float GlowRadius;
        public readonly float TrailLength;
        public readonly float PointSpacing;
        public readonly float Jitter;
        public readonly float Alpha;

        public ThemedTrailSettings(int pointCount, float coreRadius, float glowRadius, float trailLength, float pointSpacing, float jitter, float alpha)
        {
            PointCount = pointCount;
            CoreRadius = coreRadius;
            GlowRadius = glowRadius;
            TrailLength = trailLength;
            PointSpacing = pointSpacing;
            Jitter = jitter;
            Alpha = alpha;
        }
    }

    private readonly struct SlotVefectsBakeDefinition
    {
        public readonly string Label;
        public readonly SlotVefectsBakeStyle Style;
        public readonly string PrefabPath;
        public readonly string OutputPath;

        public SlotVefectsBakeDefinition(string label, SlotVefectsBakeStyle style, string prefabPath, string outputPath)
        {
            Label = label;
            Style = style;
            PrefabPath = prefabPath;
            OutputPath = outputPath;
        }
    }
}
