using System;
using System.IO;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class MonsterHoverNameBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/UI/World/PF_EnemyHoverName.prefab";
    public const string MaterialPath = "Assets/ProjectOverburst/05_Art/UI/HUD/TargetHud/EnemyHoverName.mat";
    private const string SourcePath = "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Medium.prefab";

    public static string Apply()
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("An idle Editor is required for the approved nameplate.");
        GameObject contents = PrefabUtility.LoadPrefabContents(SourcePath);
        GameObject clone = null;
        try
        {
            var view = contents.GetComponent<EnemyHpBarView>();
            var source = new SerializedObject(view).FindProperty("compactNameText").objectReferenceValue as TextMeshProUGUI;
            if (source == null || source.font == null) throw new InvalidOperationException("The current Medium name component is missing.");
            // Clone in the prefab's isolated scene so the user's open scenes stay untouched.
            clone = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
            clone.transform.SetParent(null, false);
            clone.name = "PF_EnemyHoverName";
            var text = clone.GetComponent<TextMeshProUGUI>();
            RectTransform rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(240f, 20f);
            rect.localScale = Vector3.one;
            text.text = string.Empty;
            text.fontSize = 14f;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.richText = false;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(0.94f, 0.87f, 0.76f, 1f);
            text.raycastTarget = false;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(source.fontSharedMaterial) { name = "EnemyHoverName" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, 0.85f));
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.18f);
            material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0f);
            material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.2f);
            EditorUtility.SetDirty(material);
            text.fontSharedMaterial = material;
            text.UpdateMeshPadding();
            var group = clone.GetComponent<CanvasGroup>();
            if (group == null) group = clone.AddComponent<CanvasGroup>();
            group.interactable = group.blocksRaycasts = false;
            clone.SetActive(false);
            if (PrefabUtility.SaveAsPrefabAsset(clone, PrefabPath) == null)
                throw new InvalidOperationException("Nameplate prefab save failed.");
            AssetDatabase.SaveAssetIfDirty(material);
            var saved = AssetDatabase.LoadAssetAtPath<TextMeshProUGUI>(PrefabPath);
            if (saved == null || saved.font != source.font || saved.fontSize != 14f || saved.raycastTarget
                || saved.GetComponent<EnemyHpBarView>() != null || saved.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length != 0)
                throw new InvalidOperationException("Saved nameplate references or HP-free contents do not match.");
            return JsonConvert.SerializeObject(new { status = "PASS", prefab = PrefabPath, font = AssetDatabase.GetAssetPath(saved.font),
                size = saved.fontSize, hpImages = 0, material = MaterialPath }, Formatting.Indented);
        }
        finally
        {
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
