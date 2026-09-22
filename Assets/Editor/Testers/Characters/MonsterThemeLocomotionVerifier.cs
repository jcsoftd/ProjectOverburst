using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

// Checks actual bone poses after several clip cycles, not merely Animator parameters.
public static class MonsterThemeLocomotionVerifier
{
    private static readonly List<object> results = new List<object>();
    private static string output;
    private static Camera captureCamera;
    private static Texture2D sheet;
    private const int Tile = 320;
    private static int captureIndex;
    private sealed class SoleFrame { public float time; public Vector3 root; public Vector3[] soles; }

    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        output = SessionState.GetString("MonsterThemePlayVerifier.output", "");
        Require(Directory.Exists(output), "Set an existing output directory");
        Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform, out var service), "Spawn service missing");
        foreach (var table in ui.tables) Require(service.RegisterAdditionalCatalog(table.Catalog, out _), "Catalog registration");
        results.Clear();
        var cameraObject = new GameObject("Locomotion verification camera");
        captureCamera = cameraObject.AddComponent<Camera>();
        captureCamera.enabled = false; captureCamera.orthographic = true;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = new Color(.16f,.19f,.23f);
        captureCamera.cullingMask = 1 << LayerMask.NameToLayer("Enemy");
        captureCamera.nearClipPlane = .01f; captureCamera.farClipPlane = 100;
        try
        {
            foreach (var definition in ui.tables.SelectMany(t => t.Entries).Select(e => e.definition).Distinct())
            {
                Vector3 start = player.transform.position + Vector3.right * 8 + Vector3.forward * 6;
                var request = new EnemySpawnRequest(definition, start, Quaternion.identity, player.transform, null, player.transform, null, 1, 1, 77);
                Require(service.TrySpawn(request, out var actor), definition.EnemyId + " spawn");
                var culling = actor.Animator.cullingMode;
                actor.AI.enabled = false;
                // The off-camera fixture must evaluate its bones; production culling is restored before pooling.
                actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                sheet = new Texture2D(Tile * 4, Tile * 2, TextureFormat.RGB24, false);
                captureIndex = 0;
                try
                {
                    yield return Seconds(.3f);
                    Capture(actor);
                    foreach (var mode in new[] { EnemyLocomotionMode.Walk, EnemyLocomotionMode.Run, EnemyLocomotionMode.Backpedal })
                    {
                        ResetPosition(actor, start);
                        yield return MeasureGait(actor, mode, start);
                        actor.Movement.StopMovement(); yield return Seconds(.35f);
                        Vector3 stopped = actor.transform.position;
                        yield return Seconds(.5f);
                        Require(Vector3.Distance(stopped, actor.transform.position) < .04f, definition.EnemyId + " idle sliding");
                        Require(Mathf.Abs(actor.Animator.GetFloat("Locomotion")) < .03f, definition.EnemyId + " idle parameter");
                        Require(Mathf.Abs(actor.Animator.GetFloat("MoveAnimSpeed") - 1f) < .04f, definition.EnemyId + " idle playback");
                    }

                    ResetPosition(actor,start);
                    var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name="Locomotion obstruction fixture";
                    wall.transform.position=start+Vector3.forward*2f+Vector3.up*3f;
                    wall.transform.localScale=new Vector3(12f,6f,1f);Physics.SyncTransforms();
                    try
                    {
                        actor.Movement.SetDestination(start+Vector3.forward*25,.1f,EnemyLocomotionMode.Run);
                        yield return Seconds(2.5f);
                        Vector3 held=actor.transform.position;yield return Seconds(.5f);
                        Require(Vector3.Distance(held,actor.transform.position)<.08f,definition.EnemyId+" wall fixture failed to stop body");
                        Require(Mathf.Abs(actor.Animator.GetFloat("Locomotion"))<.08f,definition.EnemyId+" running feet against a blocked body");
                    }
                    finally {Object.Destroy(wall);}
                    Vector3 released=actor.transform.position;
                    yield return Until(()=>Vector3.Distance(released,actor.transform.position)>.6f,3,definition.EnemyId+" movement did not resume after obstacle removal");
                    actor.Movement.StopMovement();
                    results.Add(new{id=definition.EnemyId,phase="obstruction-stop-resume",status="PASS"});

                    ResetPosition(actor, player.transform.position - Vector3.forward * 3);
                    actor.Health.TakeDamage(new DamageInfo(1,actor.transform.position,player.gameObject,Vector3.back));
                    actor.Movement.SetDestination(actor.transform.position + Vector3.right * 15,.1f,EnemyLocomotionMode.Walk);
                    yield return Seconds(.15f); Capture(actor);
                    yield return Until(() => !actor.AnimationBridge.IsBlockingActionActive && !actor.Movement.IsActionLocked, 6, definition.EnemyId + " hit return");
                    yield return Seconds(.5f);
                    Require(actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), definition.EnemyId + " hit gait recovery");
                    Capture(actor); actor.Movement.StopMovement();

                    var ability = Enumerable.Range(0,definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).First();
                    ResetPosition(actor, player.transform.position - Vector3.forward * (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge ? 3f : 1.1f));
                    var executor = actor.GetComponents<EnemyAbilityExecutor>().First(e => e.Supports(ability));
                    yield return Seconds(.2f);
                    Require(executor.TryStart(ability,0,player.transform), definition.EnemyId + " attack start");
                    actor.Movement.SetDestination(actor.transform.position + Vector3.right * 15,.1f,EnemyLocomotionMode.Walk);
                    yield return Seconds(.2f); Capture(actor);
                    yield return Until(() => !actor.AbilityController.IsExecuting && !actor.AnimationBridge.IsBlockingActionActive && !actor.Movement.IsActionLocked, 8, definition.EnemyId + " attack return");
                    yield return Seconds(.6f);
                    Require(actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), definition.EnemyId + " attack gait recovery");
                    Capture(actor);
                    File.WriteAllBytes(Path.Combine(output,definition.EnemyId + "-poses.png"),sheet.EncodeToPNG());
                    results.Add(new { id=definition.EnemyId, phase="stop-hit-attack-return", status="PASS" });
                }
                finally
                {
                    actor.Animator.cullingMode = culling;
                    actor.RequestPoolRelease();
                    if (sheet != null) Object.Destroy(sheet); sheet=null;
                }
                Require(service.TrySpawn(request,out var reused),definition.EnemyId + " reuse");
                reused.AI.enabled=false;
                Require(reused.Health.CurrentHp==reused.Health.MaxHp && !reused.Movement.IsActionLocked && !reused.AnimationBridge.IsBlockingActionActive,definition.EnemyId + " reuse reset");
                reused.RequestPoolRelease();
                File.WriteAllText(Path.Combine(output,"individual-results.json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
                Debug.Log("[MonsterThemeLocomotion] PASS " + definition.EnemyId);
            }
        }
        finally { Object.Destroy(cameraObject); if(sheet!=null)Object.Destroy(sheet); }
    }

    private static IEnumerator MeasureGait(EnemyActor actor, EnemyLocomotionMode mode, Vector3 start)
    {
        var controller=(AnimatorController)actor.Definition.AnimationProfile.RuntimeController;
        var tree=(BlendTree)controller.layers[0].stateMachine.states.First(s=>s.state.name=="Locomotion").state.motion;
        float threshold=mode==EnemyLocomotionMode.Run?2:mode==EnemyLocomotionMode.Backpedal?-1:1;
        var clip=(AnimationClip)tree.children.First(c=>Mathf.Approximately(c.threshold,threshold)).motion;
        Require(clip.isLooping,actor.Definition.EnemyId+" nonloop "+clip.name);
        var bones=actor.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Where(t=>t!=null).Distinct().ToArray();
        Require(bones.Length>3,actor.Definition.EnemyId+" bones missing");
        var rotations=bones.Select(b=>b.localRotation).ToArray();
        var positions=bones.Select(b=>b.localPosition).ToArray();
        var probes=MonsterThemeStrideCalibration.CreateRuntimeProbes(actor);
        Require(probes.Length>=2,actor.Definition.EnemyId+" missing sole probes");
        var soleFrames=new List<SoleFrame>();
        if(mode==EnemyLocomotionMode.Backpedal)
            actor.Movement.SetFacingDestination(start-Vector3.forward*30,.1f,start+Vector3.forward*30,mode);
        else actor.Movement.SetDestination(start+Vector3.forward*30,.1f,mode);
        float expectedSpeed=actor.Movement.ActiveMoveSpeed;
        float expectedRate=expectedSpeed/actor.Movement.Profile.GetAnimationReferenceSpeed(mode);
        float duration=Mathf.Clamp(clip.length/expectedRate*2.5f+.6f,3.5f,8f);
        float began=Time.time,last=began,frozen=0,maxFrozen=0,distance=0,minRate=float.MaxValue,maxRate=0;
        Vector3 prior=actor.transform.position;int samples=0,changed=0;
        while(Time.time-began<duration)
        {
            yield return null;
            if(Time.time-last<.04f)continue;
            float delta=Time.time-last;last=Time.time;
            Vector3 shift=actor.transform.position-prior;shift.y=0;prior=actor.transform.position;
            float poseDelta=0;
            for(int i=0;i<bones.Length;i++)
            {
                poseDelta+=Quaternion.Angle(rotations[i],bones[i].localRotation)+Vector3.Distance(positions[i],bones[i].localPosition)*100;
                rotations[i]=bones[i].localRotation;positions[i]=bones[i].localPosition;
            }
            if(Time.time-began<.6f)continue;
            soleFrames.Add(new SoleFrame{time=Time.time,root=actor.transform.position,soles=probes.Select(p=>p.sample()).ToArray()});
            distance+=shift.magnitude;samples++;
            if(poseDelta>.03f){changed++;frozen=0;}else if(shift.magnitude>.02f)frozen+=delta;
            maxFrozen=Mathf.Max(maxFrozen,frozen);
            float rate=actor.Animator.GetFloat("MoveAnimSpeed");minRate=Mathf.Min(minRate,rate);maxRate=Mathf.Max(maxRate,rate);
            Require(actor.Animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"),actor.Definition.EnemyId+" moved outside locomotion");
        }
        float measured=distance/Mathf.Max(.1f,duration-.6f);
        var drift=new List<float>();
        for(int foot=0;foot<probes.Length;foot++)
        {
            float low=soleFrames.Min(f=>f.soles[foot].y-f.root.y);
            for(int i=1;i<soleFrames.Count-1;i++)
            {
                var before=soleFrames[i-1];var after=soleFrames[i+1];var current=soleFrames[i];float dt=after.time-before.time;
                Vector3 footVelocity=(after.soles[foot]-before.soles[foot])/dt;
                Vector3 bodyVelocity=(after.root-before.root)/dt;
                float bodySpeed=Mathf.Abs(bodyVelocity.z);
                if(current.soles[foot].y-current.root.y<low+.04f
                    && Mathf.Abs(footVelocity.y-bodyVelocity.y)<.25f*Mathf.Clamp(expectedRate,.5f,2f)
                    && bodySpeed>expectedSpeed*.5f)
                    drift.Add(Mathf.Abs(footVelocity.z)/bodySpeed);
            }
        }
        drift.Sort();
        float medianFootDrift=drift.Count>0?drift[drift.Count/2]:float.MaxValue;
        float p95FootDrift=drift.Count>0?drift[(int)((drift.Count-1)*.95f)]:float.MaxValue;
        File.WriteAllText(Path.Combine(output,actor.Definition.EnemyId+"-"+mode+"-feet.json"),
            Newtonsoft.Json.JsonConvert.SerializeObject(new{feet=probes.Select(p=>p.name).ToArray(),
                frames=soleFrames.Select(f=>new{f.time,root=new[]{f.root.x,f.root.y,f.root.z},soles=f.soles.Select(v=>new[]{v.x,v.y,v.z}).ToArray()}).ToArray(),
                medianFootDrift,p95FootDrift}));
        Require(maxFrozen<.5f,actor.Definition.EnemyId+" frozen bones while moving: "+mode+" / "+maxFrozen);
        Require(changed>=samples*.9f,actor.Definition.EnemyId+" insufficient pose changes "+mode);
        Require(measured>expectedSpeed*.75f && measured<expectedSpeed*1.25f,actor.Definition.EnemyId+" movement mismatch "+mode+" / "+measured);
        Require(minRate>.45f && maxRate<1.65f,actor.Definition.EnemyId+" unnatural gait playback "+mode+" / "+minRate+".."+maxRate);
        Require(drift.Count>=10 && medianFootDrift<.25f,actor.Definition.EnemyId+" planted-foot/body mismatch "+mode+" / median="+medianFootDrift+" samples="+drift.Count);
        Capture(actor);
        results.Add(new{id=actor.Definition.EnemyId,phase=mode.ToString(),clip=clip.name,loop=clip.isLooping,duration,samples,changed,maxFrozen,expectedSpeed,measured,minRate,maxRate,plantedSamples=drift.Count,medianFootDrift,p95FootDrift,status="PASS"});
    }

    private static void ResetPosition(EnemyActor actor,Vector3 position)
    {
        actor.Movement.StopMovement();actor.Movement.CancelActionLock();actor.AnimationBridge.ResetForReuse();
        var rb=actor.GetComponent<Rigidbody>();rb.position=position;rb.rotation=Quaternion.identity;rb.linearVelocity=Vector3.zero;rb.angularVelocity=Vector3.zero;
        actor.transform.SetPositionAndRotation(position,Quaternion.identity);Physics.SyncTransforms();
    }
    private static void Capture(EnemyActor actor)
    {
        if(captureIndex>=8)return;
        var renderers=actor.VisualRoot.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && r.gameObject.activeInHierarchy).ToArray();
        var bounds=renderers[0].bounds;foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
        float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        captureCamera.orthographicSize=size*.7f;captureCamera.transform.position=bounds.center+new Vector3(1,.65f,1).normalized*size*3;
        captureCamera.transform.LookAt(bounds.center);
        var texture=RenderTexture.GetTemporary(Tile,Tile,24);var prior=RenderTexture.active;
        captureCamera.targetTexture=texture;captureCamera.Render();RenderTexture.active=texture;
        sheet.ReadPixels(new Rect(0,0,Tile,Tile),(captureIndex%4)*Tile,(1-captureIndex/4)*Tile);sheet.Apply();captureIndex++;
        captureCamera.targetTexture=null;RenderTexture.active=prior;RenderTexture.ReleaseTemporary(texture);
    }
    private static IEnumerator Seconds(float seconds){float until=Time.time+seconds;while(Time.time<until)yield return null;}
    private static IEnumerator Until(Func<bool> condition,float seconds,string message){float until=Time.time+seconds;while(!condition()){Require(Time.time<until,message);yield return null;}}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[MonsterThemeLocomotion] "+message);}
}
