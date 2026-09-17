using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

// GOAL D: 플레이어 프리팹에 공통 상호작용 조정기와 지면별 발소리를 Unity API로 연결한다.
public static class OverburstGoalDInteractionFootstepMigration
{
    public const string MenuPath = "OVERBURST/Codex/Migrate/Interaction/Apply GOAL D Interaction and Footsteps";
    public const string PrefabPath = "Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab";
    public const string ProfileFolder = "Assets/ProjectOverburst/06_Audio/SurfaceProfiles";
    private const string FootstepRoot = "Assets/ThirdParty/11_사운드/Pro Sound Collection/Footsteps";

    [MenuItem(MenuPath)]
    public static void ApplyFromMenu() => Debug.Log(ApplyAndReport());

    public static string ApplyAndReport()
    {
        string prefabFile = ResolveProjectPath(PrefabPath);
        Require(File.Exists(prefabFile), "플레이어 프리팹이 없다: " + PrefabPath);
        byte[] prefabBefore = File.ReadAllBytes(prefabFile);
        string beforeHash = Sha256(prefabBefore);
        List<string> createdAssets = new List<string>();

        try
        {
            EnsureFolders(ProfileFolder);
            SurfaceProfile concrete = LoadOrCreateProfile("Concrete", createdAssets);
            SurfaceProfile grass = LoadOrCreateProfile("Grass", createdAssets);
            SurfaceProfile gravel = LoadOrCreateProfile("Gravel", createdAssets);
            SurfaceProfile wood = LoadOrCreateProfile("Wood", createdAssets);
            ConfigureProfile(concrete, "concrete");
            ConfigureProfile(grass, "grass");
            ConfigureProfile(gravel, "gravel");
            ConfigureProfile(wood, "wood");

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            Require(contents != null, "플레이어 프리팹 로드 실패.");
            try
            {
                PlayerActorRuntime[] actors = contents.GetComponentsInChildren<PlayerActorRuntime>(true);
                Require(actors.Length == 1, "PlayerActorRuntime 수가 1이 아니다: " + actors.Length);
                GameObject actorObject = actors[0].gameObject;
                PlayerMovement movement = RequireOne<PlayerMovement>(actorObject);
                OverburstCharacterMotor3D motor = RequireOne<OverburstCharacterMotor3D>(actorObject);
                PlayerInputFacade facade = RequireOne<PlayerInputFacade>(actorObject);

                InteractionDirector director = GetOrAddSingle<InteractionDirector>(actorObject);
                InteractionPromptPresenter presenter = GetOrAddSingle<InteractionPromptPresenter>(actorObject);
                PlayerInteractionController controller = GetOrAddSingle<PlayerInteractionController>(actorObject);
                SurfaceResolver resolver = GetOrAddSingle<SurfaceResolver>(actorObject);
                FootstepEmitter emitter = GetOrAddSingle<FootstepEmitter>(actorObject);

                Transform audioRoot = actorObject.transform.Find("FootstepAudio");
                if (audioRoot == null)
                {
                    GameObject created = new GameObject("FootstepAudio");
                    audioRoot = created.transform;
                    audioRoot.SetParent(actorObject.transform, false);
                }
                AudioSource source = GetOrAddSingle<AudioSource>(audioRoot.gameObject);
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.dopplerLevel = 0f;
                source.minDistance = 1.5f;
                source.maxDistance = 18f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;

                controller.Configure(actors[0], facade, director, presenter);
                resolver.Configure(
                    concrete,
                    movement.GroundLayerMask,
                    0.65f,
                    2.2f,
                    new[]
                    {
                        new SurfaceResolutionRule(null, "grass", grass),
                        new SurfaceResolutionRule(null, "wood", wood),
                        new SurfaceResolutionRule(null, "plank", wood),
                        new SurfaceResolutionRule(null, "gravel", gravel),
                        new SurfaceResolutionRule(null, "dirt", gravel),
                        new SurfaceResolutionRule(null, "dungeon", gravel),
                    });
                emitter.Configure(movement, motor, resolver, source, 1.35f, 1.8f, 2f);

                EditorUtility.SetDirty(director);
                EditorUtility.SetDirty(presenter);
                EditorUtility.SetDirty(controller);
                EditorUtility.SetDirty(resolver);
                EditorUtility.SetDirty(emitter);
                EditorUtility.SetDirty(source);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceSynchronousImport);
            byte[] prefabAfter = File.ReadAllBytes(prefabFile);
            string afterHash = Sha256(prefabAfter);
            string state = string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase)
                && createdAssets.Count == 0
                ? "NO_CHANGE"
                : "APPLIED";
            return "[OverburstGoalDInteractionFootstepMigration] " + state + "\n"
                + "- prefab=" + PrefabPath + "\n"
                + "- interaction=director/presenter/controller 각 1개\n"
                + "- footsteps=Concrete/Grass/Gravel/Wood, walk/run/land 각 4 clips\n"
                + "- before=" + beforeHash + "\n"
                + "- after=" + afterHash;
        }
        catch
        {
            File.WriteAllBytes(prefabFile, prefabBefore);
            for (int i = createdAssets.Count - 1; i >= 0; i--)
                AssetDatabase.DeleteAsset(createdAssets[i]);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            throw;
        }
    }

    private static SurfaceProfile LoadOrCreateProfile(string id, List<string> createdAssets)
    {
        string path = ProfileFolder + "/SF_" + id + ".asset";
        SurfaceProfile profile = AssetDatabase.LoadAssetAtPath<SurfaceProfile>(path);
        if (profile != null)
            return profile;
        profile = ScriptableObject.CreateInstance<SurfaceProfile>();
        profile.name = "SF_" + id;
        AssetDatabase.CreateAsset(profile, path);
        createdAssets.Add(path);
        return profile;
    }

    private static void ConfigureProfile(SurfaceProfile profile, string token)
    {
        profile.Configure(
            char.ToUpperInvariant(token[0]) + token.Substring(1),
            LoadClips(token, "walk"),
            LoadClips(token, "run"),
            LoadClips(token, "land"),
            new Vector2(0.72f, 0.9f),
            new Vector2(0.94f, 1.06f));
        EditorUtility.SetDirty(profile);
    }

    private static AudioClip[] LoadClips(string surface, string motion)
    {
        AudioClip[] clips = new AudioClip[4];
        for (int i = 0; i < clips.Length; i++)
        {
            string path = FootstepRoot + "/footstep_" + surface + "_" + motion + "_0" + (i + 1) + ".wav";
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Require(clips[i] != null, "발소리 클립 누락: " + path);
        }
        return clips;
    }

    private static T RequireOne<T>(GameObject owner) where T : Component
    {
        T[] components = owner.GetComponents<T>();
        Require(components.Length == 1, typeof(T).Name + " 수가 1이 아니다: " + components.Length);
        return components[0];
    }

    private static T GetOrAddSingle<T>(GameObject owner) where T : Component
    {
        T[] components = owner.GetComponents<T>();
        Require(components.Length <= 1, typeof(T).Name + " 수가 2개 이상이다.");
        return components.Length == 1 ? components[0] : owner.AddComponent<T>();
    }

    private static void EnsureFolders(string assetFolder)
    {
        string[] segments = assetFolder.Split('/');
        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
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

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
