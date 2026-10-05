using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// 공급사 프리팹은 그대로 보존하고, 상주 포탈에 필요한 위치·크기·시작 효과만 프로젝트 래퍼에서 조절한다.
public static class HideoutPortalPresentationBuilder
{
    public const string DungeonSource = "Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Shock/Update/Blue/Shock_PortalBlue.prefab";
    public const string BossSource = "Assets/ThirdParty/06_VFX/Piloto Studio 1/Elemental VFX Mega Bundle/Dark/Update/Dark_Portal.prefab";
    public const string Folder = "Assets/ProjectOverburst/Resources/VFX/Portals";
    public const string DungeonAsset = Folder + "/PF_HideoutDungeonEntrance.prefab";
    public const string BossAsset = Folder + "/PF_HideoutBossEntrance.prefab";

    [MenuItem("JC Tool/월드/하이드아웃 입장 포탈 에셋 연결")]
    public static void BuildFromMenu() => Debug.Log(EnsureAssets());

    public static string EnsureAssets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("포탈 프리팹은 유휴 Editor에서 생성합니다.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/ProjectOverburst/Resources/VFX", "Portals");
        Ensure(DungeonSource, DungeonAsset);
        Ensure(BossSource, BossAsset);
        return "보유 에셋 포탈 2개 연결 완료. 기존 프로젝트 래퍼 설정은 보존했습니다.";
    }

    private static void Ensure(string sourcePath, string targetPath)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null) throw new InvalidOperationException("보유 포탈 원본을 복원하세요: " + sourcePath);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(targetPath) != null) return;
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(targetPath));
            SceneManager.MoveGameObjectToScene(root, preview);
            var effect = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            effect.transform.SetParent(root.transform, false);
            effect.transform.localPosition = Vector3.up * 2.05f;
            effect.transform.localRotation = Quaternion.Euler(0f,20f,0f) * source.transform.localRotation;
            effect.transform.localScale = source.transform.localScale * 1.05f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(effect.transform);
            foreach (var system in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                // 처음 생성될 때의 폭발만 제외하고 원본의 순환 문양·연기·입자 레이어를 유지한다.
                if (system.main.loop || system.GetComponentsInChildren<ParticleSystem>(true).Any(p=>p!=system && p.main.loop)) continue;
                system.gameObject.SetActive(false);
                PrefabUtility.RecordPrefabInstancePropertyModifications(system.gameObject);
            }
            foreach (var collider in effect.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            // 빈 기본 입자 렌더러는 표시하지 않는다. 부모 ParticleSystem과 자식 순환 효과는 유지한다.
            foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterials.Any(m=>m!=null)) continue;
                renderer.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            var saved = PrefabUtility.SaveAsPrefabAsset(root, targetPath);
            if (saved == null) throw new InvalidOperationException("포탈 래퍼 저장 실패: " + targetPath);
        }
        finally
        {
            if (root != null) UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}

