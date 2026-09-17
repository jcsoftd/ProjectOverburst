using UnityEditor;
using UnityEngine;

public static class SwordWeaponPoseResetUtility
{
    private const string SwordRootPath = "Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/Prefabs/PF_OHS01_FleurDeLys_Equipped.prefab";

    [MenuItem("OVERBURST/Codex/Weapons/Reset Sword Weapon Pose Slots")]
    public static void RunOnceFromCommandLine()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SwordRootPath);
        if (root == null)
            throw new MissingReferenceException("Missing sword prefab: " + SwordRootPath);

        try
        {
            WeaponPose pose = root.GetComponent<WeaponPose>();
            if (pose == null)
                pose = root.GetComponentInChildren<WeaponPose>(true);

            if (pose == null)
                throw new MissingReferenceException("SwordRoot is missing WeaponPose.");

            SerializedObject serialized = new SerializedObject(pose);
            SetVector3(serialized, "holdLocalPosition", Vector3.zero);
            SetVector3(serialized, "holdLocalRotation", Vector3.zero);
            SetVector3(serialized, "backLocalPosition", Vector3.zero);
            SetVector3(serialized, "backLocalRotation", Vector3.zero);
            SetVector3(serialized, "aimLocalPosition", Vector3.zero);
            SetVector3(serialized, "aimLocalRotation", Vector3.zero);
            SetVector3(serialized, "guardLocalPosition", Vector3.zero);
            SetVector3(serialized, "guardLocalRotation", Vector3.zero);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(pose);
            PrefabUtility.SaveAsPrefabAsset(root, SwordRootPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SwordWeaponPoseReset] SwordRoot WeaponPose slots reset to zero.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SetVector3(SerializedObject serialized, string propertyName, Vector3 value)
    {
        SerializedProperty property = serialized.FindProperty(propertyName);
        if (property == null)
            throw new MissingReferenceException("Missing WeaponPose property: " + propertyName);

        property.vector3Value = value;
    }
}
