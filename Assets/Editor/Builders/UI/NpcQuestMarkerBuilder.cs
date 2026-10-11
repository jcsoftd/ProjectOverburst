using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using Overburst.Quests;

public static class NpcQuestMarkerBuilder
{
    public const string FontPath = "Assets/ProjectOverburst/Resources/UI/Fonts/NpcNames/QuestMarks SDF.asset";
    const string SourcePath = "Assets/ThirdParty/RPG and MMO UI 11/Fonts/CambriaMath.ttf";

    [MenuItem("OVERBURST/UI/NPC Names And Stylist/Apply Quest Markers")]
    public static void Apply()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Preserve the open prefab stage.");
        NpcPresentationBuilder.EnsureAssets();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (!font)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>(SourcePath);
            if (!source) throw new InvalidOperationException("Restore the existing RPG UI11 Cambria Math font first.");
            font = TMP_FontAsset.CreateFontAsset(source, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic);
            font.name = "QuestMarks SDF";
            if (!font.TryAddCharacters("!?", out string missing)) throw new InvalidOperationException("Quest glyph missing: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.material.shader = Shader.Find("TextMeshPro/Distance Field Overlay");
            if (!font.material.shader) throw new InvalidOperationException("TMP overlay shader missing");
            AssetDatabase.CreateAsset(font, FontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.SaveAssetIfDirty(font);
        }
        if (!font.HasCharacters("!?")) throw new InvalidOperationException("Quest mark font is incomplete.");
        font.material.SetFloat("_FaceDilate", .15f);
        font.material.SetFloat("_OutlineWidth", .16f);
        font.material.SetColor("_OutlineColor", new Color(0, 0, 0, .92f));
        font.material.EnableKeyword("OUTLINE_ON");
        EditorUtility.SetDirty(font.material); AssetDatabase.SaveAssetIfDirty(font.material);
        var root = PrefabUtility.LoadPrefabContents(NpcPresentationBuilder.PrefabPath);
        try
        {
            var existing = root.transform.Find("Quest Mark");
            var child = existing ? existing.gameObject : new GameObject("Quest Mark", typeof(TextMeshPro));
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(0, .64f, 0);
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            var text = child.GetComponent<TextMeshPro>();
            text.font = font; text.fontSharedMaterial = font.material; text.text = "!";
            text.fontSize = 6.4f; text.color = new Color32(211, 166, 102, 255);
            text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false; text.rectTransform.sizeDelta = new Vector2(.8f, .7f);
            var renderer = text.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var marker = root.GetComponent<NpcQuestMarker>() ?? root.AddComponent<NpcQuestMarker>();
            var serialized = new SerializedObject(marker);
            serialized.FindProperty("marker").objectReferenceValue = text;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            child.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, NpcPresentationBuilder.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
