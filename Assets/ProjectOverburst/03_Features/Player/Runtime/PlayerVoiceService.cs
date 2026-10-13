using System;
using UnityEngine;

public enum PlayerVoiceChoice { Karen1, Karen2, Kimlinh, Rina, Off }

/// <summary>Player action voices, using the curated clip lists and the PC sound setting.</summary>
[DefaultExecutionOrder(-10000)]
[RequireComponent(typeof(AudioSource))]
public sealed class PlayerVoiceService : MonoBehaviour
{
    public const string ResourcePath = "Audio/PF_PlayerVoice";
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/Audio/PF_PlayerVoice.prefab";
    public enum VoiceKind { Attack, Hurt, Death, Jump }
    [Serializable]
    public sealed class VoiceSet
    {
        public string name;
        public AudioClip[] attack, hurt, death, jump;
        public AudioClip[] Clips(VoiceKind kind) => kind == VoiceKind.Attack ? attack
            : kind == VoiceKind.Hurt ? hurt : kind == VoiceKind.Death ? death : jump;
    }
    [SerializeField] private VoiceSet[] candidates;
    private static PlayerVoiceService instance;
    private AudioSource voice;
    private PlayerActorRuntime actor;
    private CombatHealth health;
    private int[,] previous;
    private PlayerVoiceChoice selection;
    public static PlayerVoiceService Current => instance;
    public VoiceSet[] Candidates => candidates;
    public int PlayedCount { get; private set; }
    public VoiceKind LastKind { get; private set; }
    public AudioClip LastClip => voice.clip;
    public void Configure(VoiceSet[] sets) => candidates = sets;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => instance = null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance) return;
        var prefab = Resources.Load<GameObject>(ResourcePath);
        if (prefab) Instantiate(prefab);
    }
    private void Awake()
    {
        if (instance && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        voice = GetComponent<AudioSource>();
        previous = new int[candidates.Length, 4];
        for (int i = 0; i < candidates.Length; i++)
            for (int k = 0; k < 4; k++) previous[i, k] = -1;
        selection = OverburstGameSettings.PlayerVoice;
    }
    private void OnEnable() => OverburstGameSettings.Changed += SettingsChanged;
    private void OnDisable()
    {
        OverburstGameSettings.Changed -= SettingsChanged;
        Unbind();
    }
    private void OnDestroy() { if (instance == this) instance = null; }
    private void SettingsChanged()
    {
        var next = OverburstGameSettings.PlayerVoice;
        if (next == selection) return;
        selection = next;
        voice.Stop();
        voice.clip = null;
    }
    private void Update()
    {
        var current = PlayerContext.Instance ? PlayerContext.Instance.CurrentActor : null;
        if (actor == current) return;
        Unbind(); actor = current; health = actor ? actor.Health : null;
        if (health) { health.OnDamageResolved += Damaged; health.OnDead += Died; }
    }
    private void Unbind()
    {
        if (health) { health.OnDamageResolved -= Damaged; health.OnDead -= Died; }
        health = null; actor = null;
        if (voice) { voice.Stop(); voice.clip = null; }
    }
    public static void Attack() { if (instance && instance.health && !instance.health.IsDead) instance.Play(VoiceKind.Attack); }
    public static void Jump(GameObject owner)
    {
        if (instance && instance.actor && instance.actor.gameObject == owner && instance.health && !instance.health.IsDead)
            instance.Play(VoiceKind.Jump);
    }
    private void Damaged(CombatHealth source, DamageInfo info, float loss, bool lethal)
    { if (loss > 0f && !lethal && !source.IsDead) Play(VoiceKind.Hurt); }
    private void Died(CombatHealth source, DamageInfo info) => Play(VoiceKind.Death);
    public bool Preview(VoiceKind kind) => Play(kind, true);
    private bool Play(VoiceKind kind, bool preview = false)
    {
        int selected = (int)selection;
        if (selected < 0 || selected >= candidates.Length) return false;
        var clips = candidates[selected].Clips(kind);
        if (clips == null || clips.Length == 0) return false;
        int last = previous[selected, (int)kind];
        int pick = UnityEngine.Random.Range(0, clips.Length > 1 && last >= 0 ? clips.Length - 1 : clips.Length);
        if (clips.Length > 1 && last >= 0 && pick >= last) pick++;
        voice.Stop(); voice.ignoreListenerPause = preview; voice.clip = clips[pick]; voice.Play();
        previous[selected, (int)kind] = pick; LastKind = kind; PlayedCount++;
        return true;
    }
}
