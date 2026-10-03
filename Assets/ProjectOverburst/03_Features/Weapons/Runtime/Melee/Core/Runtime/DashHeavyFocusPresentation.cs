using UnityEngine;
using UnityEngine.Rendering;

// A small blade highlight and six inward-moving wisps. No lights, material copies or global time effects.
public sealed class DashHeavyFocusPresentation : MonoBehaviour
{
    public const string MaterialResource = "Combat/VFX/DashHeavyFocus";
    private static Material focusMaterial;
    private static AudioClip gatherClip, releaseClip;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPrepared() { focusMaterial=null;gatherClip=null;releaseClip=null; }
    public static void Prepare()
    {
        if(focusMaterial==null)focusMaterial=Resources.Load<Material>(MaterialResource);
        if(gatherClip==null)gatherClip=CombatActionSfxService.ResolveNamedClip("DashHeavyGather");
        if(releaseClip==null)releaseClip=CombatActionSfxService.ResolveNamedClip("DashHeavyRelease");
        if(gatherClip!=null&&gatherClip.loadState==AudioDataLoadState.Unloaded)gatherClip.LoadAudioData();
        if(releaseClip!=null&&releaseClip.loadState==AudioDataLoadState.Unloaded)releaseClip.LoadAudioData();
    }
    private PlayerEquipment equipment;
    private MeleeWeaponElementFx blade;
    private LineRenderer glow;
    private readonly LineRenderer[] wisps = new LineRenderer[6];
    private AudioSource gather, release;
    private Color elementColor;
    private float sourceSeconds = -1f, gatherEndsAt, gatherStartedAt;
    private string weaponId;

    public static DashHeavyFocusPresentation Create(PlayerEquipment owner, WeaponElement element)
    {
        if(owner==null || owner.CurrentWeaponRoot==null)return null;
        Prepare();
        Material material=focusMaterial;
        if(material==null)return null;
        var root=new GameObject("DashHeavyBladeFocus");
        root.transform.SetParent(owner.CurrentWeaponRoot,false);
        var effect=root.AddComponent<DashHeavyFocusPresentation>();
        effect.equipment=owner; effect.weaponId=owner.CurrentWeaponItem?.runtimeInstanceId;
        effect.blade=owner.CurrentWeaponRoot.GetComponentInChildren<MeleeWeaponElementFx>(true);
        effect.elementColor=ColorFor(element);
        effect.glow=effect.MakeLine("BladeHighlight",material,.026f);
        for(int i=0;i<effect.wisps.Length;i++)effect.wisps[i]=effect.MakeLine("GatherWisp"+i,material,.014f);
        effect.gather=effect.MakeVoice("Gather",.28f);
        effect.release=effect.MakeVoice("Release",.32f);
        return effect;
    }
    public static Color ColorFor(WeaponElement element)
    {
        switch(element)
        {
            case WeaponElement.Fire:return new Color(1f,.35f,.09f);
            case WeaponElement.Ice:return new Color(.4f,.85f,1f);
            case WeaponElement.Electric:return new Color(.68f,.55f,1f);
            case WeaponElement.Dark:return new Color(.72f,.15f,.30f);
            case WeaponElement.Light:return new Color(1f,.89f,.5f);
            default:return new Color(.91f,.95f,1f);
        }
    }
    private LineRenderer MakeLine(string lineName,Material material,float width)
    {
        var line=new GameObject(lineName).AddComponent<LineRenderer>();
        line.transform.SetParent(transform,false);line.useWorldSpace=true;line.positionCount=2;
        line.sharedMaterial=material;line.startWidth=width;line.endWidth=width*.3f;
        line.shadowCastingMode=ShadowCastingMode.Off;line.receiveShadows=false;
        line.motionVectorGenerationMode=MotionVectorGenerationMode.ForceNoMotion;
        line.numCapVertices=2;line.enabled=false;
        return line;
    }
    private AudioSource MakeVoice(string voiceName,float volume)
    {
        var voice=new GameObject(voiceName).AddComponent<AudioSource>();
        voice.transform.SetParent(transform,false);voice.playOnAwake=false;
        voice.volume=volume;voice.spatialBlend=.6f;voice.minDistance=3f;voice.maxDistance=24f;
        voice.dopplerLevel=0f;voice.priority=90;return voice;
    }
    public void Tick(float clipSeconds) {sourceSeconds=clipSeconds;}
    public void PlayGather(float duration)
    {
        gather.clip=gatherClip;
        if(gather.clip==null)return;
        gatherStartedAt=OverburstGameClock.UnscaledTime;
        gatherEndsAt=gatherStartedAt+Mathf.Max(.03f,duration);
        gather.pitch=Mathf.Clamp(gather.clip.length/Mathf.Max(.03f,duration),.3f,3f);
        gather.Play();
    }
    public void PlayRelease()
    {
        gather.Stop();release.clip=releaseClip;
        if(release.clip!=null)release.Play();
    }
    private void LateUpdate()
    {
        if(equipment==null || equipment.CurrentWeaponItem?.runtimeInstanceId!=weaponId
            || (equipment.GetComponent<CombatHealth>()?.IsDead ?? false)) {Dispose();return;}
        bool shown=sourceSeconds>=.32f && sourceSeconds<.70f;
        glow.enabled=shown;
        if(!shown){foreach(var w in wisps)w.enabled=false;return;}
        Vector3 tip=equipment.CurrentWeaponTraceBinding?.WeaponTip?.position ?? transform.position;
        Vector3 bottom=tip-transform.forward;
        if(blade!=null)blade.TryGetBladeEndpoints(out bottom,out tip);
        Vector3 axis=(tip-bottom).normalized;
        Vector3 right=Vector3.Cross(axis,Vector3.up).normalized;
        if(right.sqrMagnitude<.01f)right=Vector3.right;
        Vector3 up=Vector3.Cross(axis,right).normalized;
        float rise=Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.32f,DashHeavyFocusClock.HoldClip,sourceSeconds));
        float fade=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.51f,.70f,sourceSeconds));
        float flash=sourceSeconds>DashHeavyFocusClock.HoldClip
            ? 1f-Mathf.Clamp01((sourceSeconds-DashHeavyFocusClock.HoldClip)/.08f):0f;
        Color color=Color.Lerp(elementColor,Color.white,flash*.60f);
        color.a=(.15f+.55f*rise+.20f*flash)*fade;
        glow.startColor=glow.endColor=color;glow.SetPosition(0,bottom);glow.SetPosition(1,tip);
        glow.widthMultiplier=1f+flash*.8f;
        for(int i=0;i<wisps.Length;i++)
        {
            var w=wisps[i];w.enabled=sourceSeconds<=DashHeavyFocusClock.HoldClip+.001f;
            float angle=i*Mathf.PI/3f+rise*1.1f;
            Vector3 target=Vector3.Lerp(bottom,tip,.22f+.13f*i);
            Vector3 radial=right*Mathf.Cos(angle)+up*Mathf.Sin(angle);
            float radius=Mathf.Lerp(.30f,.015f,rise);
            Vector3 point=target+radial*radius;
            w.SetPosition(0,point-radial*Mathf.Lerp(.10f,.018f,rise));w.SetPosition(1,point);
            Color c=elementColor;c.a=.38f*Mathf.Sin(Mathf.PI*Mathf.Clamp01(rise))+.20f*rise;
            w.startColor=new Color(c.r,c.g,c.b,0f);w.endColor=c;
        }
        if(gather.isPlaying)
        {
            float remaining=gatherEndsAt-OverburstGameClock.UnscaledTime;
            gather.volume=.28f*Mathf.Clamp01(remaining/.025f);
            if(remaining<=0f)gather.Stop();
        }
    }
    public void Dispose()
    {
        if(gather!=null)gather.Stop();if(release!=null)release.Stop();
        if(Application.isPlaying)Destroy(gameObject);else DestroyImmediate(gameObject);
    }
}
