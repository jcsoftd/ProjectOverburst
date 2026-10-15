using Overburst.Appearance;
using Overburst.Caves;
using Overburst.Mojave;
using UnityEngine;
using UnityEngine.SceneManagement;

// One persistent source keeps title/character/appearance screens and town day/night continuous.
[DisallowMultipleComponent]
public sealed class OverburstBgmPlayer : MonoBehaviour
{
    public static OverburstBgmPlayer Instance { get; private set; }
    public OverburstBgmContext Context { get; private set; }
    public AudioSource Source { get; private set; }
    public OverburstBgmCatalog Catalog { get; private set; }
    private readonly System.Random random = new System.Random();
    private readonly AudioClip[] previous = new AudioClip[7];
    private float nextContextCheck;
    private int contentHandle = int.MinValue;
    private OverburstBgmContext runContext;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance) return;
        var host = new GameObject(nameof(OverburstBgmPlayer));
        DontDestroyOnLoad(host);
        host.AddComponent<OverburstBgmPlayer>();
    }

    private void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        Catalog = Resources.Load<OverburstBgmCatalog>(OverburstBgmCatalog.ResourcePath);
        Source = gameObject.AddComponent<AudioSource>();
        Source.playOnAwake = false;
        Source.spatialBlend = 0;
        Source.ignoreListenerPause = true;
        Source.priority = 32;
        if (!Catalog) { Debug.LogError("[BGM] 음악 카탈로그가 없습니다."); enabled = false; return; }
        Source.volume = Catalog.volume; // Master/background mute already apply through AudioListener.
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextContextCheck)
        {
            nextContextCheck = Time.unscaledTime + .2f;
            SetContext(ResolveContext());
        }
        if (Context != OverburstBgmContext.None && !Source.loop && Source.clip && !Source.isPlaying)
            PlayNext();
    }

    public OverburstBgmContext ResolveContext()
    {
        var title = TitleCharacterPanel.Instance;
        var appearance = AppearanceCustomizationPanel.Instance;
        if ((title && title.isActiveAndEnabled && !title.ReadyForGameplay)
            || (appearance && !appearance.managementPreview && !appearance.IsExhibition && AppearanceCustomizationPanel.IsOpen))
            return OverburstBgmContext.Title;
        if (WorldSessionState.Phase == WorldPhase.Tutorial) return OverburstBgmContext.Tutorial;
        // Keep the current music during loading; a new ready world owns the next transition.
        if (WorldSessionState.Phase == WorldPhase.Loading || PersistentSceneFlow.Instance?.IsSwitching == true)
            return Context;
        var scene = WorldSessionState.ContentScene;
        if (!scene.IsValid() || !scene.isLoaded) return OverburstBgmContext.None;
        if (WorldSessionState.IsHideout)
        {
            if (scene.name != PersistentSceneFlow.MainSceneName) return OverburstBgmContext.None;
            var world = OutdoorWorldSpawner.Current;
            var player = PlayerContext.Instance?.CurrentActor;
            return world && player && !world.IsInTown(player.transform.position)
                ? OverburstBgmContext.Outdoor : OverburstBgmContext.Town;
        }
        if (WorldSessionState.Phase != WorldPhase.Run && WorldSessionState.Phase != WorldPhase.Settling)
            return OverburstBgmContext.None;
        if (contentHandle != scene.handle)
        {
            contentHandle = scene.handle;
            runContext = OverburstBgmContext.None;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<DesertRunWorld>(true)) { runContext = OverburstBgmContext.Desert; break; }
                if (root.GetComponentInChildren<CaveRunWorld>(true)) { runContext = OverburstBgmContext.Cave; break; }
            }
        }
        return runContext;
    }

    public void SetContext(OverburstBgmContext context)
    {
        if (!Catalog || !Source || Context == context) return;
        Context = context;
        Source.Stop();
        Source.clip = null;
        PlayNext();
    }

    private void PlayNext()
    {
        var list = Catalog.Playlist(Context);
        var clip = list == null ? Catalog.Single(Context) : Pick(list, previous[(int)Context], random);
        Source.loop = list == null;
        Source.clip = clip;
        if (!clip) return;
        previous[(int)Context] = clip;
        Source.Play();
    }

    public static AudioClip Pick(AudioClip[] clips, AudioClip last, System.Random random)
    {
        if (clips == null || clips.Length == 0) return null;
        int count = 0;
        AudioClip fallback = null;
        foreach (var clip in clips)
            if (clip) { fallback = clip; if (clip != last) count++; }
        if (count == 0) return fallback;
        int selected = random.Next(count);
        foreach (var clip in clips)
            if (clip && clip != last && selected-- == 0) return clip;
        return fallback;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
