using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Forces target changes during attacks and checks actual body rotation, damage and evaluated clips.
public static class MonsterThemeFacingVerifier
{
    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output=SessionState.GetString("MonsterThemePlayVerifier.output","");
        Require(Directory.Exists(output),"Output directory");
        Require(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out var service),"Spawn service");
        foreach(var table in ui.tables)Require(service.RegisterAdditionalCatalog(table.Catalog,out _),"Catalog");
        var results=new List<object>();var health=player.GetComponent<CombatHealth>();Vector3 home=player.transform.position;
        foreach(var definition in ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct())
        {
            var request=new EnemySpawnRequest(definition,home+Vector3.forward*8,Quaternion.identity,player.transform,null,player.transform,null,1,1,77);
            Require(service.TrySpawn(request,out var actor),"Spawn "+definition.EnemyId);
            actor.AI.enabled=false;actor.Movement.StopMovement();var culling=actor.Animator.cullingMode;
            actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            try
            {
                foreach(float angle in new[]{10f,35f,90f,-90f,180f})
                {
                    Reset(actor,home+Vector3.forward*8);yield return Seconds(.3f);
                    Vector3 start=actor.transform.position;
                    Vector3 facing=start+Quaternion.Euler(0,angle,0)*Vector3.forward*10;
                    float begin=Time.time;int evaluated=0,turnFrames=0;float poseDelta=0f;
                    var bones=actor.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Where(b=>b!=null).Distinct().ToArray();
                    var rotations=bones.Select(b=>b.localRotation).ToArray();
                    var sheet=angle==90f?new Texture2D(1024,256,TextureFormat.RGB24,false):null;int captures=0;
                    while(!actor.Movement.IsFacingForAttack(facing))
                    {
                        Require(Time.time-begin<5f,definition.EnemyId+" turn timeout "+angle);
                        actor.Movement.FacePosition(facing);yield return null;
                        if(sheet!=null && captures<4 && Time.time-begin>=new[]{.1f,.3f,.55f,.8f}[captures]*(90f/definition.MovementProfile.TurnAnimationReferenceSpeed(1f)))
                            Capture(actor,sheet,captures++);
                        if(Time.time-begin<.15f || !actor.GetComponent<EnemyLocomotionAnimator>().IsTurning)continue;
                        evaluated++;
                        if(HasTurnClip(actor.Animator,.05f))turnFrames++;
                        for(int b=0;b<bones.Length;b++){poseDelta+=Quaternion.Angle(rotations[b],bones[b].localRotation);rotations[b]=bones[b].localRotation;}
                    }
                    Require(evaluated>2 && turnFrames>=evaluated*.8f,"Missing actual turn clip "+definition.EnemyId+" / "+angle+" / "+turnFrames+"/"+evaluated);
                    Require(poseDelta>10f,"Static turn bones "+definition.EnemyId);
                    Require(Vector3.Distance(start,actor.transform.position)<.12f,"Stationary turn translated body");
                    if(sheet!=null){File.WriteAllBytes(Path.Combine(output,definition.EnemyId+"-turn.png"),sheet.EncodeToPNG());UnityEngine.Object.Destroy(sheet);}
                    results.Add(new{id=definition.EnemyId,phase="turn",angle,seconds=Time.time-begin,evaluated,turnFrames,poseDelta,status="PASS"});
                }
                Reset(actor,home+Vector3.forward*8);yield return Seconds(.15f);
                Vector3 pivot=actor.transform.position;var facingMotion=actor.GetComponent<EnemyLocomotionAnimator>();
                actor.Movement.FacePosition(pivot+Vector3.right*10);
                float turnDeadline=Time.time+4;while(!facingMotion.IsTurning){Require(Time.time<turnDeadline,"Turn did not start");yield return null;}
                yield return Seconds(.1f);
                actor.Movement.FacePosition(pivot-Vector3.right*10);
                yield return Seconds(.1f);
                // End the request, not the active turn. Otherwise a slow render frame can contain
                // both the first turn's completion and the next FixedUpdate starting a valid second turn.
                actor.Movement.StopMovement();
                while(facingMotion.IsTurning)
                {
                    Require(Time.time<turnDeadline,"Committed turn timeout");
                    yield return null;
                }
                Require(Vector3.Angle(actor.transform.forward,Vector3.right)<5f,"Turn chased a changed target before completing its step: "+definition.EnemyId+" visualYaw="+actor.transform.eulerAngles.y+" bodyYaw="+actor.GetComponent<Rigidbody>().rotation.eulerAngles.y);
                actor.Movement.StopMovement();
                results.Add(new{id=definition.EnemyId,phase="turn-target-commit",status="PASS"});
                for(int i=0;i<definition.AbilitySet.Count;i++)
                {
                    var ability=definition.AbilitySet.GetAbility(i);
                    float distance=ability.ExecutionMode==EnemyAbilityExecutionMode.Projectile?4:ability.ExecutionMode==EnemyAbilityExecutionMode.Charge?3:Mathf.Min(1.1f,ability.Range*.75f);
                    Teleport(player,home);Reset(actor,home-Vector3.forward*distance);yield return Seconds(.3f);
                    var executor=actor.GetComponents<EnemyAbilityExecutor>().First(e=>e.Supports(ability));
                    // Side target cannot start an attack by snapping its root toward the target.
                    Teleport(player,actor.transform.position+Vector3.right*distance);
                    Require(!executor.CanStart(ability,player.transform),"Misaligned attack admitted "+definition.EnemyId);
                    Teleport(player,home);health.ResetHealth();float hp=health.CurrentHp;
                    Require(executor.TryStart(ability,i,player.transform),"Aligned attack rejected "+definition.EnemyId+" / "+ability.AnimatorTrigger);
                    Quaternion committed=actor.transform.rotation;float began=Time.time,maxYaw=0;int lockedFrames=0;
                    var attackSheet=new Texture2D(1024,256,TextureFormat.RGB24,false);int attackCaptures=0;
                    yield return Seconds(.08f);
                    Vector3 escaped=home+Vector3.right*(ability.Range+ability.HitRadius+4f);
                    Teleport(player,escaped);
                    while(executor.IsExecuting || actor.AnimationBridge.IsBlockingActionActive || actor.Movement.IsActionLocked || Time.time-began<.2f)
                    {
                        Require(Time.time-began<ability.AttackAnimationDuration+4,"Attack finish timeout");
                        actor.Movement.FacePosition(escaped); // Simulates a repeated AI facing request during committed animation.
                        if(attackCaptures<4 && Time.time-began>=new[]{.15f,.35f,.55f,.8f}[attackCaptures]*ability.AttackAnimationDuration)
                            Capture(actor,attackSheet,attackCaptures++);
                        maxYaw=Mathf.Max(maxYaw,Quaternion.Angle(committed,actor.transform.rotation));lockedFrames++;
                        yield return null;
                    }
                    Require(maxYaw<.3f,"Attack tracked escaped target "+definition.EnemyId+" / "+ability.AnimatorTrigger+" yaw="+maxYaw);
                    Require(Mathf.Approximately(health.CurrentHp,hp),"Escaped player took damage "+definition.EnemyId+" / "+ability.AnimatorTrigger);
                    File.WriteAllBytes(Path.Combine(output,definition.EnemyId+"-"+ability.AnimatorTrigger+"-attack.png"),attackSheet.EncodeToPNG());UnityEngine.Object.Destroy(attackSheet);
                    executor.Cancel();float resumed=Time.time;
                    while(Quaternion.Angle(committed,actor.transform.rotation)<20f)
                    {
                        Require(Time.time-resumed<2f,"Post-attack turn did not resume");
                        actor.Movement.FacePosition(escaped);yield return null;
                    }
                    Require(HasTurnClip(actor.Animator,.15f),"Recovery turn clip missing");
                    results.Add(new{id=definition.EnemyId,phase="committed-attack",attack=ability.AnimatorTrigger,maxYaw,lockedFrames,escapedWithoutDamage=true,status="PASS"});
                }
            }
            finally {actor.Animator.cullingMode=culling;actor.RequestPoolRelease();Teleport(player,home);}
            File.WriteAllText(Path.Combine(output,"facing-results.json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
            Debug.Log("[MonsterThemeFacing] PASS "+definition.EnemyId);
        }
    }
    internal static void Reset(EnemyActor actor,Vector3 position)
    {
        actor.AbilityController.Cancel();actor.Movement.StopMovement();actor.Movement.CancelActionLock();actor.AnimationBridge.ResetForReuse();
        actor.GetComponent<EnemyLocomotionAnimator>().CancelFacingTurn();
        var body=actor.GetComponent<Rigidbody>();body.position=position;body.rotation=Quaternion.identity;body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;
        actor.transform.SetPositionAndRotation(position,Quaternion.identity);Physics.SyncTransforms();
    }
    private static bool HasTurnClip(Animator animator,float minimumWeight)
    {
        return animator.GetCurrentAnimatorClipInfo(0).Any(c=>c.weight>minimumWeight && c.clip.name.Contains("Turn90"))
            || (animator.IsInTransition(0) && animator.GetNextAnimatorClipInfo(0).Any(c=>c.weight>minimumWeight && c.clip.name.Contains("Turn90")));
    }
    internal static void Capture(EnemyActor actor,Texture2D sheet,int index)
    {
        var renderers=actor.VisualRoot.GetComponentsInChildren<Renderer>();var bounds=renderers[0].bounds;
        foreach(var renderer in renderers.Skip(1))bounds.Encapsulate(renderer.bounds);
        float size=Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z));
        var go=new GameObject("Turn pose QA");var camera=go.AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.orthographicSize=size*.75f;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.16f,.19f,.23f);camera.cullingMask=1<<LayerMask.NameToLayer("Enemy");
        camera.transform.position=bounds.center+new Vector3(1,.8f,1).normalized*size*3;camera.transform.LookAt(bounds.center);
        var texture=RenderTexture.GetTemporary(256,256,24);var prior=RenderTexture.active;
        try{camera.targetTexture=texture;camera.Render();RenderTexture.active=texture;sheet.ReadPixels(new Rect(0,0,256,256),index*256,0);sheet.Apply();}
        finally{camera.targetTexture=null;RenderTexture.active=prior;RenderTexture.ReleaseTemporary(texture);UnityEngine.Object.Destroy(go);}
    }
    private static void Teleport(PlayerInputFacade player,Vector3 position)
    {
        var cc=player.GetComponent<CharacterController>();bool enabled=cc.enabled;cc.enabled=false;player.transform.position=position;cc.enabled=enabled;
        player.GetComponent<PlayerMovement>().ResetMotionAfterTeleport();Physics.SyncTransforms();
    }
    private static IEnumerator Seconds(float seconds){float end=Time.time+seconds;while(Time.time<end)yield return null;}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[MonsterThemeFacing] "+message);}
}
