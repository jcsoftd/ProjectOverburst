using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Local authoring tool. The generated TMP font asset is the public UI asset.
public static class FlaskTooltipFontBuilder
{
    private const string Source = "Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght.ttf";
    private const string TooltipSource = "Assets/ProjectOverburst/03_Features/Items/Runtime/Flasks/FlaskTooltip.cs";
    private const string Target = "Assets/ProjectOverburst/Resources/UI/Tooltip/FlaskTooltipFont.asset";
    private const string InterfaceLabels = "장비 물약 투구 갑옷 장갑 신발 귀걸이 목걸이 빈 장착칸 번호 미등록 번 등록 재사용 대기 준비 완료 생명 재생 광전사 거인 처형자 과충전 철갑 유령 홍염 빙심 뇌광 심해";

    [MenuItem("JC Tool/UI/Build Flask Tooltip Font")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new System.InvalidOperationException("Edit mode required");
        Font source = AssetDatabase.LoadAssetAtPath<Font>(Source);
        if (source == null) throw new System.InvalidOperationException("Noto source font missing");
        var characters = new HashSet<char>();
        for (char c = ' '; c <= '~'; c++) characters.Add(c);
        foreach (char c in File.ReadAllText(TooltipSource, Encoding.UTF8))
            if ((c >= '\uAC00' && c <= '\uD7A3') || c == '★' || c == '◆' || c == '•' || c == '·') characters.Add(c);
        foreach (char c in InterfaceLabels) characters.Add(c);
        foreach (string guid in AssetDatabase.FindAssets("t:FlaskItemData",
                     new[] { "Assets/ProjectOverburst/Resources/Items/Flasks" }))
        {
            var flask = AssetDatabase.LoadAssetAtPath<FlaskItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (flask == null || string.IsNullOrEmpty(flask.itemName)) continue;
            foreach (char c in flask.itemName) characters.Add(c);
        }
        var content = new StringBuilder(characters.Count);
        foreach (char c in characters) content.Append(c);

        TMP_FontAsset asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Target);
        bool created = asset == null;
        if (created)
        {
            asset = TMP_FontAsset.CreateFontAsset(source, 58, 5,
                GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            asset.name = "FlaskTooltipFont";
        }
        else asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        if (!asset.TryAddCharacters(content.ToString(), out string missing))
            throw new System.InvalidOperationException("Missing font characters: " + missing);
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        if (created)
        {
            AssetDatabase.CreateAsset(asset, Target);
            if (asset.material != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset.material)))
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (Texture2D texture in asset.atlasTextures)
                if (texture != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(texture)))
                    AssetDatabase.AddObjectToAsset(texture, asset);
        }
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(Target, ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("FLASK_TOOLTIP_FONT_PASS: " + characters.Count + " characters, star glyph included");
    }
}
