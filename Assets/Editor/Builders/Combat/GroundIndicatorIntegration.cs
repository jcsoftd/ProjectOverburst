using System;
using UnityEditor;
using UnityEngine;

public static class GroundIndicatorIntegration
{
    private const string LibraryPath = "Assets/ProjectOverburst/Resources/Enemies/Balance/EnemyTelegraphVisualLibrary.asset";

    [MenuItem("JC Tool/VFX/인디케이터/게임에 새 표시 적용")]
    public static void Enable() => SetMode(true);

    [MenuItem("JC Tool/VFX/인디케이터/게임을 기존 표시로 복원")]
    public static void RestoreLegacy() => SetMode(false);

    public static void SetMode(bool enabled)
    {
        GroundIndicatorBuilder.RequireIdle();
        var library = AssetDatabase.LoadAssetAtPath<EnemyTelegraphVisualLibrary>(LibraryPath);
        if(library==null || EditorUtility.IsDirty(library)) throw new InvalidOperationException("Telegraph library is missing or has unsaved edits.");
        var serialized = new SerializedObject(library);
        if(enabled)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundIndicatorBuilder.PrefabPath);
            if(prefab==null || prefab.GetComponent<ProceduralGroundIndicator>()==null) throw new InvalidOperationException("Build indicator prefabs first.");
            serialized.FindProperty("proceduralIndicator").objectReferenceValue=prefab;
        }
        Undo.RecordObject(library,enabled?"새 인디케이터 적용":"기존 인디케이터 복원");
        serialized.FindProperty("useProceduralIndicator").boolValue=enabled;
        serialized.ApplyModifiedProperties();AssetDatabase.SaveAssetIfDirty(library);
    }
}
