#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>기존 프리팹 객체의 식별자를 보존하면서 제작된 디버그 UI를 반영한다.</summary>
internal static class DebugHubPrefabAuthoring
{
    public static void Save(GameObject authored, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { PrefabUtility.SaveAsPrefabAsset(authored, path); return; }
        var existing = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var map = new Dictionary<UnityEngine.Object, UnityEngine.Object>();
            var components = new List<Component>();
            Match(authored, existing, map, components);
            foreach (var source in components)
            {
                var target = (Component)map[source];
                EditorUtility.CopySerialized(source, target);
                var serialized = new SerializedObject(target);
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null
                        && map.TryGetValue(property.objectReferenceValue, out var replacement)) property.objectReferenceValue = replacement;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            PrefabUtility.SaveAsPrefabAsset(existing, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(existing); }
    }
    static void Match(GameObject source, GameObject target, Dictionary<UnityEngine.Object, UnityEngine.Object> map, List<Component> copy)
    {
        map.Add(source, target); map.Add(source.transform, target.transform);
        target.name = source.name; target.layer = source.layer; target.tag = source.tag; target.SetActive(source.activeSelf);
        target.transform.localPosition = source.transform.localPosition;
        target.transform.localRotation = source.transform.localRotation; target.transform.localScale = source.transform.localScale;
        if (source.transform is RectTransform a && target.transform is RectTransform b)
        { b.anchorMin = a.anchorMin; b.anchorMax = a.anchorMax; b.pivot = a.pivot; b.sizeDelta = a.sizeDelta; b.anchoredPosition3D = a.anchoredPosition3D; }
        var available = target.GetComponents<Component>().Where(component => component != null && !(component is Transform)).ToList();
        foreach (var component in source.GetComponents<Component>())
        {
            if (component == null || component is Transform) continue;
            var match = available.FirstOrDefault(value => value.GetType() == component.GetType());
            if (match != null) available.Remove(match); else match = target.AddComponent(component.GetType());
            map.Add(component, match); copy.Add(component);
        }
        foreach (var obsolete in available) UnityEngine.Object.DestroyImmediate(obsolete);
        var children = new List<Transform>();
        foreach (Transform child in target.transform) children.Add(child);
        int index = 0;
        foreach (Transform child in source.transform)
        {
            var match = children.FirstOrDefault(value => value.name == child.name);
            if (match != null) children.Remove(match);
            else
            {
                var created = new GameObject(child.name, child is RectTransform ? typeof(RectTransform) : typeof(Transform));
                match = created.transform; match.SetParent(target.transform, false);
            }
            match.SetSiblingIndex(index++); Match(child.gameObject, match.gameObject, map, copy);
        }
        foreach (var obsolete in children) UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
    }
}
#endif
