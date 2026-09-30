using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// GOAL B1 정적 검증: 모터 소유권 + PlayerMovement 어댑터 + prefab 1모터/reference + 금지 범위.
// Play Mode나 Player 빌드를 사용하지 않는다.
public static class OverburstPlayerMotorValidator
{
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string MenuPath = "OVERBURST/Codex/Validate/Player/Validate GOAL B1 Motor";

    private static readonly string[] RequiredMotorProperties =
    {
        "Controller", "BoundMovement",
        "VerticalVelocity", "IsGrounded", "GroundNormal", "GroundContactNormal",
        "HasWalkableGround", "GroundGap", "GroundSurfaceHeight", "GroundSnapActive",
        "DidLandThisStep", "LandingFallSpeed", "ControllerPlanarVelocity",
    };

    private static readonly string[] RequiredMovementMethods =
    {
        "BindInputSource",
        "BeginLootAutoMove", "CancelLootAutoMove", "UpdateLootAutoMoveDestination",
        "BeginMeleeAttackMoveLock", "CancelWeaponActionLocks", "CancelWeaponAimStateForSwitch",
        "PrepareEvadeMotion", "ResolveSafeEvadeDisplacement", "ApplyWeaponRootMotionDisplacement",
        "ResolveMoveDirection", "ConsumeJumpAnimationRequest",
        "Stop", "ResetMotionAfterTeleport", "SetControlAuthority",
    };

    private static readonly string[] RequiredMovementProperties =
    {
        "Motor",
        "MoveInput", "MoveDirection", "BaseMoveSpeed",
        "IsGrounded", "GroundNormal", "IsEvading", "IsMeleeAttackMoveLocked",
        "IsAiming", "IsMeleeCombatStance", "IsMeleeGuarding",
        "IsMeleeCombatLocomotionMode", "VerticalVelocity", "GroundLayerMask",
        "AnimationMoveAmount", "IsLandingRecovering",
    };

    private static readonly string[] RequiredMovementSerializedFields =
    {
        "walkSpeed", "runSpeed", "aimMoveSpeedMultiplier", "quickFireMoveSpeedMultiplier",
        "acceleration", "deceleration", "airControl", "rotationSpeed",
        "explorationFacingSharpness", "useCameraRelativeMovement", "movementCamera",
        "movementInputSource", "characterMotor",
        "characterControllerSkinWidthRadiusRatio", "characterControllerSlopeLimit",
        "characterControllerStepOffset", "characterControllerMinMoveDistance",
        "debugForceAiming", "playerEquipment", "playerBuffController",
        "playerStaminaController", "playerEvadeController",
        "meleeAimCamera", "meleeFacingRotationSpeed",
        "meleeCombatMoveSpeed", "meleeCombatGuardMoveSpeed", "weaponRootMotionEnemyClearance",
        "jumpHeight", "gravity", "groundStickVelocity", "groundCheck", "groundCheckRadius", "groundLayer",
        "groundProbeStartOffset", "groundSnapDistance", "groundNormalSharpness",
        "landingSlowDuration", "landingSpeedMultiplier", "landingAnimationMultiplier", "landingMinFallSpeed",
    };

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        UnityEngine.Debug.Log(ValidateAndReport());
    }

    public static string ValidateAndReport()
    {
        List<string> errors = new List<string>();
        ValidateMotorApi(errors);
        ValidateMovementAdapter(errors);
        ValidatePrefab(errors);
        ValidateMoveOwnership(errors);
        ValidateForbiddenScope(errors);

        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstPlayerMotorValidator] FAIL\n- " + string.Join("\n- ", errors.ToArray()));
        return "[OverburstPlayerMotorValidator] PASS\n- 모터 소유권, 어댑터 API/직렬화, prefab 1모터/reference, 금지 범위 확인";
    }

    // Play Mode 안에서 호출하는 계약 검사. git/파일 범위 검사는 제외한다(Play collateral 오탐 방지).
    public static string ValidateRuntimeContracts()
    {
        List<string> errors = new List<string>();
        ValidateMotorApi(errors);
        ValidateMovementAdapter(errors);
        ValidatePrefab(errors);
        ValidateMoveOwnership(errors);

        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstPlayerMotorValidator] FAIL\n- " + string.Join("\n- ", errors.ToArray()));
        return "[OverburstPlayerMotorValidator] RUNTIME-CONTRACTS PASS";
    }

    private static void ValidateMotorApi(List<string> errors)
    {
        Type type = typeof(OverburstCharacterMotor3D);
        Require(errors, type != null, "OverburstCharacterMotor3D가 없다.");
        if (type == null)
            return;
        RequireMethod(errors, type, "BindMovement", typeof(void), typeof(PlayerMovement));
        RequireMethod(errors, type, "Configure", typeof(void), typeof(CharacterMotorSettings));
        RequireMethod(errors, type, "EnsureInitialized", typeof(void));
        RequireMethod(errors, type, "ProbeGround", typeof(void), typeof(float));
        RequireMethod(errors, type, "Step", typeof(MotorStepResult), typeof(MotorStepCommand), typeof(float));
        RequireMethod(errors, type, "MoveDirect", typeof(void), typeof(Vector3));
        RequireMethod(errors, type, "HaltMotion", typeof(void));
        RequireMethod(errors, type, "ResetMotion", typeof(void));
        foreach (string name in RequiredMotorProperties)
            Require(errors, type.GetProperty(name) != null, "OverburstCharacterMotor3D." + name + "이 없다.");
        RequireProperty(errors, type, "Controller", typeof(CharacterController));
        RequireProperty(errors, type, "BoundMovement", typeof(PlayerMovement));
        RequireProperty(errors, type, "VerticalVelocity", typeof(float));
        RequireProperty(errors, type, "IsGrounded", typeof(bool));
        RequireProperty(errors, type, "GroundNormal", typeof(Vector3));
        Require(errors, typeof(CharacterMotorSettings).IsValueType, "CharacterMotorSettings가 값 타입이 아니다.");
        Require(errors, typeof(MotorStepCommand).IsValueType, "MotorStepCommand가 값 타입이 아니다.");
        Require(errors, typeof(MotorStepResult).IsValueType, "MotorStepResult가 값 타입이 아니다.");
        foreach (string name in new[] { "gravity", "groundLayer", "maxFallSpeed", "slopeLimit", "groundSnapDistance" })
            Require(errors, typeof(CharacterMotorSettings).GetField(name) != null, "CharacterMotorSettings." + name + "이 없다.");
        foreach (string name in new[] { "targetHorizontalVelocity", "moveRate", "airControl", "jumpVelocity", "currentHorizontalVelocity" })
            Require(errors, typeof(MotorStepCommand).GetField(name) != null, "MotorStepCommand." + name + "이 없다.");
        foreach (string name in new[] { "horizontalVelocity", "isGrounded", "didLand", "landingFallSpeed" })
            Require(errors, typeof(MotorStepResult).GetField(name) != null, "MotorStepResult." + name + "이 없다.");
        Require(errors, OverburstCharacterMotor3D.DefaultMaxFallSpeed == 60f,
            "DefaultMaxFallSpeed가 60이 아니다. 기존 무제한 동등 migration 근거를 확인하라.");
    }

    private static void ValidateMovementAdapter(List<string> errors)
    {
        Type type = typeof(PlayerMovement);
        foreach (string name in RequiredMovementMethods)
            Require(errors, HasMethod(type, name), "PlayerMovement." + name + "이 없다.");
        foreach (string name in RequiredMovementProperties)
            Require(errors, type.GetProperty(name) != null, "PlayerMovement." + name + "이 없다.");
        foreach (string name in RequiredMovementSerializedFields)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Require(errors, field != null, "PlayerMovement 직렬화 필드 " + name + "이 없다.");
        }
        Require(errors, typeof(IActorMotor).IsAssignableFrom(type), "PlayerMovement가 IActorMotor를 잃었다.");
        RequireMethod(errors, type, "BindInputSource", typeof(void), typeof(PlayerMovementInputSource));
        RequireMethod(errors, type, "ApplyMovementIntent", typeof(void), typeof(ActorMovementIntent), typeof(float));
        RequireMethod(errors, type, "BeginLootAutoMove", typeof(bool), typeof(Vector3));
        RequireMethod(errors, type, "UpdateLootAutoMoveDestination", typeof(void), typeof(Vector3));
        RequireMethod(errors, type, "CancelLootAutoMove", typeof(void));
        RequireMethod(errors, type, "ResolveMoveDirection", typeof(Vector3), typeof(Vector2));
        RequireMethod(errors, type, "ResolveSafeEvadeDisplacement", typeof(Vector3), typeof(Vector3));
        RequireMethod(errors, type, "ApplyWeaponRootMotionDisplacement", typeof(void), typeof(Vector3));
        RequireMethod(errors, type, "ResetMotionAfterTeleport", typeof(void));
        RequireMethod(errors, type, "BeginMeleeAttackMoveLock", typeof(void), typeof(float));
        RequireMethod(errors, type, "BeginMeleeAttackMoveLock", typeof(void), typeof(float), typeof(Vector3));
        RequireProperty(errors, type, "Motor", typeof(OverburstCharacterMotor3D));
        RequireProperty(errors, type, "IsGrounded", typeof(bool));
        RequireProperty(errors, type, "VerticalVelocity", typeof(float));
    }

    private static void ValidatePrefab(List<string> errors)
    {
        GameObject contents = null;
        try
        {
            if (!File.Exists(ResolveProjectPath(PrefabPath)))
            {
                errors.Add("프리팹이 없다: " + PrefabPath);
                return;
            }
            contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (contents == null)
            {
                errors.Add("프리팹 로드에 실패했다: " + PrefabPath);
                return;
            }
            PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
            if (actors.Length != 1)
            {
                errors.Add("PlayerActorRuntime이 1개가 아니다: " + actors.Length);
                return;
            }
            GameObject actorObject = actors[0].gameObject;
            OverburstCharacterMotor3D[] motors = actorObject.GetComponents<OverburstCharacterMotor3D>();
            Require(errors, motors.Length == 1, "액터의 OverburstCharacterMotor3D가 1개가 아니다: " + motors.Length);
            if (motors.Length != 1)
                return;
            PlayerMovement movement = actorObject.GetComponent<PlayerMovement>();
            Require(errors, movement != null, "액터에 PlayerMovement가 없다.");
            if (movement == null)
                return;
            Require(errors, movement.Motor == motors[0], "PlayerMovement.characterMotor가 모터에 연결되지 않았다.");
            Require(errors, motors[0].BoundMovement == movement, "OverburstCharacterMotor3D.movement가 연결되지 않았다.");
            SerializedObject serializedMovement = new SerializedObject(movement);
            RequireSerializedFloat(errors, serializedMovement, "walkSpeed", 4.5f);
            RequireSerializedFloat(errors, serializedMovement, "runSpeed", 7.8f);
            RequireSerializedFloat(errors, serializedMovement, "acceleration", 55f);
            RequireSerializedFloat(errors, serializedMovement, "deceleration", 70f);
            RequireSerializedFloat(errors, serializedMovement, "airControl", 0.35f);
            RequireSerializedFloat(errors, serializedMovement, "characterControllerSkinWidthRadiusRatio", 0.1f);
            RequireSerializedFloat(errors, serializedMovement, "characterControllerSlopeLimit", 45f);
            RequireSerializedFloat(errors, serializedMovement, "characterControllerStepOffset", 0.3f);
            RequireSerializedFloat(errors, serializedMovement, "characterControllerMinMoveDistance", 0f);
            RequireSerializedFloat(errors, serializedMovement, "jumpHeight", 1.6f);
            RequireSerializedFloat(errors, serializedMovement, "gravity", -25f);
            RequireSerializedFloat(errors, serializedMovement, "groundStickVelocity", -2f);
            RequireSerializedFloat(errors, serializedMovement, "groundCheckRadius", 0.22f);
            RequireSerializedFloat(errors, serializedMovement, "groundProbeStartOffset", 0.08f);
            RequireSerializedFloat(errors, serializedMovement, "groundSnapDistance", 0.32f);
            RequireSerializedFloat(errors, serializedMovement, "groundNormalSharpness", 22f);
            SerializedProperty groundLayer = serializedMovement.FindProperty("groundLayer");
            Require(errors, groundLayer != null && groundLayer.intValue == 64,
                "PlayerMovement.groundLayer가 baseline 64와 다르다.");
            CharacterController controller = actorObject.GetComponent<CharacterController>();
            Require(errors, controller != null, "액터에 CharacterController가 없다.");
            if (controller != null)
            {
                Require(errors, Mathf.Approximately(controller.height, 2f), "CharacterController.height가 baseline 2와 다르다.");
                Require(errors, Mathf.Approximately(controller.radius, 0.5f), "CharacterController.radius가 baseline 0.5와 다르다.");
                Require(errors, Mathf.Approximately(controller.slopeLimit, 45f), "CharacterController.slopeLimit가 baseline 45와 다르다.");
                Require(errors, Mathf.Approximately(controller.stepOffset, 0.3f), "CharacterController.stepOffset가 baseline 0.3과 다르다.");
                Require(errors, Mathf.Approximately(controller.skinWidth, 0.08f), "CharacterController.skinWidth가 baseline 0.08과 다르다.");
                Require(errors, Mathf.Approximately(controller.minMoveDistance, 0.001f), "CharacterController.minMoveDistance가 baseline 0.001과 다르다.");
                Require(errors, controller.center == Vector3.zero, "CharacterController.center가 baseline zero와 다르다.");
            }
        }
        catch (Exception exception)
        {
            errors.Add("프리팹 검사 실패: " + exception.Message);
        }
        finally
        {
            if (contents != null)
                PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ValidateMoveOwnership(List<string> errors)
    {
        string runtimeRoot = ResolveProjectPath("Assets/ProjectOverburst");
        string motorPath = ResolveProjectPath("Assets/ProjectOverburst/03_Features/Player/Runtime/OverburstCharacterMotor3D.cs");
        int motorMoveCalls = 0;
        foreach (string filePath in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(filePath);
            int count = CountOccurrences(source, "controller.Move(")
                + CountOccurrences(source, "characterController.Move(");
            if (string.Equals(filePath, motorPath, StringComparison.OrdinalIgnoreCase))
            {
                motorMoveCalls += count;
                continue;
            }

            Require(errors, count == 0,
                "CharacterController.Move 직접 호출이 모터 밖에 남았다: "
                + filePath.Substring(ResolveProjectPath("Assets").Length + 1).Replace('\\', '/'));
        }

        Require(errors, motorMoveCalls == 1,
            "OverburstCharacterMotor3D의 실제 CharacterController.Move 호출은 1개여야 한다: " + motorMoveCalls);
    }

    private static void ValidateForbiddenScope(List<string> errors)
    {
        try
        {
            string output = RunGit("status --porcelain=v1 --untracked-files=all");
            string[] lines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.Length < 4)
                    continue;
                string path = line.Substring(3).Trim().Trim('"').Replace('\\', '/');
                if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("ThirdParty/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Assets/ThirdParty/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Assets/ProjectOverburst/00_Scenes/", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("금지 범위 변경: " + path);
                    continue;
                }
                if (path.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase)
                    && !line.StartsWith("??"))
                {
                    errors.Add("Input Actions 변경: " + path);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add("범위 검사 실패: " + exception.Message);
        }
    }

    private static bool HasMethod(Type type, string name)
    {
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < methods.Length; i++)
        {
            if (methods[i].Name == name)
                return true;
        }
        return false;
    }

    private static void RequireMethod(
        List<string> errors,
        Type owner,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        MethodInfo method = owner.GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            parameterTypes,
            null);
        Require(errors, method != null, owner.Name + "." + name + " signature가 없다.");
        if (method != null)
            Require(errors, method.ReturnType == returnType, owner.Name + "." + name + " return type이 다르다.");
    }

    private static void RequireProperty(List<string> errors, Type owner, string name, Type propertyType)
    {
        PropertyInfo property = owner.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Require(errors, property != null && property.PropertyType == propertyType,
            owner.Name + "." + name + " property type이 다르다.");
    }

    private static void RequireSerializedFloat(
        List<string> errors,
        SerializedObject owner,
        string propertyName,
        float expected)
    {
        SerializedProperty property = owner.FindProperty(propertyName);
        Require(errors, property != null && Mathf.Approximately(property.floatValue, expected),
            "PlayerMovement." + propertyName + "가 baseline " + expected + "와 다르다.");
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
    }

    private static string RunGit(string arguments)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using (Process process = Process.Start(startInfo))
        {
            if (process == null)
                throw new InvalidOperationException("git 실행에 실패했다.");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);
            if (process.ExitCode != 0)
                throw new InvalidOperationException("git 종료 코드 " + process.ExitCode + ": " + process.StandardError.ReadToEnd());
            return output;
        }
    }

    private static string ResolveProjectPath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string filePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        string rootWithSeparator = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("에셋 파일 경로가 프로젝트 루트 하위가 아니다: " + filePath);
        return filePath;
    }

    private static void Require(List<string> errors, bool condition, string message)
    {
        if (!condition)
            errors.Add(message);
    }
}
