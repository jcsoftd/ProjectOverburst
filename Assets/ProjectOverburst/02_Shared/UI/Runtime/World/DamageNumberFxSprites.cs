using UnityEngine;

// 2026-09-30: 피해 숫자 장식용 작은 스프라이트를 코드로 한 번 만들어 쓴다(에셋 추가 없음, 되돌릴 때 파일만 지우면 된다).
public static class DamageNumberFxSprites
{
    private static Sprite dot, rays, streak, bolt, crack;

    public static Sprite Dot => dot != null ? dot : dot = Build("DamageFx_Dot", 32, 32, DotAlpha);
    public static Sprite Rays => rays != null ? rays : rays = Build("DamageFx_Rays", 128, 128, RaysAlpha);
    public static Sprite Streak => streak != null ? streak : streak = Build("DamageFx_Streak", 8, 64, StreakAlpha);
    public static Sprite Bolt => bolt != null ? bolt : bolt = Build("DamageFx_Bolt", 32, 64, BoltAlpha);
    public static Sprite Crack => crack != null ? crack : crack = Build("DamageFx_Crack", 6, 64, CrackAlpha);

    private delegate float AlphaAt(float u, float v);

    private static Sprite Build(string name, int width, int height, AlphaAt alpha)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float a = Mathf.Clamp01(alpha((x + .5f) / width, (y + .5f) / height));
                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(.5f, .5f), 100f);
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    // Soft ember: bright core with a quick falloff.
    private static float DotAlpha(float u, float v)
    {
        float r = Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f)) * 2f;
        float falloff = 1f - Mathf.Clamp01(r);
        return falloff * falloff * (1f + 1.5f * Mathf.Clamp01(1f - r * 2.5f));
    }

    // Eight long and eight short rays with a hollow middle so the digits stay readable.
    private static float RaysAlpha(float u, float v)
    {
        float dx = u - .5f, dy = v - .5f;
        float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
        if (r >= 1f) return 0f;
        float angle = Mathf.Atan2(dy, dx);
        float longRays = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4f)), 28f);
        float shortRays = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4f + Mathf.PI * .25f)), 36f) * Mathf.Clamp01(1f - r * 1.6f);
        float hollow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.2f, .36f, r));
        float outer = Mathf.Pow(1f - r, 1.4f);
        float halo = Mathf.Clamp01(1f - Mathf.Abs(r - .34f) * 9f) * .35f;
        return (Mathf.Max(longRays, shortRays) * outer + halo) * hollow;
    }

    // Drip: opaque top fading to nothing at the bottom, soft sides.
    private static float StreakAlpha(float u, float v)
    {
        float side = 1f - Mathf.Clamp01(Mathf.Abs(u - .5f) * 2f);
        return side * side * Mathf.Pow(v, 1.6f);
    }

    // Zigzag lightning bolt.
    private static readonly Vector2[] BoltPoints =
    {
        new Vector2(.62f, .98f), new Vector2(.3f, .62f), new Vector2(.62f, .56f),
        new Vector2(.34f, .24f), new Vector2(.5f, .22f), new Vector2(.28f, .02f)
    };

    private static float BoltAlpha(float u, float v)
    {
        var p = new Vector2(u, v * 2f);
        float best = float.MaxValue;
        for (int i = 0; i < BoltPoints.Length - 1; i++)
        {
            Vector2 a = new Vector2(BoltPoints[i].x, BoltPoints[i].y * 2f), b = new Vector2(BoltPoints[i + 1].x, BoltPoints[i + 1].y * 2f);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return Mathf.Clamp01(1f - best / .09f) + Mathf.Clamp01(1f - best / .22f) * .35f;
    }

    // Crack line: thin, bright in the middle, fading at both ends.
    private static float CrackAlpha(float u, float v)
    {
        float side = 1f - Mathf.Clamp01(Mathf.Abs(u - .5f) * 2f);
        float ends = Mathf.Clamp01(1f - Mathf.Abs(v - .5f) * 2f);
        return side * Mathf.Sqrt(ends);
    }
}
