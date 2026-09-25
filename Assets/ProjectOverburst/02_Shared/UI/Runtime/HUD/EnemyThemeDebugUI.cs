using System;
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
        if (Application.isPlaying) AppendDeathHarvestButton();
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

    private void AppendDeathHarvestButton()
    {
        if (tables == null || tables.Length != 4 || spawnButtons == null || waveButtons == null
            || spawnButtons.Length != 4 || waveButtons.Length != 4
            || tables[0] == null || tables[3] == null)
            return;
        var table = Resources.Load<EnemyThemeTable>("Enemies/Themes/Tables/DeathHarvest");
        if (table == null) return;
        var panel = transform as RectTransform;
        if (panel == null) return;
        var spawn = Instantiate(spawnButtons[3], transform);
        var wave = Instantiate(waveButtons[3], transform);
        spawn.name = "DeathHarvest spawn";
        wave.name = "DeathHarvest waves";
        TextMeshProUGUI lastName = null;
        foreach (Transform child in transform)
            if (child.name == "Theme name") lastName = child.GetComponent<TextMeshProUGUI>();
        if (lastName != null)
        {
            var nameLabel = Instantiate(lastName, transform);
            nameLabel.name = "Theme name";
            nameLabel.text = table.DisplayName;
        }
        Array.Resize(ref tables, 5);
        tables[4] = table;
        Array.Resize(ref spawnButtons, 5);
        spawnButtons[4] = spawn;
        Array.Resize(ref waveButtons, 5);
        waveButtons[4] = wave;
        var warning = Resources.Load<Material>("Enemies/Themes/Materials/DeathHarvest");
        if (warningMaterials == null) warningMaterials = new Material[5];
        else Array.Resize(ref warningMaterials, 5);
        warningMaterials[4] = warning;
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, 444);
        var header = transform.Find("Header") as RectTransform;
        if (header != null) header.anchoredPosition = new Vector2(header.anchoredPosition.x, 409);
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
        int topRow = Mathf.RoundToInt(panel.rect.height) - 85;
        int themeLabelIndex = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i) as RectTransform;
            if (child != null && child.name == "Theme name")
                child.anchoredPosition = new Vector2(child.anchoredPosition.x, topRow - themeLabelIndex++ * 41);
        }
        for (int i = 0; i < tables.Length; i++)
        {
            var spawnRect = spawnButtons[i].transform as RectTransform;
            var waveRect = waveButtons[i].transform as RectTransform;
            spawnRect.anchoredPosition = new Vector2(spawnRect.anchoredPosition.x, topRow - i * 41);
            waveRect.anchoredPosition = new Vector2(waveRect.anchoredPosition.x, topRow - i * 41);
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
        if (statusLabel == null || tables == null || tables.Length == 0) return;
        string[] names = { "거미", "독낭", "원시", "암굴", "사령" };
        string[] counts = new string[tables.Length];
        for (int i = 0; i < tables.Length; i++)
            counts[i] = (i < names.Length ? names[i] : tables[i].DisplayName)
                + EnemyThemeTrialPresets.Resolve(tables[i], trialMode).Total;
        int split = Mathf.Min(3, counts.Length);
        string first = EnemyThemeTrialPresets.Label(trialMode) + " · "
            + string.Join(" ", counts, 0, split);
        string second = string.Join(" ", counts, split, counts.Length - split);
        statusLabel.text = InArena ? $"시험장 보호 · 최소 체력 1\n{first} {second}"
            : $"{first}\n{second} · 왼쪽 1회 / 오른쪽 3회";
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
        EnsureDeathHarvestArenaZone();
        arena.ConfigureTrialMode(trialMode);
        arena.ProtectPlayer(arenaPlayer.GetComponent<CombatHealth>());
        Teleport(arena.entry.position,arena.entry.rotation);
        if(arenaButton!=null)arenaButton.GetComponentInChildren<TextMeshProUGUI>().text="하이드아웃으로 돌아가기";
        SetIdleStatus();
    }

    private void EnsureDeathHarvestArenaZone()
    {
        if (arena == null || tables == null || tables.Length < 5) return;
        var zones = arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true);
        if (zones.Length == tables.Length - 1)
        {
            var extra = Instantiate(zones[zones.Length - 1], arena.transform);
            extra.name = tables[tables.Length - 1].DisplayName + " trigger";
            var encounter = extra.GetComponent<EnemyThemeEncounter>();
            var material = warningMaterials != null && warningMaterials.Length >= tables.Length
                ? warningMaterials[tables.Length - 1] : null;
            encounter.Configure(tables[tables.Length - 1], null, material);
            extra.Configure(encounter, true);
            var label = extra.transform.Find("Zone label")?.GetComponent<TextMeshPro>();
            if (label != null) label.text = tables[tables.Length - 1].DisplayName + "\n진입하면 3회 공세";
            var marker = extra.transform.Find("Entry marker")?.GetComponent<Renderer>();
            if (marker != null && material != null) marker.sharedMaterial = material;
            zones = arena.GetComponentsInChildren<EnemyThemeTriggerZone>(true);
        }
        if (zones.Length != tables.Length) return;
        for (int i = 0; i < zones.Length; i++)
            zones[i].transform.localPosition = new Vector3((i - (zones.Length - 1) * .5f) * 40f, 0f, 0f);
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
