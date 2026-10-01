using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class StunFloatingFontBuilder
{
    public const string FontPath = "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/NotoSerifKR_Stun SDF.asset";
    private const string SourcePath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD.ttf";

    [MenuItem("OVERBURST/UI/Build Stun Floating Font")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit mode required.");
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (existing != null)
        {
            if (EditorUtility.IsDirty(existing)) throw new InvalidOperationException("Stun font has unsaved changes.");
            if (!existing.HasCharacters("기절")) throw new InvalidOperationException("Stun font glyphs are missing.");
            return;
        }
        var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
        if (source == null) throw new InvalidOperationException("Noto Serif KR SemiBold source is missing.");
        var font = TMP_FontAsset.CreateFontAsset(source, 64, 9, GlyphRenderMode.SDFAA, 256, 256,
            AtlasPopulationMode.Dynamic, false);
        if (!font.TryAddCharacters("기절", out string missing) || !string.IsNullOrEmpty(missing))
        {
            UnityEngine.Object.DestroyImmediate(font);
            throw new InvalidOperationException("Unable to bake stun glyphs: " + missing);
        }
        font.name = "NotoSerifKR_Stun SDF";
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        font.atlasTexture.name = font.name + " Atlas";
        font.material.name = font.name + " Material";
        AssetDatabase.CreateAsset(font, FontPath);
        AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
        AssetDatabase.AddObjectToAsset(font.material, font);
        EditorUtility.SetDirty(font);
        AssetDatabase.SaveAssetIfDirty(font);
    }
}
