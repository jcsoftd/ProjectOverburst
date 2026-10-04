using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CrustaspikanEncounterPortal : MonoBehaviour, IInteractable
{
    private CrustaspikanEncounterHost host;
    private CrustaspikanEncounter encounter;
    private TextMeshPro label;
    private bool prompt;
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
        var go=new GameObject("Portal Label");go.transform.SetParent(transform,false);go.transform.localPosition=Vector3.up*3.5f;
        label=go.AddComponent<TextMeshPro>();label.font=Resources.Load<TMP_FontAsset>("UI/Fonts/DamageFloating/Pretendard_Medium SDF") ?? TMP_Settings.defaultFontAsset;
        label.fontSize=3.5f;label.alignment=TextAlignmentOptions.Center;label.rectTransform.sizeDelta=new Vector2(10,2);label.text=InteractionPrompt;
    }
    private void OnEnable()=>InteractionRegistry.Register(this);
    private void OnDisable()=>InteractionRegistry.Unregister(this);
    public bool IsInteractionAvailable(PlayerActorRuntime actor)=>actor!=null && !actor.Health.IsDead && (encounter!=null || host!=null && host.CanEnter);
    public InteractionExecutionResult TryInteract(PlayerActorRuntime actor)
    {
        if(!IsInteractionAvailable(actor))return InteractionExecutionResult.Rejected;
        if(encounter!=null){encounter.Exit(true);return InteractionExecutionResult.Succeeded;}
        return host.Enter(actor)?InteractionExecutionResult.StartedTransition:InteractionExecutionResult.Rejected;
    }
    public void SetInteractionPromptVisible(bool visible){prompt=visible;}
    private void LateUpdate()
    {
        if(label==null || Camera.main==null)return;
        label.transform.rotation=Camera.main.transform.rotation;
        label.color=prompt?new Color(.5f,1f,1f):Color.white;
    }
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
    private CrustaspikanArenaVisuals portalVisuals;
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
            if(Entrance!=null){Destroy(Entrance.gameObject);Entrance=null;portalVisuals?.Dispose();portalVisuals=null;}return;
        }
        var player=PlayerContext.Instance!=null?PlayerContext.Instance.CurrentActor:null;
        if(Settings==null || player==null || Entrance!=null || ActiveEncounter!=null)return;
        portalVisuals=new CrustaspikanArenaVisuals();
        Vector3 position=player.transform.position+new Vector3(-5,0,5);
        if(Physics.Raycast(position+Vector3.up*15,Vector3.down,out var ground,40f,LayerMask.GetMask("Ground","Default"),QueryTriggerInteraction.Ignore))position.y=ground.point.y+.05f;
        var go=portalVisuals.Portal(transform,Vector3.zero,new Color(.08f,.9f,1f),"크러스피칸 임시 보스방 포탈");go.transform.position=position;
        // 포탈은 하이드아웃 씬 수명에 맞춘다.
        go.transform.SetParent(null);SceneManager.MoveGameObjectToScene(go,player.gameObject.scene);
        Entrance=go.AddComponent<CrustaspikanEncounterPortal>();Entrance.Configure(this);
    }
    public bool Enter(PlayerActorRuntime player)
    {
        if(!CanEnter)return false;
        if(!Settings.Validate(out string reason)){Debug.LogError("[Crustaspikan] "+reason);return false;}
        var go=new GameObject("Crustaspikan First Encounter");SceneManager.MoveGameObjectToScene(go,player.gameObject.scene);
        ActiveEncounter=go.AddComponent<CrustaspikanEncounter>();
        if(ActiveEncounter.Begin(this,Settings,player)){if(Entrance!=null)Entrance.gameObject.SetActive(false);return true;}
        ActiveEncounter.Exit(true);ActiveEncounter=null;return false;
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
        portalVisuals?.Dispose();if(Current==this)Current=null;
    }
}
