using System;
using UnityEditor;
using UnityEngine.SceneManagement;

// 2026-10-01: 씬을 열거나 저장하는 제작 도구의 공용 보호.
// 열린 씬에 저장 안 된 변경(다른 작업이 편집 중일 수 있음)이 있으면, 그 씬을 닫거나(Single 열기) 도구 변경과 함께 저장하지 않도록
// 쓰기 전에 멈춘다.
public static class EditorSceneSafety
{
    public static void RequireNoUnsavedScenes(string tool)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException(tool + ": exit Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && scene.isDirty)
                throw new InvalidOperationException(tool + ": scene '" + scene.path + "' has unsaved changes. "
                    + "Save or discard them first; this tool would otherwise close them or save them with its own edits.");
        }
    }

    // 대상이 정리됐거나, 만든 뒤 수작업 조정이 원본이 된 1회 이관·생성 도구의 공용 거부. 쓰기 전에 항상 멈춘다.
    public static void RefuseRetired(string tool, string reason)
    {
        throw new InvalidOperationException(tool + ": retired. " + reason);
    }
}
