using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CrustaspikanEncounterPortal : MonoBehaviour, IInteractable
{
    private CrustaspikanEncounterHost host;
    private CrustaspikanEncounter encounter;
    private GameObject promptRoot;
    public Component InteractionComponent => this;
    public Transform InteractionTransform => transform;
    public int InteractionPriority => 15;
    public string InteractionPrompt => encounter == null ? "크러스피칸 임시 보스방 입장" : "하이드아웃으로 돌아가기";
    public InteractionDistanceMode DistanceMode => InteractionDistanceMode.Horizontal;
    public float InteractionRange => 3f;
    public string StableInteractionId => "CrustaspikanTemporaryPortal_" + (encounter == null ? "Entry" : "Return");
    public bool AllowsInteractionWhileInputBlocked => false;
    public bool WantsInteractionPrompt => true;
    public void Configure(CrustaspikanEncounterHost host, CrustaspikanEncounter encounter = null)
    {
        this.host=host;this.encounter=encounter;
        // 하이드아웃에 이미 저작된 F 키캡을 재사용한다. 카메라 거리에 따라 커지는 별도 글자는 만들지 않는다.
        var source=FindFirstObjectByType<WorldInteractionKeyPrompt>(FindObjectsInactive.Include);
        if(source!=null)
        {
            promptRoot=Instantiate(source.gameObject,transform,false);
            promptRoot.name="Portal Interaction Key";
            promptRoot.transform.localPosition=Vector3.up*2.3f;
            promptRoot.SetActive(false);
        }
    }
    private void OnEnable()=>InteractionRegistry.Register(this);
    private void OnDisable(){InteractionRegistry.Unregister(this);SetInteractionPromptVisible(false);}
    public bool IsInteractionAvailable(PlayerActorRuntime actor)=>actor!=null && !actor.Health.IsDead && (encounter!=null || host!=null && host.CanEnter);
    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if(!IsInteractionAvailable(actor))return InteractionExecutionResult.Rejected;
        if(encounter!=null){encounter.Exit(true);return InteractionExecutionResult.Succeeded;}
        return host.Enter(actor)?InteractionExecutionResult.StartedTransition:InteractionExecutionResult.Rejected;
    }
    public void SetInteractionPromptVisible(bool visible){if(promptRoot!=null)promptRoot.SetActive(visible);}
}

// 하이드아웃 원본 씬을 저장하지 않고 런타임에 포탈을 한 번 생성한다.
public sealed class CrustaspikanEncounterHost : MonoBehaviour
{
    public const string SettingsPath="Enemies/Bosses/CrustaspikanEncounter/CE_Crustaspikan";
    public static CrustaspikanEncounterHost Current {get;private set;}
    public CrustaspikanEncounter ActiveEncounter {get;private set;}
    public CrustaspikanEncounterPortal Entrance {get;private set;}
    public bool CanEnter => ActiveEncounter==null && Settings!=null && PersistentSceneFlow.Instance!=null
        && !PersistentSceneFlow.Instance.IsSwitching && PersistentSceneFlow.Instance.CurrentSubSceneName==PersistentSceneFlow.HideoutSceneName;
    public CrustaspikanEncounterSettings Settings {get;private set;}
    public string LastFailure {get;private set;}
    private float nextCheck;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void Reset()=>Current=null;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Install()
    {
        if(Current!=null)return;
        var go=new GameObject("Crustaspikan Temporary Encounter Host");DontDestroyOnLoad(go);go.AddComponent<CrustaspikanEncounterHost>();
    }
    private void Awake(){Current=this;Settings=Resources.Load<CrustaspikanEncounterSettings>(SettingsPath);}
    private void Update()
    {
        if(Time.unscaledTime<nextCheck)return;nextCheck=Time.unscaledTime+.3f;
        var flow=PersistentSceneFlow.Instance;
        if(flow==null || flow.IsSwitching || flow.CurrentSubSceneName!=PersistentSceneFlow.HideoutSceneName)
        {
            if(ActiveEncounter!=null)ActiveEncounter.Exit(false);
            if(Entrance!=null){Destroy(Entrance.gameObject);Entrance=null;}return;
        }
        var player=PlayerContext.Instance!=null?PlayerContext.Instance.CurrentActor:null;
        if(Settings==null || player==null || Entrance!=null || ActiveEncounter!=null)return;
        var hideout=SceneManager.GetSceneByName(PersistentSceneFlow.HideoutSceneName);
        if(!HideoutPortalLayout.TryGetPosition(hideout,HideoutPortalKind.Boss,out var position))return;
        var go=new GameObject("크러스피칸 임시 보스방 포탈");go.transform.position=position;
        go.AddComponent<HideoutPortalVisual>().Configure(HideoutPortalKind.Boss);
        // 포탈은 하이드아웃 씬 수명에 맞춘다.
        go.transform.SetParent(null);SceneManager.MoveGameObjectToScene(go,hideout);
        Entrance=go.AddComponent<CrustaspikanEncounterPortal>();Entrance.Configure(this);
    }
    public bool Enter(PlayerActorRuntime player)
    {
        if(!CanEnter || player==null || player.Health==null)return false;
        bool entered=false; GameObject go=null; LastFailure="";
        try
        {
            if(!Settings.Validate(out string reason)){LastFailure=reason;Debug.LogWarning("[Crustaspikan] "+reason);return false;}
            go=new GameObject("Crustaspikan First Encounter");SceneManager.MoveGameObjectToScene(go,player.gameObject.scene);
            ActiveEncounter=go.AddComponent<CrustaspikanEncounter>();
            if(!ActiveEncounter.Begin(this,Settings,player))
            {LastFailure=string.IsNullOrEmpty(ActiveEncounter.LastFailure)?"보스방 준비를 완료하지 못했습니다.":ActiveEncounter.LastFailure;return false;}
            if(Entrance!=null)Entrance.gameObject.SetActive(false);
            entered=true;return true;
        }
        catch(System.Exception error)
        {
            LastFailure=error.Message;Debug.LogWarning("[Crustaspikan] 입장 준비 실패: "+LastFailure);return false;
        }
        finally
        {
            if(!entered)
            {
                if(ActiveEncounter!=null)ActiveEncounter.Exit(true);
                else if(go!=null)Destroy(go);
            }
        }
    }
    internal void OnExit(CrustaspikanEncounter encounter)
    {
        if(ActiveEncounter==encounter)ActiveEncounter=null;
        if(Entrance!=null)Entrance.gameObject.SetActive(true);
    }
    private void OnDestroy()
    {
        if(ActiveEncounter!=null)ActiveEncounter.Exit(false);
        if(Entrance!=null)Destroy(Entrance.gameObject);
        if(Current==this)Current=null;
    }
}
