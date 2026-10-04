using UnityEngine;

// A boss-owned copy of the Blood Effects Pack stream. World-space particles retain the swept trail.
[DisallowMultipleComponent]
public sealed class EnemyBossBloodSpray : MonoBehaviour
{
    ParticleSystem[] systems;
    Renderer[] renderers;
    MaterialPropertyBlock style;
    bool emitting;
    public bool IsEmitting=>emitting;
    public int ParticleCount {get{Resolve();int count=0;foreach(var ps in systems)count+=ps.particleCount;return count;}}
    void Resolve(){if(systems==null)systems=GetComponentsInChildren<ParticleSystem>(true);if(renderers==null)renderers=GetComponentsInChildren<Renderer>(true);if(style==null)style=new MaterialPropertyBlock();}
    public void Begin(BloodHitProfile profile,float scale)
    {
        Resolve();transform.localScale=Vector3.one*scale;gameObject.SetActive(true);
        style.Clear();if(profile!=null)style.SetColor("_BaseColor",profile.mainColor);
        style.SetFloat("_Smoothness",profile!=null?Mathf.Clamp(profile.specular,.1f,.4f):.2f);
        style.SetFloat("_AlbedoPower",.45f);style.SetFloat("_ColorIntensity",1.1f);style.SetFloat("_AmbientColorIntensity",.7f);
        foreach(var renderer in renderers)renderer.SetPropertyBlock(style);
        if(emitting)return;foreach(var ps in systems)ps.Play(false);emitting=true;
    }
    public void Place(Vector3 origin,Vector3 direction,float length)
    {
        Resolve();transform.SetPositionAndRotation(origin,Quaternion.LookRotation(direction,Vector3.up));
        foreach(var ps in systems){var main=ps.main;main.startLifetime=Mathf.Clamp(length/Mathf.Max(1f,main.startSpeed.constantMax),.12f,1.15f);}
    }
    public void Stop(bool clear)
    {
        Resolve();foreach(var ps in systems)ps.Stop(false,clear?ParticleSystemStopBehavior.StopEmittingAndClear:ParticleSystemStopBehavior.StopEmitting);
        emitting=false;if(clear)gameObject.SetActive(false);
    }
    void OnDisable(){if(systems!=null){foreach(var ps in systems)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);emitting=false;}}
}
