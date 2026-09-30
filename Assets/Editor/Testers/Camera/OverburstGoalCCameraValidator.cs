using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// GOAL C 정적 계약: 패키지 버전, 단일 출력 카메라, Cinemachine 파이프라인과 기존 API 호환을 검사한다.
public static class OverburstGoalCCameraValidator
{
    public const string MenuPath = "OVERBURST/Codex/Validate/Camera/Validate GOAL C Cinemachine Camera";

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu()
    {
        UnityEngine.Debug.Log(ValidateAndReport());
    }

    public static string ValidateAndReport()
    {
        List<string> errors = new List<string>();
        ValidatePackage(errors);
        ValidateTypes(errors);
        ValidateScene(errors);
        ValidateCallers(errors);
        ValidateProtectedScope(errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGoalCCameraValidator] FAIL\n- " + string.Join("\n- ", errors));
        return "[OverburstGoalCCameraValidator] PASS\n"
            + "- Cinemachine 3.1.5, Camera/AudioListener 1개, Orthographic 6.887, Follow/RotationComposer/Deoccluder/Confiner/Impulse 연결 확인\n"
            + "- QuarterViewCamera 호환 API, PlayerCameraBinder/SceneFlow/CombatHitFeedbackService 호출 계약, 보호 범위 확인";
    }

    public static string ValidateRuntimeContracts()
    {
        List<string> errors = new List<string>();
        ValidatePackage(errors);
        ValidateTypes(errors);
        ValidateScene(errors);
        ValidateCallers(errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGoalCCameraValidator] RUNTIME FAIL\n- " + string.Join("\n- ", errors));
        return "[OverburstGoalCCameraValidator] RUNTIME-CONTRACTS PASS";
    }

    private static void ValidatePackage(List<string> errors)
    {
        UnityEditor.PackageManager.PackageInfo package =
            UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.cinemachine");
        Require(errors, package != null, "Cinemachine 패키지를 찾을 수 없다.");
        if (package != null)
        {
            Require(errors, package.name == "com.unity.cinemachine", "Cinemachine package name 오류: " + package.name);
            Require(errors, package.version == "3.1.5", "Cinemachine version이 3.1.5가 아니다: " + package.version);
        }

        string manifest = File.ReadAllText(ResolveProjectPath("Packages/manifest.json"));
        string packageLock = File.ReadAllText(ResolveProjectPath("Packages/packages-lock.json"));
        Require(errors, manifest.Contains("\"com.unity.cinemachine\": \"3.1.5\""), "manifest에 Cinemachine 3.1.5 고정이 없다.");
        Require(errors, packageLock.Contains("\"com.unity.cinemachine\"") && packageLock.Contains("\"version\": \"3.1.5\""),
            "packages-lock에 Cinemachine 3.1.5 고정이 없다.");
    }

    private static void ValidateTypes(List<string> errors)
    {
        Type quarter = typeof(QuarterViewCamera);
        RequireMethod(errors, quarter, "SetTarget", typeof(void), typeof(Transform));
        RequireMethod(errors, quarter, "SetYaw", typeof(void), typeof(float));
        RequireMethod(errors, quarter, "RequestCombatImpact", typeof(void),
            typeof(CombatCameraRequestKind), typeof(Vector3), typeof(Vector3), typeof(bool),
            typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float),
            typeof(float), typeof(float), typeof(float));
        RequireProperty(errors, quarter, "CurrentTarget", typeof(Transform));
        RequireProperty(errors, quarter, "CinemachineRig", typeof(OverburstCinemachineCameraRig));
        RequireProperty(errors, quarter, "UsesCinemachine", typeof(bool));

        Type rig = typeof(OverburstCinemachineCameraRig);
        RequireMethod(errors, rig, "SynchronizeView", typeof(void), typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(bool));
        RequireMethod(errors, rig, "SetConfinerVolume", typeof(void), typeof(Collider), typeof(float));
        RequireMethod(errors, rig, "SetPriority", typeof(void), typeof(int));
        RequireMethod(errors, rig, "BoostCombatMicroShake", typeof(void), typeof(float), typeof(float), typeof(float));
        RequireMethod(errors, rig, "EmitCombatImpact", typeof(void),
            typeof(CombatCameraRequestKind), typeof(Vector2), typeof(float), typeof(float), typeof(float),
            typeof(float), typeof(float), typeof(float), typeof(float), typeof(float));
    }

    private static void ValidateScene(List<string> errors)
    {
        Scene scene = SceneManager.GetSceneByName(PersistentSceneFlow.PersistentSceneName);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Scene active = EditorSceneManager.GetActiveScene();
            if (active.path == OverburstCinemachineCameraMigration.PersistentScenePath)
                scene = active;
        }
        Require(errors, scene.IsValid() && scene.isLoaded
            && scene.path == OverburstCinemachineCameraMigration.PersistentScenePath,
            "로드된 PersistentScene을 찾을 수 없다.");
        if (!scene.IsValid() || !scene.isLoaded
            || scene.path != OverburstCinemachineCameraMigration.PersistentScenePath)
            return;
        if (!Application.isPlaying)
            Require(errors, !scene.isDirty, "PersistentScene이 dirty다.");

        List<Camera> cameras = FindInScene<Camera>(scene);
        List<AudioListener> listeners = FindInScene<AudioListener>(scene);
        List<QuarterViewCamera> quarters = FindInScene<QuarterViewCamera>(scene);
        List<OverburstCinemachineCameraRig> rigs = FindInScene<OverburstCinemachineCameraRig>(scene);
        List<CinemachineCamera> virtualCameras = FindInScene<CinemachineCamera>(scene);
        Require(errors, cameras.Count == 1, "Camera 수가 1이 아니다: " + cameras.Count);
        Require(errors, listeners.Count == 1, "AudioListener 수가 1이 아니다: " + listeners.Count);
        Require(errors, quarters.Count == 1, "QuarterViewCamera 수가 1이 아니다: " + quarters.Count);
        Require(errors, rigs.Count == 1, "OverburstCinemachineCameraRig 수가 1이 아니다: " + rigs.Count);
        Require(errors, virtualCameras.Count == 1, "CinemachineCamera 수가 1이 아니다: " + virtualCameras.Count);
        if (cameras.Count != 1 || listeners.Count != 1 || quarters.Count != 1 || rigs.Count != 1 || virtualCameras.Count != 1)
            return;

        Camera output = cameras[0];
        QuarterViewCamera quarter = quarters[0];
        OverburstCinemachineCameraRig rig = rigs[0];
        CinemachineCamera virtualCamera = virtualCameras[0];
        CinemachineBrain brain = output.GetComponent<CinemachineBrain>();
        Require(errors, output.GetComponent<AudioListener>() == listeners[0], "AudioListener가 출력 Camera에 있지 않다.");
        Require(errors, output.GetComponent<QuarterViewCamera>() == quarter, "QuarterViewCamera가 출력 Camera에 있지 않다.");
        Require(errors, output.orthographic, "출력 Camera가 Orthographic이 아니다.");
        RequireNear(errors, output.orthographicSize, OverburstCinemachineCameraMigration.ReferenceOrthographicSize, 0.001f, "출력 Camera Orthographic Size");
        Require(errors, brain != null, "출력 Camera에 CinemachineBrain이 없다.");
        if (brain != null)
        {
            Require(errors, brain.UpdateMethod == CinemachineBrain.UpdateMethods.ManualUpdate, "Brain UpdateMethod가 ManualUpdate가 아니다.");
            Require(errors, brain.IgnoreTimeScale, "Brain IgnoreTimeScale이 꺼져 있다.");
            Require(errors, brain.LensModeOverride.Enabled
                && brain.LensModeOverride.DefaultMode == LensSettings.OverrideModes.Orthographic,
                "Brain LensModeOverride가 Orthographic이 아니다.");
        }

        Require(errors, rig.IsConfigured, "Cinemachine rig 참조가 완전하지 않다.");
        Require(errors, quarter.CinemachineRig == rig && quarter.UsesCinemachine, "QuarterViewCamera 어댑터 연결 오류.");
        Require(errors, quarter.CurrentTarget != null && quarter.CurrentTarget.GetComponent<PlayerActorRuntime>() != null,
            "QuarterViewCamera 대상이 플레이어 액터가 아니다.");
        Require(errors, rig.OutputCamera == output && rig.Brain == brain && rig.VirtualCamera == virtualCamera,
            "rig 출력 참조 오류.");
        Require(errors, rig.FocusTarget != null
            && virtualCamera.Target.TrackingTarget == rig.FocusTarget
            && virtualCamera.Target.LookAtTarget == rig.FocusTarget,
            "Cinemachine Tracking/LookAt 대상 오류.");
        Require(errors, virtualCamera.Lens.ModeOverride == LensSettings.OverrideModes.Orthographic,
            "Virtual Camera lens가 Orthographic이 아니다.");
        RequireNear(errors, virtualCamera.Lens.OrthographicSize, OverburstCinemachineCameraMigration.ReferenceOrthographicSize, 0.001f,
            "Virtual Camera Orthographic Size");
        Require(errors, rig.Follow != null
            && rig.Follow.TrackerSettings.BindingMode == BindingMode.WorldSpace
            && rig.Follow.TrackerSettings.PositionDamping == Vector3.zero,
            "CinemachineFollow WorldSpace/무중복 damping 계약 오류.");
        Require(errors, rig.RotationComposer != null && rig.RotationComposer.Damping == Vector2.zero,
            "RotationComposer가 없거나 중복 damping이 있다.");
        Require(errors, rig.Deoccluder != null
            && rig.Deoccluder.AvoidObstacles.Enabled
            && rig.Deoccluder.CollideAgainst.value == OverburstCinemachineCameraMigration.EnvironmentOcclusionMask,
            "Deoccluder 환경 mask/회피 설정 오류.");
        Require(errors, rig.Confiner != null, "Confiner 3D 확장점이 없다.");
        Require(errors, rig.ImpulseSource != null
            && rig.ImpulseSource.ImpulseDefinition.ImpulseChannel == OverburstCinemachineCameraRig.CombatImpulseChannel,
            "ImpulseSource channel 오류.");
        Require(errors, rig.ImpulseListener != null
            && rig.ImpulseListener.ChannelMask == OverburstCinemachineCameraRig.CombatImpulseChannel
            && rig.ImpulseListener.UseCameraSpace
            && rig.ImpulseListener.SignalCombinationMode == CinemachineImpulseListener.SignalCombinationModes.UseLargest,
            "ImpulseListener channel/중복 억제 설정 오류.");
    }

    private static void ValidateCallers(List<string> errors)
    {
        string binder = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/03_Features/Player/Runtime/PlayerCameraBinder.cs"));
        string sceneFlow = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/01_Core/SceneFlow/Runtime/PersistentSceneFlow.cs"));
        string feedback = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/02_Shared/Combat/Runtime/Feedback/CombatHitFeedbackService.cs"));
        string quarter = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/01_Core/Camera/Runtime/QuarterViewCamera.cs"));
        string rig = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/01_Core/Camera/Runtime/OverburstCinemachineCameraRig.cs"));
        Require(errors, binder.Contains("quarterViewCamera.SetTarget(actor.transform)"), "PlayerCameraBinder SetTarget 호출 계약이 없다.");
        Require(errors, sceneFlow.Contains("cameraController.SetYaw(DungeonRunCameraYaw)"), "SceneFlow Dungeon yaw 계약이 없다.");
        Require(errors, feedback.Contains("cameraController.RequestCombatImpact("), "CombatHitFeedbackService 카메라 정책 호출이 없다.");
        Require(errors, quarter.Contains("cinemachineRig.EmitCombatImpact("), "QuarterViewCamera Impulse 어댑터 호출이 없다.");
        Require(errors, rig.Contains("brain.ManualUpdate();"), "Cinemachine 수동 최종 갱신 소유자가 없다.");
    }

    private static void ValidateProtectedScope(List<string> errors)
    {
        try
        {
            string output = RunGit("status --porcelain=v1 --untracked-files=all");
            foreach (string raw in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Length < 4)
                    continue;
                string path = raw.Substring(3).Trim().Trim('"').Replace('\\', '/');
                if (path.StartsWith("ThirdParty/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("Assets/ThirdParty/", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("보호 범위 변경: " + path);
                }
                if (path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                    && path != "Packages/manifest.json"
                    && path != "Packages/packages-lock.json")
                {
                    errors.Add("승인되지 않은 Packages 변경: " + path);
                }
            }
        }
        catch (Exception exception)
        {
            errors.Add("git 보호 범위 검사 실패: " + exception.Message);
        }
    }

    private static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToList();
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
        return Path.GetFullPath(Path.Combine(root, assetPath));
    }

    private static void RequireNear(List<string> errors, float actual, float expected, float tolerance, string label)
    {
        Require(errors, Mathf.Abs(actual - expected) <= tolerance,
            label + " 오류: actual=" + actual.ToString("F4") + ", expected=" + expected.ToString("F4"));
    }

    private static void Require(List<string> errors, bool condition, string message)
    {
        if (!condition)
            errors.Add(message);
    }
}
