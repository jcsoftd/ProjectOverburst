using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class NpcPresentationBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/02_Shared/Interaction/Prefabs/PF_NpcNameLabel.prefab";
    public const string FontPath = "Assets/ProjectOverburst/Resources/UI/Fonts/NpcNames/NpcNames SDF.asset";
    const string Names = "무기상인방어구잡화물약치장사";
    static readonly Color Gold = new Color32(218, 186, 122, 255);

    public static void EnsureAssets()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (!font)
        {
            var reference = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-SemiBold-HUD SDF.asset");
            if (!reference || !reference.sourceFontFile) throw new InvalidOperationException("NPC name font source missing");
            string directory = Path.GetDirectoryName(FontPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(directory)) AssetDatabase.CreateFolder(Path.GetDirectoryName(directory).Replace('\\', '/'), "NpcNames");
            font = TMP_FontAsset.CreateFontAsset(reference.sourceFontFile, 90, 12, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 512, 512, AtlasPopulationMode.Dynamic);
            font.name = "NpcNames SDF";
            if (!font.TryAddCharacters(Names, out string missing)) throw new InvalidOperationException("NPC glyphs missing: " + missing);
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.material.shader = Shader.Find("TextMeshPro/Distance Field Overlay");
            if (!font.material.shader) throw new InvalidOperationException("NPC overlay shader missing");
            font.material.SetFloat("_OutlineWidth", .16f);
            font.material.SetColor("_OutlineColor", new Color(0, 0, 0, .92f));
            font.material.EnableKeyword("OUTLINE_ON");
            AssetDatabase.CreateAsset(font, FontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.SaveAssetIfDirty(font);
        }
        if (!font.HasCharacters(Names)) throw new InvalidOperationException("NPC name font lacks required glyphs");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)) return;
        var root = new GameObject("PF_NpcNameLabel", typeof(TextMeshPro), typeof(AppearanceStylistLabel));
        try
        {
            Configure(root.GetComponent<TextMeshPro>(), font, "무기상인");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { Object.DestroyImmediate(root); }
    }

    static void Configure(TextMeshPro text, TMP_FontAsset font, string name)
    {
        text.font = font; text.fontSharedMaterial = font.material; text.text = name;
        text.fontSize = 2.8f; text.color = Gold; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false; text.rectTransform.sizeDelta = new Vector2(3.2f, .4f);
        text.transform.localPosition = new Vector3(0, 2.35f, 0); text.transform.localScale = Vector3.one;
        var renderer = text.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
    }

    public static void ApplyName(Component npc)
    {
        EnsureAssets();
        var merchant = npc as GeneralGoodsMerchantInteractable;
        string name = merchant ? merchant.Definition.MerchantName : "치장사";
        var existing = npc.transform.Find("NPC Name") ?? npc.transform.Find("Stylist Name");
        TextMeshPro text;
        if (existing) text = existing.GetComponent<TextMeshPro>();
        else
        {
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), npc.transform);
            root.name = "NPC Name"; text = root.GetComponent<TextMeshPro>();
        }
        if (!text) throw new InvalidOperationException("Existing NPC name object lacks TextMeshPro");
        Configure(text, AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath), name);
        if (!text.GetComponent<AppearanceStylistLabel>()) text.gameObject.AddComponent<AppearanceStylistLabel>();
        text.gameObject.SetActive(true);
        if (merchant)
        {
            var so = new SerializedObject(merchant); so.FindProperty("nameLabel").objectReferenceValue = text; so.ApplyModifiedPropertiesWithoutUndo();
            Record(merchant);
        }
        foreach (var component in text.GetComponents<Component>()) Record(component);
        Record(text.gameObject);
    }

    [MenuItem("OVERBURST/UI/NPC Names And Stylist/Apply")]
    public static void Apply()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        foreach (string path in new[]{MainTownBuilder.ScenePath, "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity", OverburstUIWorkshopBuilder.ScenePath})
            if (SceneManager.GetSceneByPath(path).IsValid()) throw new InvalidOperationException("Preserve open scene: " + path);
        EnsureAssets();
        var stylist = PrefabUtility.LoadPrefabContents(AppearanceCustomizationBuilder.NpcPath);
        try { ApplyName(stylist.GetComponent<AppearanceStylistInteractable>()); PrefabUtility.SaveAsPrefabAsset(stylist, AppearanceCustomizationBuilder.NpcPath); }
        finally { PrefabUtility.UnloadPrefabContents(stylist); }
        foreach (string path in new[]{MainTownBuilder.ScenePath, "Assets/ProjectOverburst/00_Scenes/HideoutScene.unity"})
        {
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                foreach (var npc in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GeneralGoodsMerchantInteractable>(true))) ApplyName(npc);
                foreach (var npc in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AppearanceStylistInteractable>(true))) ApplyName(npc);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("NPC name scene save failed: " + path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
        var management = EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath, OpenSceneMode.Additive);
        try { QuestMerchantWorkshopBuilder.AppendStylist(management); if (!EditorSceneManager.SaveScene(management)) throw new IOException("Stylist preview save failed"); }
        finally { EditorSceneManager.CloseScene(management, true); }
    }
    static void Record(Object value) { if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value); }
}
