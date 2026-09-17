using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// GOAL C: PersistentScene의 기존 단일 Camera를 Cinemachine 3 출력 카메라로 전환한다.
// 씬 변경은 Unity Editor API로만 수행하고, 실패하면 실행 전 scene bytes로 복구한다.
public static class OverburstCinemachineCameraMigration
{
    public const string PersistentScenePath = "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    public const string RigRootName = "__OVERBURST_CINEMACHINE_RIG__";
    public const string FocusName = "CameraFocus";
    public const string VirtualCameraName = "QuarterViewCinemachineCamera";
    public const string MenuPath = "OVERBURST/Codex/Migrate/Camera/Apply GOAL C Cinemachine Camera";
    public const float ReferenceDistance = 20f;
    public const float LegacyFieldOfView = 38f;
    public static readonly float ReferenceOrthographicSize =
        ReferenceDistance * Mathf.Tan(LegacyFieldOfView * 0.5f * Mathf.Deg2Rad);
    public const int EnvironmentOcclusionMask = (1 << 0) | (1 << 6) | (1 << 9);

    [MenuItem(MenuPath)]
    public static void ApplyFromMenu()
    {
        Debug.Log(ApplyAndReport());
    }

    public static string ApplyAndReport()
    {
        string fullScenePath = ResolveProjectPath(PersistentScenePath);
        byte[] beforeBytes = File.ReadAllBytes(fullScenePath);
        string beforeHash = Sha256(beforeBytes);

        try
        {
            Scene scene = EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
            ApplyScene(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, PersistentScenePath))
                throw new InvalidOperationException("PersistentScene 저장 실패.");
            AssetDatabase.SaveAssets();

            byte[] afterBytes = File.ReadAllBytes(fullScenePath);
            string afterHash = Sha256(afterBytes);
            string state = string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase)
                ? "NO_CHANGE"
                : "APPLIED";
            return "[OverburstCinemachineCameraMigration] " + state + "\n"
                + "- scene=" + PersistentScenePath + "\n"
                + "- package=com.unity.cinemachine@3.1.5\n"
                + "- projection=Orthographic size=" + ReferenceOrthographicSize.ToString("F3") + "\n"
                + "- before=" + beforeHash + "\n"
                + "- after=" + afterHash;
        }
        catch
        {
            File.WriteAllBytes(fullScenePath, beforeBytes);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
            throw;
        }
    }

    private static void ApplyScene(Scene scene)
    {
        List<Camera> cameras = FindInScene<Camera>(scene);
        if (cameras.Count != 1)
            throw new InvalidOperationException("PersistentScene Camera 수가 1이 아니다: " + cameras.Count);
        List<AudioListener> listeners = FindInScene<AudioListener>(scene);
        if (listeners.Count != 1)
            throw new InvalidOperationException("PersistentScene AudioListener 수가 1이 아니다: " + listeners.Count);

        Camera outputCamera = cameras[0];
        QuarterViewCamera quarter = outputCamera.GetComponent<QuarterViewCamera>();
        if (quarter == null)
            throw new InvalidOperationException("출력 Camera에 QuarterViewCamera가 없다.");

        CinemachineBrain brain = GetOrAdd<CinemachineBrain>(outputCamera.gameObject);
        brain.ShowDebugText = false;
        brain.ShowCameraFrustum = false;
        brain.IgnoreTimeScale = true;
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
        brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.35f);
        brain.LensModeOverride = new CinemachineBrain.LensModeOverrideSettings
        {
            Enabled = true,
            DefaultMode = LensSettings.OverrideModes.Orthographic,
        };

        GameObject rigRoot = FindRoot(scene, RigRootName);
        if (rigRoot == null)
        {
            rigRoot = new GameObject(RigRootName);
            SceneManager.MoveGameObjectToScene(rigRoot, scene);
        }
        rigRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        rigRoot.transform.localScale = Vector3.one;

        Transform focus = FindOrCreateChild(rigRoot.transform, FocusName);
        Transform virtualCameraTransform = FindOrCreateChild(rigRoot.transform, VirtualCameraName);
        GameObject virtualCameraObject = virtualCameraTransform.gameObject;

        CinemachineCamera virtualCamera = GetOrAdd<CinemachineCamera>(virtualCameraObject);
        CinemachineFollow follow = GetOrAdd<CinemachineFollow>(virtualCameraObject);
        CinemachineRotationComposer composer = GetOrAdd<CinemachineRotationComposer>(virtualCameraObject);
        CinemachineDeoccluder deoccluder = GetOrAdd<CinemachineDeoccluder>(virtualCameraObject);
        CinemachineConfiner3D confiner = GetOrAdd<CinemachineConfiner3D>(virtualCameraObject);
        CinemachineImpulseListener impulseListener = GetOrAdd<CinemachineImpulseListener>(virtualCameraObject);
        CinemachineImpulseSource impulseSource = GetOrAdd<CinemachineImpulseSource>(rigRoot);
        OverburstCinemachineCameraRig rig = GetOrAdd<OverburstCinemachineCameraRig>(rigRoot);

        Transform target = quarter.CurrentTarget;
        Vector3 targetOffset = ReadVector3(quarter, "targetOffset");
        float distance = Mathf.Max(0.01f, ReadFloat(quarter, "distance", ReferenceDistance));
        float pitch = ReadFloat(quarter, "pitch", 45f);
        float yaw = ReadFloat(quarter, "yaw", 0f);
        Vector3 focusPosition = target != null ? target.position + targetOffset : Vector3.zero;
        focus.position = focusPosition;
        focus.rotation = Quaternion.identity;
        focus.localScale = Vector3.one;

        virtualCamera.Target = new CameraTarget
        {
            TrackingTarget = focus,
            LookAtTarget = focus,
        };
        virtualCamera.Priority = 100;
        LensSettings lens = LensSettings.FromCamera(outputCamera);
        lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
        lens.FieldOfView = LegacyFieldOfView;
        lens.OrthographicSize = ReferenceOrthographicSize * distance / ReferenceDistance;
        lens.Dutch = 0f;
        virtualCamera.Lens = lens;

        TrackerSettings tracker = TrackerSettings.Default;
        tracker.BindingMode = BindingMode.WorldSpace;
        tracker.PositionDamping = Vector3.zero;
        tracker.RotationDamping = Vector3.zero;
        tracker.QuaternionDamping = 0f;
        follow.TrackerSettings = tracker;
        Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);
        follow.FollowOffset = viewRotation * Vector3.back * distance;

        ScreenComposerSettings composition = ScreenComposerSettings.Default;
        composition.ScreenPosition = Vector2.zero;
        composition.DeadZone.Enabled = false;
        composition.HardLimits.Enabled = false;
        composer.Composition = composition;
        composer.CenterOnActivate = true;
        composer.TargetOffset = Vector3.zero;
        composer.Damping = Vector2.zero;

        deoccluder.CollideAgainst = EnvironmentOcclusionMask;
        deoccluder.TransparentLayers = 0;
        deoccluder.IgnoreTag = "Player";
        deoccluder.MinimumDistanceFromTarget = 0.3f;
        deoccluder.AvoidObstacles = new CinemachineDeoccluder.ObstacleAvoidance
        {
            Enabled = true,
            DistanceLimit = 0f,
            MinimumOcclusionTime = 0f,
            CameraRadius = 0.35f,
            Strategy = CinemachineDeoccluder.ObstacleAvoidance.ResolutionStrategy.PullCameraForward,
            MaximumEffort = 4,
            SmoothingTime = 0f,
            Damping = 0.4f,
            DampingWhenOccluded = 0.15f,
        };
        confiner.SlowingDistance = Mathf.Max(0f, confiner.SlowingDistance);

        impulseListener.ApplyAfter = CinemachineCore.Stage.Noise;
        impulseListener.ChannelMask = OverburstCinemachineCameraRig.CombatImpulseChannel;
        impulseListener.Gain = 1f;
        impulseListener.Use2DDistance = false;
        impulseListener.UseCameraSpace = true;
        impulseListener.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.UseLargest;
        CinemachineImpulseDefinition definition = impulseSource.ImpulseDefinition
            ?? new CinemachineImpulseDefinition();
        definition.ImpulseChannel = OverburstCinemachineCameraRig.CombatImpulseChannel;
        definition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Custom;
        definition.CustomImpulseShape = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        definition.ImpulseDuration = 0.2f;
        definition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        definition.DissipationDistance = 100f;
        definition.DissipationRate = 0f;
        definition.PropagationSpeed = 343f;
        impulseSource.ImpulseDefinition = definition;
        impulseSource.DefaultVelocity = Vector3.right;

        rig.Configure(
            outputCamera,
            brain,
            virtualCamera,
            focus,
            follow,
            composer,
            deoccluder,
            confiner,
            impulseSource,
            impulseListener,
            ReferenceDistance,
            ReferenceOrthographicSize);

        SerializedObject serializedQuarter = new SerializedObject(quarter);
        SerializedProperty rigProperty = serializedQuarter.FindProperty("cinemachineRig");
        if (rigProperty == null)
            throw new InvalidOperationException("QuarterViewCamera.cinemachineRig 직렬화 필드가 없다.");
        rigProperty.objectReferenceValue = rig;
        serializedQuarter.ApplyModifiedPropertiesWithoutUndo();

        Vector3 cameraOffset = viewRotation * Vector3.back * distance;
        Vector3 cameraPosition = focusPosition + cameraOffset;
        Quaternion cameraRotation = Quaternion.LookRotation(focusPosition - cameraPosition, Vector3.up);
        virtualCameraTransform.SetPositionAndRotation(cameraPosition, cameraRotation);
        outputCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        outputCamera.orthographic = true;
        outputCamera.orthographicSize = lens.OrthographicSize;
        outputCamera.fieldOfView = LegacyFieldOfView;

        EditorUtility.SetDirty(outputCamera);
        EditorUtility.SetDirty(brain);
        EditorUtility.SetDirty(quarter);
        EditorUtility.SetDirty(rig);
        EditorUtility.SetDirty(virtualCamera);
        EditorUtility.SetDirty(follow);
        EditorUtility.SetDirty(composer);
        EditorUtility.SetDirty(deoccluder);
        EditorUtility.SetDirty(confiner);
        EditorUtility.SetDirty(impulseSource);
        EditorUtility.SetDirty(impulseListener);
    }

    private static List<T> FindInScene<T>(Scene scene) where T : Component
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<T>(true))
            .ToList();
    }

    private static GameObject FindRoot(Scene scene, string name)
    {
        return scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
    }

    private static Transform FindOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;
        GameObject created = new GameObject(name);
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static T GetOrAdd<T>(GameObject owner) where T : Component
    {
        T component = owner.GetComponent<T>();
        return component != null ? component : owner.AddComponent<T>();
    }

    private static float ReadFloat(UnityEngine.Object owner, string field, float fallback)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.floatValue : fallback;
    }

    private static Vector3 ReadVector3(UnityEngine.Object owner, string field)
    {
        SerializedProperty property = new SerializedObject(owner).FindProperty(field);
        return property != null ? property.vector3Value : Vector3.zero;
    }

    private static string ResolveProjectPath(string assetPath)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private static string Sha256(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
    }
}
