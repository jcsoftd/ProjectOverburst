using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// GOAL B1: PF_PlayerActor에 OverburstCharacterMotor3D를 Editor API로만 연결한다.
// - 프리팹을 텍스트/YAML로 직접 패치하지 않는다.
// - PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset와 SerializedObject를 사용하고 GUID/meta를 보존한다.
// - 멱등: 이미 정확히 연결됐으면 NO_CHANGE.
public static class OverburstPlayerMotorMigration
{
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string MenuPath = "OVERBURST/Codex/Setup/Player/Apply GOAL B1 Motor Migration";

    [MenuItem(MenuPath)]
    public static void MigrateFromMenu()
    {
        Debug.Log(MigrateAndReport());
    }

    public static string MigrateAndReport()
    {
        Require(File.Exists(ResolveProjectPath(PrefabPath)), "프리팹을 찾을 수 없다: " + PrefabPath);

        string prefabFilePath = ResolveProjectPath(PrefabPath);
        byte[] originalBytes = File.ReadAllBytes(prefabFilePath);

        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        Require(contents != null, "프리팹 로드에 실패했다: " + PrefabPath);
        try
        {
            PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
            Require(actors.Length == 1, "PlayerActorRuntime이 1개가 아니다: " + actors.Length);
            GameObject actorObject = actors[0].gameObject;

            PlayerMovement movement = actorObject.GetComponent<PlayerMovement>();
            Require(movement != null, "PlayerMovement가 액터에 없다.");

            OverburstCharacterMotor3D[] motors = actorObject.GetComponents<OverburstCharacterMotor3D>();
            Require(motors.Length <= 1, "OverburstCharacterMotor3D가 2개 이상이다. 수동 정리가 필요하다.");

            bool changed = false;
            OverburstCharacterMotor3D motor = motors.Length == 1 ? motors[0] : null;
            if (motor == null)
            {
                motor = actorObject.AddComponent<OverburstCharacterMotor3D>();
                changed = true;
            }

            SerializedObject movementSettings = new SerializedObject(movement);
            SerializedProperty motorProperty = movementSettings.FindProperty("characterMotor");
            Require(motorProperty != null, "PlayerMovement.characterMotor 속성을 찾을 수 없다.");
            if (motorProperty.objectReferenceValue != motor)
            {
                motorProperty.objectReferenceValue = motor;
                movementSettings.ApplyModifiedProperties();
                changed = true;
            }

            SerializedObject motorSettings = new SerializedObject(motor);
            SerializedProperty movementProperty = motorSettings.FindProperty("movement");
            Require(movementProperty != null, "OverburstCharacterMotor3D.movement 속성을 찾을 수 없다.");
            if (movementProperty.objectReferenceValue != movement)
            {
                movementProperty.objectReferenceValue = movement;
                motorSettings.ApplyModifiedProperties();
                changed = true;
            }

            OverburstCharacterMotor3D[] afterMotors = actorObject.GetComponents<OverburstCharacterMotor3D>();
            Require(afterMotors.Length == 1, "적용 후 OverburstCharacterMotor3D가 1개가 아니다.");
            Require(afterMotors[0].BoundMovement == movement, "적용 후 motor.movement가 연결되지 않았다.");

            if (!changed)
            {
                string noChangeReport = OverburstPlayerMotorValidator.ValidateAndReport();
                return "[OverburstPlayerMotorMigration] NO_CHANGE"
                    + "\n- prefab: " + PrefabPath
                    + "\n" + noChangeReport;
            }

            try
            {
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                string appliedReport = OverburstPlayerMotorValidator.ValidateAndReport();
                return "[OverburstPlayerMotorMigration] APPLIED"
                    + "\n- prefab: " + PrefabPath
                    + "\n- motor 1개, movement 양방향 연결"
                    + "\n" + appliedReport;
            }
            catch (Exception)
            {
                File.WriteAllBytes(prefabFilePath, originalBytes);
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static string ResolveProjectPath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string filePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Require(
            filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase),
            "에셋 파일 경로가 프로젝트 루트 하위가 아니다: " + filePath);
        return filePath;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
