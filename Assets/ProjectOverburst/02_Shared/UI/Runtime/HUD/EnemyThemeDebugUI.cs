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
    private void Awake()
    {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
        gameObject.SetActive(false);return;
#else
        for(int i=0;i<tables.Length;i++)
        {int index=i;spawnButtons[i].onClick.AddListener(()=>Begin(index,false));waveButtons[i].onClick.AddListener(()=>Begin(index,true));}
        clearButton.onClick.AddListener(Clear);
        if(arenaButton!=null)arenaButton.onClick.AddListener(ToggleArena);
#endif
    }
    public bool Begin(int index,bool waves)
    {
        if(index<0 || index>=tables.Length)return false;
        var player=PlayerInputFacade.Current;
        if(player==null){statusLabel.text="플레이어가 있는 전투 씬에서 사용하세요.";return false;}
        if(encounter!=null && (encounter.Running || encounter.AliveCount>0))
        {statusLabel.text="진행 중인 시험을 먼저 정리하세요.";return false;}
        Clear();
        var root=new GameObject("Theme debug encounter");SceneManager.MoveGameObjectToScene(root,player.gameObject.scene);
        encounter=root.AddComponent<EnemyThemeEncounter>();
        encounter.Configure(tables[index],null,index<warningMaterials.Length?warningMaterials[index]:null);
        return encounter.Begin(player.transform,waves);
    }
    public void Clear()
    {
        if(arena!=null)arena.ClearEncounters();
        if(encounter!=null){encounter.StopEncounter(true);Destroy(encounter.gameObject);encounter=null;}
        if(statusLabel!=null)statusLabel.text="테마별 소형 40 · 중형 9 · 정예 1";
    }
    private void Update()
    {
        if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.2f;
        if(arena!=null && (arenaPlayer==null || PersistentSceneFlow.Instance==null
            || PersistentSceneFlow.Instance.IsSwitching
            || PersistentSceneFlow.Instance.CurrentSubSceneName!=PersistentSceneFlow.HideoutSceneName)) ExitArena(false);
        if(encounter!=null)statusLabel.text=$"{encounter.Table.DisplayName} · {encounter.LastMessage}\n생존 {encounter.AliveCount} / 처치 {encounter.DefeatedCount} / 누적 {encounter.SpawnedCount}";
        bool busy=encounter!=null && (encounter.Running || encounter.AliveCount>0);
        foreach(var b in spawnButtons)b.interactable=!busy;
        foreach(var b in waveButtons)b.interactable=!busy;
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
        arena.ProtectPlayer(arenaPlayer.GetComponent<CombatHealth>());
        Teleport(arena.entry.position,arena.entry.rotation);
        if(arenaButton!=null)arenaButton.GetComponentInChildren<TextMeshProUGUI>().text="하이드아웃으로 돌아가기";
        statusLabel.text="시험장 사망 방지 · 최소 체력 1\n50마리 버튼 또는 색상 발판으로 시작 · 정리 버튼으로 재시험";
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
