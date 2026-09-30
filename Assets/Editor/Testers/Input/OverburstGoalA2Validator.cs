using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// GOAL A2 검증: Input Actions 계약 + prefab component/reference + 상태 API/축 + 남은 직접 입력 예외 목록.
// Play Mode나 Player 빌드를 사용하지 않는다.
public static class OverburstGoalA2Validator
{
    public const string AssetPath = "Assets/ProjectOverburst/01_Core/Settings/Input/InputSystem_Actions.inputactions";
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string MenuPath = "OVERBURST/Codex/Validate/Player/Validate GOAL A2";

    // 승인된 직접 입력 예외. 그 외 Assets/ProjectOverburst 일반 런타임의 실제 장치 직접 읽기는 금지.
    public const string SmokeRunnerPath =
        "Assets/ProjectOverburst/01_Core/Input/Runtime/OverburstDevelopmentSmokeRunner.cs";
    public const string DebugReturnInputPath =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Flow/DungeonRunDebugReturnInput.cs";
    public const string SmokeRunnerReason =
        "개발 검증 입력 주입 예외: -overburstSmokeOutput 개발 빌드에서만 InputSystem.QueueStateEvent로 검증 입력을 주입한다.";
    public const string DebugReturnReason =
        "Debug/Validation 런타임 도구 예외: DungeonRun F8 quick extract는 DebugValidation map에 임의 기능을 추가하지 않으므로 Keyboard 직접 읽기를 예외 목록으로 관리한다.";

    private static readonly string[] ForbiddenTokens =
    {
        "Keyboard.current",
        "Mouse.current",
        "Gamepad.current",
        "Pointer.current",
        "Touchscreen.current",
        "QueueStateEvent",
        "Input.mousePosition",
        "Input.GetKey",
        "Input.GetMouseButton",
        "Input.GetAxis",
    };

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        Debug.Log(ValidateAndReport());
    }

    public static string ValidateAndReport()
    {
        List<string> errors = new List<string>();

        // 1) Input Actions 계약은 기존 validator에 누적된다(18 action).
        try
        {
            OverburstGameplayInputValidator.ValidateAndReport();
        }
        catch (Exception exception)
        {
            errors.Add("Input 계약 실패: " + exception.Message);
        }

        // 2) prefab component/reference.
        ValidatePrefab(errors);

        // 3) 상태 API/축.
        ValidateStateApi(errors);

        // 3b) 상태 우선순위/해제/파괴 정리 EditMode 동작 검사. Play Mode가 아니다.
        ValidateStateBehavior(errors);

        // 4) 남은 직접 입력 예외 목록.
        List<string> remaining = FindRemainingDirectInputs(errors);

        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGoalA2Validator] FAIL\n- " + string.Join("\n- ", errors.ToArray()));

        string remainingReport = remaining.Count == 0
            ? "남은 직접 입력 없음(예외 2건 제외)"
            : "예외 목록: " + string.Join(", ", remaining.ToArray());
        return "[OverburstGoalA2Validator] PASS\n"
            + "- Input 18-action 계약, prefab 1+1/reference, 상태 3축 API 확인\n"
            + "- 상태 우선순위/해제/파괴 정리 EditMode 동작 확인(Play Mode 아님)\n"
            + "- " + remainingReport + "\n"
            + "- 예외1 " + SmokeRunnerPath + ": " + SmokeRunnerReason + "\n"
            + "- 예외2 " + DebugReturnInputPath + ": " + DebugReturnReason;
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
            PlayerInputFacade[] facades = contents.GetComponentsInChildren<PlayerInputFacade>(true);
            PlayerStateCoordinator[] coordinators = contents.GetComponentsInChildren<PlayerStateCoordinator>(true);
            if (facades.Length != 1)
                errors.Add("PF_PlayerActor의 PlayerInputFacade가 1개가 아니다: " + facades.Length);
            if (coordinators.Length != 1)
                errors.Add("PF_PlayerActor의 PlayerStateCoordinator가 1개가 아니다: " + coordinators.Length);
            if (facades.Length == 1)
            {
                InputActionAssetFacadeCheck(facades[0], errors);
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

    private static void InputActionAssetFacadeCheck(PlayerInputFacade facade, List<string> errors)
    {
        try
        {
            var source = facade.SourceAsset;
            if (source == null)
            {
                errors.Add("PlayerInputFacade.sourceAsset이 비어 있다.");
                return;
            }
            string path = AssetDatabase.GetAssetPath(source);
            if (!string.Equals(path, AssetPath, StringComparison.OrdinalIgnoreCase))
                errors.Add("PlayerInputFacade.sourceAsset 경로 불일치: " + path);
        }
        catch (Exception exception)
        {
            errors.Add("Facade 레퍼런스 검사 실패: " + exception.Message);
        }
    }

    private static void ValidateStateApi(List<string> errors)
    {
        try
        {
            RequireEnum(errors, typeof(PlayerConditionState),
                new[] { "Normal", "InputBlocked", "Stunned", "Dead" });
            RequireEnum(errors, typeof(PlayerLocomotionState),
                new[] { "Idle", "Moving", "Airborne", "Evading", "ControlledMove" });
            RequireEnum(errors, typeof(PlayerActionState),
                new[] { "None", "Attack", "GuardOrAim", "Interacting" });

            Type type = typeof(PlayerStateCoordinator);
            foreach (string name in new[]
            {
                "RequestCondition", "ReleaseCondition",
                "RequestInputBlocked", "RequestStun", "ReleaseStun",
                "RequestDeath", "ReleaseDeath",
                "RequestLocomotion", "ReleaseLocomotion",
                "RequestAction", "ReleaseAction",
                "RequestInteracting", "ReleaseInteracting",
                "ClearTransientStates", "ResetAllStates", "BindActor",
            })
                Require(errors, type.GetMethod(name) != null, "PlayerStateCoordinator." + name + "이 없다.");
            foreach (string name in new[]
            {
                "CurrentCondition", "CurrentLocomotion", "CurrentAction",
                "CanMove", "CanAttack", "CanInteract",
            })
                Require(errors, type.GetProperty(name) != null, "PlayerStateCoordinator." + name + "이 없다.");
            foreach (string name in new[] { "ConditionChanged", "LocomotionChanged", "ActionChanged" })
                Require(errors, type.GetEvent(name) != null, "PlayerStateCoordinator." + name + " 이벤트가 없다.");
            Require(errors, type.GetProperty("Current") != null, "PlayerStateCoordinator.Current가 없다.");

            Type blocker = typeof(GameplayInputBlocker);
            Require(errors, blocker.GetEvent("BlockStateChanged") != null,
                "GameplayInputBlocker.BlockStateChanged가 없다.");
            Require(errors, blocker.GetMethod("Block") != null, "GameplayInputBlocker.Block이 없다.");
            Require(errors, blocker.GetMethod("Unblock") != null, "GameplayInputBlocker.Unblock이 없다.");
            Require(errors, blocker.GetMethod("SetBlocked") != null, "GameplayInputBlocker.SetBlocked이 없다.");
            Require(errors, blocker.GetProperty("IsGameplayInputBlocked") != null,
                "GameplayInputBlocker.IsGameplayInputBlocked가 없다.");
        }
        catch (Exception exception)
        {
            errors.Add("상태 API 검사 실패: " + exception.Message);
        }
    }

    private static void ValidateStateBehavior(List<string> errors)
    {
        // 일시 GameObject로 실제 요청/해제/우선순위/파괴 정리를 구동한다. Play Mode가 아니다.
        GameObject host = null;
        GameObject sourceA = null;
        GameObject sourceB = null;
        GameObject sourceC = null;
        GameObject doomedSource = null;
        Scene validationScene = default;
        Scene originalScene = SceneManager.GetActiveScene();
        PlayerStateCoordinator savedCurrent = PlayerStateCoordinator.Current;
        try
        {
            validationScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            if (SceneManager.GetActiveScene() != validationScene && !SceneManager.SetActiveScene(validationScene))
                throw new InvalidOperationException("상태 동작 검사용 임시 Scene을 활성화할 수 없다.");
            host = new GameObject("A2Validator_StateProbe");
            host.hideFlags = HideFlags.HideAndDontSave;
            PlayerStateCoordinator coordinator = host.AddComponent<PlayerStateCoordinator>();
            int conditionEvents = 0;
            int locomotionEvents = 0;
            int actionEvents = 0;
            coordinator.ConditionChanged += _ => conditionEvents++;
            coordinator.LocomotionChanged += _ => locomotionEvents++;
            coordinator.ActionChanged += _ => actionEvents++;

            sourceA = NewProbeSource("A2Validator_SourceA");
            sourceB = NewProbeSource("A2Validator_SourceB");
            sourceC = NewProbeSource("A2Validator_SourceC");

            coordinator.RequestInputBlocked(sourceA);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.InputBlocked, "상태 동작: InputBlocked가 반영되지 않았다.");
            coordinator.RequestStun(sourceB);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.Stunned, "상태 동작: Stunned 우선순위가 깨졌다.");
            coordinator.RequestDeath(sourceC);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.Dead, "상태 동작: Dead 우선순위가 깨졌다.");
            Require(errors, !coordinator.CanMove && !coordinator.CanAttack && !coordinator.CanInteract, "상태 동작: Dead에서 질의가 막히지 않았다.");
            coordinator.ReleaseDeath(sourceC);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.Stunned, "상태 동작: Dead 해제 뒤 Stunned로 복귀하지 않았다.");
            coordinator.ReleaseStun(sourceB);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.InputBlocked, "상태 동작: Stun 해제 뒤 InputBlocked로 복귀하지 않았다.");
            coordinator.ReleaseCondition(sourceA);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.Normal, "상태 동작: 전 해제 뒤 Normal로 복귀하지 않았다.");
            Require(errors, coordinator.CanMove && coordinator.CanAttack && coordinator.CanInteract, "상태 동작: Normal에서 질의가 허용되지 않았다.");

            coordinator.RequestLocomotion(sourceA, PlayerLocomotionState.Moving);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.Moving, "상태 동작: Moving이 반영되지 않았다.");
            coordinator.RequestLocomotion(sourceB, PlayerLocomotionState.ControlledMove);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.ControlledMove, "상태 동작: ControlledMove 우선순위가 깨졌다.");
            coordinator.RequestLocomotion(sourceC, PlayerLocomotionState.Evading);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.Evading, "상태 동작: Evading 우선순위가 깨졌다.");
            coordinator.ReleaseLocomotion(sourceC);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.ControlledMove, "상태 동작: Evading 해제 뒤 ControlledMove로 복귀하지 않았다.");
            coordinator.ReleaseLocomotion(sourceB);
            coordinator.ReleaseLocomotion(sourceA);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.Idle, "상태 동작: 이동 전 해제 뒤 Idle로 복귀하지 않았다.");

            coordinator.RequestInteracting(sourceA);
            Require(errors, coordinator.CurrentAction == PlayerActionState.Interacting, "상태 동작: Interacting이 반영되지 않았다.");
            coordinator.RequestAction(sourceB, PlayerActionState.Attack);
            Require(errors, coordinator.CurrentAction == PlayerActionState.Attack, "상태 동작: Attack 우선순위가 깨졌다.");
            coordinator.ReleaseAction(sourceB);
            Require(errors, coordinator.CurrentAction == PlayerActionState.Interacting, "상태 동작: Attack 해제 뒤 Interacting으로 복귀하지 않았다.");
            coordinator.ReleaseInteracting(sourceA);
            Require(errors, coordinator.CurrentAction == PlayerActionState.None, "상태 동작: 상호작용 해제 뒤 None으로 복귀하지 않았다.");

            // 파괴된 소유자는 LateUpdate 정리로 해당 축만 복구하고 해당 이벤트만 올린다.
            doomedSource = NewProbeSource("A2Validator_Doomed");
            coordinator.RequestLocomotion(doomedSource, PlayerLocomotionState.Evading);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.Evading, "상태 동작: Evading 사전 조건이 깨졌다.");
            int conditionBefore = conditionEvents;
            int locomotionBefore = locomotionEvents;
            int actionBefore = actionEvents;
            UnityEngine.Object.DestroyImmediate(doomedSource);
            doomedSource = null;
            MethodInfo lateUpdate = typeof(PlayerStateCoordinator).GetMethod(
                "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            Require(errors, lateUpdate != null, "상태 동작: LateUpdate 정리를 찾을 수 없다.");
            if (lateUpdate != null)
                lateUpdate.Invoke(coordinator, null);
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.Idle, "상태 동작: 파괴 소유자 정리 뒤 Idle로 복귀하지 않았다.");
            Require(errors, locomotionEvents == locomotionBefore + 1, "상태 동작: 파괴 정리 locomotion 이벤트가 1회가 아니다.");
            Require(errors, conditionEvents == conditionBefore && actionEvents == actionBefore, "상태 동작: 파괴 정리가 다른 축 이벤트를 올렸다.");

            // ClearTransientStates는 Dead만 남기고 transient을 비운다.
            coordinator.RequestDeath(sourceC);
            coordinator.RequestLocomotion(sourceA, PlayerLocomotionState.Moving);
            coordinator.RequestInteracting(sourceA);
            coordinator.ClearTransientStates();
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.Dead, "상태 동작: ClearTransientStates가 Dead를 지웠다.");
            Require(errors, coordinator.CurrentLocomotion == PlayerLocomotionState.Idle, "상태 동작: ClearTransientStates가 이동 transient을 남겼다.");
            Require(errors, coordinator.CurrentAction == PlayerActionState.None, "상태 동작: ClearTransientStates가 행동 transient을 남겼다.");
            coordinator.ReleaseDeath(sourceC);
            Require(errors, coordinator.CurrentCondition == PlayerConditionState.Normal, "상태 동작: 최종 Normal 복귀가 안 됐다.");
        }
        catch (Exception exception)
        {
            errors.Add("상태 동작 검사 실패: " + exception.Message);
        }
        finally
        {
            if (doomedSource != null)
                UnityEngine.Object.DestroyImmediate(doomedSource);
            if (sourceA != null)
                UnityEngine.Object.DestroyImmediate(sourceA);
            if (sourceB != null)
                UnityEngine.Object.DestroyImmediate(sourceB);
            if (sourceC != null)
                UnityEngine.Object.DestroyImmediate(sourceC);
            if (host != null)
                UnityEngine.Object.DestroyImmediate(host);
            RestoreCoordinatorCurrent(savedCurrent);
            if (originalScene.IsValid())
                SceneManager.SetActiveScene(originalScene);
            if (validationScene.IsValid())
                EditorSceneManager.CloseScene(validationScene, true);
        }
    }

    private static GameObject NewProbeSource(string name)
    {
        GameObject source = new GameObject(name);
        source.hideFlags = HideFlags.HideAndDontSave;
        return source;
    }

    private static void RestoreCoordinatorCurrent(PlayerStateCoordinator savedCurrent)
    {
        try
        {
            MethodInfo setter = typeof(PlayerStateCoordinator).GetProperty("Current").GetSetMethod(true);
            if (setter != null)
                setter.Invoke(null, new object[] { savedCurrent });
        }
        catch (Exception)
        {
        }
    }

    private static List<string> FindRemainingDirectInputs(List<string> errors)
    {
        List<string> remaining = new List<string>();
        try
        {
            string root = ResolveProjectPath("Assets/ProjectOverburst");
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                string relative = ToAssetPath(file);
                if (string.Equals(relative, SmokeRunnerPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(relative, DebugReturnInputPath, StringComparison.OrdinalIgnoreCase))
                {
                    remaining.Add(relative);
                    continue;
                }
                string text = File.ReadAllText(file);
                string code = StripLineComments(text);
                foreach (string token in ForbiddenTokens)
                {
                    if (code.IndexOf(token, StringComparison.Ordinal) >= 0)
                    {
                        errors.Add("직접 입력 잔류: " + relative + " (" + token + ")");
                        break;
                    }
                }
            }
            // 예외 파일 존재 확인.
            foreach (string required in new[] { SmokeRunnerPath, DebugReturnInputPath })
            {
                if (!File.Exists(ResolveProjectPath(required)))
                    errors.Add("예외 파일이 없다: " + required);
            }
        }
        catch (Exception exception)
        {
            errors.Add("직접 입력 스캔 실패: " + exception.Message);
        }
        return remaining;
    }

    private static void RequireEnum(List<string> errors, Type enumType, string[] names)
    {
        if (enumType == null || !enumType.IsEnum)
        {
            errors.Add("enum이 없다: " + (enumType != null ? enumType.Name : "null"));
            return;
        }
        foreach (string name in names)
        {
            try
            {
                object value = Enum.Parse(enumType, name);
                Require(errors, Enum.IsDefined(enumType, value), "enum 값이 없다: " + enumType.Name + "." + name);
            }
            catch (Exception)
            {
                errors.Add("enum 값이 없다: " + enumType.Name + "." + name);
            }
        }
    }

    private static string StripLineComments(string text)
    {
        // // 주석과 using 행의 오탐을 줄인다. 문자열 리터럴 내 토큰은 검사 conservatively 유지.
        string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
                line = line.Substring(0, comment);
            lines[i] = line;
        }
        return string.Join("\n", lines);
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

    private static string ToAssetPath(string fullPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string full = Path.GetFullPath(fullPath);
        string prefix = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return full;
        return full.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static void Require(List<string> errors, bool condition, string message)
    {
        if (!condition)
            errors.Add(message);
    }
}
