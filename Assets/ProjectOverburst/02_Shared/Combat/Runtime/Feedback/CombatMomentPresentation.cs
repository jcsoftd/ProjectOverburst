using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Parry/full-energy presentation with persisted game settings. Combat owns damage, energy and action timing.
public sealed class CombatMomentPresentation : MonoBehaviour
{
    public const string ResourcePath = "Combat/VFX/PF_CombatMomentPresentation";
    [Serializable] public sealed class ContactVisual
    {
        public LineRenderer core, cross;
        public LineRenderer[] sparks;
        [NonSerialized] public float started;
        [NonSerialized] public bool active;
        [NonSerialized] public int count;
        [NonSerialized] public Vector3 point;
        [NonSerialized] public Color color;
        [NonSerialized] public Vector3[] velocities;
    }
    [SerializeField] private LineRenderer bladeGlow, bladeSweep, impactCore;
    [SerializeField] private ContactVisual[] contacts;
    [SerializeField] private float parryPeak = .045f, sparkLifetime = .14f, parryLifetime = .24f;
    [SerializeField] private float heavyPeak = .065f, heavyLifetime = .28f;
    [SerializeField] private Color fire = new Color(1f,.35f,.09f), ice = new Color(.4f,.85f,1f), electric = new Color(.68f,.55f,1f);
    [SerializeField] private Color dark = new Color(.72f,.15f,.30f);
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("light")] private Color lightColor = new Color(1f,.89f,.5f);
    private static CombatMomentPresentation instance;
    private static bool parryEnabled, heavyEnabled, screenEnabled, bladeEnabled, localEnabled;
    public static bool ParryEnabled => parryEnabled;
    public static bool HeavyEnabled => heavyEnabled;
    public static bool ScreenEnabled => screenEnabled;
    public static bool BladeEnabled => bladeEnabled;
    public static bool LocalEnabled => localEnabled;
    public static int ParryPulses { get; private set; }
    public static int HeavyPulses { get; private set; }
    public static int ContactBursts { get; private set; }
    public static int PreparedPulses { get; private set; }
    public static int ActiveContactCount { get { int n=0; if(instance!=null) foreach(var c in instance.contacts) if(c.active)n++; return n; } }
    public static bool IsPrepared => instance != null;
    public static int ActiveVisualCount => instance == null ? 0 : ActiveContactCount + (instance.bladeKind>0?1:0) + (instance.impactActive?1:0);
    public static float CurrentScreenGain => instance != null ? instance.EvaluateScreen()*instance.ScaleForKind(instance.screenKind) : 0f;
    public static WeaponElement LastHeavyElement { get; private set; }
    public static float LastHeavyEnergy { get; private set; }
    public static float LastHeavyOvercharge { get; private set; }
    public LineRenderer BladeGlow => bladeGlow;
    public LineRenderer ImpactCore => impactCore;

    private float parryIntensity=1f,heavyIntensity=1f;
    private float ScaleForKind(int kind)=>kind==1?parryIntensity:heavyIntensity;
    private PlayerEquipment owner;
    private CombatHealth ownerHealth;
    private Transform weaponRoot;
    private string weaponId;
    private MeleeWeaponElementFx bladeBinding;
    private Camera mainCamera;
    private readonly Key[] recent = new Key[64];
    private readonly Vector3[] impactPoints = new Vector3[28];
    private int nextKey, lastContactFrame=-1;
    private int bladeKind, bladeAction, screenKind, screenAction;
    private float bladeStarted, bladeContact, bladeGain, screenStarted, screenFrom, impactStarted;
    private Vector3 screenPoint;
    private Color bladeColor, impactColor;
    private bool impactActive;
    private struct Key { public int owner, root, action, phase, kind; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        instance=null;
        parryEnabled=heavyEnabled=screenEnabled=bladeEnabled=localEnabled=true;
        ParryPulses=HeavyPulses=ContactBursts=PreparedPulses=0;
        LastHeavyElement=WeaponElement.None; LastHeavyEnergy=LastHeavyOvercharge=0f;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Prepare()
    {

        if(instance!=null)return;
        var prefab=Resources.Load<GameObject>(ResourcePath);
        if(prefab!=null)Instantiate(prefab);

    }
    private void Awake()
    {
        if(instance!=null && instance!=this){Destroy(gameObject);return;}
        instance=this; DontDestroyOnLoad(gameObject);
        foreach(var c in contacts)c.velocities=new Vector3[c.sparks.Length];
        ClearVisuals(); SceneManager.activeSceneChanged+=SceneChanged;
        OverburstGameSettings.Changed+=ApplySettings;ApplySettings();
    }
    private static float Now => OverburstGameClock.UnscaledTime;
    private static float Smooth(float x){x=Mathf.Clamp01(x);return x*x*(3f-2f*x);}
    private Color ColorFor(WeaponElement element)
    {
        switch(element){case WeaponElement.Fire:return fire;case WeaponElement.Ice:return ice;
            case WeaponElement.Electric:return electric;case WeaponElement.Dark:return dark;case WeaponElement.Light:return lightColor;
            default:return new Color(.91f,.95f,1f);}
    }
    private bool Bind(PlayerEquipment equipment)
    {
        if(equipment==null || !equipment.isActiveAndEnabled || equipment.CurrentWeaponRoot==null || equipment.CurrentWeaponItem==null)return false;
        if(owner!=equipment || weaponRoot!=equipment.CurrentWeaponRoot || weaponId!=equipment.CurrentWeaponItem.runtimeInstanceId)
        {
            ClearVisuals(); Array.Clear(recent,0,recent.Length);
            if(ownerHealth!=null){ownerHealth.OnDead-=Died;ownerHealth.OnReset-=ResetHealth;}
            owner=equipment; weaponRoot=equipment.CurrentWeaponRoot; weaponId=equipment.CurrentWeaponItem.runtimeInstanceId;
            ownerHealth=equipment.GetComponent<CombatHealth>();
            if(ownerHealth!=null){ownerHealth.OnDead+=Died;ownerHealth.OnReset+=ResetHealth;}
            bladeBinding=weaponRoot.GetComponentInChildren<MeleeWeaponElementFx>(true);
        }
        return OwnerValid();
    }
    private bool OwnerValid() => owner!=null && owner.isActiveAndEnabled && weaponRoot!=null && weaponRoot.gameObject.activeInHierarchy
        && owner.CurrentWeaponRoot==weaponRoot && owner.CurrentWeaponItem!=null && owner.CurrentWeaponItem.runtimeInstanceId==weaponId
        && (ownerHealth==null || !ownerHealth.IsDead);
    private bool Once(int action,int phase,int kind)
    {
        var key=new Key{owner=owner.GetInstanceID(),root=weaponRoot.GetInstanceID(),action=action,phase=phase,kind=kind};
        foreach(var k in recent)if(k.owner==key.owner && k.root==key.root && k.action==action && k.phase==phase && k.kind==kind)return false;
        recent[nextKey]=key;nextKey=(nextKey+1)%recent.Length;return true;
    }
    public static void SetParryEnabled(bool value)=>OverburstGameSettings.ParryPresentationEnabled=value;
    public static void SetHeavyEnabled(bool value)=>OverburstGameSettings.HeavyPresentationEnabled=value;
    private void ApplySettings()
    {
        parryEnabled=OverburstGameSettings.ParryPresentationEnabled;
        heavyEnabled=OverburstGameSettings.HeavyPresentationEnabled;
        parryIntensity=OverburstGameSettings.ParryPresentationIntensity;
        heavyIntensity=OverburstGameSettings.HeavyPresentationIntensity;
        if(!parryEnabled || parryIntensity<=0f)CancelKind(1);
        if(!heavyEnabled || heavyIntensity<=0f){CancelKind(2);CancelKind(3);}
        foreach(var c in contacts)if(c.active)TickContact(c);
        TickBlade();TickImpact();
    }
    public static void SetScreenEnabled(bool value){screenEnabled=value;if(!value && instance!=null)instance.screenKind=0;}
    public static void SetBladeEnabled(bool value){bladeEnabled=value;if(!value && instance!=null){instance.bladeKind=0;instance.bladeGlow.enabled=instance.bladeSweep.enabled=false;}}
    public static void SetLocalEnabled(bool value){localEnabled=value;if(!value && instance!=null){instance.StopContacts();instance.impactActive=false;instance.impactCore.enabled=false;}}

    public static void Parry(PlayerEquipment equipment,int action,int chain,int tier,ElementGemAttackSnapshot snapshot,Vector3 point,Vector3 direction)
    {
        if(!parryEnabled || instance==null || instance.parryIntensity<=0f || !instance.isActiveAndEnabled || action<=0 || !snapshot.IsCurrent || !instance.Bind(equipment))return;
        var s=instance;
        if(!s.Once(action,chain,1))return;
        if(localEnabled && s.lastContactFrame!=Time.frameCount){s.SpawnContact(point,direction,tier,s.ColorFor(snapshot.Element),action+chain);s.lastContactFrame=Time.frameCount;}
        if(!s.Once(action,0,2))return;
        ParryPulses++;
        s.StartScreen(1,action,point);
        s.StartBlade(1,action,s.ColorFor(snapshot.Element),1.25f,point);
        CancelPreparation(equipment,action);
    }
    public static void Heavy(PlayerEquipment equipment,int action,int phase,OverburstElementDischarge discharge,Vector3 point,Vector3 facing,float radius,bool sweep)
    {
        if(!heavyEnabled || instance==null || instance.heavyIntensity<=0f || !instance.isActiveAndEnabled || action<=0 || discharge==null || discharge.NormalizedEnergy<1f-.0001f
            || !discharge.GemAttack.IsCurrent || !instance.Bind(equipment) || !instance.Once(action,phase,3))return;
        var s=instance;HeavyPulses++;LastHeavyElement=discharge.Element;LastHeavyEnergy=discharge.Energy;LastHeavyOvercharge=discharge.Overcharge;
        float gain=1.6f*(1f+.15f*discharge.Overcharge);Color color=s.ColorFor(discharge.Element);
        s.StartScreen(3,action,point);s.StartBlade(3,action,color,gain,point);
        if(localEnabled)s.StartImpact(point,facing,Mathf.Max(.15f,radius),sweep,color,gain);
    }
    public static void PrepareHeavy(PlayerEquipment equipment,int action,int phase,float remaining,bool sweep)
    {
        if(!heavyEnabled || instance==null || instance.heavyIntensity<=0f || !instance.isActiveAndEnabled || remaining<0f || remaining>.10f || !instance.Bind(equipment))return;
        if(!instance.Once(action,phase,4))return;
        PreparedPulses++;instance.StartScreen(2,action,equipment.transform.position);
        if(!sweep)instance.StartBlade(2,action,Color.white,.15f,equipment.transform.position);
    }
    public static void CancelPreparation(PlayerEquipment equipment,int action)
    {
        if(instance==null || instance.owner!=equipment)return;
        if(instance.screenKind==2 && instance.screenAction==action)instance.screenKind=0;
        if(instance.bladeKind==2 && instance.bladeAction==action){instance.bladeKind=0;instance.bladeGlow.enabled=false;}
    }
    private void StartScreen(int kind,int action,Vector3 point)
    {
        if(!screenEnabled)return;
        EvaluateScreen();int priority=kind==3?30:kind==1?20:10;
        int currentPriority=screenKind==3?30:screenKind==1?20:screenKind==2?10:0;
        if(priority<currentPriority)return;
        screenFrom=EvaluateScreen();screenKind=kind;screenAction=action;screenStarted=Now;screenPoint=point;
    }
    private float EvaluateScreen()
    {
        if(screenKind==0 || !screenEnabled)return 0f;
        float age=Now-screenStarted;
        if(age>=(screenKind==1?.20f:screenKind==2?.18f:.25f)){screenKind=0;return 0f;}
        if(screenKind==1)return age<.015f?Mathf.Lerp(screenFrom,-.05f,Smooth(age/.015f)):age<.05f?-.05f:-.05f*(1f-Smooth((age-.05f)/.15f));
        if(screenKind==2)return -.04f*Smooth(age/.10f);
        return age<.015f?Mathf.Lerp(screenFrom,.04f,Smooth(age/.015f)):age<.05f?.04f:.04f*(1f-Smooth((age-.05f)/.20f));
    }
    public static bool TryGetScreen(Camera camera,out float gain,out Vector2 center)
    {
        gain=0f;center=new Vector2(.5f,.5f);
        if(instance==null || !instance.isActiveAndEnabled || !instance.OwnerValid() || camera==null)return false;
        gain=instance.EvaluateScreen()*instance.ScaleForKind(instance.screenKind);if(Mathf.Abs(gain)<.000001f)return false;
        var viewport=camera.WorldToViewportPoint(instance.screenPoint);
        if(viewport.z<=0f || viewport.x<0f || viewport.x>1f || viewport.y<0f || viewport.y>1f){gain=0f;return false;}
        center=new Vector2(viewport.x,viewport.y);return true;
    }
    private void StartBlade(int kind,int action,Color color,float gain,Vector3 point)
    {
        if(!bladeEnabled || bladeBinding==null || !bladeBinding.TryGetBladeEndpoints(out var bottom,out var tip))return;
        bladeKind=kind;bladeAction=action;bladeStarted=Now;bladeColor=color;bladeGain=gain;
        Vector3 axis=tip-bottom;bladeContact=Mathf.Clamp(Vector3.Dot(point-bottom,axis)/Mathf.Max(.0001f,axis.sqrMagnitude),.15f,.95f);
        TickBlade();
    }
    private void TickBlade()
    {
        if(bladeKind==0 || !bladeEnabled){bladeGlow.enabled=bladeSweep.enabled=false;return;}
        float age=Now-bladeStarted, duration=bladeKind==1?parryLifetime:bladeKind==2?.18f:heavyLifetime;
        if(age>=duration || bladeBinding==null || !bladeBinding.TryGetBladeEndpoints(out var bottom,out var tip))
        {bladeKind=0;bladeGlow.enabled=bladeSweep.enabled=false;return;}
        float peak=bladeKind==1?parryPeak:bladeKind==3?heavyPeak:0f;
        float fade=bladeKind==2?Smooth(age/.10f):age<=peak?1f:1f-Smooth((age-peak)/(duration-peak));
        Color c=bladeColor;c.a=bladeGain*fade*(bladeKind==1?.14f:.30f)*ScaleForKind(bladeKind);
        float length=Vector3.Distance(bottom,tip);bladeGlow.enabled=true;bladeGlow.widthMultiplier=Mathf.Clamp(length*.018f,.014f,.055f);
        bladeGlow.startColor=bladeGlow.endColor=c;bladeGlow.SetPosition(0,bottom);bladeGlow.SetPosition(1,tip);
        bladeSweep.enabled=bladeKind==1 && age>=.02f && age<.11f;
        if(bladeSweep.enabled){float p=Mathf.Lerp(bladeContact,0f,Smooth((age-.02f)/.09f));c.a=.65f*parryIntensity;
            bladeSweep.startColor=bladeSweep.endColor=c;bladeSweep.widthMultiplier=Mathf.Clamp(length*.015f,.014f,.045f);
            bladeSweep.SetPosition(0,Vector3.Lerp(bottom,tip,Mathf.Clamp01(p-.09f)));bladeSweep.SetPosition(1,Vector3.Lerp(bottom,tip,Mathf.Clamp01(p+.09f)));}
    }
    private void SpawnContact(Vector3 point,Vector3 direction,int tier,Color color,int seed)
    {
        foreach(var c in contacts)if(!c.active)
        {
            c.active=true;c.started=Now;c.point=point;c.color=color;c.count=Mathf.Min(c.sparks.Length,6+2*Mathf.Clamp(tier,0,2));
            direction.y=0f;if(direction.sqrMagnitude<.001f)direction=Vector3.forward;direction.Normalize();
            for(int i=0;i<c.count;i++){float hash=Hash((uint)(seed*31+i));float angle=(hash*2f-1f)*35f;
                c.velocities[i]=(Quaternion.AngleAxis(angle,Vector3.up)*direction+Vector3.up*(.2f+.5f*Hash((uint)(seed+i*97))))*(2f+2f*hash);}
            ContactBursts++;TickContact(c);return;
        }
    }
    private static float Hash(uint x){x^=x>>16;x*=0x7feb352du;x^=x>>15;x*=0x846ca68bu;x^=x>>16;return(x&65535)/65535f;}
    private void TickContact(ContactVisual c)
    {
        float age=Now-c.started;if(age>=sparkLifetime){c.active=false;HideContact(c);return;}
        var camera=mainCamera;Vector3 right=camera!=null?camera.transform.right:Vector3.right, up=camera!=null?camera.transform.up:Vector3.up;
        bool flash=age<parryPeak;c.core.enabled=c.cross.enabled=flash;
        if(flash){Color white=new Color(.91f,.95f,1f,.8f*(1f-Smooth(age/parryPeak))*parryIntensity);
            c.core.startColor=c.core.endColor=c.cross.startColor=c.cross.endColor=white;
            c.core.SetPosition(0,c.point-right*.11f);c.core.SetPosition(1,c.point+right*.11f);
            c.cross.SetPosition(0,c.point-up*.055f);c.cross.SetPosition(1,c.point+up*.055f);}
        Color col=Color.Lerp(c.color,Color.white,.55f);col.a=.65f*(1f-Smooth(age/sparkLifetime))*parryIntensity;
        for(int i=0;i<c.sparks.Length;i++){var line=c.sparks[i];line.enabled=i<c.count;if(!line.enabled)continue;
            Vector3 p=c.point+c.velocities[i]*age;line.startColor=line.endColor=col;
            line.SetPosition(0,p);line.SetPosition(1,p-c.velocities[i].normalized*(.07f*(1f-age/sparkLifetime)));}
    }
    private void StartImpact(Vector3 point,Vector3 facing,float radius,bool sweep,Color color,float gain)
    {
        impactStarted=Now;impactActive=true;impactColor=color;impactColor.a=.45f*gain;
        facing.y=0f;if(facing.sqrMagnitude<.001f)facing=Vector3.forward;facing.Normalize();
        Vector3 side=Vector3.Cross(Vector3.up,facing);float r=Mathf.Min(radius*.32f,1.3f);
        for(int i=0;i<impactPoints.Length;i++){float angle=sweep?Mathf.Lerp(-.65f,.65f,i/(float)(impactPoints.Length-1)):i*Mathf.PI*2f/impactPoints.Length;
            impactPoints[i]=point+(facing*Mathf.Cos(angle)+side*Mathf.Sin(angle))*r+Vector3.up*.035f;}
        impactCore.loop=!sweep;impactCore.SetPositions(impactPoints);impactCore.widthMultiplier=.045f;TickImpact();
    }
    private void TickImpact()
    {
        if(!impactActive || !localEnabled){impactCore.enabled=false;return;}
        float age=Now-impactStarted;if(age>=heavyLifetime){impactActive=false;impactCore.enabled=false;return;}
        Color c=impactColor;c.a*=heavyIntensity*(age<=heavyPeak?1f:1f-Smooth((age-heavyPeak)/(heavyLifetime-heavyPeak)));
        impactCore.startColor=impactCore.endColor=c;impactCore.enabled=true;
    }
    private void LateUpdate()
    {
        if(!OwnerValid()){ClearVisuals();return;}
        if(mainCamera==null || !mainCamera.isActiveAndEnabled)mainCamera=Camera.main;
        foreach(var c in contacts)if(c.active)TickContact(c);TickBlade();TickImpact();EvaluateScreen();
    }
    private void CancelKind(int kind)
    {
        if(screenKind==kind)screenKind=0;if(bladeKind==kind){bladeKind=0;bladeGlow.enabled=bladeSweep.enabled=false;}
        if(kind==1)StopContacts();if(kind==3){impactActive=false;impactCore.enabled=false;}
    }
    private static void HideContact(ContactVisual c){c.core.enabled=c.cross.enabled=false;foreach(var line in c.sparks)line.enabled=false;}
    private void StopContacts(){foreach(var c in contacts){c.active=false;HideContact(c);}}
    private void ClearVisuals(){StopContacts();bladeKind=screenKind=0;impactActive=false;bladeGlow.enabled=bladeSweep.enabled=impactCore.enabled=false;}
    private void Died(CombatHealth health,DamageInfo info){ClearVisuals();Array.Clear(recent,0,recent.Length);}
    private void ResetHealth(CombatHealth health){ClearVisuals();Array.Clear(recent,0,recent.Length);}
    private void SceneChanged(Scene previous,Scene next){ClearVisuals();Array.Clear(recent,0,recent.Length);}
    private void OnDisable(){if(instance==this)ClearVisuals();}
    private void OnDestroy()
    {
        SceneManager.activeSceneChanged-=SceneChanged;OverburstGameSettings.Changed-=ApplySettings;
        if(ownerHealth!=null){ownerHealth.OnDead-=Died;ownerHealth.OnReset-=ResetHealth;}
        if(instance==this)instance=null;
    }
}
