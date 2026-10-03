#if UNITY_EDITOR
using System;
using System.Linq;
using Overburst.DebugTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>저장된 디버그 프리팹의 참조와 등록부 일치를 검사한다.</summary>
public static partial class DebugHubPrefabVerifier
{
    public static object ValidateAsset()
    {
        string path = DebugHubPrefabBuilder.PrefabPath;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            DebugHubView view = root.GetComponent<DebugHubView>();
            if (view == null || view.Style == null || view.Style.font == null || view.Window == null
                || view.Tabs == null || view.Content == null || view.Overlay == null || view.FallbackEventSystem == null)
                throw new InvalidOperationException("디버그 프리팹의 직렬화 참조 누락");
            var input = view.FallbackEventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (input == null || input.actionsAsset == null || input.point == null || input.leftClick == null
                || !EditorUtility.IsPersistent(input.point) || !EditorUtility.IsPersistent(input.leftClick))
                throw new InvalidOperationException("예비 EventSystem의 정식 입력 참조 누락");
            if (root.activeSelf || root.GetComponent<DebugHub>() == null
                || root.GetComponent<Canvas>() == null || root.GetComponent<GraphicRaycaster>() == null)
                throw new InvalidOperationException("디버그 프리팹의 루트 설정 오류");
            if (view.Pages.Length != DebugTabs.Order.Length + 1 || view.Pages.Any(p => p == null || p.parent != view.Content))
                throw new InvalidOperationException("현재 등록된 탭과 검색 페이지 연결 오류");
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) > 0)
                    throw new InvalidOperationException("Missing Script: " + node.name);
            foreach (Image image in root.GetComponentsInChildren<Image>(true))
                if (image.type == Image.Type.Sliced && (image.sprite == null || !EditorUtility.IsPersistent(image.sprite)))
                    throw new InvalidOperationException("영구 둥근 모서리 Sprite 누락: " + image.name);
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
                if (button.colors.fadeDuration != 0)
                    throw new InvalidOperationException("틴트 전환 지연: " + button.name);
            string[] registered = DebugRegistry.AllItems.Select(item => item.Id).OrderBy(id => id).ToArray();
            if (registered.Length > 0 && !registered.SequenceEqual(view.ItemIds))
                throw new InvalidOperationException("등록 항목이 바뀌었습니다. Debug Hub Prefab 빌더를 실행하세요.");
            return new { status = "PASS", path, guid = AssetDatabase.AssetPathToGUID(path),
                pages = view.Pages.Length, items = view.ItemIds.Length,
                transforms = root.GetComponentsInChildren<Transform>(true).Length, missingScripts = 0 };
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
