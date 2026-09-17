using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// GOAL D 정적 계약: F 입력 단일 소유, 결정적 후보 선택, 프리팹 연결, 표면별 walk/run/land 클립을 검사한다.
public static class OverburstGoalDValidator
{
    public const string MenuPath = "OVERBURST/Codex/Validate/Interaction/Validate GOAL D Interaction and Footsteps";

    [MenuItem(MenuPath)]
    public static void ValidateFromMenu() => UnityEngine.Debug.Log(ValidateAndReport());

    public static string ValidateAndReport()
    {
        List<string> errors = new List<string>();
        ValidateInputOwnership(errors);
        ValidateContracts(errors);
        ValidatePrefab(errors);
        ValidateProfiles(errors);
        ValidateProtectedScope(errors);
        if (errors.Count > 0)
            throw new InvalidOperationException("[OverburstGoalDValidator] FAIL\n- " + string.Join("\n- ", errors));
        return "[OverburstGoalDValidator] PASS\n"
            + "- Interact 입력 소유자 1개, availability→priority→angle→distance→stable ID 선택 확인\n"
            + "- Portal/Entry/Exit/Stash/Merchant/Loot 공통 계약 연결 확인\n"
            + "- PF_PlayerActor 통합 상호작용 3종 및 SurfaceResolver/FootstepEmitter/전용 AudioSource 연결 확인\n"
            + "- Concrete/Grass/Gravel/Wood walk/run/land 각 4 clips, 보호 범위 확인";
    }

    private static void ValidateInputOwnership(List<string> errors)
    {
        string root = ResolveProjectPath("Assets/ProjectOverburst");
        string[] owners = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("InteractPressedThisFrame"))
            .Select(path => path.Substring(root.Length + 1).Replace('\\', '/'))
            .ToArray();
        Require(errors, owners.Length == 2, "InteractPressedThisFrame 참조 파일 수가 2가 아니다: " + string.Join(", ", owners));
        Require(errors, owners.Any(path => path.EndsWith("01_Core/Input/Runtime/PlayerInputFacade.cs", StringComparison.OrdinalIgnoreCase)),
            "PlayerInputFacade Interact 계약이 없다.");
        Require(errors, owners.Any(path => path.EndsWith("02_Shared/Interaction/Runtime/PlayerInteractionController.cs", StringComparison.OrdinalIgnoreCase)),
            "PlayerInteractionController가 Interact를 소비하지 않는다.");

        foreach (string path in new[]
        {
            "Assets/ProjectOverburst/01_Core/SceneFlow/Runtime/HubScenePortal.cs",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Flow/DungeonPortalEntry.cs",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Flow/DungeonPortalExit.cs",
            "Assets/ProjectOverburst/03_Features/Items/Runtime/ItemSystem/Stash/StashInteractable.cs",
            "Assets/ProjectOverburst/03_Features/Items/Runtime/Shop/Interaction/GeneralGoodsMerchantInteractable.cs",
            "Assets/ProjectOverburst/03_Features/Items/Runtime/ItemSystem/Pickups/PlayerPickupInteractor.cs",
        })
        {
            string source = File.ReadAllText(ResolveProjectPath(path));
            Require(errors, source.Contains("IInteractable"), path + " 공통 상호작용 계약 누락.");
            Require(errors, !source.Contains("InteractPressedThisFrame"), path + "가 F 입력을 직접 소비한다.");
        }
    }

    private static void ValidateContracts(List<string> errors)
    {
        string director = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/02_Shared/Interaction/Runtime/InteractionDirector.cs"));
        Require(errors, director.Contains("candidate.IsInteractionAvailable(actor)"), "availability 판정이 없다.");
        Require(errors, director.Contains("priority > other.priority"), "priority 정렬이 없다.");
        Require(errors, director.Contains("angle < other.angle"), "angle 정렬이 없다.");
        Require(errors, director.Contains("distanceSqr < other.distanceSqr"), "distance 정렬이 없다.");
        Require(errors, director.Contains("string.CompareOrdinal(stableId, other.stableId)"), "stable ID tie-break가 없다.");
        Require(errors, director.Contains("GameplayInputBlocker.IsGameplayInputBlocked"), "UI 입력 차단 계약이 없다.");
        Require(errors, director.Contains("flow.IsSwitching"), "씬 전환 차단 계약이 없다.");

        string emitter = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/06_Audio/Runtime/Footsteps/FootstepEmitter.cs"));
        Require(errors, emitter.Contains("motor.DidLandThisStep"), "착지 이벤트 연결이 없다.");
        Require(errors, emitter.Contains("animationDrivenUntil"), "애니메이션 이벤트/거리 주기 중복 차단이 없다.");
        Require(errors, emitter.Contains("accumulatedDistance"), "거리 기반 보행 주기가 없다.");

        string resolver = File.ReadAllText(ResolveProjectPath("Assets/ProjectOverburst/06_Audio/Runtime/Footsteps/SurfaceResolver.cs"));
        int explicitIndex = resolver.IndexOf("SurfaceOverride explicitOverride", StringComparison.Ordinal);
        int materialIndex = resolver.IndexOf("surfaceCollider.sharedMaterial", StringComparison.Ordinal);
        int terrainIndex = resolver.IndexOf("ResolveTerrainLayerName", materialIndex + 1, StringComparison.Ordinal);
        Require(errors, explicitIndex >= 0 && materialIndex > explicitIndex && terrainIndex > materialIndex,
            "표면 우선순위 override→PhysicsMaterial→Terrain 계약 오류.");
    }

    private static void ValidatePrefab(List<string> errors)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(OverburstGoalDInteractionFootstepMigration.PrefabPath);
        if (contents == null)
        {
            errors.Add("PF_PlayerActor 프리팹 로드 실패.");
            return;
        }
        try
        {
            PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
            Require(errors, actors.Length == 1, "PlayerActorRuntime 수가 1이 아니다: " + actors.Length);
            if (actors.Length != 1)
                return;
            GameObject actor = actors[0].gameObject;
            RequireOne<InteractionDirector>(errors, actor);
            RequireOne<InteractionPromptPresenter>(errors, actor);
            RequireOne<PlayerInteractionController>(errors, actor);
            RequireOne<SurfaceResolver>(errors, actor);
            RequireOne<FootstepEmitter>(errors, actor);
            Transform audioRoot = actor.transform.Find("FootstepAudio");
            Require(errors, audioRoot != null, "FootstepAudio 자식이 없다.");
            if (audioRoot != null)
            {
                AudioSource[] sources = audioRoot.GetComponents<AudioSource>();
                Require(errors, sources.Length == 1, "발소리 AudioSource가 1개가 아니다: " + sources.Length);
                if (sources.Length == 1)
                {
                    Require(errors, !sources[0].playOnAwake && !sources[0].loop && Mathf.Approximately(sources[0].spatialBlend, 1f),
                        "발소리 AudioSource 설정 오류.");
                }
            }

            PlayerInteractionController controller = actor.GetComponent<PlayerInteractionController>();
            SurfaceResolver resolver = actor.GetComponent<SurfaceResolver>();
            FootstepEmitter emitter = actor.GetComponent<FootstepEmitter>();
            ValidateObjectReference(errors, controller, "actor", actors[0]);
            ValidateObjectReference(errors, controller, "inputFacade", actor.GetComponent<PlayerInputFacade>());
            ValidateObjectReference(errors, controller, "director", actor.GetComponent<InteractionDirector>());
            ValidateObjectReference(errors, controller, "promptPresenter", actor.GetComponent<InteractionPromptPresenter>());
            ValidateObjectReference(errors, emitter, "movement", actor.GetComponent<PlayerMovement>());
            ValidateObjectReference(errors, emitter, "motor", actor.GetComponent<OverburstCharacterMotor3D>());
            ValidateObjectReference(errors, emitter, "surfaceResolver", resolver);
            ValidateObjectReference(errors, emitter, "audioSource", audioRoot != null ? audioRoot.GetComponent<AudioSource>() : null);
            SerializedProperty rules = new SerializedObject(resolver).FindProperty("rules");
            Require(errors, rules != null && rules.arraySize == 6, "SurfaceResolver rule 수가 6이 아니다.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    private static void ValidateProfiles(List<string> errors)
    {
        foreach (string id in new[] { "Concrete", "Grass", "Gravel", "Wood" })
        {
            string path = OverburstGoalDInteractionFootstepMigration.ProfileFolder + "/SF_" + id + ".asset";
            SurfaceProfile profile = AssetDatabase.LoadAssetAtPath<SurfaceProfile>(path);
            Require(errors, profile != null, "표면 프로필 누락: " + path);
            if (profile == null)
                continue;
            SerializedObject serialized = new SerializedObject(profile);
            ValidateClipArray(errors, serialized.FindProperty("walkClips"), id + " walk");
            ValidateClipArray(errors, serialized.FindProperty("runClips"), id + " run");
            ValidateClipArray(errors, serialized.FindProperty("landingClips"), id + " land");
        }
    }

    private static void ValidateClipArray(List<string> errors, SerializedProperty clips, string label)
    {
        Require(errors, clips != null && clips.arraySize == 4, label + " clip 수가 4가 아니다.");
        if (clips == null)
            return;
        for (int i = 0; i < clips.arraySize; i++)
            Require(errors, clips.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip, label + " clip " + i + " 누락.");
    }

    private static void ValidateProtectedScope(List<string> errors)
    {
        string output = RunGit("status --porcelain=v1 --untracked-files=all");
        foreach (string raw in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Length < 4)
                continue;
            string path = raw.Substring(3).Trim().Trim('"').Replace('\\', '/');
            if (path.StartsWith("Assets/ThirdParty/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("보호 범위 변경: " + path);
            }
        }
    }

    private static void RequireOne<T>(List<string> errors, GameObject owner) where T : Component
    {
        T[] components = owner.GetComponents<T>();
        Require(errors, components.Length == 1, typeof(T).Name + " 수가 1이 아니다: " + components.Length);
    }

    private static void ValidateObjectReference(List<string> errors, UnityEngine.Object owner, string propertyName, UnityEngine.Object expected)
    {
        SerializedProperty property = owner != null ? new SerializedObject(owner).FindProperty(propertyName) : null;
        Require(errors, property != null && property.objectReferenceValue == expected,
            (owner != null ? owner.GetType().Name : "null") + "." + propertyName + " 연결 오류.");
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
            Require(new List<string>(), process != null, "git 시작 실패.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit(30000);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(error);
            return output;
        }
    }

    private static string ResolveProjectPath(string path)
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private static void Require(List<string> errors, bool condition, string message)
    {
        if (!condition)
            errors.Add(message);
    }
}
