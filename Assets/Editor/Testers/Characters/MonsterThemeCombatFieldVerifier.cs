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

// Real Gameplay input, production AI and weapon damage. Nothing is saved to scenes.
public static class MonsterThemeCombatFieldVerifier
{
    private sealed class Observation
    {
        public EnemyActor actor;
        public string id;
        public uint lease;
        public Vector3 position;
        public float travel, inactive, maxInactive, wait, maxWait;
        public int attacks, hits, deaths;
        public bool executing;
        public readonly HashSet<string> abilities = new HashSet<string>();
    }

    public static IEnumerator Verify(EnemyThemeDebugUI ui, PlayerInputFacade player)
    {
        string output = SessionState.GetString("MonsterThemePlayVerifier.output", "");
        if (!Directory.Exists(output)) throw new Exception("Artifact output missing");
        Application.targetFrameRate=SessionState.GetInt("MonsterThemeCombatField.frameRate",60);
        float captureStart=SessionState.GetFloat("MonsterThemeCombatField.captureStart",26f);
        float captureEnd=SessionState.GetFloat("MonsterThemeCombatField.captureEnd",38f);
        bool focusTurning=SessionState.GetBool("MonsterThemeCombatField.focusTurning",false);
        var keyboard = InputSystem.AddDevice<Keyboard>("CombatFieldKeyboard");
        var mouse = InputSystem.AddDevice<Mouse>("CombatFieldMouse");
        var physical = InputSystem.devices.Where(d => d.enabled && d != keyboard && d != mouse && (d is Keyboard || d is Mouse)).ToArray();
        var previousDevices = player.RuntimeAsset.devices;
        player.RuntimeAsset.devices = new InputDevice[] { keyboard, mouse };
        Key[] keys = Array.Empty<Key>(); bool attacking = false;
        Vector2 aim = Vector2.zero;
        Action drive = () =>
        {
            if(InputState.currentUpdateType != InputUpdateType.Dynamic)return;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            var state = new MouseState { position = aim };
            if (attacking) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
        };
        InputSystem.onBeforeUpdate += drive;
        foreach (var d in physical) InputSystem.DisableDevice(d);
        var sword = AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP01_OneHandSword/OHS01_FleurDeLys/OHS01_FleurDeLys.asset");
        var actor = PlayerContext.GetOrCreate().CurrentActor;
        if (!actor.Equipment.EquipWeaponItem(new ItemData(sword, 1, ItemGrade.Common))) throw new Exception("Equip weapon");
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
        var results = new List<object>();
        int playerHits = 0;
        void OnPlayerHit(CombatHealth h, DamageInfo d) { playerHits++; }
        actor.Health.OnDamaged += OnPlayerHit;
        var cameraObject = new GameObject("Combat field review camera");
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
        camera.CopyFrom(Camera.main); camera.enabled = false;
        var texture = RenderTexture.GetTemporary(768,432,24);
        var pixels = new Texture2D(768,432,TextureFormat.RGB24,false);
        camera.targetTexture = texture;
        bool attackDebug=CombatDebugSettings.ShowAttackPatternDebug, aiDebug=CombatDebugSettings.ShowEnemyAiStateDebug;
        var originalInputSettings=InputSystem.settings;
        InputSettings fixtureInputSettings=null;
        try
        {
            // Synthetic Gameplay events must be evaluated even if a user selects another Editor tab.
            // Clone settings so the saved project asset and the player's focus rules stay unchanged.
            fixtureInputSettings=Object.Instantiate(originalInputSettings);
            fixtureInputSettings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            fixtureInputSettings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings=fixtureInputSettings;
            CombatDebugSettings.SetAttackPatternDebug(false);CombatDebugSettings.SetEnemyAiStateDebug(false);
            for (int index=0; index<ui.tables.Length; index++)
            {
                string selected=SessionState.GetString("MonsterThemeCombatField.table","");
                if(!string.IsNullOrEmpty(selected) && ui.tables[index].ThemeId!=selected)continue;
                keys=Array.Empty<Key>();attacking=false;ui.Clear();yield return Seconds(.3f);
                if(!ui.Begin(index,false))throw new Exception("Cannot spawn table "+index);
                var encounter=Object.FindObjectsByType<EnemyThemeEncounter>(FindObjectsSortMode.None).First(e=>e.name=="Theme debug encounter");
                float timeout=Time.time+20;
                while(encounter.SpawnedCount!=50){if(Time.time>timeout)throw new Exception("Spawn timeout");yield return null;}
                // Exercise normal combat densities without changing the shipped 50-spawn debug button.
                // Spawn placement remains a 50-actor test; this fixture retains a table-weighted subset.
                int count=SessionState.GetInt("MonsterThemeCombatField.count",50);
                if(count!=10 && count!=20 && count!=50)throw new Exception("Supported sample counts: 10, 20, 50");
                if(count<50)
                {
                    var quota=ui.tables[index].BuildRoster(count==10?8:16,count==10?1:3,1,731)
                        .GroupBy(d=>d.EnemyId).ToDictionary(g=>g.Key,g=>g.Count());
                    foreach(var a in encounter.SnapshotActors())
                    {
                        if(quota.TryGetValue(a.Definition.EnemyId,out int remaining) && remaining>0)quota[a.Definition.EnemyId]=remaining-1;
                        else a.RequestPoolRelease();
                    }
                    yield return null;
                }
                var observations=encounter.SnapshotActors().Select(a=>new Observation{actor=a,id=a.Definition.EnemyId,lease=a.LeaseVersion,position=a.transform.position}).ToArray();
                if(observations.Length!=count)throw new Exception("Combat sample count mismatch");
                var handlers=new List<(CombatHealth health,Action<CombatHealth,DamageInfo> hit,Action<CombatHealth,DamageInfo> death)>();
                foreach(var o in observations)
                {
                    Action<CombatHealth,DamageInfo> hit=(h,d)=>o.hits++;
                    Action<CombatHealth,DamageInfo> died=(h,d)=>o.deaths++;
                    o.actor.Health.OnDamaged+=hit;o.actor.Health.OnDead+=died;handlers.Add((o.actor.Health,hit,died));
                }
                string folder=Path.Combine(output,"FieldReview",ui.tables[index].ThemeId);Directory.CreateDirectory(folder);
                var frames=new List<object>();var traces=new List<object>();var alignmentFrames=new List<object>();
                float begin=Time.time,last=begin,nextTrace=begin,nextCapture=begin;int beforeHits=playerHits;
                Vector3 playerPrevious=player.transform.position;float playerTravel=0;
                float unreadAttackSeconds=0;int playerAttackFrames=0;
                while(Time.time-begin<60)
                {
                    float elapsed=Time.time-begin;
                    // Stand for arrival, then short directional movement and sustained attacks.
                    // Target selection changes the mouse aim, never the monster or combat state.
                    bool moving=elapsed>=12 && elapsed<24 || elapsed>=38 && elapsed<46;
                    keys=moving?new[]{new[]{Key.W,Key.D,Key.S,Key.A}[(int)(elapsed/.65f)%4]}:Array.Empty<Key>();
                    attacking=elapsed>=24 && elapsed<38 || elapsed>=46;
                    var target=observations.Where(o=>o.actor!=null && o.actor.IsLeased && o.actor.LeaseVersion==o.lease && !o.actor.Health.IsDead)
                        .OrderBy(o=>(o.actor.transform.position-player.transform.position).sqrMagnitude).FirstOrDefault();
                    if(target!=null) aim=Camera.main.WorldToScreenPoint(target.actor.transform.position+Vector3.up*.2f);
                    yield return null;
                    var liveArena=Object.FindFirstObjectByType<EnemyThemeDebugArena>();
                    if(liveArena==null || Vector3.Distance(player.transform.position,liveArena.entry.position)>75)
                        throw new Exception("Player left the real test arena");
                    float now=Time.time,dt=now-last;last=now;
                    if(attacking && !player.AttackHeld)unreadAttackSeconds+=dt;
                    if(player.GetComponent<MeleeRuntime>().IsAttackInProgress)playerAttackFrames++;
                    playerTravel+=Vector3.Distance(playerPrevious,player.transform.position);playerPrevious=player.transform.position;
                    foreach(var o in observations)
                    {
                        var a=o.actor;if(a==null || !a.IsLeased || a.LeaseVersion!=o.lease || a.Health.IsDead)continue;
                        float step=Vector3.Distance(o.position,a.transform.position);o.travel+=step;o.position=a.transform.position;
                        bool executing=a.AbilityController.IsExecuting;
                        if(executing && !o.executing)o.attacks++;
                        o.executing=executing;
                        if(a.AbilityController.LastCommittedAbility!=null)o.abilities.Add(a.AbilityController.LastCommittedAbility.name);
                        o.inactive=step>.002f || executing || a.AnimationBridge.IsBlockingActionActive?0:o.inactive+dt;
                        o.maxInactive=Mathf.Max(o.maxInactive,o.inactive);
                        if(o.inactive>3 && o.id.Contains("Venosaur"))alignmentFrames.Add(new{time=elapsed,detail=Describe(o,player)});
                        o.wait=a.AI.CurrentStateName=="CombatWait"?o.wait+dt:0;o.maxWait=Mathf.Max(o.maxWait,o.wait);
                    }
                    if(now>=nextTrace)
                    {
                        nextTrace=now+.5f;
                        var melee=player.GetComponent<MeleeRuntime>();var playerState=player.GetComponent<PlayerStateCoordinator>();
                        traces.Add(new{time=elapsed,alive=encounter.AliveCount,playerHits,
                            player=new{requestedAttack=attacking,mouseDown=mouse.leftButton.ReadValue(),y=player.transform.position.y,grounded=player.GetComponent<PlayerMovement>().IsGrounded,
                                focused=Application.isFocused,gameplayEnabled=player.IsGameplayEnabled,mouseEnabled=mouse.enabled,keyboardEnabled=keyboard.enabled,
                                inputRouting=InputSystem.settings.editorInputBehaviorInPlayMode.ToString(),
                                held=player.AttackHeld,allowed=player.CombatInputs.AllowsHeldAttack,buffered=player.CombatInputs.HasAttack,
                                pickupSuppressed=PlayerPickupInteractor.IsPrimaryAttackSuppressed,condition=playerState.CurrentCondition.ToString(),action=playerState.CurrentAction.ToString(),
                                attacking=melee.IsAttackInProgress,ready=melee.IsAttackReady,canUse=melee.CanUseCurrentWeapon},
                            states=observations.Where(o=>o.actor!=null && o.actor.IsLeased && o.actor.LeaseVersion==o.lease && !o.actor.Health.IsDead)
                            .Select(o=>Describe(o,player)).ToArray()});
                    }
                    if(elapsed>=captureStart && elapsed<captureEnd && now>=nextCapture)
                    {
                        nextCapture=now+.083333f;
                        camera.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);
                        if(focusTurning)
                        {
                            var focus=observations.Where(o=>o.id.Contains("Venosaur") && o.actor!=null && o.actor.IsLeased && o.actor.LeaseVersion==o.lease && !o.actor.Health.IsDead)
                                .OrderByDescending(o=>o.inactive).FirstOrDefault();
                            if(focus!=null)
                            {
                                Vector3 point=focus.actor.transform.position+Vector3.up*.6f;
                                camera.transform.position=point+new Vector3(0,6,-7);
                                camera.transform.LookAt(point);camera.orthographic=true;camera.orthographicSize=4f;
                            }
                        }
                        camera.Render();var previous=RenderTexture.active;
                        try{RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,768,432),0,0);pixels.Apply();}
                        finally{RenderTexture.active=previous;}
                        string name=frames.Count.ToString("D4")+".png";File.WriteAllBytes(Path.Combine(folder,name),pixels.EncodeToPNG());
                        frames.Add(new{time=elapsed-captureStart,file=name,alive=encounter.AliveCount});
                    }
                }
                attacking=false;keys=Array.Empty<Key>();
                foreach(var h in handlers){h.health.OnDamaged-=h.hit;h.health.OnDead-=h.death;}
                var summary=observations.Select(o=>new{o.id,instance=o.actor.GetInstanceID(),o.travel,o.attacks,o.hits,o.deaths,o.maxWait,o.maxInactive,abilities=o.abilities.ToArray()}).ToArray();
                var suspects=summary.Where(o=>o.maxInactive>12).ToArray();
                var result=new{table=ui.tables[index].ThemeId,count=observations.Length,playerTravel,unreadAttackSeconds,playerAttackFrames,playerHits=playerHits-beforeHits,killed=summary.Sum(o=>o.deaths),summary,suspects};
                results.Add(result);
                File.WriteAllText(Path.Combine(folder,"frames.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{frames},Newtonsoft.Json.Formatting.Indented));
                File.WriteAllText(Path.Combine(output,ui.tables[index].ThemeId+"-field.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{result,traces,alignmentFrames},Newtonsoft.Json.Formatting.Indented));
                File.WriteAllText(Path.Combine(output,"field-summary.json"),Newtonsoft.Json.JsonConvert.SerializeObject(results,Newtonsoft.Json.Formatting.Indented));
                Debug.Log("[MonsterCombatField] "+ui.tables[index].ThemeId+" killed="+summary.Sum(o=>o.deaths)+" suspects="+suspects.Length);
                if(unreadAttackSeconds>1 || playerAttackFrames<20)throw new Exception("Input/attack coverage failed: "+ui.tables[index].ThemeId+" unread="+unreadAttackSeconds+" attackFrames="+playerAttackFrames);
                if(suspects.Length>0)throw new Exception("Inactive actors require inspection: "+ui.tables[index].ThemeId);
                ui.Clear();yield return Seconds(.4f);
            }
        }
        finally
        {
            CombatDebugSettings.SetAttackPatternDebug(attackDebug);CombatDebugSettings.SetEnemyAiStateDebug(aiDebug);
            actor.Health.OnDamaged-=OnPlayerHit;InputSystem.onBeforeUpdate-=drive;
            player.RuntimeAsset.devices=previousDevices;
            InputSystem.RemoveDevice(keyboard);InputSystem.RemoveDevice(mouse);
            foreach(var d in physical)if(d.added)InputSystem.EnableDevice(d);
            InputSystem.settings=originalInputSettings;
            if(fixtureInputSettings!=null)Object.Destroy(fixtureInputSettings);
            camera.targetTexture=null;RenderTexture.ReleaseTemporary(texture);Object.Destroy(pixels);Object.Destroy(cameraObject);ui.Clear();
        }
    }
    private static object Describe(Observation o,PlayerInputFacade player)
    {
        var a=o.actor;var delta=player.transform.position-a.transform.position;delta.y=0;
        string obstacle=null;
        if(o.inactive>4 && Physics.Raycast(a.transform.position+Vector3.up*.8f,delta.normalized,out var hit,delta.magnitude,
            ~((1<<LayerMask.NameToLayer("Enemy"))|(1<<LayerMask.NameToLayer("Ignore Raycast"))),QueryTriggerInteraction.Ignore))
            obstacle=hit.collider.name+"/"+LayerMask.LayerToName(hit.collider.gameObject.layer);
        return new{id=o.id,instance=a.GetInstanceID(),state=a.AI.CurrentDebugStateName,actualState=a.AI.CurrentStateName,
            frame=Time.frameCount,aiTick=a.AI.AiTickCount,turning=a.GetComponent<EnemyLocomotionAnimator>().IsTurning,
            normalized=a.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime,anim=a.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
            inTransition=a.Animator.IsInTransition(0),
            distance=a.AI.TargetDistance,inactive=o.inactive,range=a.AI.AttackEnterRange,yaw=Vector3.Angle(a.transform.forward,delta),
            facing=a.Movement.IsFacingForAttack(player.transform.position),locked=a.Movement.IsActionLocked,blocking=a.AnimationBridge.IsBlockingActionActive,
            bolt=a.GetComponent<EnemyThemeSpecialExecutor>().HasProjectile,obstacle,x=a.transform.position.x,z=a.transform.position.z};
    }
    private static IEnumerator Seconds(float duration){float until=Time.time+duration;while(Time.time<until)yield return null;}
}
