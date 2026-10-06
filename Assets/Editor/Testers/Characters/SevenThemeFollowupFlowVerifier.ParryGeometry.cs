using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static partial class SevenThemeFollowupFlowVerifier
{
    static JObject parryTrace;
    static readonly FieldInfo firstImpactField=typeof(EnemyAbilityController).GetField("firstImpactAt",PrivateInstance);
    static readonly FieldInfo lastImpactField=typeof(EnemyAbilityController).GetField("lastImpactAt",PrivateInstance);
    static readonly FieldInfo cadenceField=typeof(EnemyAbilityController).GetField("attacksSinceStrong",PrivateInstance);
    static EnemyAbilityDefinition Strong(EnemyDefinition d)=>Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).First(a=>a.IsParryable);
    static IEnumerator ParryGeometry(EnemyCatalog catalog,CombatHealth health,CapsuleCollider capsule,CombatTarget combat)
    {
        EnemyStrongAttackWarning.PlayerTarget=combat;
        yield return FootprintComparison(catalog,health,capsule,combat);
        if(Mode=="ParryGeometryAfter")yield return StrongGeometrySafety(catalog,health,capsule,combat);
        else yield return ReaperTimeline(catalog,health,combat);
        parryTrace=null;EnemyStrongAttackWarning.PlayerTarget=null;
    }
    static GameObject MeshOutline(ProceduralGroundIndicator indicator)
    {
        var renderer=indicator.Surface;if(renderer==null||renderer.mesh==null)throw new InvalidOperationException("Actual indicator surface missing");
        var original=renderer.mesh;var vertices=original.vertices;var triangles=original.triangles;
        var edges=new Dictionary<(int,int),int>();
        for(int i=0;i<triangles.Length;i+=3)for(int j=0;j<3;j++)
        {
            int a=triangles[i+j],b=triangles[i+(j+1)%3];var key=a<b?(a,b):(b,a);
            edges.TryGetValue(key,out int n);edges[key]=n+1;
        }
        var lines=edges.Where(x=>x.Value==1).SelectMany(x=>new[]{x.Key.Item1,x.Key.Item2}).ToArray();
        var mesh=new Mesh{name="Owned exact runtime telegraph mesh outline"};owned.Add(mesh);
        mesh.vertices=vertices.Select(v=>renderer.transform.TransformPoint(v)+Vector3.up*.016f).ToArray();mesh.SetIndices(lines,MeshTopology.Lines,0);mesh.RecalculateBounds();
        var go=new GameObject("Actual telegraph surface cyan outline");owned.Add(go);go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));owned.Add(material);material.color=new Color(.1f,1f,.95f);go.AddComponent<MeshRenderer>().sharedMaterial=material;
        return go;
    }
    static bool SurfaceOverlapsBody(ProceduralGroundIndicator indicator,CombatTargetVolume body)
    {
        var renderer=indicator.Surface;var mesh=renderer.mesh;var vertices=mesh.vertices;var indices=mesh.triangles;
        Vector2 p=new Vector2(body.Center.x,body.Center.z);float squared=body.Radius*body.Radius;
        Vector2 World(int i){Vector3 q=renderer.transform.TransformPoint(vertices[i]);return new Vector2(q.x,q.z);}
        float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        float Edge(Vector2 a,Vector2 b){var ab=b-a;float t=Mathf.Clamp01(Vector2.Dot(p-a,ab)/Mathf.Max(.0000001f,ab.sqrMagnitude));return (p-a-ab*t).sqrMagnitude;}
        for(int i=0;i<indices.Length;i+=3){var a=World(indices[i]);var b=World(indices[i+1]);var c=World(indices[i+2]);if(Mathf.Abs(Cross(b-a,c-a))<.0000001f)continue;float x=Cross(b-a,p-a),y=Cross(c-b,p-b),z=Cross(a-c,p-c);
            if((x>=0&&y>=0&&z>=0)||(x<=0&&y<=0&&z<=0))return true;
            if(Mathf.Min(Edge(a,b),Mathf.Min(Edge(b,c),Edge(c,a)))<=squared)return true;}
        return false;
    }
    static Vector3 Point(float radius,float degrees)=>Quaternion.Euler(0,degrees,0)*Vector3.forward*radius;
    static IEnumerator FootprintComparison(EnemyCatalog catalog,CombatHealth health,CapsuleCollider capsule,CombatTarget combat)
    {
        var arcHit=typeof(EnemyMeleeAttackController).GetMethod("ResolveArcHit",PrivateInstance);
        var chargeHit=typeof(EnemyThemeSpecialExecutor).GetMethod("ResolveChargeHit",PrivateInstance);
        foreach(string id in Mode=="ParryGeometry2"?new[]{"SpiderBrood_Carcinoptera"}:new[]{"DeathHarvest_Reaper","CavernMutants_Ursacetus","SpiderBrood_Carcinoptera"})foreach(float yaw in new[]{0f,90f,180f})
        {
            if(!catalog.TryGet(id,out var d))throw new InvalidOperationException(id);
            var ability=Strong(d);currentId=id+"_geometry_"+yaw;trace=new JArray();damages=new JArray();begin=Time.time;phase="boundary_samples";
            target.position=Vector3.forward*15;health.SetMaxHp(100000,true);
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.Euler(0,yaw,0),target,context:EncounterContext.Test),out actor))throw new InvalidOperationException(id);
            actor.AI.enabled=false;actor.Movement.StopMovement();actor.GetComponent<Rigidbody>().isKinematic=true;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;yield return null;
            float radius=EnemyAttackThreatGeometry.ResolveRadius(actor,ability),angle=EnemyAttackThreatGeometry.ResolveHitAngle(actor,ability),inner=EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor,ability);
            bool charge=ability.ExecutionMode==EnemyAbilityExecutionMode.Charge;
            var special=actor.GetComponent<EnemyThemeSpecialExecutor>();
            if(charge)typeof(EnemyThemeSpecialExecutor).GetField("chargeDirection",PrivateInstance).SetValue(special,actor.transform.forward);
            var warning=actor.GetComponent<EnemyStrongAttackWarning>()??actor.gameObject.AddComponent<EnemyStrongAttackWarning>();
            warning.Show(radius,true,angle,charge,true,1.1f,EnemyAttackThreatGeometry.ChargeHalfWidth,inner);
            Vector3 center=charge?actor.transform.position:EnemyAttackThreatGeometry.ResolveImpactCenter(actor,ability,actor.Melee.AttackPoint.position);
            warning.SetCenter(center);warning.SetFacing(actor.transform.forward);warning.SetRemaining(.2f,false);
            var indicator=actor.GetComponentInChildren<ProceduralGroundIndicator>();
            if(indicator==null)throw new InvalidOperationException("Saved procedural indicator missing");
            var outline=MeshOutline(indicator);var markers=new List<GameObject>();var samples=new JArray();
            var points=new List<(string,Vector3)>();float bodyRadius=combat.CurrentVolume.Radius;
            foreach(float offset in new[]{-.05f,.05f})
            {
                if(charge)
                {
                    float half=EnemyAttackThreatGeometry.ChargeHalfWidth;
                    points.Add(("side_center"+offset,new Vector3(half+offset,0,radius*.5f)));
                    points.Add(("side_body"+offset,new Vector3(half+bodyRadius+offset,0,radius*.5f)));
                    points.Add(("end_body"+offset,new Vector3(0,0,radius+half+bodyRadius+offset)));
                    points.Add(("start_body"+offset,new Vector3(0,0,-half-bodyRadius+offset)));
                }
                else
                {
                    points.Add(("outer_center"+offset,Point(radius+offset,0)));
                    points.Add(("outer_body"+offset,Point(radius+bodyRadius+offset,0)));
                    if(angle<359.9f)
                    {
                        points.Add(("side_center"+offset,Point(radius*.65f,angle*.5f+offset*Mathf.Rad2Deg/(radius*.65f))));
                        points.Add(("side_body"+offset,Point(radius*.65f,angle*.5f+(bodyRadius+offset)*Mathf.Rad2Deg/(radius*.65f))));
                        points.Add(("inner_center"+offset,Point(Mathf.Max(.01f,inner+offset),0)));
                        points.Add(("inner_body"+offset,Point(Mathf.Max(.01f,inner-bodyRadius+offset),0)));
                    }
                }
            }
            camera=new Capture(Path.Combine(Folder,currentId),target,actor,true);camera.Mark(phase);
            foreach(var (label,local) in points)
            {
                health.SetMaxHp(100000,true);target.position=center+Quaternion.Euler(0,yaw,0)*local;target.position=new Vector3(target.position.x,0,target.position.z);Physics.SyncTransforms();
                bool would=EnemyAttackThreatGeometry.WouldHit(actor,ability,combat);int n=damages.Count;
                // Invoke the production geometry consumer, without a combat timeline. This isolates shape and height rules.
                if(charge)chargeHit.Invoke(special,new object[]{ability,actor.transform.forward});
                else arcHit.Invoke(actor.Melee,new object[]{10f,radius,angle,ability});
                bool damaged=damages.Count>n;
                samples.Add(new JObject{["label"]=label,["root"]=V(target.position),["local"]=V(local),["wouldHit"]=would,["damageDelivered"]=damaged,["surfaceOverlapsCombatBody"]=SurfaceOverlapsBody(indicator,combat.CurrentVolume),["hpLoss"]=100000-health.CurrentHp});
                var mark=GameObject.CreatePrimitive(PrimitiveType.Sphere);owned.Add(mark);markers.Add(mark);Object.DestroyImmediate(mark.GetComponent<Collider>());
                mark.transform.position=target.position+Vector3.up*.12f;mark.transform.localScale=Vector3.one*.12f;
                var m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));owned.Add(m);m.color=damaged?new Color(.2f,1f,.38f):new Color(1f,.25f,.2f);mark.GetComponent<Renderer>().sharedMaterial=m;
                yield return null;
            }
            target.position=Vector3.forward*15;Physics.SyncTransforms();phase="all_boundary_results";camera.Mark(phase);yield return null;yield return null;
            results.Add(new JObject{["scenario"]="actual_indicator_geometry_boundaries",["id"]=id,["name"]=d.DisplayName,["yaw"]=yaw,["ability"]=ability.AbilityId,["mode"]=ability.ExecutionMode.ToString(),
                ["radius"]=radius,["innerRadius"]=inner,["angle"]=angle,["playerRadius"]=bodyRadius,["physicalCapsuleRadius"]=capsule.radius,["physicalCapsuleHeight"]=capsule.height,["physicalCapsuleCenter"]=V(capsule.center),["shape"]=indicator.Shape.ToString(),["indicatorRadius"]=indicator.OuterRadius,
                ["indicatorInnerRadius"]=indicator.InnerRadius,["indicatorAngle"]=indicator.Angle,["indicatorCenter"]=V(indicator.transform.position),["damageCenter"]=V(center),
                ["shapeValuesMatch"]=Mathf.Abs(radius-indicator.OuterRadius)<.001f&&Mathf.Abs(inner-indicator.InnerRadius)<.001f&&Mathf.Abs(angle-indicator.Angle)<.001f,
                ["samples"]=samples,["previewMatchesDamage"]=samples.All(x=>(bool)x["wouldHit"]==(bool)x["damageDelivered"]),["exactRuntimeSurfaceMeshOutline"]=true,["assetWrites"]=0,["actualAttackTimeline"]=false});
            foreach(var mark in markers)Object.DestroyImmediate(mark);Object.DestroyImmediate(outline);warning.Hide();SaveCase(currentId);yield return null;
        }
    }
    static IEnumerator ReaperTimeline(EnemyCatalog catalog,CombatHealth health,CombatTarget combat)
    {
        if(!catalog.TryGet("DeathHarvest_Reaper",out var d))throw new InvalidOperationException("Reaper");var ability=Strong(d);
        foreach(float speed in new[]{.75f,1f,1.5f})
        {
            currentId=d.EnemyId+"_timeline_"+Mathf.RoundToInt(speed*100);trace=new JArray();damages=new JArray();begin=Time.time;phase="windup";
            target.position=Vector3.forward*2.5f;health.SetMaxHp(100000,true);Physics.SyncTransforms();
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("Timeline spawn");
            actor.AI.enabled=false;actor.Movement.StopMovement();actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;actor.Melee.SetRuntimeAttackSpeedMultiplier(speed);
            cadenceField.SetValue(actor.AbilityController,3);yield return null;yield return new WaitForFixedUpdate();
            float started=Time.time;bool ok=actor.AbilityController.TryStartAbility(ability,target);
            if(!ok)throw new InvalidOperationException("Saved Reaper heavy start refused");begin=started;
            camera=new Capture(Path.Combine(Folder,currentId),target,actor,true,30);camera.Mark(phase);
            float motionAt=-1,threatAt=-1,cueAt=-1;var marks=new HashSet<string>();
            while(actor.AbilityController.IsExecuting&&Time.time-started<9f)
            {
                float normalized=-1;bool motion=actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger,out normalized);
                bool threat=actor.AbilityController.IsParryThreatTo(combat);var warning=actor.GetComponent<EnemyStrongAttackWarning>();bool cue=warning!=null&&warning.FinalSignal;
                if(motion&&motionAt<0)motionAt=Time.time-started;if(threat&&threatAt<0)threatAt=Time.time-started;if(cue&&cueAt<0)cueAt=Time.time-started;
                phase=motion?"strong_motion":"windup";
                string key=threat&&!motion?"parry_before_clip":motion&&normalized<.15f?"first_swing":motion&&normalized<.38f?"second_swing":motion?"third_and_recovery":"windup";
                if(marks.Add(key))camera.Mark(key);
                parryTrace=new JObject{["elapsed"]=Time.time-started,["motionPresent"]=motion,["normalized"]=normalized,["threat"]=threat,["cueFinal"]=cue,
                    ["firstImpactRemaining"]=(float)firstImpactField.GetValue(actor.AbilityController)-Time.time,["lastImpactRemaining"]=(float)lastImpactField.GetValue(actor.AbilityController)-Time.time};
                yield return null;
            }
            parryTrace=null;
            results.Add(new JObject{["scenario"]="reaper_current_time_and_motion",["id"]=d.EnemyId,["name"]=d.DisplayName,["speed"]=speed,["ability"]=ability.AbilityId,["hitCount"]=ability.HitCount,
                ["motionFirstSeenSeconds"]=motionAt,["actualThreatFirstSeconds"]=threatAt,["cueFirstSeconds"]=cueAt,["currentAllowedBeforeClip"]=threatAt>=0&&motionAt>threatAt,
                ["computedWindup"]=ability.ResolveWindupDelay(speed),["computedFirstHit"]=ability.ResolveFirstImpactTime(speed),["computedLastHit"]=ability.ResolveLastImpactTime(speed),
                ["damageEvents"]=damages,["assetWrites"]=0,["parryTimingWrites"]=0,["fixtureCadencePrepared"]=true,["actualPlayerInput"]=false});
            SaveCase(currentId);yield return null;
        }
    }
    static IEnumerator StrongGeometrySafety(EnemyCatalog catalog,CombatHealth health,CapsuleCollider capsule,CombatTarget combat)
    {
        var arcHit=typeof(EnemyMeleeAttackController).GetMethod("ResolveArcHit",PrivateInstance);
        var chargeHit=typeof(EnemyThemeSpecialExecutor).GetMethod("ResolveChargeHit",PrivateInstance);
        foreach(string id in new[]{"DeathHarvest_Reaper","CavernMutants_Ursacetus","SpiderBrood_Carcinoptera"})
        {
            catalog.TryGet(id,out var d);var ability=Strong(d);currentId=id+"_geometry_safety";trace=new JArray();damages=new JArray();begin=Time.time;phase="geometry_safety";
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException(id);
            actor.AI.enabled=false;actor.GetComponent<Rigidbody>().isKinematic=true;yield return null;
            var special=actor.GetComponent<EnemyThemeSpecialExecutor>();bool charge=ability.ExecutionMode==EnemyAbilityExecutionMode.Charge;
            if(charge)typeof(EnemyThemeSpecialExecutor).GetField("chargeDirection",PrivateInstance).SetValue(special,Vector3.forward);
            void Damage(){if(charge)chargeHit.Invoke(special,new object[]{ability,Vector3.forward});else arcHit.Invoke(actor.Melee,new object[]{10f,EnemyAttackThreatGeometry.ResolveRadius(actor,ability),EnemyAttackThreatGeometry.ResolveHitAngle(actor,ability),ability});}
            bool Probe(){Physics.SyncTransforms();int n=damages.Count;Damage();return damages.Count>n;}
            target.position=new Vector3(0,0,2.5f);health.SetMaxHp(100000,true);CombatTargetRegistry.NotifySpatialChanged(combat);yield return null;
            bool normal=Probe();
            target.position=new Vector3(0,6,2.5f);CombatTargetRegistry.NotifySpatialChanged(combat);yield return null;bool highDenied=!Probe()&&!EnemyAttackThreatGeometry.WouldHit(actor,ability,combat);
            target.position=new Vector3(0,0,2.5f);combat.Configure(CombatTeam.Enemy,false);CombatTargetRegistry.NotifySpatialChanged(combat);yield return null;bool teamDenied=!Probe()&&!EnemyAttackThreatGeometry.WouldHit(actor,ability,combat);combat.Configure(CombatTeam.PlayerParty,false);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(wall);wall.name="Owned saved strong area obstruction test";wall.transform.SetPositionAndRotation(new Vector3(0,1,.95f),Quaternion.identity);wall.transform.localScale=new Vector3(3,2,.3f);yield return null;
            bool obstructionApplicable=charge||ability.RequireLineOfSight;bool obstacleDenied=!obstructionApplicable||!Probe()&&!EnemyAttackThreatGeometry.WouldHit(actor,ability,combat);Object.DestroyImmediate(wall);yield return null;
            capsule.enabled=false;yield return null;bool logicalBodyHit=Probe();capsule.enabled=true;
            int previous=actor.GetInstanceID();service.Release(actor);actor=null;yield return null;
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("Geometry pool "+id);
            actor.AI.enabled=false;actor.GetComponent<Rigidbody>().isKinematic=true;special=actor.GetComponent<EnemyThemeSpecialExecutor>();if(charge)typeof(EnemyThemeSpecialExecutor).GetField("chargeDirection",PrivateInstance).SetValue(special,Vector3.forward);yield return null;
            bool reset=!actor.Health.IsDead&&!actor.AbilityController.IsExecuting&&Probe();
            results.Add(new JObject{["scenario"]="strong_geometry_safety",["id"]=id,["normalDamage"]=normal,["heightDenied"]=highDenied,["friendlyDenied"]=teamDenied,["obstructionApplicable"]=obstructionApplicable,["obstructionDenied"]=obstacleDenied,["authoredCombatBodyUsedWithoutPhysicalCollider"]=logicalBodyHit,["pooledInstanceReused"]=actor.GetInstanceID()==previous,["poolReuseQueryFresh"]=reset,["actualAttackTimeline"]=false});
            if(!normal||!highDenied||!teamDenied||!obstacleDenied||!logicalBodyHit||!reset)throw new InvalidOperationException("Strong geometry safety "+id);
            foreach(int count in new[]{50,150})
            {
                var sample=new double[120];var body=combat.CurrentVolume;var sw=new System.Diagnostics.Stopwatch();int overlaps=0;
                EnemyAttackThreatGeometry.OverlapsStrongArea(actor,ability,actor.transform.position,Vector3.forward,body);
                long start=GC.GetAllocatedBytesForCurrentThread();
                for(int frame=0;frame<sample.Length;frame++){sw.Restart();for(int i=0;i<count;i++)if(EnemyAttackThreatGeometry.OverlapsStrongArea(actor,ability,actor.transform.position,Vector3.forward,body))overlaps++;sw.Stop();sample[frame]=sw.Elapsed.TotalMilliseconds;}
                long bytes=GC.GetAllocatedBytesForCurrentThread()-start;Array.Sort(sample);
                results.Add(new JObject{["scenario"]="strong_shape_CPU_GC_only",["id"]=id,["shapeChecksPerSample"]=count,["samples"]=120,["medianMs"]=sample[60],["p95Ms"]=sample[114],["allocatedBytes"]=bytes,["includesRegistryOrPhysics"]=false,["includesRendering"]=false,["overlapCount"]=overlaps});
            }
            SaveCase(currentId);yield return null;
        }
    }

}
