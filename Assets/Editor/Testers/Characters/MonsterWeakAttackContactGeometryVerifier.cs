using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MonsterWeakAttackContactGeometryVerifier
{
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    public static string Run(string outputDirectory)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)
            throw new InvalidOperationException("접촉 판정 검사는 유휴 EditMode에서 실행합니다.");
        string workspace=Directory.GetParent(Application.dataPath).Parent.FullName;
        string allowed=Path.GetFullPath(Path.Combine(workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        outputDirectory=Path.GetFullPath(outputDirectory);
        if(!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("산출 경로가 아닙니다.");
        var checks=new List<object>();int failed=0;
        void Check(string name,bool pass){checks.Add(new {name,pass});if(!pass)failed++;}
        void Reject(string name,Action call){bool bad=false;try{call();}catch(ArgumentException){bad=true;}Check(name,bad);}
        var capsule=new EnemyWeakAttackContactCapsule(new Vector3(0,1,1),new Vector3(0,1,1.6f),.2f);
        var frames=new[]{new EnemyWeakAttackContactFrame(.4f,capsule,capsule),new EnemyWeakAttackContactFrame(.6f,capsule,capsule)};
        var geometry=EnemyWeakAttackContactGeometry.Create(frames,new Vector2(.4f,.6f));
        frames[0]=default;
        Check("geometry copies authoring frames",geometry.TryEvaluateCapsule(0,.5f,out var middle)&&middle.Radius==.2f);
        Check("geometry includes actual capsule radius in planar bound",Mathf.Abs(geometry.MaximumPlanarReach-1.8f)<.0001f);
        Check("no geometry before contact window",!geometry.TryEvaluateCapsule(0,.39f,out _));
        Check("no geometry after contact window",!geometry.TryEvaluateCapsule(0,.61f,out _));
        Check("invalid capsule index rejected",!geometry.TryEvaluateCapsule(-1,.5f,out _)&&!geometry.TryEvaluateCapsule(2,.5f,out _));
        Check("nonfinite motion progress rejected",!geometry.TryEvaluateCapsule(0,float.NaN,out _));
        Reject("missing window coverage rejected",()=>EnemyWeakAttackContactGeometry.Create(new[]{new EnemyWeakAttackContactFrame(.45f,capsule),new EnemyWeakAttackContactFrame(.6f,capsule)},new Vector2(.4f,.6f)));
        Reject("nonfinite radius rejected",()=>EnemyWeakAttackContactGeometry.Create(new[]{new EnemyWeakAttackContactFrame(.4f,new EnemyWeakAttackContactCapsule(Vector3.zero,Vector3.one,float.NaN)),new EnemyWeakAttackContactFrame(.6f,capsule)},new Vector2(.4f,.6f)));
        Reject("changing capsule count rejected",()=>EnemyWeakAttackContactGeometry.Create(new[]{new EnemyWeakAttackContactFrame(.4f,capsule,capsule),new EnemyWeakAttackContactFrame(.6f,capsule)},new Vector2(.4f,.6f)));
        VerifyNative(workspace,geometry,Check,Reject);
        Directory.CreateDirectory(outputDirectory);
        string result=Path.Combine(outputDirectory,"contact-geometry-results.json");
        File.WriteAllText(result,Newtonsoft.Json.JsonConvert.SerializeObject(new{status=failed==0?"PASS_SCOPED":"FAIL",checks,failed,
            physics="INDEPENDENT_NATIVE_PREVIEW_SCENE",gameRosterApplied=false,actualAnimatorAndPlayerLoop="NOT_RUN",newAudioApplied=false},Newtonsoft.Json.Formatting.Indented));
        if(failed>0)throw new InvalidOperationException("접촉 판정 검사 실패: "+failed);
        return result;
    }
    static void VerifyNative(string workspace,EnemyWeakAttackContactGeometry geometry,Action<string,bool> check,Action<string,Action> reject)
    {
        int registry=CombatTargetRegistry.RegisteredCount;var scene=EditorSceneManager.NewPreviewScene();var owned=new List<UnityEngine.Object>();
        EnemyMeleeAttackController melee=null;
        try
        {
            GameObject Root(string name,Vector3 position)
            {
                var go=new GameObject(name);SceneManager.MoveGameObjectToScene(go,scene);go.transform.position=position;owned.Add(go);return go;
            }
            var clip=new AnimationClip{name="ContactGeometryFixture",frameRate=30};owned.Add(clip);
            clip.SetCurve("",typeof(Transform),"localPosition.x",AnimationCurve.Linear(0,0,1,0));
            var profile=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();owned.Add(profile);
            var shape=new EnemyWeakAttackContactCapsule(new Vector3(0,1,1),new Vector3(0,1,1.6f),.2f);
            var contactFrames=new[]{new EnemyWeakAttackContactFrame(.4f,shape,shape),new EnemyWeakAttackContactFrame(.6f,shape,shape)};
            profile.Configure("contact-fixture",clip,clip,new Vector2(0,1),EnemyWeakAttackMotionPolicy.ShortAdvance,1.8f,.5f,
                new Vector2(.1f,.4f),AnimationCurve.Linear(0,0,1,1),"",new[]{new Vector2(.4f,.6f)},new[]{contactFrames});
            var ability=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();owned.Add(ability);
            ability.Configure("contact-fixture","Attack1",10,1.8f,10,360,1,0,.5f,1,1,false);
            ability.ConfigureWeakAttackExecution(profile);
            var origin=new Vector3(17000,0,17000);var actor=Root("V3ContactActor",origin);
            var motor=actor.AddComponent<EnemyMotor>();motor.ResolveReferences();
            var body=actor.GetComponent<Rigidbody>();body.useGravity=false;body.interpolation=RigidbodyInterpolation.None;
            var attacker=actor.AddComponent<CombatTarget>();attacker.Configure(CombatTeam.Enemy,false);attacker.ConfigureVolume(Vector3.up,.2f,2);
            var movement=actor.AddComponent<EnemyMovement>();movement.ResolveReferences();
            melee=actor.AddComponent<EnemyMeleeAttackController>();
            Set(melee,"health",actor.GetComponent<CombatHealth>());Set(melee,"combatTarget",attacker);Set(melee,"attackPoint",actor.transform);
            Set(melee,"targetLayer",(LayerMask)(~0));Set(melee,"weakAttackMotor",motor);
            var victim=Root("V3ContactVictim",origin+Vector3.forward*1.3f);
            var collider=victim.AddComponent<CapsuleCollider>();collider.radius=.2f;collider.height=1.8f;collider.center=Vector3.up*.9f;
            var secondCollider=victim.AddComponent<BoxCollider>();secondCollider.center=Vector3.up*.9f;secondCollider.size=Vector3.one*.3f;
            var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.Neutral,false);target.ConfigureVolume(Vector3.up*.9f,.2f,1.8f);
            var hp=victim.GetComponent<CombatHealth>();var serialized=new SerializedObject(hp);serialized.FindProperty("showDamageNumbers").boolValue=false;serialized.ApplyModifiedPropertiesWithoutUndo();
            var events=new List<DamageInfo>();hp.OnDamaged+=(_,info)=>events.Add(info);
            void Begin(EnemyWeakAttackExecutionProfile p,int phase=0,float time=.5f,Vector3? forward=null)
            {
                hp.ResetHealth();events.Clear();Set(melee,"activeWeakExecution",p);Set(melee,"activeWeakContactGeometry",p.CopyContactGeometry());
                Set(melee,"weakCommittedForward",forward??Vector3.forward);Set(melee,"attackSequenceId",EnemyAttackSequence.Next());Set(melee,"attackPhaseIndex",phase);
                var clock=(EnemyAttackClock)Get(melee,"weakAttackClock");clock.Begin(500,false,0);clock.Observe(501,.1f,true,time);Physics.SyncTransforms();
            }
            void Damage(EnemyAbilityDefinition a)=>typeof(EnemyMeleeAttackController).GetMethod("ResolveContactGeometryHit",Fields).Invoke(melee,new object[]{5f,a,null});
            Begin(profile);Damage(ability);
            check("overlapping capsules damage the target once per event",events.Count==1&&Mathf.Abs(hp.CurrentHp-95)<.001f);
            check("geometry damage preserves attack metadata and on-hit route",events.Count==1&&events[0].enemyAbility==ability
                &&events[0].sourceAttackSequenceId>0&&events[0].sourceAttackPhaseIndex==0&&events[0].triggersOnHitEffects&&!events[0].isDamageOverTime);
            check("live preview agrees with actual capsule contact",melee.WouldAbilityHitTarget(ability,target));
            var wall=Root("V3ContactWall",origin+Vector3.up+Vector3.forward*.55f);
            wall.AddComponent<BoxCollider>().size=new Vector3(.8f,2,.12f);
            Set(ability,"requireLineOfSight",true);Begin(profile);Damage(ability);
            check("owned scene wall blocks both preview and real contact",events.Count==0&&!melee.WouldAbilityHitTarget(ability,target));
            Set(melee,"activeWeakExecution",null);Set(melee,"activeWeakContactGeometry",null);
            check("pre-start geometry preview respects owned scene wall",!melee.WouldAbilityHitTarget(ability,target));
            wall.SetActive(false);Begin(profile);Damage(ability);
            check("removing wall restores contact without stale query state",events.Count==1&&melee.WouldAbilityHitTarget(ability,target));
            Set(ability,"requireLineOfSight",false);
            victim.transform.position=origin+Vector3.forward*2.1f;Begin(profile);Damage(ability);
            check("legacy radius10 and advance allowance do not enlarge contact",events.Count==0&&!melee.WouldAbilityHitTarget(ability,target));
            check("final geometry radius ignores legacy hit radius",Mathf.Abs(EnemyAttackThreatGeometry.ResolveRadius(null,ability)-1.8f)<.0001f);
            victim.transform.position=origin+Vector3.up*4+Vector3.forward*1.3f;Begin(profile);Damage(ability);
            check("capsule geometry rejects vertically separated target",events.Count==0);
            actor.transform.rotation=Quaternion.Euler(0,90,0);victim.transform.position=origin+Vector3.forward*1.3f;Begin(profile);Damage(ability);
            check("committed attack facing survives later actor turn",events.Count==1);
            victim.transform.position=origin+Vector3.right*1.3f;Begin(profile,forward:Vector3.right);Damage(ability);
            check("new start facing rotates the authored shape",events.Count==1);
            actor.transform.rotation=Quaternion.identity;victim.transform.position=origin+Vector3.forward*2.1f;Begin(profile);
            movement.ApplyActionLock(1f);
            bool accepted=movement.RequestBoundedAttackDisplacement(Vector3.forward*.3f,Quaternion.identity);
            check("requested movement cannot grant preview contact",accepted&&body.position==origin&&!melee.WouldAbilityHitTarget(ability,target));
            typeof(EnemyMovement).GetMethod("FixedUpdate",Fields).Invoke(movement,null);
            Physics.SyncTransforms();scene.GetPhysicsScene().Simulate(.02f);Damage(ability);
            check("completed physics position rebases authored geometry",body.position.z>origin.z+.29f&&events.Count==1);

            // Replacing profile authoring does not mutate the accepted execution's immutable shape objects.
            var frozen=profile.CopyContactGeometry();
            var far=new EnemyWeakAttackContactCapsule(Vector3.up+Vector3.forward*10,Vector3.up+Vector3.forward*11,.2f);
            profile.Configure("contact-fixture",clip,clip,new Vector2(0,1),EnemyWeakAttackMotionPolicy.Stationary,12,0,Vector2.zero,null,"",
                new[]{new Vector2(.4f,.6f)},new[]{new[]{new EnemyWeakAttackContactFrame(.4f,far),new EnemyWeakAttackContactFrame(.6f,far)}});
            check("profile replacement preserves accepted geometry snapshot",frozen[0].TryEvaluateCapsule(0,.5f,out var old)&&old.B.z<2);
            reject("invalid replacement refuses missing geometry phases",()=>profile.Configure("bad",clip,clip,new Vector2(0,1),
                EnemyWeakAttackMotionPolicy.Stationary,1,0,Vector2.zero,null,"",new[]{new Vector2(.4f,.6f)},Array.Empty<EnemyWeakAttackContactFrame[]>()));
            check("failed authoring preserves geometry profile",profile.GetContactGeometry(0).TryEvaluateCapsule(0,.5f,out var retained)&&retained.B.z==11);
            body.position=origin;actor.transform.position=origin;Physics.SyncTransforms();

            var path=Path.Combine(workspace,"개인파일/코덱스산출/Monsters/MonsterOverhaulV3/GOAL_A/20261004/NativeContacts/Geometry/skin-contact-measurement.json");
            var native=JObject.Parse(File.ReadAllText(path));
            if((string)native["status"]!="PASS_NATIVE_CONTACT_SKIN_MEASUREMENT")throw new InvalidOperationException("Native skin data is not verified.");
            foreach(JObject row in native["rows"])
            {
                string asset=AssetDatabase.GUIDToAssetPath((string)row["sourceGuid"]);
                string absolute=Path.Combine(Directory.GetParent(Application.dataPath).FullName,asset);
                if(Hash(absolute)!=(string)row["sourceSha256"])throw new InvalidOperationException("Measured source changed.");
                var source=AssetDatabase.LoadAllAssetsAtPath(asset).OfType<AnimationClip>().Single(c=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c,out string g,out long id)&&g==(string)row["sourceGuid"]&&id==(long)row["sourceLocalId"]);
                var windows=row["phases"].Select(p=>new Vector2((float)p["contactWindowNormalized"][0],(float)p["contactWindowNormalized"][1])).ToArray();
                var measured=row["phases"].Select(p=>p["frames"].Select(f=>new EnemyWeakAttackContactFrame((float)f["normalizedTime"],
                    f["capsules"].Select(c=>new EnemyWeakAttackContactCapsule(Vector(c["a"]),Vector(c["b"]),(float)c["radius"])).ToArray())).ToArray()).ToArray();
                var actual=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();owned.Add(actual);
                actual.Configure((string)row["selectionKey"],source,source,new Vector2(0,source.length),EnemyWeakAttackMotionPolicy.Stationary,
                    10,0,Vector2.zero,null,"",windows,measured);
                for(int phase=0;phase<measured.Length;phase++)
                {
                    float time=measured[phase][measured[phase].Length/2].NormalizedTime;
                    actual.GetContactGeometry(phase).TryEvaluateCapsule(0,time,out var shapeAtTime);
                    victim.transform.position=origin+(shapeAtTime.A+shapeAtTime.B)*.5f-Vector3.up*.9f;Begin(actual,phase,time);Damage(ability);
                    check(row["clip"]+" phase"+phase+" measured native shape hits once",events.Count==1&&events[0].sourceAttackPhaseIndex==phase);
                    victim.transform.position+=Vector3.right*(actual.MaximumContactPlanarReach+5);Begin(actual,phase,time);Damage(ability);
                    check(row["clip"]+" phase"+phase+" outside measured shape misses",events.Count==0);
                }
            }
            profile.Configure("contact-fixture",clip,clip,new Vector2(0,1),EnemyWeakAttackMotionPolicy.Stationary,1.8f,0,
                Vector2.zero,null,"",new[]{new Vector2(.4f,.6f)},new[]{contactFrames});
            victim.transform.position=origin+Vector3.forward*1.3f;
            var other=Root("V3ContactCallbackVictim",victim.transform.position+Vector3.right*.05f);
            var otherCollider=other.AddComponent<CapsuleCollider>();otherCollider.center=Vector3.up*.9f;otherCollider.height=1.8f;otherCollider.radius=.2f;
            var otherTarget=other.AddComponent<CombatTarget>();otherTarget.Configure(CombatTeam.Neutral,false);otherTarget.ConfigureVolume(Vector3.up*.9f,.2f,1.8f);
            var otherHp=other.GetComponent<CombatHealth>();otherHp.ResetHealth();
            var otherSettings=new SerializedObject(otherHp);otherSettings.FindProperty("showDamageNumbers").boolValue=false;otherSettings.ApplyModifiedPropertiesWithoutUndo();
            void CancelOnDamage(CombatHealth receiver,DamageInfo info)=>melee.CancelAttack();
            hp.OnDamaged+=CancelOnDamage;otherHp.OnDamaged+=CancelOnDamage;
            try
            {
                Begin(profile);Damage(ability);
                check("damage callback cancellation stops remaining overlapping targets",Mathf.Abs(200-hp.CurrentHp-otherHp.CurrentHp-5)<.001f);
                check("damage callback cancellation clears accepted geometry",Get(melee,"activeWeakContactGeometry")==null);
            }
            finally {hp.OnDamaged-=CancelOnDamage;otherHp.OnDamaged-=CancelOnDamage;}
            check("geometry execution cancellation clears its snapshot",CancelClears(melee));
        }
        finally
        {
            melee?.CancelAttack();
            foreach(var root in owned.OfType<GameObject>())if(root!=null)
                foreach(var target in root.GetComponentsInChildren<CombatTarget>(true))CombatTargetRegistry.Unregister(target);
            for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
        }
        check("geometry fixtures return combat registry",CombatTargetRegistry.RegisteredCount==registry);
    }
    static bool CancelClears(EnemyMeleeAttackController melee){melee.CancelAttack();return Get(melee,"activeWeakContactGeometry")==null;}
    static object Get(object owner,string field)=>owner.GetType().GetField(field,Fields).GetValue(owner);
    static void Set(object owner,string field,object value)=>owner.GetType().GetField(field,Fields).SetValue(owner,value);
    static Vector3 Vector(JToken p)=>new Vector3((float)p[0],(float)p[1],(float)p[2]);
    static string Hash(string path){using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
}
