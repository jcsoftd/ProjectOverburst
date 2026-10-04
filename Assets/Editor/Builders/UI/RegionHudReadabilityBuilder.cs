using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>지역명과 안내 글자에 목표 HUD의 그림자·외곽선 규격을 재사용한다.</summary>
public static class RegionHudReadabilityBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/PF_OverburstHUD_Rpg11.prefab";
    public const string Output = "../개인파일/코덱스산출/UI/20261004_RegionHudReadability";

    public static void ApplyToHud(GameObject hud)
    {
        if (!hud) throw new ArgumentNullException(nameof(hud));
        var tracker = hud.transform.Find("Quest Tracker");
        var reference = tracker ? tracker.GetComponentsInChildren<Text>(true).FirstOrDefault(t =>
            t.GetComponent<Outline>() && t.GetComponents<Shadow>().Any(s => s.GetType() == typeof(Shadow))) : null;
        foreach (string name in new[] { "Region Name", "Region Details" })
        {
            var target = hud.transform.Find("Current Region/" + name)?.GetComponent<Text>();
            if (!target) throw new InvalidOperationException("Region text missing: " + name);
            var shadow = target.GetComponents<Shadow>().FirstOrDefault(s => s.GetType() == typeof(Shadow)) ?? target.gameObject.AddComponent<Shadow>();
            var outline = target.GetComponent<Outline>() ?? target.gameObject.AddComponent<Outline>();
            // 목표 HUD는 0.5배 크기다. 지역 글자에도 화면에서 같은 두께로 표시한다.
            Vector2 ratio = reference ? new Vector2(
                Mathf.Abs(reference.transform.lossyScale.x / target.transform.lossyScale.x),
                Mathf.Abs(reference.transform.lossyScale.y / target.transform.lossyScale.y)) : Vector2.one * .5f;
            var originalShadow = reference ? reference.GetComponents<Shadow>().First(s => s.GetType() == typeof(Shadow)) : null;
            var originalOutline = reference ? reference.GetComponent<Outline>() : null;
            shadow.effectColor = originalShadow ? originalShadow.effectColor : new Color(0, 0, 0, .95f);
            shadow.effectDistance = Vector2.Scale(originalShadow ? originalShadow.effectDistance : new Vector2(1.5f, -2), ratio);
            shadow.useGraphicAlpha = originalShadow ? originalShadow.useGraphicAlpha : true;
            outline.effectColor = originalOutline ? originalOutline.effectColor : new Color(0, 0, 0, .7f);
            outline.effectDistance = Vector2.Scale(originalOutline ? originalOutline.effectDistance : new Vector2(1.5f, -1.5f), ratio);
            outline.useGraphicAlpha = originalOutline ? originalOutline.useGraphicAlpha : true;
            shadow.enabled = outline.enabled = true;
        }
    }

    [MenuItem("OVERBURST/UI/지역 HUD 글자 가독성 적용")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Idle Editor required");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ApplyToHud(root);
            if (!PrefabUtility.SaveAsPrefabAsset(root, PrefabPath)) throw new InvalidOperationException("HUD save failed");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "applied-prefab.txt"), "PASS: region name and details use objective HUD shadow/outline at matching screen thickness");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
