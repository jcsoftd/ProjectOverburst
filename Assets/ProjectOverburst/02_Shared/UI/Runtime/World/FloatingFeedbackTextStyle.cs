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
        public readonly Material[] Styled = new Material[10]; // DamageNumberMaterialStyle
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

    // 2026-09-30: 피해 숫자 종류별 재질. Mobile SDF 셰이더라 빛 번짐은 오프셋 0의 색 있는 밑그림자로 만든다.
    public static void ApplyStyle(TMP_Text text, Material baseMaterial, DamageNumberMaterialStyle style)
    {
        if (style == DamageNumberMaterialStyle.Normal)
        {
            Apply(text, baseMaterial, false);
            return;
        }
        if (text == null)
            return;
        text.outlineWidth = 0f;
        text.outlineColor = Color.clear;
        Material source = baseMaterial != null ? baseMaterial : text.fontSharedMaterial;
        if (source == null)
            return;
        StyleMaterials materials = EnsureSharedMaterials(source);
        int index = (int)style;
        if (index < 0 || index >= materials.Styled.Length)
        {
            text.fontSharedMaterial = materials.Normal;
            return;
        }
        if (materials.Styled[index] == null)
        {
            materials.Styled[index] = new Material(source)
            {
                name = source.name + "_Damage" + style + "_Shared",
                hideFlags = HideFlags.HideAndDontSave
            };
            ApplyStyledMaterial(materials.Styled[index], style);
        }
        text.fontSharedMaterial = materials.Styled[index];
    }

    private static void ApplyStyledMaterial(Material material, DamageNumberMaterialStyle style)
    {
        // 2026-09-30 v2(카툰풍 지적 뒤): 색 있는 두꺼운 빛 번짐을 없애고, 어두운 판타지 UI처럼
        // 얇고 어두운 외곽선 + 부드러운 검은 그림자로 통일했다. 원소 느낌은 외곽선에 살짝 섞인 색과 글자색으로만 낸다.
        // outline width/color, underlay color, offset, dilate, softness
        float outline; Color outlineColor; Color underlay; Vector2 offset; float dilate; float softness;
        Color shadow = new Color(0f, 0f, 0f, .82f);
        Vector2 drop = new Vector2(.3f, -.42f);
        switch (style)
        {
            case DamageNumberMaterialStyle.Critical:
                outline = .12f; outlineColor = new Color(.2f, .09f, .02f, 1f);
                underlay = shadow; offset = drop; dilate = .22f; softness = .35f; break;
            case DamageNumberMaterialStyle.DamageOverTime:
                outline = .08f; outlineColor = new Color(0f, 0f, 0f, .9f);
                underlay = new Color(0f, 0f, 0f, .7f); offset = drop; dilate = .1f; softness = .3f; break;
            case DamageNumberMaterialStyle.Fire:
                outline = .12f; outlineColor = new Color(.18f, .05f, .02f, 1f);
                underlay = shadow; offset = drop; dilate = .22f; softness = .35f; break;
            case DamageNumberMaterialStyle.Ice:
                outline = .12f; outlineColor = new Color(.04f, .09f, .15f, 1f);
                underlay = shadow; offset = drop; dilate = .22f; softness = .35f; break;
            case DamageNumberMaterialStyle.Electric:
                outline = .12f; outlineColor = new Color(.09f, .05f, .17f, 1f);
                underlay = shadow; offset = drop; dilate = .22f; softness = .35f; break;
            case DamageNumberMaterialStyle.Dark:
                outline = .12f; outlineColor = new Color(.05f, .02f, .08f, 1f);
                underlay = new Color(0f, 0f, 0f, .9f); offset = new Vector2(0f, -.4f); dilate = .38f; softness = .6f; break; // 검은 번짐
            case DamageNumberMaterialStyle.Light:
                outline = .12f; outlineColor = new Color(.17f, .12f, .03f, 1f);
                underlay = shadow; offset = drop; dilate = .22f; softness = .35f; break;
            case DamageNumberMaterialStyle.PlayerHit:
                outline = .14f; outlineColor = new Color(.1f, 0f, 0f, 1f);
                underlay = shadow; offset = drop; dilate = .2f; softness = .3f; break;
            case DamageNumberMaterialStyle.Heal:
                outline = .12f; outlineColor = new Color(.02f, .1f, .04f, 1f);
                underlay = shadow; offset = drop; dilate = .18f; softness = .3f; break;
            default:
                ApplyMaterial(material, false);
                return;
        }
        material.EnableKeyword("UNDERLAY_ON");
        SetFloat(material, ShaderUtilities.ID_OutlineWidth, outline);
        SetColor(material, ShaderUtilities.ID_OutlineColor, outlineColor);
        SetColor(material, ShaderUtilities.ID_UnderlayColor, underlay);
        SetFloat(material, ShaderUtilities.ID_UnderlayOffsetX, offset.x);
        SetFloat(material, ShaderUtilities.ID_UnderlayOffsetY, offset.y);
        SetFloat(material, ShaderUtilities.ID_UnderlayDilate, dilate);
        SetFloat(material, ShaderUtilities.ID_UnderlaySoftness, softness);
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
