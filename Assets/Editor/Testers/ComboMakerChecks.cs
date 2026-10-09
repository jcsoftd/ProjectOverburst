using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Overburst.EditorTools.ComboMaker;
using UnityEngine.UIElements;
using System.Reflection;

public static class ComboMakerChecks
{
    public static string BloodRendering()
    {
        var log=new List<string>();
        var sceneState=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Select(s=>(s.handle,s.isDirty,s.rootCount)).ToArray();
        int scenes=EditorSceneManager.previewSceneCount;
        var item=AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst/03_Features/Weapons"}).Select(g=>AssetDatabase.LoadAssetAtPath<WeaponItemData>(AssetDatabase.GUIDToAssetPath(g))).First(x=>x.GetMeleeDefinition()?.heavyAttackDefinition!=null);
        var enemy=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").OrderBy(t=>t.name).SelectMany(t=>t.Entries).Select(e=>e.definition).First(d=>d!=null&&d.ActorPrefab!=null&&d.ActorPrefab.GetComponentInChildren<BloodHitTarget>(true)?.Profile?.suppressBlood==false);
        using(var session=new ComboMakerSession())
        using(var preview=new ComboMakerPreview())
        {
            session.Load(item.GetMeleeComboDefinition());MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(session.Source,session.Working);
            preview.TargetDefinition=enemy;preview.TargetPrefab=enemy.ActorPrefab.gameObject;preview.ShowEffects=false;preview.ShowHits=false;
            preview.Load(item,session.Working);preview.PlaceTargetAtImpact();
            preview.ShowBlood=false;preview.Seek(.32f);
            var without=ReadPreviewPixels(preview,"BloodOff.png");
            preview.ShowBlood=true;preview.Seek(.32f);
            foreach(var d in preview.Actor.transform.root.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>())d.enabled=false;
            var with=ReadPreviewPixels(preview,"BloodOn.png");
            int changed=Enumerable.Range(0,with.Length).Count(i=>Math.Abs(with[i].r-without[i].r)+Math.Abs(with[i].g-without[i].g)+Math.Abs(with[i].b-without[i].b)>15);
            Check(changed>40,"blood spray changes rendered pixels without decals: "+changed,log);
            var graph=preview.Actor.transform.root.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
            var names=new List<string>();graph.GetSpawnSystemNames(names);
            float time=graph.GetSpawnSystemInfo(names[0]).totalTime;
            Check(time>0&&time<.3f,"native spawner advances to preview time: "+time,log);
            preview.Render(new Rect(0,0,800,600));
            Check(Mathf.Abs(graph.GetSpawnSystemInfo(names[0]).totalTime-time)<.0001f,"paused rendering does not advance blood",log);
            preview.Seek(.05f);Check(preview.EffectCount==0,"backward seek clears blood",log);
            preview.Seek(.32f);preview.Render(new Rect(0,0,800,600));
            graph=preview.Actor.transform.root.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
            Check(Mathf.Abs(graph.GetSpawnSystemInfo(names[0]).totalTime-time)<.0001f,"repeated seek reconstructs blood clock",log);
            foreach(float speed in new[]{.25f,1f,2f})
            {
                preview.Seek(.32f);preview.Render(new Rect(0,0,800,600));
                graph=preview.Actor.transform.root.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
                float before=graph.GetSpawnSystemInfo(names[0]).totalTime;
                preview.Speed=speed;preview.Playing=true;preview.Tick(.02f);preview.Playing=false;
                preview.Render(new Rect(0,0,800,600));
                Check(Mathf.Abs(graph.GetSpawnSystemInfo(names[0]).totalTime-before-.02f*speed)<.001f,"blood playback clock at speed "+speed,log);
            }
        }
        Check(EditorSceneManager.previewSceneCount==scenes,"blood render preview scene cleanup",log);
        foreach(var before in sceneState){var s=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).First(s=>s.handle==before.handle);Check(s.isDirty==before.isDirty&&s.rootCount==before.rootCount,"shared scene state preserved",log);}
        File.WriteAllLines(Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/BloodRendering.txt"),log);
        return string.Join("\n",log);
    }
    private static Color32[] ReadPreviewPixels(ComboMakerPreview preview,string file)
    {
        var texture=preview.Render(new Rect(0,0,800,600));
        var old=RenderTexture.active;RenderTexture.active=(RenderTexture)texture;
        var image=new Texture2D(texture.width,texture.height,TextureFormat.RGB24,false);
        try{image.ReadPixels(new Rect(0,0,texture.width,texture.height),0,0);image.Apply();File.WriteAllBytes(Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/"+file),image.EncodeToPNG());return image.GetPixels32();}
        finally{RenderTexture.active=old;UnityEngine.Object.DestroyImmediate(image);}
    }
    public static string BloodAndRoster()
    {
        var log=new List<string>();
        int scenes=EditorSceneManager.previewSceneCount;
        var roster=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").SelectMany(t=>t.Entries).Select(e=>e.definition).Where(d=>d!=null).Distinct().ToArray();
        var item=AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst/03_Features/Weapons"}).Select(g=>AssetDatabase.LoadAssetAtPath<WeaponItemData>(AssetDatabase.GUIDToAssetPath(g))).First(x=>x.GetMeleeDefinition()?.heavyAttackDefinition!=null);
        using(var session=new ComboMakerSession())
        using(var preview=new ComboMakerPreview())
        {
            session.LoadHeavy(item.GetMeleeComboDefinition(),item.GetMeleeDefinition().heavyAttackDefinition);
            MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(session.Source,session.Working);
            preview.HeavyDefinition=session.HeavyWorking;preview.ShowEffects=false;preview.ShowHits=false;
            foreach(var enemy in roster)
            {
                preview.TargetDefinition=enemy;preview.TargetPrefab=enemy.ActorPrefab.gameObject;
                preview.Load(item,session.Working);preview.PlaceTargetAtImpact();preview.Seek(.8f);
                Check(preview.HitCount>0,enemy.DisplayName+" actual target hit",log);
                var profile=enemy.ActorPrefab.GetComponentInChildren<BloodHitTarget>(true)?.Profile;
                var roots=preview.Actor.transform.root;
                bool expected=profile!=null&&!profile.suppressBlood;
                var graphs=roots.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true);
                var decals=roots.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true);
                Check((graphs.Length>0)==expected,enemy.DisplayName+" blood profile routing",log);
                Check((decals.Length>0)==expected,enemy.DisplayName+" ground blood routing",log);
                if(expected)Check(decals.All(d=>d.material!=null&&d.enabled),enemy.DisplayName+" delayed decal enabled",log);
                preview.ShowBlood=false;preview.Seek(.8f);
                Check(roots.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true).Length==0&&roots.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true).Length==0,"blood toggle clears both effects",log);
                preview.ShowBlood=true;preview.Seek(.8f);preview.Seek(0);
                Check(preview.EffectCount==0,"reverse seek clears blood",log);
            }
        }
        Check(EditorSceneManager.previewSceneCount==scenes,"roster preview scenes released",log);
        File.WriteAllLines(Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/BloodAndRoster.txt"),log);
        return string.Join("\n",log);
    }
    public static string CombatExtension()
    {
        var log=new List<string>();
        var roster=Resources.LoadAll<EnemyThemeTable>("Enemies/Themes/Tables").SelectMany(t=>t.Entries).Select(e=>e.definition).Where(d=>d!=null).Distinct().ToArray();
        Check(roster.Length==29,"adopted theme roster has 29 enemies",log);
        Check(roster.All(d=>!AssetDatabase.GetAssetPath(d.ActorPrefab).Contains("/Protofactor/")),"legacy Protofactor excluded",log);
        var item=AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst/03_Features/Weapons"}).Select(g=>AssetDatabase.LoadAssetAtPath<WeaponItemData>(AssetDatabase.GUIDToAssetPath(g))).First(x=>x.GetMeleeDefinition()?.heavyAttackDefinition!=null);
        var heavy=item.GetMeleeDefinition().heavyAttackDefinition;
        string sourceJson=EditorJsonUtility.ToJson(heavy);
        using(var session=new ComboMakerSession())
        using(var preview=new ComboMakerPreview())
        {
            session.LoadHeavy(item.GetMeleeComboDefinition(),heavy);
            Check(!session.Dirty&&session.Validate().Count==0,"heavy clean load and validation",log);
            MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(session.Source,session.Working);
            preview.HeavyDefinition=session.HeavyWorking;preview.TargetDefinition=roster[0];preview.TargetPrefab=roster[0].ActorPrefab.gameObject;
            preview.ShowKnockback=true;preview.Loop=false;preview.Load(item,session.Working);
            preview.PlaceTargetAtImpact();
            foreach(var element in Enum.GetValues(typeof(WeaponElement)).Cast<WeaponElement>().Where(OverburstElementRules.IsActive))
            {
                preview.Element=element;
                var fx=preview.Actor.GetComponentInChildren<MeleeWeaponElementFx>(true);
                using var fxData=new SerializedObject(fx);
                string prefix=char.ToLowerInvariant(element.ToString()[0])+element.ToString().Substring(1);
                bool configured=fxData.FindProperty(prefix+"BladeAccent").objectReferenceValue!=null;
                float weakRate=0;
                foreach(float energy in new[]{0f,.1f,.35f,1f})
                {
                    preview.EnergyNormalized=energy;preview.Begin(0,false,true);
                    bool aura=preview.Actor.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="ElementBladeAccent");
                    Check(aura==configured,element+" energy "+energy+" matches equipped FX binding",log);
                    if(!configured)continue;
                    var auraRoot=preview.Actor.GetComponentsInChildren<Transform>(true).First(t=>t.name=="ElementBladeAccent");
                    float rate=auraRoot.GetComponentsInChildren<ParticleSystem>(true).Sum(p=>p.emission.rateOverTimeMultiplier);
                    if(energy==0)weakRate=rate;
                    if(energy==1)Check(rate>weakRate,element+" full energy increases emission",log);
                }
                preview.EnergyNormalized=1;preview.Seek(.9f);
                Check(preview.HeavyDischarged&&preview.CurrentEnergy==0,element+" heavy consumes preview energy",log);
                Check(preview.HitCount>0,element+" heavy hits adopted monster",log);
                Check(preview.TargetDisplacement>0,element+" monster knockback",log);
                float distance=preview.TargetDisplacement;preview.Seek(.05f);preview.Seek(.9f);
                Check(Mathf.Abs(distance-preview.TargetDisplacement)<.001f,element+" deterministic reaction seek",log);
            }
            preview.EnergyNormalized=0;preview.Seek(.9f);
            Check(preview.CurrentEnergy==0,"empty heavy preview",log);
            Check(EditorJsonUtility.ToJson(heavy)==sourceJson,"heavy original unchanged",log);
            SaveTexture(preview.Render(new Rect(0,0,960,600)),Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/HeavyMonster.png"));
        }
        string path=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/Testers/ComboMakerHeavyFixture.asset");
        var fixture=UnityEngine.Object.Instantiate(heavy);
        string fixtureGuid=null;
        try
        {
            AssetDatabase.CreateAsset(fixture,path); fixtureGuid=AssetDatabase.AssetPathToGUID(path);
            using(var s=new ComboMakerSession())
            {
                s.LoadHeavy(item.GetMeleeComboDefinition(),fixture);s.Working.steps[0].attackName="heavy save check";s.HeavyWorking.chargedDamageMultiplier=1.47f;s.Apply();
                Check(fixture.attack.attackName=="heavy save check"&&Mathf.Approximately(fixture.chargedDamageMultiplier,1.47f),"heavy apply writes attack and energy multiplier",log);
                Check(File.ReadAllText(path).Contains("heavy save check")&&!s.Dirty,"heavy persisted and clean",log);
                Check(fixture.attack.comboInputWindow.Equals(heavy.attack.comboInputWindow),"heavy preserves unused combo window",log);
                Undo.ClearUndo(fixture);
            }
        }
        finally { if(fixture!=null&&!EditorUtility.IsPersistent(fixture))UnityEngine.Object.DestroyImmediate(fixture); RecycleFixture(path,fixtureGuid); }
        File.WriteAllLines(Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/CombatExtension.txt"),log);
        return string.Join("\n",log);
    }

    public static string TimelineInput()
    {
        var w=EditorWindow.GetWindow<ComboMakerWindow>();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var session=(ComboMakerSession)typeof(ComboMakerWindow).GetField("session",flags).GetValue(w);
        var preview=(ComboMakerPreview)typeof(ComboMakerWindow).GetField("preview",flags).GetValue(w);
        if(session.Dirty||!preview.Ready)throw new Exception("Requires a clean, ready preview window");
        var timeline=w.rootVisualElement.Q<ComboMakerTimeline>();
        float width=timeline.contentRect.width-54;
        var log=new List<string>();
        try
        {
            SendTimeline(timeline,EventType.MouseDown,new Vector2(46+.55f*width,80));
            SendTimeline(timeline,EventType.MouseUp,new Vector2(46+.55f*width,80));
            Check(Mathf.Abs(preview.Progress-.55f)<.005f,"timeline pointer scrub",log);
            int step=preview.StepIndex;
            float start=session.Working.steps[step].attackPhases[0].startNormalizedTime;
            SendTimeline(timeline,EventType.MouseDown,new Vector2(46+start*width,25));
            SendTimeline(timeline,EventType.MouseDrag,new Vector2(46+(start+.02f)*width,25));
            SendTimeline(timeline,EventType.MouseUp,new Vector2(46+(start+.02f)*width,25));
            Check(Mathf.Abs(session.Working.steps[step].attackPhases[0].startNormalizedTime-start-.02f)<.005f,"timeline pointer edits phase edge",log);
            Check(session.Dirty,"timeline changes mark draft dirty",log);
            File.WriteAllLines(Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/TimelineInput.txt"),log);
            return string.Join("\n",log);
        }
        finally
        {
            Undo.ClearUndo(session.Working);
            typeof(ComboMakerWindow).GetMethod("LoadSource",flags).Invoke(w,null);
        }
    }
    private static void SendTimeline(VisualElement target,EventType type,Vector2 local)
    {
        var input=new Event {type=type,button=0,mousePosition=target.LocalToWorld(local)};
        if(type==EventType.MouseDown){using(var e=PointerDownEvent.GetPooled(input))target.SendEvent(e);}
        else if(type==EventType.MouseUp){using(var e=PointerUpEvent.GetPooled(input))target.SendEvent(e);}
        else {using(var e=PointerMoveEvent.GetPooled(input))target.SendEvent(e);}
    }

    public static string Authoring()
    {
        var log=new List<string>();
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var w=ScriptableObject.CreateInstance<ComboMakerWindow>();
        try
        {
            w.ShowUtility();
            w.CreateGUI();
            var session=(ComboMakerSession)typeof(ComboMakerWindow).GetField("session",flags).GetValue(w);
            string source=EditorJsonUtility.ToJson(session.Source);
            var field=w.rootVisualElement.Q<FloatField>("field-steps.Array.data[0].animationSpeedMultiplier");
            float old=field.value;
            Undo.IncrementCurrentGroup();
            field.value=old+.125f;
            Undo.FlushUndoRecordObjects();
            Check(Mathf.Approximately(session.Working.steps[0].animationSpeedMultiplier,old+.125f)&&session.Dirty,"native field changes draft",log);
            Undo.PerformUndo();
            Check(Mathf.Approximately(session.Working.steps[0].animationSpeedMultiplier,old),"native edit undo",log);
            Undo.PerformRedo();
            Check(Mathf.Approximately(session.Working.steps[0].animationSpeedMultiplier,old+.125f),"native edit redo",log);
            typeof(ComboMakerWindow).GetMethod("DuplicateStep",flags).Invoke(w,null);
            Check(session.Working.StepCount==session.Source.StepCount+1,"duplicate step",log);
            Check(session.Working.TryGetStableAttackIds(out _,out _),"duplicated ID remains unique",log);
            string moved=session.Working.steps[1].attackId;
            typeof(ComboMakerWindow).GetMethod("MoveStep",flags).Invoke(w,new object[]{1});
            Check(session.Working.steps[2].attackId==moved,"reorder step",log);
            Check(EditorJsonUtility.ToJson(session.Source)==source,"UI changes preserve source",log);
            Undo.ClearUndo(session.Working);
            w.DiscardChanges();
        }
        finally {w.DiscardChanges();w.Close();}

        // Register an isolated asset in the baker for this synchronous test only.
        // The same production Apply/bake/save path runs; live weapon assets never change.
        var profiles=(Array)typeof(MeleeAttackVfxSlopeBakeUtility).GetField("Profiles",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
        object originalProfile=profiles.GetValue(0);
        var profileType=originalProfile.GetType();
        string itemPath=(string)profileType.GetField("ItemPath").GetValue(originalProfile);
        string sourcePath=(string)profileType.GetField("ComboPath").GetValue(originalProfile);
        string fixturePath=AssetDatabase.GenerateUniqueAssetPath("Assets/Editor/Testers/ComboMakerApplyFixture.asset");
        var live=AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(sourcePath);
        byte[] liveBytes=File.ReadAllBytes(sourcePath);
        var fixture=UnityEngine.Object.Instantiate(live);
        string fixtureGuid=null;
        try
        {
            AssetDatabase.CreateAsset(fixture,fixturePath); fixtureGuid=AssetDatabase.AssetPathToGUID(fixturePath);
            profiles.SetValue(Activator.CreateInstance(profileType,new object[]{itemPath,fixturePath,"Apply fixture",fixture.StepCount,3}),0);
            using(var session=new ComboMakerSession())
            {
                session.Load(fixture);
                string oldName=fixture.steps[0].attackName;
                session.Working.steps[0].attackName="ComboMaker save verification";
                session.Apply();
                Check(!session.Dirty&&!session.SourceChanged,"apply refreshes baseline",log);
                Check(fixture.steps[0].attackName=="ComboMaker save verification","apply updates source",log);
                Check(File.ReadAllText(fixturePath).Contains("ComboMaker save verification"),"apply persists to disk",log);
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Check(fixture.steps[0].attackName==oldName,"apply source undo",log);
                Check(session.SourceChanged,"external change detected",log);
                bool rejected=false;try{session.Apply();}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"stale draft apply rejected",log);
                Undo.ClearUndo(fixture);
            }
            Check(liveBytes.SequenceEqual(File.ReadAllBytes(sourcePath)),"live asset bytes preserved",log);
        }
        finally
        {
            profiles.SetValue(originalProfile,0);
            if(fixture!=null&&!EditorUtility.IsPersistent(fixture))UnityEngine.Object.DestroyImmediate(fixture);
            RecycleFixture(fixturePath,fixtureGuid);
        }
        File.WriteAllLines(Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker/Authoring.txt"),log);
        return string.Join("\n",log);
    }

    public static string NativeUI()
    {
        var window=ScriptableObject.CreateInstance<ComboMakerWindow>();
        try
        {
            window.CreateGUI();
            if(window.rootVisualElement.Query<IMGUIContainer>().ToList().Count!=0)
                throw new Exception("IMGUI container exists");
            if(typeof(ComboMakerWindow).GetMethod("OnGUI",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)!=null)
                throw new Exception("OnGUI exists");
            if(window.rootVisualElement.Query<TwoPaneSplitView>().ToList().Count!=2)
                throw new Exception("resizable panes missing");
            if(window.rootVisualElement.Q<ComboMakerTimeline>()==null || window.rootVisualElement.Q<Image>()==null)
                throw new Exception("native timeline or preview surface missing");
            if(window.rootVisualElement.Query<Slider>().ToList().Count==0)
                throw new Exception("native timing sliders missing");
            return "PASS UI Toolkit tree: IMGUI 0, OnGUI 0, split panes 2, timeline/image/sliders present";
        }
        finally {UnityEngine.Object.DestroyImmediate(window);}
    }

    public static string Run()
    {
        var log = new List<string>();
        string folder = Path.GetFullPath("../개인파일/코덱스산출/Tools/ComboMaker");
        Directory.CreateDirectory(folder);
        var scenes = Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i))
            .Select(s=>new {s.handle,s.isDirty,roots=s.rootCount}).ToArray();
        int previewCount = EditorSceneManager.previewSceneCount;
        try
        {
            foreach(var guid in AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst/03_Features/Weapons"}))
            {
                var item = AssetDatabase.LoadAssetAtPath<WeaponItemData>(AssetDatabase.GUIDToAssetPath(guid));
                var source = item.GetMeleeComboDefinition(); if(source==null) continue;
                string original = EditorJsonUtility.ToJson(source);
                byte[] disk = File.ReadAllBytes(AssetDatabase.GetAssetPath(source));
                using(var session = new ComboMakerSession())
                using(var preview = new ComboMakerPreview())
                {
                    session.Load(source);
                    Check(!session.Dirty,"clean load",log);
                    Check(session.Validate().Count==0,"runtime data validation",log);
                    session.Working.steps[0].attackName += " test";
                    Check(session.Dirty && source.steps[0].attackName != session.Working.steps[0].attackName,"draft isolation",log);
                    string recovery=session.Capture();
                    session.Load(source,recovery,session.Baseline);
                    Check(session.Dirty && session.Working.steps[0].attackName.EndsWith(" test"),"reload recovery",log);
                    session.Load(source);
                    MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(source,session.Working);
                    preview.Load(item,session.Working); preview.Loop=false;
                    for(int s=0;s<source.StepCount;s++)
                    {
                        preview.Begin(s,false,true); preview.Playing=true;
                        for(int frame=0;frame<400 && preview.Playing;frame++) preview.Tick(1f/60);
                        Check(preview.Progress>=.999f,"step reaches end "+s,log);
                        int expected=source.steps[s].attackPhases.Sum(p=>p.vfxCues?.Length??0);
                        Check(preview.CueCount==expected,$"{source.name} step {s+1} cues {preview.CueCount}/{expected}",log);
                        Check(preview.HitCount==source.steps[s].attackPhases.Length,$"{source.name} step {s+1} hits={preview.HitCount}",log);
                        preview.Seek(.5f); int hits=preview.HitCount,cues=preview.CueCount;
                        preview.Seek(.15f); preview.Seek(.5f);
                        Check(preview.HitCount==hits && preview.CueCount==cues,"deterministic backward seek "+s,log);
                    }
                    preview.All=true; preview.Begin(0,false,true); preview.Playing=true;
                    for(int frame=0;frame<1000 && preview.Playing;frame++) preview.Tick(1f/60);
                    Check(preview.StepIndex==source.StepCount-1 && preview.Progress>=.999f,"whole combo continuation",log);
                    log.Add($"whole combo hits={preview.HitCount}, cues={preview.CueCount}");
                    preview.All=false; preview.Begin(0,false,true);
                    Check(preview.EffectCount==0 && preview.HitCount==0 && preview.CueCount==0,"stop clears preview",log);
                    preview.TargetPosition=new Vector3(100,.9f,100); preview.Seek(.9f);
                    Check(preview.HitCount==0,"out-of-range target receives no hit",log);
                    preview.TargetPosition=new Vector3(0,.9f,1.5f);
                    preview.Seek(.27f);
                    var texture=preview.Render(new Rect(0,0,900,650));
                    SaveTexture(texture,Path.Combine(folder,source.name+"_Preview.png"));
                    for(int n=0;n<3;n++) { preview.Load(item,session.Working); preview.Seek(.5f); }
                    foreach(var element in Enum.GetValues(typeof(WeaponElement)).Cast<WeaponElement>().Where(OverburstElementRules.IsActive))
                    {
                        preview.Element=element; preview.Begin(0,false,true); preview.Seek(source.steps[0].attackPhases[0].SafeEnd);
                        Check(preview.HitCount==1,$"{element} hit preview",log);
                        if(source.name.Contains("Greatsword") && source.steps[0].trailPhases?.Length>0)
                        {
                            var trailPhase=source.steps[0].trailPhases[0];
                            preview.Seek((trailPhase.SafeStart+trailPhase.SafeEnd)*.5f);
                            Check(preview.TrailParticleCount>0,$"{element} Weapon Effects 2 trail in attack phase",log);
                            preview.Seek(0);
                            int resetParticles=preview.TrailParticleCount;
                            preview.Seek((trailPhase.SafeStart+trailPhase.SafeEnd)*.5f);preview.Seek(0);
                            Check(preview.TrailParticleCount==resetParticles,$"{element} backward seek reconstructs original package history",log);
                        }
                    }
                    preview.Begin(0,false,true); preview.Loop=true; preview.Playing=true;
                    for(int frame=0;frame<600;frame++) preview.Tick(1f/60);
                    Check(preview.Playing && preview.EffectCount<30,"repeat remains bounded",log);
                    preview.Playing=false;
                    Check(EditorJsonUtility.ToJson(source)==original && File.ReadAllBytes(AssetDatabase.GetAssetPath(source)).SequenceEqual(disk),"source and disk unchanged",log);
                    session.Working.steps[0].animationClip=null;
                    Check(session.Validate().Count>0,"missing animation rejected",log);
                    try { session.Apply(); throw new Exception("invalid apply accepted"); }
                    catch(InvalidOperationException) { log.Add("PASS invalid apply leaves source intact"); }
                    session.Load(source);
                    var expanded=session.Working.steps.ToList(); var copy=expanded[expanded.Count-1]; copy.attackId="ComboMaker.Test.NewStep"; expanded.Add(copy);
                    session.Working.steps=expanded.ToArray();
                    MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(source,session.Working);
                    Check(session.Working.AttackTrajectoryBakeData.steps.Length==source.StepCount+1,"added combo step bakes",log);
                }
            }
            foreach(var before in scenes)
            {
                var scene=Enumerable.Range(0,SceneManager.sceneCount).Select(SceneManager.GetSceneAt).First(s=>s.handle==before.handle);
                Check(scene.isDirty==before.isDirty && scene.rootCount==before.roots,"loaded scene state preserved",log);
            }
            Check(EditorSceneManager.previewSceneCount==previewCount,"preview scene disposal",log);
            File.WriteAllLines(Path.Combine(folder,"Checks.txt"),log);
            return "PASS\n"+string.Join("\n",log);
        }
        catch(Exception e)
        { log.Add("FAIL "+e); File.WriteAllLines(Path.Combine(folder,"Checks.txt"),log); throw; }
    }
    private static void Check(bool condition,string label,List<string> log)
    { if(!condition) throw new Exception(label); log.Add("PASS "+label); }
    private static void SaveTexture(Texture texture,string path)
    {
        var rt=RenderTexture.GetTemporary(texture.width,texture.height,0,RenderTextureFormat.ARGB32);
        var old=RenderTexture.active;
        Graphics.Blit(texture,rt); RenderTexture.active=rt;
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image); RenderTexture.active=old; RenderTexture.ReleaseTemporary(rt);
    }
    private static void RecycleFixture(string path, string guid)
    {
        string current = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
        if (string.IsNullOrEmpty(current)) return;
        if (string.IsNullOrEmpty(guid) || current != guid || !path.StartsWith("Assets/Editor/Testers/ComboMaker", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture ownership changed; preserved: " + path);
        if (!AssetDatabase.MoveAssetToTrash(path))
            throw new InvalidOperationException("Could not recycle owned fixture; preserved: " + path);
    }

}
