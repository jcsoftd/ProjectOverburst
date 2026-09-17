using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// GOAL B2 정적 계약: 이동 기반 API, 책임 분리, prefab 연결, 금지 범위를 검사한다.
public static class OverburstGoalB2MovementValidator
{
    public const string PrefabPath = OverburstPlayerMovementArchitectureMigration.PrefabPath;
    public const string MenuPath = "OVERBURST/Codex/Validate/Player/Validate GOAL B2 Movement Architecture";

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        UnityEngine.Debug.Log(ValidateAndReport());
    }

    public static string ValidateAndReport()
    {
        List<string> errors = new List<string>();
        ValidateTypes(errors);
        ValidatePrefab(errors);
        ValidateOwnership(errors);
        ValidateForbiddenScope(errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGoalB2MovementValidator] FAIL\n- " + string.Join("\n- ", errors));
        return "[OverburstGoalB2MovementValidator] PASS\n"
            + "- 플랫폼/외력/높이 API, 이동·전투 책임 분리, prefab 참조, 단일 CharacterController.Move, 금지 범위 확인";
    }

    public static string ValidateRuntimeContracts()
    {
        List<string> errors = new List<string>();
        ValidateTypes(errors);
        ValidatePrefab(errors);
        ValidateOwnership(errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGoalB2MovementValidator] RUNTIME FAIL\n- " + string.Join("\n- ", errors));
        return "[OverburstGoalB2MovementValidator] RUNTIME-CONTRACTS PASS";
    }

    private static void ValidateTypes(List<string> errors)
    {
        Type motor = typeof(OverburstCharacterMotor3D);
        RequireMethod(errors, motor, "AttachToPlatform", typeof(void), typeof(Transform));
        RequireMethod(errors, motor, "DetachFromPlatform", typeof(void), typeof(bool));
        RequireMethod(errors, motor, "AddExternalForce", typeof(void), typeof(Vector3));
        RequireMethod(errors, motor, "ClearExternalForces", typeof(void));
        RequireMethod(errors, motor, "SetHeightWithHeadroom", typeof(bool), typeof(float));
        RequireProperty(errors, motor, "ExternalVelocity", typeof(Vector3));
        RequireProperty(errors, motor, "CurrentPlatform", typeof(Transform));
        RequireProperty(errors, motor, "PlatformVelocity", typeof(Vector3));
        RequireProperty(errors, motor, "StandingHeight", typeof(float));
        RequireProperty(errors, motor, "CurrentHeight", typeof(float));
        Require(errors, typeof(CharacterMotorSettings).GetField("externalVelocityDecay") != null,
            "CharacterMotorSettings.externalVelocityDecay가 없다.");
        Require(errors, typeof(CharacterMotorSettings).GetField("maxExternalSpeed") != null,
            "CharacterMotorSettings.maxExternalSpeed가 없다.");

        Type locomotion = typeof(PlayerLocomotion);
        RequireMethod(errors, locomotion, "Bind", typeof(void), typeof(PlayerMovement), typeof(OverburstCharacterMotor3D));
        RequireMethod(errors, locomotion, "ResolveMoveDirection", typeof(Vector3), typeof(Vector2), typeof(bool), typeof(Transform));
        RequireMethod(errors, locomotion, "Step", typeof(MotorStepResult), typeof(ActorMovementIntent), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float));
        RequireMethod(errors, locomotion, "RotateTowards", typeof(void), typeof(Vector3), typeof(float), typeof(float), typeof(float));
        RequireMethod(errors, locomotion, "RotateSmooth", typeof(void), typeof(Vector3), typeof(float), typeof(float));
        RequireProperty(errors, locomotion, "HorizontalVelocity", typeof(Vector3));

        Type combat = typeof(CombatMotionDriver);
        RequireMethod(errors, combat, "Bind", typeof(void), typeof(PlayerMovement), typeof(OverburstCharacterMotor3D), typeof(CombatTarget), typeof(float));
        RequireMethod(errors, combat, "ApplyEvadeDisplacement", typeof(Vector3), typeof(Vector3));
        RequireMethod(errors, combat, "ApplyWeaponRootMotion", typeof(Vector3), typeof(Vector3));
        RequireMethod(errors, combat, "ResolveSafeDisplacement", typeof(Vector3), typeof(Vector3));

        Type movement = typeof(PlayerMovement);
        RequireProperty(errors, movement, "Motor", typeof(OverburstCharacterMotor3D));
        RequireProperty(errors, movement, "Locomotion", typeof(PlayerLocomotion));
        RequireProperty(errors, movement, "CombatMotion", typeof(CombatMotionDriver));
        foreach (string fieldName in new[] { "characterMotor", "locomotion", "combatMotion", "externalVelocityDecay", "maxExternalSpeed" })
        {
            Require(errors, movement.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic) != null,
                "PlayerMovement 직렬화 필드가 없다: " + fieldName);
        }
        Require(errors, typeof(PlayerEvadeController).GetField("combatMotion", BindingFlags.Instance | BindingFlags.NonPublic) != null,
            "PlayerEvadeController.combatMotion이 없다.");
    }

    private static void ValidatePrefab(List<string> errors)
    {
        GameObject contents = null;
        try
        {
            contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            Require(errors, contents != null, "PF_PlayerActor 로드 실패.");
            if (contents == null)
                return;
            PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
            Require(errors, actors.Length == 1, "PlayerActorRuntime이 1개가 아니다: " + actors.Length);
            if (actors.Length != 1)
                return;
            GameObject actor = actors[0].gameObject;
            PlayerMovement movement = actor.GetComponent<PlayerMovement>();
            OverburstCharacterMotor3D motor = actor.GetComponent<OverburstCharacterMotor3D>();
            PlayerLocomotion[] locomotions = actor.GetComponents<PlayerLocomotion>();
            CombatMotionDriver[] combatDrivers = actor.GetComponents<CombatMotionDriver>();
            PlayerEvadeController evade = actor.GetComponent<PlayerEvadeController>();
            CombatTarget target = actor.GetComponent<CombatTarget>();
            Require(errors, movement != null && motor != null && evade != null && target != null,
                "기존 플레이어 구성 요소가 누락됐다.");
            Require(errors, locomotions.Length == 1, "PlayerLocomotion이 1개가 아니다: " + locomotions.Length);
            Require(errors, combatDrivers.Length == 1, "CombatMotionDriver가 1개가 아니다: " + combatDrivers.Length);
            if (movement == null || motor == null || evade == null || target == null
                || locomotions.Length != 1 || combatDrivers.Length != 1)
                return;
            PlayerLocomotion locomotion = locomotions[0];
            CombatMotionDriver combat = combatDrivers[0];
            Require(errors, movement.Motor == motor, "PlayerMovement.characterMotor 참조 오류.");
            Require(errors, movement.Locomotion == locomotion, "PlayerMovement.locomotion 참조 오류.");
            Require(errors, movement.CombatMotion == combat, "PlayerMovement.combatMotion 참조 오류.");
            Require(errors, motor.BoundMovement == movement, "motor.movement 참조 오류.");
            Require(errors, locomotion.BoundMovement == movement && locomotion.Motor == motor,
                "PlayerLocomotion 참조 오류.");
            Require(errors, combat.BoundMovement == movement && combat.Motor == motor && combat.CombatTarget == target,
                "CombatMotionDriver 참조 오류.");
            SerializedObject serializedEvade = new SerializedObject(evade);
            Require(errors, serializedEvade.FindProperty("combatMotion")?.objectReferenceValue == combat,
                "PlayerEvadeController.combatMotion 참조 오류.");
            SerializedObject serializedMovement = new SerializedObject(movement);
            RequireFloat(errors, serializedMovement, "walkSpeed", 4.5f);
            RequireFloat(errors, serializedMovement, "runSpeed", 7.8f);
            RequireFloat(errors, serializedMovement, "acceleration", 55f);
            RequireFloat(errors, serializedMovement, "deceleration", 70f);
            RequireFloat(errors, serializedMovement, "externalVelocityDecay", 8f);
            RequireFloat(errors, serializedMovement, "maxExternalSpeed", 20f);
        }
        catch (Exception exception)
        {
            errors.Add("prefab 검사 실패: " + exception.Message);
        }
        finally
        {
            if (contents != null)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ValidateOwnership(List<string> errors)
    {
        string runtimeRoot = ResolveProjectPath("Assets/ProjectOverburst");
        string motorPath = ResolveProjectPath("Assets/ProjectOverburst/03_Features/Player/Runtime/OverburstCharacterMotor3D.cs");
        int motorCalls = 0;
        foreach (string file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file);
            int calls = Count(source, "controller.Move(") + Count(source, "characterController.Move(");
            if (string.Equals(file, motorPath, StringComparison.OrdinalIgnoreCase))
                motorCalls += calls;
            else
                Require(errors, calls == 0, "모터 밖 CharacterController.Move: " + file);
        }
        Require(errors, motorCalls == 1, "모터의 CharacterController.Move 호출 수가 1이 아니다: " + motorCalls);

        string movementSource = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/03_Features/Player/Runtime/PlayerMovement.cs"));
        string combatSource = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/03_Features/Player/Runtime/CombatMotionDriver.cs"));
        Require(errors, !movementSource.Contains("CapsuleCastNonAlloc") && !movementSource.Contains("OverlapCapsuleNonAlloc"),
            "PlayerMovement에 전투 충돌 조회가 남았다.");
        Require(errors, combatSource.Contains("CapsuleCastNonAlloc") && combatSource.Contains("OverlapCapsuleNonAlloc"),
            "CombatMotionDriver에 전투 충돌 조회가 없다.");
    }

    private static void ValidateForbiddenScope(List<string> errors)
    {
        try
        {
            string output = RunGit("status --porcelain=v1 --untracked-files=all");
            foreach (string raw in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Length < 4)
                    continue;
                string path = raw.Substring(3).Trim().Trim('"').Replace('\\', '/');
                if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("ThirdParty/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Assets/ThirdParty/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Assets/ProjectOverburst/00_Scenes/", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("금지 범위 변경: " + path);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add("git 범위 검사 실패: " + exception.Message);
        }
    }

    private static void RequireMethod(List<string> errors, Type owner, string name, Type result, params Type[] parameters)
    {
        MethodInfo method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
        Require(errors, method != null && method.ReturnType == result, owner.Name + "." + name + " signature 오류.");
    }

    private static void RequireProperty(List<string> errors, Type owner, string name, Type result)
    {
        PropertyInfo property = owner.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Require(errors, property != null && property.PropertyType == result, owner.Name + "." + name + " property 오류.");
    }

    private static void RequireFloat(List<string> errors, SerializedObject owner, string name, float expected)
    {
        SerializedProperty property = owner.FindProperty(name);
        Require(errors, property != null && Mathf.Approximately(property.floatValue, expected),
            "PlayerMovement." + name + "가 " + expected + "와 다르다.");
    }

    private static int Count(string source, string value)
    {
        int count = 0;
        for (int index = 0; (index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0; index += value.Length)
            count++;
        return count;
    }

    private static string RunGit(string arguments)
    {
        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using (Process process = Process.Start(info))
        {
            if (process == null)
                throw new InvalidOperationException("git 시작 실패.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(error);
            return output;
        }
    }

    private static string ResolveProjectPath(string assetPath)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string path = Path.GetFullPath(Path.Combine(root, assetPath));
        return path;
    }

    private static void Require(List<string> errors, bool condition, string message)
    {
        if (!condition)
            errors.Add(message);
    }
}
