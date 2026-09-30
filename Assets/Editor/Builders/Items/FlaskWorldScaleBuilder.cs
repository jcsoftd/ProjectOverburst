using System;
using UnityEditor;
using UnityEngine;

public static class FlaskWorldScaleBuilder
{
    private const string Folder = "Assets/ProjectOverburst/03_Features/Items/Prefabs/Flasks";
    private const float TargetScale = .82f;

    [MenuItem("JC Tool/Items/Resize Flask World Models")]
    public static void Resize()
    {
        int changed = 0;
        foreach (FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
        {
            string path = Folder + "/PF_Flask_" + kind + ".prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) throw new InvalidOperationException("Missing flask prefab: " + path);
            try
            {
                Transform model = root.transform.Find("ModelRoot");
                if (model == null) throw new InvalidOperationException("Missing ModelRoot: " + path);
                if (model.localScale == Vector3.one)
                {
                    model.localScale = Vector3.one * TargetScale;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed++;
                }
                else if (Vector3.Distance(model.localScale, Vector3.one * TargetScale) > .0001f)
                    throw new InvalidOperationException("Unexpected current scale at " + path + ": " + model.localScale);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[FlaskWorldScaleBuilder] world model scale=" + TargetScale + " changed=" + changed);
    }
}
