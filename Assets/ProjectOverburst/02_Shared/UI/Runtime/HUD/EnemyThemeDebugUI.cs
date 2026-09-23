using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public sealed class EnemyThemeDebugUI : MonoBehaviour
{
    public EnemyThemeTable[] tables;
    public Material[] warningMaterials;
    public Button[] spawnButtons, waveButtons;
    public Button clearButton;
    public Button arenaButton;
    public EnemyThemeDebugArena arenaPrefab;
    public TextMeshProUGUI statusLabel;
    private EnemyThemeDebugArena arena;
    private Transform arenaPlayer;
    private Vector3 returnPosition;
    private Quaternion returnRotation;
    private bool previousHideoutSpawn;
    public bool InArena => arena != null;
    private EnemyThemeEncounter encounter;
    private float nextRefresh;
    private readonly Button[] modeButtons = new Button[4];
    private EnemyThemeTrialMode trialMode = EnemyThemeTrialMode.Normal;
    public EnemyThemeTrialMode TrialMode => trialMode;
    private void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        gameObject.SetActive(false);return;
#else
        InitializeRuntimeUi();
#endif
    }
    private void OnEnable()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (Application.isPlaying) InitializeRuntimeUi();
#endif
    }
    private void InitializeRuntimeUi()
    {
        if (!Application.isPlaying || tables == null || spawnButtons == null || waveButtons == null
            || statusLabel == null || tables.Length != spawnButtons.Length || tables.Length != waveButtons.Length)
            return;
        bool rebuilt = transform.Find("Roster modes") == null;
        if (rebuilt) trialMode = EnemyThemeTrialMode.Normal;
        for(int i=0;i<tables.Length;i++)
        {
            int index=i;
            spawnButtons[i].onClick.RemoveAllListeners();
            waveButtons[i].onClick.RemoveAllListeners();
            spawnButtons[i].onClick.AddListener(()=>Begin(index,false));
            waveButtons[i].onClick.AddListener(()=>Begin(index,true));
        }
        clearButton.onClick.RemoveAllListeners();
        clearButton.onClick.AddListener(Clear);
        if(arenaButton!=null)
        {
            arenaButton.onClick.RemoveAllListeners();
            arenaButton.onClick.AddListener(ToggleArena);
        }
        BuildModeControls();
        SetTrialMode(trialMode);
    }
    public bool SetTrialMode(EnemyThemeTrialMode mode)
    {
        if (HasActiveTrial()) return false;
        if (arena != null && !arena.ConfigureTrialMode(mode)) return false;
        trialMode = mode;
        for (int i = 0; i < tables.Length; i++)
        {
            int count = EnemyThemeTrialPresets.Resolve(tables[i], mode).Total;
            spawnButtons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = $"{count}마리";
            waveButtons[i].GetComponentInChildren<TextMeshProUGUI>(true).text = $"{count}×3";
        }
        for (int i = 0; i < modeButtons.Length; i++)
        {
            if (modeButtons[i] == null) continue;
            modeButtons[i].GetComponent<Image>().color = i == (int)mode
                ? new Color(.22f, .43f, .40f) : new Color(.13f, .20f, .25f);
        }
        SetIdleStatus();
        return true;
    }
    private bool HasActiveTrial() => (encounter != null && encounter.HasOutstandingLeases)
        || (arena != null && arena.HasActiveEncounters);

    private void BuildModeControls()
    {
        var panel = transform as RectTransform;
        if (panel == null) return;
        // Keep the authored panel height: increasing it clips the header at 720p.
        int themeLabelIndex = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i) as RectTransform;
            if (child != null && child.name == "Theme name")
                child.anchoredPosition = new Vector2(child.anchoredPosition.x, 310 - themeLabelIndex++ * 41);
        }
        for (int i = 0; i < tables.Length; i++)
        {
            var spawnRect = spawnButtons[i].transform as RectTransform;
            var waveRect = waveButtons[i].transform as RectTransform;
            spawnRect.anchoredPosition = new Vector2(spawnRect.anchoredPosition.x, 310 - i * 41);
            waveRect.anchoredPosition = new Vector2(waveRect.anchoredPosition.x, 310 - i * 41);
        }
        var bar = transform.Find("Roster modes") as RectTransform;
        if (bar == null)
        {
            bar = new GameObject("Roster modes", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(transform, false);
        }
        bar.anchorMin = bar.anchorMax = bar.pivot = Vector2.zero;
        bar.anchoredPosition = new Vector2(12, 155);
        bar.sizeDelta = new Vector2(286, 30);
        for (int i = 0; i < modeButtons.Length; i++)
        {
            int index = i;
            string name = "Mode " + EnemyThemeTrialPresets.Label((EnemyThemeTrialMode)i);
            var rect = bar.Find(name) as RectTransform;
            if (rect == null)
            {
                rect = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button))
                    .GetComponent<RectTransform>();
                rect.SetParent(bar, false);
            }
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(i * 73, 0);
            rect.sizeDelta = new Vector2(67, 30);
            var button = rect.GetComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => SetTrialMode((EnemyThemeTrialMode)index));
            modeButtons[i] = button;

            var label = rect.Find("Label")?.GetComponent<TextMeshProUGUI>();
            if (label == null)
            {
                label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI))
                    .GetComponent<TextMeshProUGUI>();
                label.rectTransform.SetParent(rect, false);
            }
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
            label.font = statusLabel.font;
            label.fontSize = 12;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = EnemyThemeTrialPresets.Label((EnemyThemeTrialMode)i);
        }
    }
    private void SetIdleStatus()
    {
        if (statusLabel == null || tables == null || tables.Length < 4) return;
        int spider = EnemyThemeTrialPresets.Resolve(tables[0], trialMode).Total;
        int venom = EnemyThemeTrialPresets.Resolve(tables[1], trialMode).Total;
        int primal = EnemyThemeTrialPresets.Resolve(tables[2], trialMode).Total;
        int cavern = EnemyThemeTrialPresets.Resolve(tables[3], trialMode).Total;
        string counts = $"{EnemyThemeTrialPresets.Label(trialMode)} · 거미{spider} 독낭{venom} 원시{primal} 암굴{cavern}";
        statusLabel.text = InArena ? $"시험장 보호 · 최소 체력 1\n{counts}"
            : $"{counts}\n왼쪽 1회 / 오른쪽 3회 공세";
    }
    public bool Begin(int index,bool waves)
    {
        if(index<0 || index>=tables.Length)return false;
        var player=PlayerInputFacade.Current;
        if(player==null){statusLabel.text="플레이어가 있는 전투 씬에서 사용하세요.";return false;}
        if(HasActiveTrial())
        {statusLabel.text="진행 중인 시험을 먼저 정리하세요.";return false;}
        Clear();
        var root=new GameObject("Theme debug encounter");SceneManager.MoveGameObjectToScene(root,player.gameObject.scene);
        encounter=root.AddComponent<EnemyThemeEncounter>();
        encounter.Configure(tables[index],null,index<warningMaterials.Length?warningMaterials[index]:null);
        encounter.ConfigureTrialRoster(EnemyThemeTrialPresets.Resolve(tables[index], trialMode));
        return encounter.Begin(player.transform,waves);
    }
    public void Clear()
    {
        if(arena!=null)arena.ClearEncounters();
        if(encounter!=null){encounter.StopEncounter(true);Destroy(encounter.gameObject);encounter=null;}
        SetIdleStatus();
    }
    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (modeButtons[0] == null) InitializeRuntimeUi();
#endif
        if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.2f;
        if(arena!=null && (arenaPlayer==null || PersistentSceneFlow.Instance==null
            || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName!=PersistentSceneFlow.HideoutSceneName)) ExitArena(false);
        if(encounter!=null)statusLabel.text=$"{encounter.Table.DisplayName} · {encounter.LastMessage}\n생존 {encounter.AliveCount} / 처치 {encounter.DefeatedCount} / 누적 {encounter.SpawnedCount}";
        else SetIdleStatus();
        bool busy=HasActiveTrial();
        foreach(var b in spawnButtons)b.interactable=!busy;
        foreach(var b in waveButtons)b.interactable=!busy;
        foreach(var b in modeButtons)if(b!=null)b.interactable=!busy;
    }
    public void ToggleArena()
    {
        if(arena!=null){ExitArena(true);return;}
        if(arenaPrefab==null || PlayerInputFacade.Current==null || PersistentSceneFlow.Instance==null
            || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName!=PersistentSceneFlow.HideoutSceneName)
        {statusLabel.text="시험장은 하이드아웃에서 입장하세요.";return;}
        Clear();arenaPlayer=PlayerInputFacade.Current.transform;returnPosition=arenaPlayer.position;returnRotation=arenaPlayer.rotation;
        previousHideoutSpawn=CombatDebugSettings.SpawnHideoutMonsters;CombatDebugSettings.SetHideoutMonsterSpawn(false);
        arena=Instantiate(arenaPrefab,new Vector3(1000,0,1000),Quaternion.identity);
        SceneManager.MoveGameObjectToScene(arena.gameObject,arenaPlayer.gameObject.scene);
        arena.ConfigureTrialMode(trialMode);
        arena.ProtectPlayer(arenaPlayer.GetComponent<CombatHealth>());
        Teleport(arena.entry.position,arena.entry.rotation);
        if(arenaButton!=null)arenaButton.GetComponentInChildren<TextMeshProUGUI>().text="하이드아웃으로 돌아가기";
        SetIdleStatus();
    }
    private void Teleport(Vector3 position,Quaternion rotation)
    {
        if(arenaPlayer==null)return;
        var controller=arenaPlayer.GetComponent<CharacterController>();bool enabled=controller!=null && controller.enabled;
        if(enabled)controller.enabled=false;
        arenaPlayer.SetPositionAndRotation(position,rotation);
        if(enabled)controller.enabled=true;
        arenaPlayer.GetComponent<PlayerMovement>()?.ResetMotionAfterTeleport();Physics.SyncTransforms();
    }
    private void ExitArena(bool restorePosition)
    {
        if(arena==null)return;
        arena.ReleasePlayerProtection();
        Clear();if(restorePosition)Teleport(returnPosition,returnRotation);
        Destroy(arena.gameObject);arena=null;arenaPlayer=null;
        CombatDebugSettings.SetHideoutMonsterSpawn(previousHideoutSpawn);
        if(arenaButton!=null)arenaButton.GetComponentInChildren<TextMeshProUGUI>().text="독립 시험장 입장";
    }
    private void OnDestroy(){ExitArena(false);Clear();}
}
