using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyBossBloodSpray : MonoBehaviour
{
    ParticleSystem[] systems;
    Renderer[] renderers;
    MaterialPropertyBlock style;
    bool emitting;
    int seededSequence=-1;
    public bool IsEmitting=>emitting;
    void Resolve()
    {
        if(systems==null)systems=GetComponentsInChildren<ParticleSystem>(true);
        if(renderers==null)renderers=GetComponentsInChildren<Renderer>(true);
        if(style==null)style=new MaterialPropertyBlock();
    }
    public int ParticleCount {get{Resolve();int count=0;foreach(var ps in systems)count+=ps.particleCount;return count;}}
    public void Begin(BloodHitProfile profile,float scale)
    {
        Resolve();var parentScale=transform.parent!=null?transform.parent.lossyScale:Vector3.one;
        transform.localScale=new Vector3(scale/Mathf.Max(.001f,parentScale.x),scale/Mathf.Max(.001f,parentScale.y),scale/Mathf.Max(.001f,parentScale.z));
        gameObject.SetActive(true);
        style.Clear();if(profile!=null)style.SetColor("_BaseColor",profile.mainColor);
        style.SetFloat("_Smoothness",profile!=null?Mathf.Clamp(profile.specular,.1f,.4f):.2f);
        style.SetFloat("_AlbedoPower",.45f);style.SetFloat("_ColorIntensity",1.1f);style.SetFloat("_AmbientColorIntensity",.7f);
        foreach(var renderer in renderers)renderer.SetPropertyBlock(style);
        if(emitting)return;foreach(var ps in systems)ps.Play(false);emitting=true;
    }
    public void Place(Vector3 origin,Vector3 velocity,float duration,float halfAngle,float gravity,int sequence)
    {
        Resolve();transform.SetPositionAndRotation(origin,Quaternion.LookRotation(velocity.normalized,Vector3.up));
        for(int i=0;i<transform.childCount;i++){
            var layer=transform.GetChild(i);float t=transform.childCount<=1?.5f:i/(float)(transform.childCount-1);
            layer.localRotation=Quaternion.Inverse(transform.rotation)*Quaternion.AngleAxis(Mathf.Lerp(-halfAngle*.72f,halfAngle*.72f,t),Vector3.up)*transform.rotation;
        }
        for(int i=0;i<systems.Length;i++){
            var ps=systems[i];var main=ps.main;main.startSpeed=new ParticleSystem.MinMaxCurve(velocity.magnitude*.82f,velocity.magnitude*1.08f);
            main.startLifetime=new ParticleSystem.MinMaxCurve(Mathf.Clamp(duration*.82f,.12f,2f),Mathf.Clamp(duration*1.05f,.12f,2f));
            var force=ps.forceOverLifetime;force.enabled=true;force.space=ParticleSystemSimulationSpace.World;force.x=force.z=0f;force.y=-gravity;
            var shape=ps.shape;shape.angle=halfAngle*.32f;shape.scale=new Vector3(1f,.32f,1f);
            if(seededSequence!=sequence){ps.useAutoRandomSeed=false;ps.randomSeed=unchecked((uint)sequence*0x9E3779B9u+(uint)(i+1)*0x85EBCA6Bu);}
        }
        seededSequence=sequence;
    }
    public void Stop(bool clear)
    {
        Resolve();foreach(var ps in systems)ps.Stop(false,clear?ParticleSystemStopBehavior.StopEmittingAndClear:ParticleSystemStopBehavior.StopEmitting);emitting=false;if(clear)gameObject.SetActive(false);
    }
    void OnDisable(){if(systems!=null){foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);emitting=false;}seededSequence=-1;}
}
