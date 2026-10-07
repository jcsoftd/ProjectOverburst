using System;
using Overburst.Appearance;
using Overburst.Persistence;
using UnityEngine;

[DisallowMultipleComponent,DefaultExecutionOrder(1000)]
public sealed class PlayerAppearanceController : MonoBehaviour
{
    private CharacterAppearanceCatalog catalog;
    private PlayerContext context;
    private AccountGameplaySession account;
    private P09AppearanceApplier applier;
    private CharacterAppearanceSnapshot displayed;
    private PlayerActorRuntime actor;
    private float nextBind;
    public static PlayerAppearanceController Instance {get;private set;}
    public string Error {get;private set;}

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()=>Instance=null;
    public static PlayerAppearanceController Install()
    {
        var context=PlayerContext.GetOrCreate();
        if(!context)return null;
        var controller=context.GetComponent<PlayerAppearanceController>();
        if(!controller)controller=context.gameObject.AddComponent<PlayerAppearanceController>();
        controller.Bind();return controller;
    }
    private void Awake(){Instance=this;catalog=Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);}
    private void OnEnable()=>Bind();
    private void Update(){if(Time.unscaledTime<nextBind)return;nextBind=Time.unscaledTime+.5f;Bind();}
    private void Bind()
    {
        var nextContext=PlayerContext.Instance;
        if(context!=nextContext)
        {
            if(context)context.CurrentActorChanged-=ActorChanged;
            context=nextContext;
            if(context)context.CurrentActorChanged+=ActorChanged;
        }
        var nextAccount=AccountGameplaySession.Current;
        if(account!=nextAccount)
        {
            if(account!=null)account.AppearanceCommitted-=OnCommitted;
            account=nextAccount;
            if(account!=null)account.AppearanceCommitted+=OnCommitted;
            displayed=null;applier=null;actor=null;
        }
        if(!catalog)catalog=Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);
        var nextActor=context?context.CurrentActor:null;
        if(actor!=nextActor || (actor&&applier==null))ActorChanged(nextActor);
    }
    private void ActorChanged(PlayerActorRuntime next)
    {
        actor=next;applier=null;
        if(!actor || !catalog || account==null)return;
        var adapter=actor.GetComponentInChildren<P09CharacterVisualAdapter>(true);
        if(!adapter || !adapter.ModelRoot)return;
        try
        {
            applier=new P09AppearanceApplier(adapter.ModelRoot,catalog);
            var value=catalog.ResolveForDisplay(account.ReadAppearance(),out _);
            applier.ApplyAppearance(value);displayed=value;Error=null;
        }
        catch(Exception error){Error=error.Message;Debug.LogException(error);}
    }
    private void OnCommitted(CharacterAppearanceSnapshot value)
    {
        if(applier==null){Bind();if(applier==null)throw new InvalidOperationException("플레이어 외모 모델이 연결되지 않았습니다.");}
        applier.ApplyAppearance(value);displayed=value.Copy();Error=null;
    }
    public void ValidateCurrent(CharacterAppearanceSnapshot value)
    {
        Bind();
        if(applier==null)throw new InvalidOperationException("플레이어 외모 모델이 연결되지 않았습니다.");
        applier.ValidatePlan(value);
    }
    public bool TryApplyCurrent()
    {
        Bind();
        if(account==null || !catalog)return false;
        ActorChanged(actor);return Error==null&&applier!=null;
    }
    private void LateUpdate(){if(applier!=null&&displayed!=null)applier.ApplyBodyStyle(displayed.bodyShapeId);}
    private void OnDisable()
    {
        if(context)context.CurrentActorChanged-=ActorChanged;
        if(account!=null)account.AppearanceCommitted-=OnCommitted;
        context=null;account=null;applier=null;actor=null;displayed=null;
    }
    private void OnDestroy(){if(Instance==this)Instance=null;}
}

