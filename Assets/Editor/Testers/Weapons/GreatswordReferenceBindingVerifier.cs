using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>실제 효과 인스턴스의 위치·방향·크기를 청금 장식대검과 전수 비교한다.</summary>
public static class GreatswordReferenceBindingVerifier
{
    public static void Verify(string outputDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("유휴 편집 모드에서 실행하세요.");
        string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "../../개인파일/코덱스산출")) + Path.DirectorySeparatorChar;
        string output = Path.GetFullPath(outputDirectory);
        if (!(output + Path.DirectorySeparatorChar).StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("검증 결과는 코덱스산출 아래에 저장하세요.");
        Directory.CreateDirectory(output);
        var checks = new List<object>();
        var failures = new List<string>();
        void Check(bool pass, string name) { checks.Add(new { name, pass }); if (!pass) failures.Add(name); }
        var paths = GreatswordReferenceBindingBuilder.EquippedPaths();
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(GreatswordReferenceBindingBuilder.ReferencePath);
        var items = AssetDatabase.FindAssets("t:WeaponItemData", new[] { "Assets/ProjectOverburst" })
            .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponItemData>)
            .Where(i => i != null && i.weaponClass == WeaponClass.Greatsword && i.weaponRootPrefab != null).ToArray();
        var referenceItem = items.Single(i => AssetDatabase.GetAssetPath(i.weaponRootPrefab) == GreatswordReferenceBindingBuilder.ReferencePath);
        Check(paths.Length == 34 && items.Length == 34, "34 greatswords available");
        foreach (var path in paths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Check(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), path + " missing scripts");
            foreach (var rt in CommonTransforms(reference))
            {
                string node = AnimationUtility.CalculateTransformPath(rt, reference.transform);
                var ct = node == "" ? prefab.transform : prefab.transform.Find(node);
                Check(ct != null, path + " node " + node);
                if (ct == null) continue;
                foreach (var rc in rt.GetComponents<Component>())
                {
                    var cc = ct.GetComponent(rc.GetType());
                    Check(cc != null && Normalize(rc, reference) == Normalize(cc, prefab), path + " common " + node + "/" + rc.GetType().Name);
                }
            }
            using var fx = new SerializedObject(prefab.GetComponent<MeleeWeaponElementFx>());
            var frame = fx.FindProperty("bladeEffectFrame").objectReferenceValue as Transform;
            Check(path == GreatswordReferenceBindingBuilder.ReferencePath ? frame == null : frame == prefab.transform.Find(GreatswordReferenceBindingBuilder.FramePath), path + " effect frame binding");
        }
        foreach (var item in items)
        {
            Check(item.combatDefinition == referenceItem.combatDefinition && item.defaultElement == referenceItem.defaultElement, item.itemName + " combat definition / element");
            Check(item.worldPickupPrefab != null, item.itemName + " world prefab");
            if (item.worldPickupPrefab == null) continue;
            foreach (var rc in referenceItem.worldPickupPrefab.GetComponents<Component>())
            {
                var cc = item.worldPickupPrefab.GetComponent(rc.GetType());
                Check(cc != null && Normalize(rc, referenceItem.worldPickupPrefab) == Normalize(cc, item.worldPickupPrefab), item.itemName + " world " + rc.GetType().Name);
            }
        }
        var elements = new[] { WeaponElement.Fire, WeaponElement.Ice, WeaponElement.Electric, WeaponElement.Dark, WeaponElement.Light };
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            for (int pose = 0; pose < 2; pose++)
            foreach (var element in elements)
            {
                GameObject source = null;
                try
                {
                    source = CreatePreview(reference, scene, pose);
                    var sourceFx = source.GetComponent<MeleeWeaponElementFx>();
                    sourceFx.EditorPreviewEnergy(element, .6f);
                    var expected = EffectTransforms(sourceFx);
                    Check(expected.Length > 0, element + " reference effect exists");
                    foreach (var path in paths.Where(p => p != GreatswordReferenceBindingBuilder.ReferencePath))
                    {
                        GameObject candidate = null;
                        try
                        {
                            candidate = CreatePreview(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene, pose);
                            var fx = candidate.GetComponent<MeleeWeaponElementFx>();
                            fx.EditorPreviewEnergy(element, .6f);
                            var actual = EffectTransforms(fx);
                            Check(actual.Length == expected.Length, path + " " + element + " effect parts " + pose);
                            for (int i = 0; i < Mathf.Min(actual.Length, expected.Length); i++)
                            {
                                Check(actual[i].name == expected[i].name && Vector3.Distance(actual[i].position, expected[i].position) < .0003f &&
                                    Quaternion.Angle(actual[i].rotation, expected[i].rotation) < .03f && Vector3.Distance(actual[i].lossyScale, expected[i].lossyScale) < .0003f,
                                    path + " " + element + " actual frame " + pose + "/" + i);
                            }
                            fx.EditorBeginTrailPreview();
                            for (int sample = 0; sample < 4; sample++)
                            {
                                candidate.transform.position += new Vector3(.03f, 0, .01f);
                                fx.EditorAdvanceTrailPreview(1f / 60f);
                            }
                            Check(fx.GetComponentsInChildren<TrailRenderer>(true).All(t => t.widthMultiplier > 0), path + " " + element + " tip width");
                            fx.EditorEndTrailPreview();
                            Check(fx.GetComponentsInChildren<TrailRenderer>(true).All(t => !t.emitting), path + " " + element + " trail end");
                            fx.EditorPreviewEnergy(element, 0f);
                            Check(fx.GetComponentsInChildren<WeaponElectricLineAfterimage>(true).All(a => !a.IsEmitting), path + " " + element + " discharge afterimages");
                            fx.EditorClearEnergyPreview();
                            Check(!candidate.GetComponentsInChildren<Transform>(true).Any(t => t.name == "WeaponEffects2Blade" || t.name == "VefectsSwordTipTrail"), path + " " + element + " clear");
                            fx.EditorPreviewEnergy(element, 1f);
                            Check(EffectTransforms(fx).Length > 0, path + " " + element + " rebuild");
                            fx.EditorClearEnergyPreview();
                        }
                        finally { if (candidate != null) UnityEngine.Object.DestroyImmediate(candidate); }
                    }
                }
                finally { if (source != null) UnityEngine.Object.DestroyImmediate(source); }
            }
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Check(!scene.IsValid(), "preview scene closed");
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonConvert.SerializeObject(new {
            status = failures.Count == 0 ? "PASS" : "FAIL", checks = checks.Count, failures, cases = 33 * 5 * 2, details = checks
        }, Formatting.Indented));
        if (failures.Count != 0) throw new InvalidOperationException("대검 기준 검증 실패: " + string.Join(" | ", failures.Take(12)));
        Debug.Log("[대검 기준 검증] PASS · " + checks.Count + "검사 · 신규 33종×5원소×2자세 실제 효과 비교.");
    }

    private static IEnumerable<Transform> CommonTransforms(GameObject asset) => asset.GetComponentsInChildren<Transform>(true)
        .Where(t => !AnimationUtility.CalculateTransformPath(t, asset.transform).StartsWith("VisualRoot/ModelRoot/", StringComparison.Ordinal));

    private static GameObject CreatePreview(GameObject prefab, UnityEngine.SceneManagement.Scene scene, int pose)
    {
        var parent = new GameObject("GreatswordReferencePreviewHost");
        parent.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(parent, scene);
        GameObject instance = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(prefab, parent.transform, false);
            foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(script is MeleeWeaponElementFx)) UnityEngine.Object.DestroyImmediate(script);
            instance.GetComponent<MeleeWeaponElementFx>().enabled = false;
            instance.transform.SetParent(null, true);
            if (pose != 0)
            {
                instance.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(23, 41, 17));
                instance.transform.localScale = Vector3.one * 1.25f;
            }
            instance.SetActive(true);
            return instance;
        }
        catch { if (instance != null) UnityEngine.Object.DestroyImmediate(instance); throw; }
        finally { UnityEngine.Object.DestroyImmediate(parent); }
    }

    private static Transform[] EffectTransforms(MeleeWeaponElementFx fx) => fx.GetComponentsInChildren<Transform>(true)
        .Where(t => t.parent != null && t.parent.name == "WeaponEffects2Blade" || t.name == "Trail placement")
        .OrderBy(t => t.name, StringComparer.Ordinal).ToArray();

    private static string Normalize(Component component, GameObject asset)
    {
        var json = JObject.Parse(EditorJsonUtility.ToJson(component));
        var body = (JObject)json.Properties().First().Value;
        using var so = new SerializedObject(component);
        var prop = so.GetIterator();
        while (prop.Next(true))
        {
            if (prop.propertyType != SerializedPropertyType.ObjectReference || !body.ContainsKey(prop.propertyPath)) continue;
            if (prop.propertyPath == "bladeRenderer") { body[prop.propertyPath] = "$OWN_MODEL_RENDERER"; continue; }
            if (prop.propertyPath == "itemDataAsset") { body[prop.propertyPath] = "$OWN_ITEM"; continue; }
            var obj = prop.objectReferenceValue;
            if (obj == null) { body[prop.propertyPath] = "$NULL"; continue; }
            var t = obj is Component c ? c.transform : obj is GameObject g ? g.transform : null;
            body[prop.propertyPath] = t != null && (t == asset.transform || t.IsChildOf(asset.transform))
                ? "$LOCAL:" + AnimationUtility.CalculateTransformPath(t, asset.transform) + ":" + obj.GetType().Name
                : "$ASSET:" + AssetDatabase.GetAssetPath(obj) + ":" + obj.name;
        }
        body.Remove("m_Name");
        body.Remove("m_EditorClassIdentifier");
        body.Remove("itemLevel");
        // The normalized frame replaces a model-dependent renderer frame; all other fields must match.
        body.Remove("bladeEffectFrame");
        return body.ToString(Formatting.None);
    }
}
