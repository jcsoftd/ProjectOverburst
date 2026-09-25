using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static class FloatingFeedbackTextStyle
{
    private static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.8f);
    private sealed class StyleMaterials
    {
        public Material Normal;
        public Material Reaction;
    }

    // Several font families can be visible at once while the player cycles debug choices.
    private static readonly Dictionary<Material, StyleMaterials> MaterialsBySource =
        new Dictionary<Material, StyleMaterials>();

    public static void Apply(TMP_Text text, Material baseMaterial)
    {
        Apply(text, baseMaterial, false);
    }

    public static void ApplyReaction(TMP_Text text, Material baseMaterial)
    {
        Apply(text, baseMaterial, true);
    }

    private static void Apply(TMP_Text text, Material baseMaterial, bool isReaction)
    {
        if (text == null)
            return;

        text.outlineWidth = 0f;
        text.outlineColor = Color.clear;

        Material source = baseMaterial != null ? baseMaterial : text.fontSharedMaterial;
        if (source == null)
            return;

        StyleMaterials materials = EnsureSharedMaterials(source);
        text.fontSharedMaterial = isReaction ? materials.Reaction : materials.Normal;
    }

    private static StyleMaterials EnsureSharedMaterials(Material source)
    {
        if (MaterialsBySource.TryGetValue(source, out StyleMaterials existing)
            && existing.Normal != null && existing.Reaction != null)
            return existing;

        var materials = new StyleMaterials();
        materials.Normal = new Material(source)
        {
            name = source.name + "_FloatingShadow_Shared",
            hideFlags = HideFlags.HideAndDontSave
        };
        materials.Reaction = new Material(source)
        {
            name = source.name + "_ReactionShadow_Shared",
            hideFlags = HideFlags.HideAndDontSave
        };
        ApplyMaterial(materials.Normal, false);
        ApplyMaterial(materials.Reaction, true);
        MaterialsBySource[source] = materials;
        return materials;
    }

    private static void ApplyMaterial(Material material, bool isReaction)
    {
        if (material == null)
            return;

        material.EnableKeyword("UNDERLAY_ON");
        SetFloat(material, ShaderUtilities.ID_OutlineWidth, 0f);
        SetColor(material, ShaderUtilities.ID_OutlineColor, Color.clear);
        Color shadowColor = isReaction ? new Color(0f, 0f, 0f, 0.92f) : ShadowColor;
        SetColor(material, ShaderUtilities.ID_UnderlayColor, shadowColor);
        SetFloat(material, ShaderUtilities.ID_UnderlayOffsetX, 0.44f);
        SetFloat(material, ShaderUtilities.ID_UnderlayOffsetY, -0.44f);
        SetFloat(material, ShaderUtilities.ID_UnderlayDilate, isReaction ? 0.2f : 0.14f);
        SetFloat(material, ShaderUtilities.ID_UnderlaySoftness, isReaction ? 0.14f : 0.2f);
    }

    private static void SetFloat(Material material, int id, float value)
    {
        if (material.HasProperty(id))
            material.SetFloat(id, value);
    }

    private static void SetColor(Material material, int id, Color value)
    {
        if (material.HasProperty(id))
            material.SetColor(id, value);
    }
}
