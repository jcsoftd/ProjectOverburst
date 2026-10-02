using System;
using System.Collections.Generic;
using UnityEngine;

// Owns committed casts independently of the weapon action that created them.
public sealed class ElementChainScheduler : MonoBehaviour
{
    private sealed class Cast
    {
        public ElementDischargeBatch Batch;
        public GameObject Source,Fire,Link;
        public float Damage,Energy,FireReferenceRadius,SfxEnergy;
        public readonly Action<Vector3,float> FireCallback;
        public readonly Action<Vector3,Vector3> LinkCallback;
        public Cast(){FireCallback=PlayFire;LinkCallback=PlayLink;}
        private void PlayFire(Vector3 point,float radius)
        {
            Spawn(Fire,point,radius/FireReferenceRadius);
            MeleeElementSfxService.TryPlayFollowUp(WeaponElement.Fire,point,SfxEnergy); // A20 화염 전파
        }
        private void PlayLink(Vector3 from,Vector3 to)
        {
            MeleeElementSfxService.TryPlayFollowUp(WeaponElement.Electric,to,SfxEnergy); // A19 번개 홉
            if(Link==null||ChainElectricityBatchRenderer.TrySpawn(Link,from,to))return;
            Vector3 delta=to-from;float length=delta.magnitude;if(length<=.05f)return;
            var prefab=Link;
            TransientVfxPool.Spawn(prefab,(from+to)*.5f,Quaternion.LookRotation(delta/length),0,12,
                prepareBeforeActivation:g=>g.transform.localScale=Vector3.Scale(prefab.transform.localScale,new Vector3(1,1,length)),
                returnMode:TransientVfxReturnMode.NaturalParticleCompletion);
        }
        private static void Spawn(GameObject prefab,Vector3 point,float scale)
        {
            if(prefab==null)return;
            TransientVfxPool.Spawn(prefab,point,Quaternion.identity,0,MeleeHeavyVfxPreparation.RetainedCapacity(prefab),
                prepareBeforeActivation:g=>g.transform.localScale=prefab.transform.localScale*scale);
        }
        public void Clear(){Batch.Clear();Source=Fire=Link=null;Damage=Energy=SfxEnergy=0;}
    }
    private static ElementChainScheduler instance;
    private readonly List<Cast> active=new List<Cast>(8);
    private readonly Stack<Cast> free=new Stack<Cast>(8);
    public static int ActiveCastCount=>(instance!=null?instance.active.Count:0)+(ShatterWaveScheduler.PendingCount>0?1:0);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()=>instance=null;
    public static ElementDischargeBatch Submit(ElementDischargeBatch batch,Vector3 center,
        float damage,float energy,GameObject source,GameObject fire,GameObject link,
        float fireReferenceRadius=1f,float sfxEnergy=MeleeElementSfxService.FullVolumeEnergy)
    {
        if(batch.OriginCount==0){batch.Clear();return batch;}
        if(instance==null)
        {
            var go=new GameObject("ElementChainScheduler");DontDestroyOnLoad(go);instance=go.AddComponent<ElementChainScheduler>();
        }
        Cast cast=instance.free.Count>0?instance.free.Pop():new Cast{Batch=new ElementDischargeBatch()};
        var replacement=cast.Batch;cast.Batch=batch;cast.Source=source;cast.Fire=fire;cast.Link=link;
        cast.Damage=damage;cast.Energy=energy;cast.FireReferenceRadius=Mathf.Max(.01f,fireReferenceRadius);
        cast.SfxEnergy=sfxEnergy;
        batch.BeginDelayed(center,Time.time);instance.active.Add(cast);
        return replacement;
    }
    private void Update()
    {
        for(int i=active.Count-1;i>=0;i--)
        {
            var cast=active[i];
            if(cast.Batch.AdvanceDelayed(Time.time,cast.Damage,cast.Energy,cast.Source,cast.FireCallback,cast.LinkCallback))continue;
            cast.Clear();active.RemoveAt(i);free.Push(cast);
        }
    }
    private void OnDestroy(){foreach(var cast in active)cast.Clear();active.Clear();free.Clear();if(instance==this)instance=null;}
}
