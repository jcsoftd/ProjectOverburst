using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class MonsterThemeCrowdMotionVerifier
{
    private sealed class Sample
    {
        public EnemyActor actor;
        public Transform[] bones;
        public Quaternion[] rotations;
        public SkinnedMeshRenderer[] renderers;
        public Vector3 position;
        public float frozen, idle, maxFrozen, maxIdle, maxRate;
        public int visibleMoving;
    }
    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output=SessionState.GetString("MonsterThemePlayVerifier.output","");
        Require(Directory.Exists(output),"Set output directory");
        var physical=InputSystem.devices.OfType<Keyboard>().Where(k=>k.enabled).ToArray();
        var keyboard=InputSystem.AddDevice<Keyboard>("MonsterMotionQA");
        var originalDevices=player.RuntimeAsset.devices;
        keyboard.MakeCurrent();
        player.RuntimeAsset.devices=new InputDevice[]{keyboard};
        Key[] heldKeys=Array.Empty<Key>();
        Action driveInput=()=>InputState.Change(keyboard,new KeyboardState(heldKeys),InputUpdateType.Dynamic);
        InputSystem.onBeforeUpdate+=driveInput;
        foreach(var device in physical)InputSystem.DisableDevice(device);
        var reports=new List<object>();
        try
        {
            for(int table=0;table<ui.tables.Length;table++)
            {
                ui.Clear();yield return null;
                Require(ui.Begin(table,false),"Spawn rejected");
                var encounter=Object.FindObjectsByType<EnemyThemeEncounter>(FindObjectsSortMode.None).First(e=>e.name=="Theme debug encounter");
                yield return Until(()=>encounter.SpawnedCount==50,20,"Expected 50 actors");
                var fixtures=encounter.SnapshotActors().Select(a=>new Sample{
                    actor=a,bones=a.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones).Where(b=>b!=null).Distinct().ToArray(),
                    renderers=a.VisualRoot.GetComponentsInChildren<SkinnedMeshRenderer>(),position=a.transform.position}).ToArray();
                foreach(var f in fixtures)f.rotations=f.bones.Select(b=>b.localRotation).ToArray();
                Vector3 playerStart=player.transform.position;float playerTravel=0;Vector3 priorPlayer=playerStart;
                float began=Time.time,last=began;int shot=0;int oldKey=-1;var rows=new List<object>();
                while(Time.time-began<16f)
                {
                    float elapsed=Time.time-began;
                    // A short square through Gameplay actions keeps the pursuit changing without teleporting the target.
                    int key=elapsed<3f || elapsed>=12f ? -1 : (int)((elapsed-3f)/.65f)%4;
                    if(key!=oldKey)
                    {
                        heldKeys=key<0?Array.Empty<Key>():new[]{new[]{Key.W,Key.D,Key.S,Key.A}[key]};
                        oldKey=key;
                    }
                    yield return null;
                    float now=Time.time;if(now-last<.08f)continue;
                    float dt=now-last;last=now;
                    playerTravel+=Vector3.Distance(priorPlayer,player.transform.position);priorPlayer=player.transform.position;
                    foreach(var f in fixtures)
                    {
                        var a=f.actor;Vector3 delta=a.transform.position-f.position;delta.y=0;f.position=a.transform.position;
                        float speed=delta.magnitude/dt,poseDelta=0;
                        for(int i=0;i<f.bones.Length;i++){poseDelta+=Quaternion.Angle(f.rotations[i],f.bones[i].localRotation);f.rotations[i]=f.bones[i].localRotation;}
                        bool visible=f.renderers.Any(r=>r.isVisible);
                        bool locomotion=a.Animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion") && !a.Animator.IsInTransition(0);
                        float amount=a.Animator.GetFloat("Locomotion"),rate=a.Animator.GetFloat("MoveAnimSpeed");
                        bool moving=visible && locomotion && speed>.25f && !a.Movement.IsActionLocked;
                        if(moving)
                        {
                            f.visibleMoving++;f.maxRate=Mathf.Max(f.maxRate,rate);
                            f.frozen=poseDelta<.03f?f.frozen+dt:0;
                            f.idle=Mathf.Abs(amount)<.15f?f.idle+dt:0;
                            f.maxFrozen=Mathf.Max(f.maxFrozen,f.frozen);f.maxIdle=Mathf.Max(f.maxIdle,f.idle);
                        }
                        else {f.frozen=0;f.idle=0;}
                        Vector3 localVelocity=a.transform.InverseTransformDirection(delta/dt);
                        rows.Add(new{id=a.Definition.EnemyId,elapsed,speed,amount,rate,visible,locomotion,poseDelta,sideSpeed=localVelocity.x,forwardSpeed=localVelocity.z,locked=a.Movement.IsActionLocked});
                    }
                    if(shot<3 && elapsed>=new[]{2f,8f,14f}[shot])
                    {ScreenCapture.CaptureScreenshot(Path.Combine(output,ui.tables[table].ThemeId+"-crowd-"+shot+".png"));shot++;}
                }
                heldKeys=Array.Empty<Key>();
                File.WriteAllText(Path.Combine(output,ui.tables[table].ThemeId+"-crowd-diagnostic.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{playerTravel,gameplayEnabled=player.IsGameplayEnabled,move=player.MoveValue.ToString(),rows}));
                Require(playerTravel>10f,"Gameplay movement input did not move the target: "+playerTravel+" / enabled="+player.IsGameplayEnabled+" / move="+player.MoveValue);
                var stats=EnemySquadPursuitRuntimeService.GetRuntimeStats();Require(stats.SquadCount==6,"Squad count changed");
                foreach(var f in fixtures)
                {
                    Require(f.maxFrozen<.5f,"Frozen visible moving bones: "+f.actor.Definition.EnemyId+" / "+f.maxFrozen);
                    Require(f.maxIdle<.5f,"Visible idle sliding: "+f.actor.Definition.EnemyId+" / "+f.maxIdle);
                    Require(f.maxRate<2f,"Excessive crowd playback: "+f.actor.Definition.EnemyId+" / "+f.maxRate);
                }
                foreach(var entry in ui.tables[table].Entries)
                    Require(fixtures.Where(f=>f.actor.Definition==entry.definition).Sum(f=>f.visibleMoving)>5,"No visible moving coverage: "+entry.definition.EnemyId);
                var summary=fixtures.GroupBy(f=>f.actor.Definition.EnemyId).Select(g=>new{id=g.Key,count=g.Count(),visibleMoving=g.Sum(f=>f.visibleMoving),maxFrozen=g.Max(f=>f.maxFrozen),maxIdle=g.Max(f=>f.maxIdle),maxRate=g.Max(f=>f.maxRate)}).ToArray();
                reports.Add(new{table=ui.tables[table].ThemeId,playerTravel,summary,status="PASS"});
                File.WriteAllText(Path.Combine(output,ui.tables[table].ThemeId+"-crowd-motion.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{summary,rows}));
                File.WriteAllText(Path.Combine(output,"crowd-results.json"),Newtonsoft.Json.JsonConvert.SerializeObject(reports,Newtonsoft.Json.Formatting.Indented));
                Debug.Log("[MonsterThemeCrowdMotion] PASS "+ui.tables[table].ThemeId+" / 50 / six squads / visible bones / Gameplay pursuit");
            }
        }
        finally
        {
            InputSystem.onBeforeUpdate-=driveInput;
            if(player!=null && player.RuntimeAsset!=null)player.RuntimeAsset.devices=originalDevices;
            if(keyboard.added){InputState.Change(keyboard,new KeyboardState(),InputUpdateType.Dynamic);InputSystem.RemoveDevice(keyboard);}
            foreach(var device in physical)if(device.added)InputSystem.EnableDevice(device);
            ui.Clear();
        }
    }
    private static IEnumerator Until(Func<bool> condition,float seconds,string message){float until=Time.time+seconds;while(!condition()){Require(Time.time<until,message);yield return null;}}
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException("[MonsterThemeCrowdMotion] "+message);}
}
