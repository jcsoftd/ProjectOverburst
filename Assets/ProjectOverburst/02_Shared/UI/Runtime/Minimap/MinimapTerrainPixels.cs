using System;
using UnityEngine;

public static class MinimapTerrainPixels
{
    public const byte Empty = 0, Ground = 1, Obstacle = 2;
    public static readonly Color32 GroundColor = new Color32(66, 65, 58, 225);
    public static readonly Color32 EdgeColor = new Color32(139, 130, 108, 235);
    public static readonly Color32 ObstacleColor = new Color32(26, 28, 29, 245);

    // Preserve the sampled topology: smoothing must not close a narrow passage or fill a hole.
    public static Color32[] Build(byte[] cells, int width, int height)
    {
        if (cells == null || width < 1 || height < 1 || (long)width * height != cells.Length)
            throw new ArgumentException("Minimap terrain dimensions do not match the cells.");
        var pixels = new Color32[cells.Length];
        for (int z = 0; z < height; z++)
        for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            byte kind = cells[index];
            if (kind == Empty) continue;
            if (kind == Obstacle) { pixels[index] = ObstacleColor; continue; }
            if (kind != Ground) throw new ArgumentException("Unknown minimap terrain cell.");
            bool edge = x == 0 || z == 0 || x == width - 1 || z == height - 1
                || cells[index - 1] != Ground || cells[index + 1] != Ground
                || cells[index - width] != Ground || cells[index + width] != Ground;
            pixels[index] = edge ? EdgeColor : GroundColor;
        }
        return pixels;
    }

    public static Texture2D Create(byte[] cells, int width, int height)
    {
        Color32[] pixels = Build(cells, width, height);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Minimap terrain (session)",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        try
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }
        catch
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
            throw;
        }
    }
}
