using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// GOAL B2: PF_PlayerActor의 일반 이동과 전투 직접 이동 소유자를 Unity API로 연결한다.
// YAML 직접 편집 없이 멱등 적용하며, 저장/검증 실패 시 원본 bytes를 복구한다.
public static class OverburstPlayerMovementArchitectureMigration
{
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string MenuPath = "OVERBURST/Codex/Setup/Player/Apply GOAL B2 Movement Architecture Migration";

    [MenuItem(MenuPath)]
    public static void MigrateFromMenu()
    {
        Debug.Log(MigrateAndReport());
    }

    public static string MigrateAndReport()
    {
        string filePath = ResolveProjectPath(PrefabPath);
        Require(File.Exists(filePath), "프리팹을 찾을 수 없다: " + PrefabPath);
        byte[] originalBytes = File.ReadAllBytes(filePath);
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        Require(contents != null, "프리팹 로드에 실패했다: " + PrefabPath);
        try
        {
            PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
            Require(actors.Length == 1, "PlayerActorRuntime이 1개가 아니다: " + actors.Length);
            GameObject actor = actors[0].gameObject;
            PlayerMovement movement = actor.GetComponent<PlayerMovement>();
            OverburstCharacterMotor3D motor = actor.GetComponent<OverburstCharacterMotor3D>();
            PlayerEvadeController evade = actor.GetComponent<PlayerEvadeController>();
            CombatTarget target = actor.GetComponent<CombatTarget>();
            Require(movement != null, "PlayerMovement가 없다.");
            Require(motor != null, "OverburstCharacterMotor3D가 없다.");
            Require(evade != null, "PlayerEvadeController가 없다.");
            Require(target != null, "CombatTarget이 없다.");

            PlayerLocomotion[] locomotions = actor.GetComponents<PlayerLocomotion>();
            CombatMotionDriver[] combatDrivers = actor.GetComponents<CombatMotionDriver>();
            Require(locomotions.Length <= 1, "PlayerLocomotion이 2개 이상이다.");
            Require(combatDrivers.Length <= 1, "CombatMotionDriver가 2개 이상이다.");
            bool changed = false;
            PlayerLocomotion locomotion = locomotions.Length == 1
                ? locomotions[0]
                : actor.AddComponent<PlayerLocomotion>();
            CombatMotionDriver combatDriver = combatDrivers.Length == 1
                ? combatDrivers[0]
                : actor.AddComponent<CombatMotionDriver>();
            changed |= locomotions.Length == 0 || combatDrivers.Length == 0;

            changed |= Assign(movement, "characterMotor", motor);
            changed |= Assign(movement, "locomotion", locomotion);
            changed |= Assign(movement, "combatMotion", combatDriver);
            changed |= Assign(motor, "movement", movement);
            changed |= Assign(locomotion, "movement", movement);
            changed |= Assign(locomotion, "motor", motor);
            changed |= Assign(combatDriver, "movement", movement);
            changed |= Assign(combatDriver, "motor", motor);
            changed |= Assign(combatDriver, "combatTarget", target);
            changed |= Assign(evade, "combatMotion", combatDriver);

            if (!changed)
            {
                return "[OverburstPlayerMovementArchitectureMigration] NO_CHANGE\n"
                    + OverburstGoalB2MovementValidator.ValidateAndReport();
            }

            try
            {
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                return "[OverburstPlayerMovementArchitectureMigration] APPLIED\n"
                    + "- PlayerLocomotion/CombatMotionDriver 각 1개 및 양방향 참조 연결\n"
                    + OverburstGoalB2MovementValidator.ValidateAndReport();
            }
            catch (Exception)
            {
                File.WriteAllBytes(filePath, originalBytes);
                AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static bool Assign(Component owner, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.FindProperty(propertyName);
        Require(property != null, owner.GetType().Name + "." + propertyName + "를 찾을 수 없다.");
        if (property.objectReferenceValue == value)
            return false;
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    private static string ResolveProjectPath(string assetPath)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string filePath = Path.GetFullPath(Path.Combine(root, assetPath));
        string prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Require(filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "프로젝트 밖 경로다: " + filePath);
        return filePath;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
