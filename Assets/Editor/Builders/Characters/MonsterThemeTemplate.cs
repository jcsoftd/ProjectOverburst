using UnityEditor;
using UnityEngine;

// 2026-10-01: 옛 Protofactor 파일럿 생성기(ProtofactorEnemyPilotBuilder, 삭제)에서 테마 제작 도구가 쓰던 값 중
// 지금도 유효한 기본 변형 경로와, 새 액터를 만들 때 필요한 템플릿 계약만 옮겼다.
// 옛 템플릿(Protofactor Ceratoferox 액터·정의)은 188dcdb에서 지웠고 새 템플릿은 아직 정하지 않았다(사용자 결정).
// 템플릿을 정하기 전에는 새 액터 생성이 쓰기 전에 멈추고, 기존 액터의 보존·등록 흐름만 동작한다.
public static class MonsterThemeTemplate
{
    public const string DefaultVariantPath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Variants/EVP_Default.asset";

    // 새 액터 템플릿의 액터 프리팹과 원본 정의 경로. 비어 있으면 템플릿이 없다.
    public const string ActorPrefabPath = "";
    public const string SourceDefinitionPath = "";

    public static bool IsConfigured => !string.IsNullOrEmpty(ActorPrefabPath) && !string.IsNullOrEmpty(SourceDefinitionPath);

    public static GameObject LoadActor() => IsConfigured ? AssetDatabase.LoadAssetAtPath<GameObject>(ActorPrefabPath) : null;

    public static EnemyDefinition LoadSource() => IsConfigured ? AssetDatabase.LoadAssetAtPath<EnemyDefinition>(SourceDefinitionPath) : null;

    public static EnemyVariantProfile LoadDefaultVariant() => AssetDatabase.LoadAssetAtPath<EnemyVariantProfile>(DefaultVariantPath);
}
