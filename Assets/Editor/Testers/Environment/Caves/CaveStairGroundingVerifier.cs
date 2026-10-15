using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEngine;

public static class CaveStairGroundingVerifier
{
    public const string Key = "Overburst.CaveStairGroundingVerifier.";
    public static bool Requested => SessionState.GetBool(Key + "enabled", false)
        && SessionState.GetString(Key + "output", "") == SessionState.GetString("Overburst.MapRunEntryPlayVerifier.return", "");
    static readonly RaycastHit[] hits = new RaycastHit[32];
    static float[] V(Vector3 p) => new[] { p.x, p.y, p.z };
    static float XZ(Vector3 a, Vector3 b) => new Vector2(a.x-b.x, a.z-b.z).magnitude;
    static void Write(string directory, string file, object data) => File.WriteAllText(Path.Combine(directory, file), JsonConvert.SerializeObject(data, Formatting.Indented));

    static string Evidence(string name) => Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/World/20261014_CaveStairGrounding/Evidence/",name));
    [MenuItem("Tools/OVERBURST/Validation/Cave Stair Grounding/Baseline 5")]
    static void Baseline5() => Start(Evidence("Observation5_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")),5,false,false);
    [MenuItem("Tools/OVERBURST/Validation/Cave Stair Grounding/Verify 5")]
    static void Verify5() => Start(Evidence("Verify5_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")),5,true,true);
    [MenuItem("Tools/OVERBURST/Validation/Cave Stair Grounding/Verify 10")]
    static void Verify10() => Start(Evidence("Verify10_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")),10,true,true);

    public static void Start(string directory, int count = 5, bool strict = false, bool lanes = false)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Wait for an idle Editor.");
        SessionState.SetBool(Key + "enabled", true);
        SessionState.SetString(Key + "output", Path.GetFullPath(directory));
        SessionState.SetBool(Key + "strict", strict);
        SessionState.SetBool(Key + "lanes", lanes);
        try { MapRunEntryPlayVerifier.Start(directory, caveCount: count, caveOnly: true, includeLargeCave: false, caveStairTraversalOnly: true); }
        catch { Clear(); throw; }
    }

    static void Clear()
    {
        SessionState.EraseBool(Key + "enabled");
        SessionState.EraseString(Key + "output");
        SessionState.EraseBool(Key + "strict");
        SessionState.EraseBool(Key + "lanes");
    }

    static object Support(OverburstCharacterMotor3D motor, out float separation, out bool found)
    {
        var cc = motor.Controller;
        var scale = motor.transform.lossyScale;
        float radius = cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float height = Mathf.Max(radius*2, cc.height*Mathf.Abs(scale.y));
        var center = motor.transform.TransformPoint(cc.center);
        var foot = center - Vector3.up * height*.5f;
        var sphere = foot + Vector3.up*radius;
        const float lift = .25f;
        // 계단 굴곡에서는 캡슐 뒤쪽 접촉도 지지점이므로 중심선 Ray 대신 실제 하부 구를 검사한다.
        int count = Physics.SphereCastNonAlloc(sphere + Vector3.up*lift, radius, Vector3.down, hits, 4f, motor.ActiveSettings.groundLayer, QueryTriggerInteraction.Ignore);
        separation = float.PositiveInfinity; found = false; RaycastHit best = default;
        for (int i=0; i<count; i++)
        {
            var hit = hits[i];
            if (!hit.collider || hit.collider.transform.IsChildOf(motor.transform) || hit.normal.y < .3f) continue;
            float distance = hit.distance-lift;
            if (distance < -cc.skinWidth-.04f || distance >= separation) continue;
            separation = distance; best = hit; found = true;
        }
        return new { found, separation = found ? (float?)separation : null, foot=V(foot), radius, height, skinWidth=cc.skinWidth,
            hit = found ? V(best.point) : null, normal = found ? V(best.normal) : null,
            collider = found ? best.collider.name : null, layer = found ? best.collider.gameObject.layer : -1,
            controllerGrounded=cc.isGrounded, motorGrounded=motor.IsGrounded, walkable=motor.HasWalkableGround,
            probeGap=motor.HasWalkableGround ? (float?)motor.GroundGap : null,
            contactNormal=V(motor.GroundContactNormal), verticalVelocity=motor.VerticalVelocity, snap=motor.GroundSnapActive };
    }

    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool,string> check)
    {
        var world=gate.GetComponent<CaveWorld>();
        var actor=PlayerContext.Instance.CurrentActor;
        var movement=actor.GetComponent<PlayerMovement>();
        var motor=movement.Motor; var cc=motor.Controller;
        var combat=actor.GetComponent<CombatMotionDriver>();
        var position=actor.transform.position; var rotation=actor.transform.rotation;
        bool enabled=movement.enabled; int excluded=cc.excludeLayers.value;
        bool strict=SessionState.GetBool(Key+"strict",false), lanes=SessionState.GetBool(Key+"lanes",false);
        var records=new List<object>(); int totalFrames=0, lostFrames=0, detachedFrames=0, failedRoutes=0;
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var evade=actor.GetComponent<PlayerEvadeController>();
        float dashDistance=(float)typeof(PlayerEvadeController).GetField("dashDistance",flags).GetValue(evade);
        float dashDuration=(float)typeof(PlayerEvadeController).GetField("dashDuration",flags).GetValue(evade);
        var fpsCases=new[] { 30, 60, 120 };
        var laneCases=lanes ? new[] { 0f, -.15f, .15f } : new[] { 0f };
        try
        {
            CaveStairTraversalVerifier.Geometry(world,check);
            movement.enabled=false;
            int enemy=LayerMask.NameToLayer("Enemy"); if(enemy>=0) cc.excludeLayers=excluded|(1<<enemy);
            var entryConnection=world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().First(c=>c.stairCount>0 && c.walkCollider);
            var upper=entryConnection.start.y>=entryConnection.end.y ? entryConnection.start : entryConnection.end;
            var entryAxis=entryConnection.end-entryConnection.start;entryAxis.y=0;entryAxis.Normalize();
            bool upperIsA=entryConnection.start.y>=entryConnection.end.y;
            upper+=(upperIsA ? -entryAxis : entryAxis)*1.2f;
            var upperBoundary=upperIsA ? entryConnection.boundaryA : entryConnection.boundaryB;
            check(upperBoundary.Sample(upper,10,10,out var upperGround),"Jump regression uses the real upper platform");
            float entryFoot=actor.transform.TransformVector(cc.center).y-cc.height*Mathf.Abs(actor.transform.lossyScale.y)*.5f;
            var entryPosition=new Vector3(upper.x,upperGround.y-entryFoot+.04f,upper.z);
            ActorTeleportUtility.TeleportSafely(actor.transform,entryPosition,rotation);motor.ResetMotion();
            for(int i=0;i<8;i++){motor.ProbeGround(1f/60);motor.Step(new MotorStepCommand{moveRate=1000,airControl=1},1f/60);yield return null;}
            motor.Step(new MotorStepCommand{jumpVelocity=8,moveRate=1000,airControl=1},1f/60);
            float jumpedY=actor.transform.position.y;
            combat.ApplyEvadeDisplacement(entryAxis*.1f);
            check(!motor.IsGrounded && !motor.GroundSnapActive && actor.transform.position.y>=jumpedY-.001f,"Ascending jump is not pulled back onto the stairs by direct motion");
            ActorTeleportUtility.TeleportSafely(actor.transform,entryPosition+Vector3.up,rotation);motor.ResetMotion();motor.ProbeGround(1f/60);
            float airborneY=actor.transform.position.y;combat.ApplyEvadeDisplacement(entryAxis*.1f);
            check(!motor.IsGrounded && Mathf.Abs(actor.transform.position.y-airborneY)<.005f,"Already airborne direct motion preserves normal falling instead of snapping to ground");
            foreach(var connection in world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().Where(c=>c.stairCount>0 && c.walkCollider))
            foreach(var fps in fpsCases)
            foreach(var lane in laneCases)
            foreach(var mode in new[] { "Walk", "Run", "Dash" })
            {
                if(lane != 0f && fps != 60) continue;
                bool reverse=connection.end.y>connection.start.y;
                var d=connection.end-connection.start; d.y=0; d.Normalize();
                var side=new Vector3(-d.z,0,d.x)*(lane*connection.walkWidth);
                var a=(reverse ? connection.end+d*1.2f : connection.start-d*1.2f)+side;
                var b=(reverse ? connection.start-d*1.2f : connection.end+d*1.2f)+side;
                var source=reverse ? connection.boundaryB : connection.boundaryA;
                check(source.Sample(a,10,10,out var ground),connection.name+": descent begins on real upper platform");
                float footOffset=actor.transform.TransformVector(cc.center).y-cc.height*Mathf.Abs(actor.transform.lossyScale.y)*.5f;
                ActorTeleportUtility.TeleportSafely(actor.transform,new Vector3(a.x,ground.y-footOffset+.04f,a.z),rotation);
                motor.ResetMotion();
                float dt=1f/fps;
                for(int settle=0;settle<8;settle++){motor.ProbeGround(dt);motor.Step(new MotorStepCommand{moveRate=1000,airControl=1},dt);yield return null;}
                var frames=new List<object>(); int frame=0, lost=0, detached=0, stuck=0;
                float maxSeparation=0, maxDropSpeed=0;
                bool dash=mode=="Dash"; float speed=mode=="Walk" ? movement.WalkMoveSpeed : movement.RunMoveSpeed;
                try
                {
                    if(dash) combat.BeginEvadeMotion();
                    while(XZ(actor.transform.position,b)>.15f && frame++<1500)
                    {
                        var before=actor.transform.position;
                        var toward=b-before; toward.y=0; toward.Normalize();
                        float distance=Mathf.Min(XZ(before,b),(dash ? dashDistance/dashDuration : speed)*dt);
                        motor.ProbeGround(dt);
                        bool groundedBefore=motor.IsGrounded;
                        if(dash) combat.ApplyEvadeDisplacement(toward*distance);
                        var velocity=dash ? Vector3.zero : toward*(distance/dt);
                        motor.Step(new MotorStepCommand{currentHorizontalVelocity=velocity,targetHorizontalVelocity=velocity,moveRate=1000,airControl=1},dt);
                        var support=Support(motor,out float separation,out bool found);
                        bool lostContact=!motor.IsGrounded || !motor.HasWalkableGround;
                        bool physicallyDetached=!cc.isGrounded && (!found || separation>Mathf.Max(.06f,cc.skinWidth+.025f));
                        if(lostContact) lost++; if(physicallyDetached) detached++;
                        if(found && !cc.isGrounded) maxSeparation=Mathf.Max(maxSeparation,separation);
                        maxDropSpeed=Mathf.Max(maxDropSpeed,(before.y-actor.transform.position.y)/dt);
                        frames.Add(new{frame,dt,groundedBefore,from=V(before),to=V(actor.transform.position),commandDistance=distance,support});
                        if(XZ(before,actor.transform.position)<.001f) stuck++; else stuck=0;
                        if(stuck>=30) break;
                        yield return null;
                    }
                }
                finally{if(dash) combat.EndEvadeMotion();}
                bool reached=XZ(actor.transform.position,b)<.3f;
                totalFrames+=frames.Count;lostFrames+=lost;detachedFrames+=detached;if(!reached||lost>0||detached>0) failedRoutes++;
                var record=new{connection=connection.name,mode,fps,lane,reverse,reached,frames=frames.Count,lostFrames=lost,detachedFrames=detached,maxSeparation,maxDropSpeed,
                    from=V(a),target=V(b),finish=V(actor.transform.position)};
                records.Add(record);
                Write(output,"grounding-route-"+records.Count.ToString("D3")+".json",new{record,samples=frames});
                Write(output,"grounding-progress.json",new{routes=records.Count,totalFrames,lostFrames,detachedFrames,failedRoutes,last=record});
            }
            Write(output,"grounding-summary.json",new{status=failedRoutes==0 ? "PASS" : "FAIL",strict,records,totalFrames,lostFrames,detachedFrames,failedRoutes,
                walkSpeed=movement.WalkMoveSpeed,runSpeed=movement.RunMoveSpeed,dashDistance,dashDuration,
                scope="Real isolated cave map and player motor. Deterministic 30/60/120 Hz command delta on rendered Play frames; real walking/running speeds and dash displacement path. Enemy body contact excluded. Actual CC contact or bounded lower-sphere support and grounded state, including platform seams. Sphere cast misses during existing CC contact are not detachment."});
            check(records.Count>0,"Actual cave stairs exercised in all three movement modes");
            if(strict)check(failedRoutes==0,"Every descent frame maintains grounded state and physical support");
        }
        finally
        {
            combat.EndEvadeMotion();cc.excludeLayers=excluded;
            ActorTeleportUtility.TeleportSafely(actor.transform,position,rotation);
            motor.ResetMotion();movement.enabled=enabled;Clear();
        }
    }
}
