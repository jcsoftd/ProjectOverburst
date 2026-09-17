using System.Collections.Generic;
using DunGen;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonRoomVisibilityController : MonoBehaviour
{
    [SerializeField] private RuntimeDungeon runtimeDungeon;
    [SerializeField] private AdjacentRoomCulling roomCulling;
    [SerializeField] private bool pauseParticleSystems = true;
    [SerializeField] private bool pauseAudioSources = true;
    [SerializeField] private bool pauseAnimators = true;

    private readonly Dictionary<Tile, TileAmbientState> hiddenTileStates =
        new();
    private PlayerContext subscribedPlayer;
    private DungeonGenerator subscribedGenerator;
    private AdjacentRoomCulling subscribedCulling;
    private Transform leaderTarget;
    private int visibilityChangeCount;
    private int leaderTargetRevision;

    public RuntimeDungeon RuntimeDungeon => runtimeDungeon;
    public AdjacentRoomCulling RoomCulling => roomCulling;
    public Transform LeaderTarget => leaderTarget;
    public int HiddenTileCount => hiddenTileStates.Count;
    public int VisibilityChangeCount => visibilityChangeCount;
    public int LeaderTargetRevision => leaderTargetRevision;
    public int PausedParticleSystemCount =>
        CountPausedComponents(state => state.ParticleSystems.Count);
    public int PausedAudioSourceCount =>
        CountPausedComponents(state => state.AudioSources.Count);
    public int PausedAnimatorCount =>
        CountPausedComponents(state => state.Animators.Count);

    public void Configure(
        RuntimeDungeon configuredRuntimeDungeon,
        AdjacentRoomCulling configuredRoomCulling)
    {
        runtimeDungeon = configuredRuntimeDungeon;
        roomCulling = configuredRoomCulling;
        RefreshBindings();
    }

    public bool IsTileManagedAsHidden(Tile tile)
    {
        return tile != null && hiddenTileStates.ContainsKey(tile);
    }

    public void RefreshGeneratedContent(Dungeon dungeon)
    {
        RefreshBindings();
        if (roomCulling != null
            && roomCulling.isActiveAndEnabled
            && dungeon != null)
        {
            roomCulling.SetDungeon(dungeon);
        }
        else
        {
            RestoreAllTileStates();
        }
    }

    public void RefreshBindings()
    {
        if (runtimeDungeon == null)
            runtimeDungeon = GetComponent<RuntimeDungeon>();
        if (roomCulling == null)
            roomCulling = GetComponent<AdjacentRoomCulling>();

        BindCulling();
        BindGenerator();
        BindPlayer();
        RefreshLeaderTarget();
    }

    private void OnEnable()
    {
        RefreshBindings();
    }

    private void Update()
    {
        RefreshBindings();
    }

    private void OnDisable()
    {
        UnbindPlayer();
        UnbindGenerator();
        UnbindCulling();
        RestoreAllTileStates();
        leaderTarget = null;
    }

    private void BindCulling()
    {
        if (subscribedCulling == roomCulling)
            return;

        UnbindCulling();
        subscribedCulling = roomCulling;
        if (subscribedCulling != null)
        {
            subscribedCulling.TileVisibilityChanged +=
                HandleTileVisibilityChanged;
        }
    }

    private void UnbindCulling()
    {
        if (subscribedCulling != null)
        {
            subscribedCulling.TileVisibilityChanged -=
                HandleTileVisibilityChanged;
        }
        subscribedCulling = null;
    }

    private void BindGenerator()
    {
        DungeonGenerator generator =
            runtimeDungeon != null ? runtimeDungeon.Generator : null;
        if (subscribedGenerator == generator)
            return;

        UnbindGenerator();
        subscribedGenerator = generator;
        if (subscribedGenerator != null)
            subscribedGenerator.Cleared += HandleDungeonCleared;
    }

    private void UnbindGenerator()
    {
        if (subscribedGenerator != null)
            subscribedGenerator.Cleared -= HandleDungeonCleared;
        subscribedGenerator = null;
    }

    private void BindPlayer()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        if (subscribedPlayer == context)
            return;

        UnbindPlayer();
        subscribedPlayer = context;
        if (subscribedPlayer != null)
            subscribedPlayer.CurrentActorChanged += HandleLeaderChanged;
    }

    private void UnbindPlayer()
    {
        if (subscribedPlayer != null)
            subscribedPlayer.CurrentActorChanged -= HandleLeaderChanged;
        subscribedPlayer = null;
    }

    private void RefreshLeaderTarget()
    {
        PlayerActorRuntime leader =
            subscribedPlayer != null ? subscribedPlayer.CurrentActor : null;
        Transform nextTarget = leader != null ? leader.transform : null;
        if (leaderTarget == nextTarget)
            return;

        leaderTarget = nextTarget;
        leaderTargetRevision++;
        if (roomCulling != null)
            roomCulling.TargetOverride = leaderTarget;
    }

    private void HandleLeaderChanged(PlayerActorRuntime leader)
    {
        Transform nextTarget = leader != null ? leader.transform : null;
        if (leaderTarget == nextTarget)
            return;

        leaderTarget = nextTarget;
        leaderTargetRevision++;
        if (roomCulling != null)
            roomCulling.TargetOverride = leaderTarget;
    }

    private void HandleDungeonCleared()
    {
        RestoreAllTileStates();
    }

    private void HandleTileVisibilityChanged(Tile tile, bool visible)
    {
        if (tile == null)
            return;

        visibilityChangeCount++;
        if (visible)
            RestoreTileState(tile);
        else
            PauseTileState(tile);
    }

    private void PauseTileState(Tile tile)
    {
        if (hiddenTileStates.ContainsKey(tile))
            return;

        TileAmbientState state = new(tile);
        if (pauseParticleSystems)
            PauseParticleSystems(tile, state);
        if (pauseAudioSources)
            PauseAudioSources(tile, state);
        if (pauseAnimators)
            PauseAnimators(tile, state);
        hiddenTileStates.Add(tile, state);
    }

    private static void PauseParticleSystems(
        Tile tile,
        TileAmbientState state)
    {
        ParticleSystem[] systems =
            tile.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem system = systems[i];
            if (system == null
                || !system.gameObject.activeInHierarchy
                || !system.isPlaying)
            {
                continue;
            }

            state.ParticleSystems.Add(system);
            system.Pause(false);
        }
    }

    private static void PauseAudioSources(
        Tile tile,
        TileAmbientState state)
    {
        AudioSource[] sources =
            tile.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null
                || !source.isActiveAndEnabled
                || !source.isPlaying)
            {
                continue;
            }

            state.AudioSources.Add(source);
            source.Pause();
        }
    }

    private static void PauseAnimators(
        Tile tile,
        TileAmbientState state)
    {
        Animator[] animators =
            tile.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator == null
                || !animator.isActiveAndEnabled
                || Mathf.Approximately(animator.speed, 0f)
                || IsGameplayAnimator(animator))
            {
                continue;
            }

            state.Animators.Add(
                new AnimatorState(animator, animator.speed));
            animator.speed = 0f;
        }
    }

    private static bool IsGameplayAnimator(Animator animator)
    {
        return animator.GetComponentInParent<Door>() != null
            || animator.GetComponentInParent<DungeonPortalEntry>() != null
            || animator.GetComponentInParent<DungeonPortalExit>() != null;
    }

    private void RestoreTileState(Tile tile)
    {
        if (!hiddenTileStates.TryGetValue(
                tile,
                out TileAmbientState state))
        {
            return;
        }

        state.Restore();
        hiddenTileStates.Remove(tile);
    }

    private void RestoreAllTileStates()
    {
        foreach (TileAmbientState state in hiddenTileStates.Values)
            state.Restore();
        hiddenTileStates.Clear();
    }

    private int CountPausedComponents(
        System.Func<TileAmbientState, int> selector)
    {
        int count = 0;
        foreach (TileAmbientState state in hiddenTileStates.Values)
            count += selector(state);
        return count;
    }

    private sealed class TileAmbientState
    {
        public TileAmbientState(Tile tile)
        {
            Tile = tile;
        }

        public Tile Tile { get; }
        public List<ParticleSystem> ParticleSystems { get; } = new();
        public List<AudioSource> AudioSources { get; } = new();
        public List<AnimatorState> Animators { get; } = new();

        public void Restore()
        {
            for (int i = 0; i < ParticleSystems.Count; i++)
            {
                ParticleSystem system = ParticleSystems[i];
                if (system != null && system.isPaused)
                    system.Play(false);
            }

            for (int i = 0; i < AudioSources.Count; i++)
            {
                AudioSource source = AudioSources[i];
                if (source != null)
                    source.UnPause();
            }

            for (int i = 0; i < Animators.Count; i++)
            {
                AnimatorState animatorState = Animators[i];
                if (animatorState.Animator != null)
                    animatorState.Animator.speed =
                        animatorState.Speed;
            }
        }
    }

    private readonly struct AnimatorState
    {
        public AnimatorState(Animator animator, float speed)
        {
            Animator = animator;
            Speed = speed;
        }

        public Animator Animator { get; }
        public float Speed { get; }
    }
}
