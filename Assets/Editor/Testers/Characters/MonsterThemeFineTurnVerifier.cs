using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Inspects partial turns in actual Play Mode, including the supporting soles in world space.
public static class MonsterThemeFineTurnVerifier
{
    private sealed class Frame
    {
        public float time, yaw;
        public Vector3 root;
        public Vector3[] feet;
    }

    public static IEnumerator Verify(EnemyThemeDebugUI ui,PlayerInputFacade player)
    {
        string output=SessionState.GetString("MonsterThemePlayVerifier.output","");
        Require(Directory.Exists(output),"Output directory");
        Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out var service),"Spawn service");
        foreach(var table in ui.tables)Require(service.RegisterAdditionalCatalog(table.Catalog,out _),"Catalog");
        var results=new List<object>();
        string filter=SessionState.GetString("MonsterThemeFineTurn.species","");
        const string suffix="-authored";
        foreach(var definition in ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().Where(d=>string.IsNullOrEmpty(filter)||filter.Split(',').Contains(d.EnemyId)))
        {
            Vector3 home=player.transform.position+Vector3.forward*8;
            var request=new EnemySpawnRequest(definition,home,Quaternion.identity,player.transform,null,player.transform,null,1,1,77);
            Require(service.TrySpawn(request,out var actor),"Spawn "+definition.EnemyId);
            actor.AI.enabled=false;var previousCulling=actor.Animator.cullingMode;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            try
            {
                var probes=MonsterThemeStrideCalibration.CreateRuntimeProbes(actor);
                Require(probes.Length>=2,"Sole probes");
                foreach(float angle in new[]{15f,45f,-45f,135f})
                {
                    MonsterThemeFacingVerifier.Reset(actor,home);
                    float settle=Time.time+.3f;while(Time.time<settle)yield return null;
                    Vector3 destination=home+Quaternion.Euler(0,angle,0)*Vector3.forward*10;
                    var frames=new List<Frame>();var sheet=angle==45f?new Texture2D(1024,256,TextureFormat.RGB24,false):null;
                    float began=Time.time;int capture=0;
                    try
                    {
                        while(!actor.Movement.IsFacingForAttack(destination))
                        {
                            Require(Time.time-began<5f,"Timeout "+definition.EnemyId+" / "+angle);
                            actor.Movement.FacePosition(destination);yield return null;
                            float yaw=Mathf.DeltaAngle(0,actor.transform.eulerAngles.y);
                            Require(yaw*Mathf.Sign(angle)>=-3f && yaw*Mathf.Sign(angle)<=Mathf.Abs(angle)+3f,"Wrong-way turn or overshoot "+definition.EnemyId);
                            frames.Add(new Frame{time=Time.time,yaw=yaw,root=actor.transform.position,feet=probes.Select(p=>p.sample()).ToArray()});
                            if(sheet!=null && capture<4 && Time.time-began>=new[]{.1f,.3f,.55f,.8f}[capture]*90f/definition.MovementProfile.TurnAnimationReferenceSpeed(1))
                                MonsterThemeFacingVerifier.Capture(actor,sheet,capture++);
                        }
                        Require(Vector3.Distance(home,actor.transform.position)<.12f,"Turn translation");
                        var drift=new List<float>();
                        for(int foot=0;foot<probes.Length;foot++)
                        {
                            float low=frames.Min(f=>f.feet[foot].y-f.root.y);
                            for(int i=1;i<frames.Count-1;i++)
                            {
                                var before=frames[i-1];var after=frames[i+1];var current=frames[i];float dt=after.time-before.time;
                                float angular=Mathf.Abs(Mathf.DeltaAngle(before.yaw,after.yaw))/dt*Mathf.Deg2Rad;
                                Vector3 velocity=(after.feet[foot]-before.feet[foot])/dt;
                                Vector3 offset=current.feet[foot]-current.root;offset.y=0;
                                if(angular>.2f && offset.magnitude>.1f && current.feet[foot].y-current.root.y<low+.025f && Mathf.Abs(velocity.y)<.2f)
                                    drift.Add(new Vector2(velocity.x,velocity.z).magnitude/(angular*offset.magnitude));
                            }
                        }
                        drift.Sort();
                        float median=drift.Count>0?drift[drift.Count/2]:-1f;
                        File.WriteAllText(Path.Combine(output,definition.EnemyId+"-turn-"+angle+suffix+"-feet.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{feet=probes.Select(p=>p.name),frames=frames.Select(f=>new{f.time,f.yaw,root=new[]{f.root.x,f.root.y,f.root.z},feet=f.feet.Select(v=>new[]{v.x,v.y,v.z})}),drift,median}));
                        if(sheet!=null)File.WriteAllBytes(Path.Combine(output,definition.EnemyId+"-fine-turn"+suffix+".png"),sheet.EncodeToPNG());
                        results.Add(new{id=definition.EnemyId,angle,seconds=Time.time-began,samples=frames.Count,plantedSamples=drift.Count,medianRotationalFootDrift=median,finalError=Vector3.Angle(actor.transform.forward,destination-actor.transform.position),status="PASS",footContact="MEASURED_REQUIRES_REVIEW"});
                    }
                    finally{if(sheet!=null)UnityEngine.Object.Destroy(sheet);}
                }
                File.WriteAllText(Path.Combine(output,"fine-turn"+suffix+"-results.json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
                Debug.Log("[MonsterThemeFineTurn] PASS angles / measured soles: "+definition.EnemyId);
            }
            finally
            {
                actor.Animator.cullingMode=previousCulling;actor.RequestPoolRelease();
            }
        }
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[MonsterThemeFineTurn] "+message);}
}
