using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>청금 장식대검의 공통 효과 좌표를 새 모델의 FBX 축과 독립적으로 연결한다.</summary>
public static class GreatswordReferenceBindingBuilder
{
    public const string ReferencePath = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/Prefabs/PF_GRS01_AzureStarblade_Equipped.prefab";
    public const string FramePath = "VfxRoot/BladeEffectFrame";
    public const string WeaponFolder = "Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword";
    private static readonly string[] ValueFields = {
        "bladeEffectBounds", "fireBladeAccent", "iceBladeAccent", "electricBladeAccent", "darkBladeAccent", "lightBladeAccent",
        "fireTrail", "iceTrail", "electricTrail", "darkTrail", "lightTrail", "fireTipTrail", "iceTipTrail", "electricTipTrail", "darkTipTrail", "lightTipTrail",
        "tipTrailLifetime", "tipTrailWidthScale", "fireSettings", "iceSettings", "electricSettings", "darkSettings", "lightSettings"
    };
    public static string[] EquippedPaths() => AssetDatabase.FindAssets("t:Prefab", new[] { WeaponFolder })
        .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith("_Equipped.prefab", StringComparison.Ordinal)).OrderBy(p => p).ToArray();

    [MenuItem("OVERBURST/Weapons/Match Greatswords To Azure Starblade")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 편집 모드에서만 대검 프리팹을 변경할 수 있습니다.");
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(ReferencePath);
        var sourceFx = reference != null ? reference.GetComponent<MeleeWeaponElementFx>() : null;
        if (sourceFx == null) throw new InvalidOperationException("청금 장식대검 원본이 필요합니다.");
        using var source = new SerializedObject(sourceFx);
        var renderer = source.FindProperty("bladeRenderer").objectReferenceValue as Renderer;
        if (renderer == null) throw new InvalidOperationException("기준 검신 렌더러가 없습니다.");
        Vector3 framePosition = reference.transform.InverseTransformPoint(renderer.transform.position);
        Quaternion frameRotation = Quaternion.Inverse(reference.transform.rotation) * renderer.transform.rotation;
        var paths = EquippedPaths();
        if (paths.Length != 34) throw new InvalidOperationException("대검 34종의 전체 카탈로그를 확인하세요.");
        foreach (var path in paths)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            ValidateAnchors(asset, reference, path);
            if (asset.GetComponent<MeleeWeaponElementFx>() == null)
                throw new InvalidOperationException(path + ": 공통 원소 컴포넌트 누락");
        }
        int updated = 0;
        foreach (var path in paths.Where(p => p != ReferencePath))
        {
            GameObject contents = null;
            try
            {
                contents = PrefabUtility.LoadPrefabContents(path);
                var fx = contents.GetComponent<MeleeWeaponElementFx>();
                var vfxRoot = contents.transform.Find("VfxRoot");
                var frame = contents.transform.Find(FramePath);
                if (frame == null)
                {
                    frame = new GameObject("BladeEffectFrame").transform;
                    frame.SetParent(vfxRoot, false);
                }
                frame.SetPositionAndRotation(contents.transform.TransformPoint(framePosition), contents.transform.rotation * frameRotation);
                frame.localScale = Vector3.one;
                using var target = new SerializedObject(fx);
                foreach (var field in ValueFields) target.CopyFromSerializedProperty(source.FindProperty(field));
                target.FindProperty("bladeEffectFrame").objectReferenceValue = frame;
                target.FindProperty("auraAnchor").objectReferenceValue = vfxRoot;
                target.FindProperty("trailAnchor").objectReferenceValue = contents.transform.Find("WeaponTrace/WeaponTip");
                target.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(fx);
                PrefabUtility.SaveAsPrefabAsset(contents, path, out bool saved);
                if (!saved) throw new InvalidOperationException(path + ": 프리팹 저장 실패");
                updated++;
            }
            finally { if (contents != null) PrefabUtility.UnloadPrefabContents(contents); }
        }
        Debug.Log("[대검 기준 통일] 청금 장식대검 기준 효과 프레임·개별 설정 " + updated + "종 연결 완료.");
    }

    private static void ValidateAnchors(GameObject candidate, GameObject reference, string path)
    {
        foreach (string node in new[] { "", "VisualRoot", "VisualRoot/ModelRoot", "GripRoot", "GripRoot/RightHandGripPoint",
            "GripRoot/LeftHandGripPoint", "GripRoot/BackGripPoint", "VfxRoot", "VfxRoot/SlashVfxAnchor", "WeaponTrace", "WeaponTrace/WeaponTip" })
        {
            var expected = node == "" ? reference.transform : reference.transform.Find(node);
            var actual = node == "" ? candidate.transform : candidate.transform.Find(node);
            if (expected == null || actual == null || Vector3.Distance(expected.localPosition, actual.localPosition) > .0001f ||
                Quaternion.Angle(expected.localRotation, actual.localRotation) > .001f || Vector3.Distance(expected.localScale, actual.localScale) > .0001f)
                throw new InvalidOperationException(path + ": 기준 장착/판정 앵커 불일치 " + node);
        }
    }
}
