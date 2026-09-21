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
    public TextMeshProUGUI statusLabel;
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
        if(encounter!=null){encounter.StopEncounter(true);Destroy(encounter.gameObject);encounter=null;}
        if(statusLabel!=null)statusLabel.text="테마별 소형 40 · 중형 9 · 정예 1";
    }
    private void Update()
    {
        if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.2f;
        if(encounter!=null)statusLabel.text=$"{encounter.Table.DisplayName} · {encounter.LastMessage}\n생존 {encounter.AliveCount} / 처치 {encounter.DefeatedCount} / 누적 {encounter.SpawnedCount}";
        bool busy=encounter!=null && (encounter.Running || encounter.AliveCount>0);
        foreach(var b in spawnButtons)b.interactable=!busy;
        foreach(var b in waveButtons)b.interactable=!busy;
    }
    private void OnDestroy(){Clear();}
}
