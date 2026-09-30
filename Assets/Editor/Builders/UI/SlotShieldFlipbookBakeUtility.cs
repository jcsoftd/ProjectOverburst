using System.IO;
using UnityEditor;
using UnityEngine;

public static class SlotShieldFlipbookBakeUtility
{
    private const string AllMenuPath = "OVERBURST/Codex/VFX/Bake Slot Shield Flipbook/All Shield Types";
    private const string BarrierMenuPath = "OVERBURST/Codex/VFX/Bake Slot Shield Flipbook/Barrier";
    private const string HexMenuPath = "OVERBURST/Codex/VFX/Bake Slot Shield Flipbook/Hex";
    private const string PulseMenuPath = "OVERBURST/Codex/VFX/Bake Slot Shield Flipbook/Pulse";
    private const string RuneMenuPath = "OVERBURST/Codex/VFX/Bake Slot Shield Flipbook/Rune";
    private const string PrismMenuPath = "OVERBURST/Codex/VFX/Bake Slot Shield Flipbook/Prism";
    private const string OutputFolder = "Assets/ProjectOverburst/Resources/UI/SlotShield";
    private const int FrameSize = 256;
    private const int Columns = 8;
    private const int Rows = 8;
    private const int FrameCount = 60;
    private const float SquarePaddingPixels = 18f;
    private const float ShieldFresnelWidthPixels = 18f;

    private static readonly SlotShieldBakeDefinition[] BakeDefinitions =
    {
        new SlotShieldBakeDefinition("Barrier", SlotShieldBakeStyle.Barrier, OutputFolder + "/SlotShield_Flipbook.png"),
        new SlotShieldBakeDefinition("Hex", SlotShieldBakeStyle.Hex, OutputFolder + "/SlotShield_Hex_Flipbook.png"),
        new SlotShieldBakeDefinition("Pulse", SlotShieldBakeStyle.Pulse, OutputFolder + "/SlotShield_Pulse_Flipbook.png"),
        new SlotShieldBakeDefinition("Rune", SlotShieldBakeStyle.Rune, OutputFolder + "/SlotShield_Rune_Flipbook.png"),
        new SlotShieldBakeDefinition("Prism", SlotShieldBakeStyle.Prism, OutputFolder + "/SlotShield_Prism_Flipbook.png")
    };

    [MenuItem(AllMenuPath)]
    public static void BakeAllFromMenu()
    {
        BakeAllFlipbookAssets();
    }

    [MenuItem(BarrierMenuPath)]
    public static void BakeBarrierFromMenu()
    {
        BakeFlipbookAsset();
    }

    [MenuItem(HexMenuPath)]
    public static void BakeHexFromMenu()
    {
        BakeDefinition(BakeDefinitions[1]);
    }

    [MenuItem(PulseMenuPath)]
    public static void BakePulseFromMenu()
    {
        BakeDefinition(BakeDefinitions[2]);
    }

    [MenuItem(RuneMenuPath)]
    public static void BakeRuneFromMenu()
    {
        BakeDefinition(BakeDefinitions[3]);
    }

    [MenuItem(PrismMenuPath)]
    public static void BakePrismFromMenu()
    {
        BakeDefinition(BakeDefinitions[4]);
    }

    public static void RunOnceFromCommandLine()
    {
        RunAllOnceFromCommandLine();
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
        BakeDefinition(BakeDefinitions[0]);
    }

    public static void BakeAllFlipbookAssets()
    {
        for (int i = 0; i < BakeDefinitions.Length; i++)
            BakeDefinition(BakeDefinitions[i]);
    }

    private static void BakeDefinition(SlotShieldBakeDefinition definition)
    {
        EnsureFolder(OutputFolder);
        Texture2D sheetTexture = null;

        try
        {
            sheetTexture = new Texture2D(FrameSize * Columns, FrameSize * Rows, TextureFormat.RGBA32, false);
            Fill(sheetTexture, Color.clear);

            Rect rect = Rect.MinMaxRect(
                SquarePaddingPixels,
                SquarePaddingPixels,
                FrameSize - SquarePaddingPixels,
                FrameSize - SquarePaddingPixels);

            int visiblePixels = 0;
            for (int frame = 0; frame < FrameCount; frame++)
            {
                float progress = frame / (float)FrameCount;
                visiblePixels += PaintFrame(definition.Style, rect, progress, sheetTexture, frame);
            }

            sheetTexture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(definition.OutputPath), sheetTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(definition.OutputPath, ImportAssetOptions.ForceUpdate);
            ConfigureTextureImporter(definition.OutputPath);
            AssetDatabase.SaveAssets();

            Debug.Log("[SlotShieldFlipbookBake] baked " + definition.Label + " " + definition.OutputPath + " visiblePixels=" + visiblePixels);
        }
        finally
        {
            if (sheetTexture != null)
                Object.DestroyImmediate(sheetTexture);
        }
    }

    private static int PaintFrame(SlotShieldBakeStyle style, Rect rect, float progress, Texture2D sheetTexture, int frame)
    {
        Color32[] pixels = new Color32[FrameSize * FrameSize];

        switch (style)
        {
            case SlotShieldBakeStyle.Hex:
                PaintFresnelField(pixels, rect, progress, frame, 0.18f, 0.18f, 0.18f);
                PaintHexGrid(pixels, rect, progress, frame);
                PaintCornerNodes(pixels, rect, progress, frame, 0.85f);
                break;
            case SlotShieldBakeStyle.Pulse:
                PaintFresnelField(pixels, rect, progress, frame, 0.16f, 0.1f, 0.26f);
                PaintPulseBands(pixels, rect, progress, frame);
                PaintCornerNodes(pixels, rect, progress, frame, 1.05f);
                break;
            case SlotShieldBakeStyle.Rune:
                PaintFresnelField(pixels, rect, progress, frame, 0.12f, 0.14f, 0.2f);
                PaintRuneMarks(pixels, rect, progress, frame);
                PaintCornerNodes(pixels, rect, progress, frame, 0.95f);
                break;
            case SlotShieldBakeStyle.Prism:
                PaintFresnelField(pixels, rect, progress, frame, 0.14f, 0.12f, 0.18f);
                PaintPrismShards(pixels, rect, progress, frame);
                PaintCornerNodes(pixels, rect, progress, frame, 0.8f);
                break;
            default:
                PaintFresnelField(pixels, rect, progress, frame, 0.28f, 0.22f, 0.32f);
                PaintCornerNodes(pixels, rect, progress, frame, 1f);
                break;
        }

        return CommitFrame(pixels, sheetTexture, frame);
    }

    private static void PaintFresnelField(Color32[] pixels, Rect rect, float progress, int frame, float baseAlpha, float scanStrength, float gridStrength)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(rect.xMin - ShieldFresnelWidthPixels));
        int maxX = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(rect.xMax + ShieldFresnelWidthPixels));
        int minY = Mathf.Max(0, Mathf.FloorToInt(rect.yMin - ShieldFresnelWidthPixels));
        int maxY = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(rect.yMax + ShieldFresnelWidthPixels));
        float scan = progress * 80f;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float fresnel = ResolveEdgeMask(rect, px, py, ShieldFresnelWidthPixels);
                if (fresnel <= 0f)
                    continue;

                fresnel = Mathf.Pow(fresnel, 1.45f);
                float gridX = LinePattern(px * 0.31f + py * 0.08f + scan, 12f, 1.1f);
                float gridY = LinePattern(py * 0.31f - px * 0.08f - scan * 0.55f, 12f, 1.1f);
                float scanLine = LinePattern(py + frame * 2.2f, 22f, 1.8f);
                float noise = Hash01(frame * 1031 + x * 37 + y * 73);
                float alpha = fresnel * baseAlpha;
                alpha += fresnel * Mathf.Max(gridX, gridY) * gridStrength;
                alpha += fresnel * scanLine * scanStrength;
                alpha *= Mathf.Lerp(0.78f, 1.18f, noise);

                SetMaxAlpha(pixels, x, y, alpha);
            }
        }
    }

    private static void PaintHexGrid(Color32[] pixels, Rect rect, float progress, int frame)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(rect.xMin - 26f));
        int maxX = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(rect.xMax + 26f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(rect.yMin - 26f));
        int maxY = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(rect.yMax + 26f));
        float scroll = progress * 24f;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float edge = ResolveEdgeMask(rect, px, py, 32f);
                if (edge <= 0f)
                    continue;

                float a = LinePattern(px * 0.23f + py * 0.13f + scroll, 16f, 0.95f);
                float b = LinePattern(px * 0.23f - py * 0.13f - scroll * 0.72f, 16f, 0.95f);
                float c = LinePattern(py * 0.27f + frame * 0.18f, 16f, 0.95f);
                float alpha = Mathf.Pow(edge, 1.15f) * Mathf.Max(Mathf.Max(a, b), c) * 0.36f;
                SetMaxAlpha(pixels, x, y, alpha);
            }
        }
    }

    private static void PaintPulseBands(Color32[] pixels, Rect rect, float progress, int frame)
    {
        int minX = Mathf.Max(0, Mathf.FloorToInt(rect.xMin - 34f));
        int maxX = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(rect.xMax + 34f));
        int minY = Mathf.Max(0, Mathf.FloorToInt(rect.yMin - 34f));
        int maxY = Mathf.Min(FrameSize - 1, Mathf.CeilToInt(rect.yMax + 34f));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float edgeDistance = ResolveEdgeDistance(rect, px, py);
                if (edgeDistance > 40f)
                    continue;

                float band = LinePattern(edgeDistance * 1.22f - progress * 34f, 20f, 1.8f);
                float shimmer = LinePattern(px * 0.12f + py * 0.08f + frame * 0.35f, 28f, 2.2f);
                float fade = 1f - Mathf.Clamp01(edgeDistance / 40f);
                float alpha = Mathf.Pow(fade, 1.1f) * (band * 0.42f + shimmer * 0.12f);
                SetMaxAlpha(pixels, x, y, alpha);
            }
        }
    }

    private static void PaintRuneMarks(Color32[] pixels, Rect rect, float progress, int frame)
    {
        float perimeter = (rect.width + rect.height) * 2f;
        int markCount = 32;

        for (int i = 0; i < markCount; i++)
        {
            float seed = Hash01(i * 907 + 23);
            float distance = perimeter * (i / (float)markCount) + progress * perimeter * 0.18f + seed * 10f;
            Vector2 point = GetSquarePoint(rect, distance);
            Vector2 tangent = GetSquareTangent(rect, distance);
            Vector2 normal = GetPathNormal(tangent);
            point += normal * Mathf.Lerp(-7f, 11f, Hash01(i * 613 + frame * 3));

            float pulse = Mathf.Sin(progress * Mathf.PI * 2f * 1.5f + i * 0.73f) * 0.5f + 0.5f;
            float alpha = Mathf.Lerp(0.16f, 0.46f, pulse);
            PaintSoftLine(pixels, point - tangent * 4.5f, point + tangent * 4.5f, 1.15f, alpha);

            if (i % 3 == 0)
                PaintSoftLine(pixels, point - normal * 3f, point + normal * 3f, 0.95f, alpha * 0.82f);
            if (i % 5 == 0)
                PaintRing(pixels, point, Mathf.Lerp(3.2f, 5.6f, pulse), 0.7f, alpha * 0.85f);
        }
    }

    private static void PaintPrismShards(Color32[] pixels, Rect rect, float progress, int frame)
    {
        float perimeter = (rect.width + rect.height) * 2f;
        int shardCount = 38;

        for (int i = 0; i < shardCount; i++)
        {
            float seed = Hash01(i * 1291 + 17);
            float distance = progress * perimeter * 0.5f + i * perimeter / shardCount + seed * 18f;
            Vector2 point = GetSquarePoint(rect, distance);
            Vector2 tangent = GetSquareTangent(rect, distance);
            Vector2 normal = GetPathNormal(tangent);
            point += normal * Mathf.Lerp(-14f, 15f, Hash01(i * 397 + frame * 11));

            Vector2 direction = tangent * Mathf.Lerp(0.35f, 1f, seed) + normal * Mathf.Lerp(-1.15f, 1.15f, Hash01(i * 541 + 9));
            if (direction.sqrMagnitude < 0.001f)
                direction = tangent;
            direction.Normalize();

            float length = Mathf.Lerp(9f, 24f, Hash01(i * 733 + 3));
            float pulse = Mathf.Sin(progress * Mathf.PI * 2f * 2f + i * 0.41f) * 0.5f + 0.5f;
            float alpha = Mathf.Lerp(0.1f, 0.42f, pulse);
            PaintSoftLine(pixels, point - direction * length * 0.5f, point + direction * length * 0.5f, 3.4f, alpha * 0.32f);
            PaintSoftLine(pixels, point - direction * length * 0.5f, point + direction * length * 0.5f, 1.05f, alpha);

            if ((i + frame) % 9 == 0)
                PaintSoftDot(pixels, point, 4.8f, alpha * 0.7f);
        }
    }

    private static void PaintCornerNodes(Color32[] pixels, Rect rect, float progress, int frame, float alphaScale)
    {
        Vector2[] corners =
        {
            new Vector2(rect.xMin, rect.yMin),
            new Vector2(rect.xMax, rect.yMin),
            new Vector2(rect.xMax, rect.yMax),
            new Vector2(rect.xMin, rect.yMax)
        };

        for (int i = 0; i < corners.Length; i++)
        {
            float pulse = Mathf.Sin(progress * Mathf.PI * 2f + i * 1.57f + frame * 0.05f) * 0.5f + 0.5f;
            PaintSoftDot(pixels, corners[i], Mathf.Lerp(6f, 9.5f, pulse), Mathf.Lerp(0.12f, 0.24f, pulse) * alphaScale);
            PaintRing(pixels, corners[i], Mathf.Lerp(5f, 8f, pulse), 0.9f, Mathf.Lerp(0.2f, 0.42f, pulse) * alphaScale);
        }
    }

    private static void PaintSoftLine(Color32[] pixels, Vector2 from, Vector2 to, float radius, float alpha)
    {
        float length = Vector2.Distance(from, to);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 1.35f));
        for (int step = 0; step <= steps; step++)
            PaintSoftDot(pixels, Vector2.Lerp(from, to, step / (float)steps), radius, alpha);
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
                SetMaxAlpha(pixels, x, y, alpha * coverage);
            }
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
                SetMaxAlpha(pixels, x, y, alpha * coverage);
            }
        }
    }

    private static void SetMaxAlpha(Color32[] pixels, int x, int y, float alpha)
    {
        byte sourceAlpha = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
        int index = y * FrameSize + x;
        if (sourceAlpha > pixels[index].a)
            pixels[index] = new Color32(255, 255, 255, sourceAlpha);
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

    private static float ResolveEdgeMask(Rect rect, float px, float py, float width)
    {
        float edgeDistance = ResolveEdgeDistance(rect, px, py);
        if (edgeDistance > width)
            return 0f;

        return 1f - Mathf.Clamp01(edgeDistance / width);
    }

    private static float ResolveEdgeDistance(Rect rect, float px, float py)
    {
        float outsideX = Mathf.Max(Mathf.Max(rect.xMin - px, 0f), px - rect.xMax);
        float outsideY = Mathf.Max(Mathf.Max(rect.yMin - py, 0f), py - rect.yMax);
        float outsideDistance = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);
        if (outsideDistance > 0f)
            return outsideDistance;

        return Mathf.Min(
            Mathf.Abs(px - rect.xMin),
            Mathf.Abs(px - rect.xMax),
            Mathf.Abs(py - rect.yMin),
            Mathf.Abs(py - rect.yMax));
    }

    private static float LinePattern(float value, float period, float width)
    {
        float half = period * 0.5f;
        float delta = Mathf.Abs(Mathf.Repeat(value, period) - half);
        return 1f - Mathf.Clamp01(delta / width);
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

    private readonly struct SlotShieldBakeDefinition
    {
        public readonly string Label;
        public readonly SlotShieldBakeStyle Style;
        public readonly string OutputPath;

        public SlotShieldBakeDefinition(string label, SlotShieldBakeStyle style, string outputPath)
        {
            Label = label;
            Style = style;
            OutputPath = outputPath;
        }
    }

    private enum SlotShieldBakeStyle
    {
        Barrier,
        Hex,
        Pulse,
        Rune,
        Prism
    }
}
