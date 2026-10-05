using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class ParryFloatingFontBuilder
{
    public const string FontPath = "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/NotoSerifKR_Parry SDF.asset";
    public const string Labels = "불완전패링패링완벽패링";
    private const string SourcePath = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD.ttf";

    [MenuItem("OVERBURST/UI/Build Parry Floating Font")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Edit mode required.");
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (existing != null)
        {
            if (EditorUtility.IsDirty(existing)) throw new InvalidOperationException("Parry font has unsaved changes.");
            if (!existing.HasCharacters(Labels)) throw new InvalidOperationException("Parry font glyphs are missing.");
            return;
        }
        var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
        if (source == null) throw new InvalidOperationException("Noto Serif KR SemiBold source is missing.");
        TMP_FontAsset font = null;
        bool created = false;
        bool saved = false;
        try
        {
            font = TMP_FontAsset.CreateFontAsset(source, 64, 9, GlyphRenderMode.SDFAA, 256, 256,
                AtlasPopulationMode.Dynamic, false);
            if (!font.TryAddCharacters(Labels, out string missing) || !string.IsNullOrEmpty(missing))
                throw new InvalidOperationException("Unable to bake parry glyphs: " + missing);
            font.name = "NotoSerifKR_Parry SDF";
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.atlasTexture.name = font.name + " Atlas";
            font.material.name = font.name + " Material";
            AssetDatabase.CreateAsset(font, FontPath);
            created = true;
            AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
            saved = true;
        }
        finally
        {
            if (!saved && font != null)
            {
                if (created) AssetDatabase.DeleteAsset(FontPath);
                else
                {
                    if (font.material != null) UnityEngine.Object.DestroyImmediate(font.material);
                    if (font.atlasTexture != null) UnityEngine.Object.DestroyImmediate(font.atlasTexture);
                    UnityEngine.Object.DestroyImmediate(font);
                }
            }
        }
    }
}
