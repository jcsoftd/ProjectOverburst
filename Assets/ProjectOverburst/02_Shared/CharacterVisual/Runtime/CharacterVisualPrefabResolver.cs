using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class CharacterVisualPrefabResolver
{
    public static GameObject Resolve(string assetName)
    {
        if (string.IsNullOrWhiteSpace(assetName))
            return null;

#if UNITY_EDITOR
        string[] guids = AssetDatabase.FindAssets(assetName);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject mainAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (mainAsset != null && IsMatchingVisual(mainAsset, assetName))
                return mainAsset;

            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int j = 0; j < assets.Length; j++)
            {
                GameObject subAsset = assets[j] as GameObject;
                if (subAsset != null && IsMatchingVisual(subAsset, assetName))
                    return subAsset;
            }
        }
#endif

        return null;
    }

    private static bool IsMatchingVisual(GameObject asset, string assetName)
    {
        if (asset == null)
            return false;

        if (asset.name == assetName)
            return true;

        Transform[] children = asset.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == assetName)
                return true;
        }

        return false;
    }
}
