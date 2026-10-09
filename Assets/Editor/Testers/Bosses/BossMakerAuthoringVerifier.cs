using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using Overburst.EditorTools.BossMaker;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BossMakerAuthoringVerifier
{
    static readonly BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static BossMakerWindow window;
    static IEnumerator routine;
    static string output, folder, folderGuid;
    static double deadline;
    static EnemyBossMaterialCollection collection, original;
    static readonly JArray cases = new JArray();
    static readonly Dictionary<string,string> hashes = new Dictionary<string,string>();
    static JArray scenes;
    static int previews;
    static BossMakerAuthoringVerifier() { AssemblyReloadEvents.beforeAssemblyReload += Reload; }
    static void Check(bool valid, string label) { if (!valid) throw new InvalidOperationException(label); cases.Add(new JObject { ["pass"] = true, ["case"] = label }); }
    static string Hash(string path) { using (var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    static JArray Scenes() => new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new JObject{["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount};}));
    public static string Start(string destination)
    {
        if (routine != null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.RequiresAccountChoice)
            return "DEFERRED_BUSY";
        output=Path.GetFullPath(destination); string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if (!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(output,"result.json"))) throw new InvalidOperationException("Fresh private output required.");
        Directory.CreateDirectory(output); cases.Clear(); hashes.Clear(); scenes=Scenes(); previews=BossMakerWindow.OpenPreviews;
        original=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        foreach (var m in original.attacks) foreach (var asset in new Object[]{m,m.ability})
        { var path=AssetDatabase.GetAssetPath(asset);hashes[path]=Hash(path);hashes[path+".meta"]=Hash(path+".meta"); }
        folder="Assets/Editor/Testers/Bosses/BossMakerFixture_"+Guid.NewGuid().ToString("N");folderGuid=AssetDatabase.CreateFolder("Assets/Editor/Testers/Bosses",Path.GetFileName(folder));
        deadline=EditorApplication.timeSinceStartup+240; routine=Run(); EditorApplication.update+=Tick; Persist("RUNNING"); return "STARTED";
    }
    static T Save<T>(T value,string name) where T:Object { value.name=name;value.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(value,folder+"/"+name+".asset");return value; }
    static EnemyBossAttackMaterial Copy(string id)
    {
        var source=original.attacks.Single(m=>m.originalClip.name==id);
        var ability=Save(Object.Instantiate(source.ability),"EAD_"+id);var material=Object.Instantiate(source);material.ability=ability;
        return Save(material,"BAM_"+id);
    }
    static IEnumerator Run()
    {
        Check(original.attacks.Length==16&&original.attacks.All(m=>m.IsValid&&m.DamageMultiplier==1f&&m.AnimationSpeedMultiplier==1f),"Existing 16 materials valid; default behavior neutral");
        var combo=Copy("2HitComboAttack");var spit=Copy("SpitterShot1");var rock=Copy("ThrowRock");
        collection=Object.Instantiate(original);collection.attacks=new[]{combo,spit,rock};Save(collection,"BMC_AuthoringFixture");
        window=ScriptableObject.CreateInstance<BossMakerWindow>();window.titleContent=new GUIContent("보스 메이커 검증");window.minSize=new Vector2(1140,780);window.position=new Rect(30,45,1480,880);window.ShowUtility();
        yield return null;window.LoadCollection(collection);for(int i=0;i<8;i++)yield return null;
        var root=window.rootVisualElement;
        Check(root.Query<IMGUIContainer>().ToList().Count==0,"UI Toolkit only; no embedded IMGUI");
        Check(window.Preview!=null&&root.Q<Image>("boss-preview").image is RenderTexture,"Model preview connected to Toolkit Image");
        Check(!window.Session.Dirty,"Opening an attack does not edit source or draft");
        Check(root.Q("boss-viewport").contentRect.width>=300&&root.Q("boss-viewport").contentRect.height>=210,"Preview retains usable space at desktop window size");
        Click(root.Q<Button>("boss-tab-2"));yield return null;
        Change(root.Q<FloatField>("boss-damage-multiplier"),1.75f);Change(root.Q<FloatField>("boss-animation-speed"),.8f);yield return null;
        Check(window.Draft.Material.DamageMultiplier==1.75f&&window.Draft.Material.AnimationSpeedMultiplier==.8f,"Damage and speed fields edit runtime tuning on working copy");
        Check(combo.DamageMultiplier==1f&&combo.AnimationSpeedMultiplier==1f,"Unsaved edits leave persistent material untouched");
        Undo.FlushUndoRecordObjects();Undo.PerformUndo();yield return null;Check(Mathf.Abs(window.Draft.Material.AnimationSpeedMultiplier-1f)<.001f,"Undo restores one field edit");
        Undo.PerformRedo();yield return null;Check(Mathf.Abs(window.Draft.Material.AnimationSpeedMultiplier-.8f)<.001f,"Redo restores field edit");
        Click(root.Q<Button>("boss-tab-0"));yield return null;
        foreach(int kind in new[]{1,2,3,0})
        {
            var shape=root.Q<DropdownField>("boss-shape");Change(shape,shape.choices[kind]);yield return null;
            Check(window.Draft.Material.strikes[0].shape==(GroundIndicatorShape)kind&&window.Draft.Material.strikes[0].IsValid,"Shape picker supports "+(GroundIndicatorShape)kind);
        }
        Change(root.Q<FloatField>("boss-radius"),12f);yield return null;
        var overlay=root.Q("boss-geometry");var strike=window.Draft.Material.strikes[0];
        var old=window.Preview.Project(window.Preview.Origin(0)+window.Preview.Rotation(0)*Vector3.forward*strike.radius+Vector3.up*.045f,overlay.contentRect.size);
        var next=window.Preview.Project(window.Preview.Origin(0)+window.Preview.Rotation(0)*Vector3.forward*(strike.radius+1f)+Vector3.up*.045f,overlay.contentRect.size);
        Pointer(overlay,EventType.MouseDown,old);Pointer(overlay,EventType.MouseDrag,next);Pointer(overlay,EventType.MouseUp,next);yield return null;
        Check(Mathf.Abs(strike.radius-13f)<.15f,"Viewport radius handle edits actual geometry");
        float cancelledRadius=strike.radius;
        old=window.Preview.Project(window.Preview.Origin(0)+window.Preview.Rotation(0)*Vector3.forward*strike.radius+Vector3.up*.045f,overlay.contentRect.size);
        next=window.Preview.Project(window.Preview.Origin(0)+window.Preview.Rotation(0)*Vector3.forward*(strike.radius+1f)+Vector3.up*.045f,overlay.contentRect.size);
        Pointer(overlay,EventType.MouseDown,old);Pointer(overlay,EventType.MouseDrag,next);
        using(var escape=KeyDownEvent.GetPooled(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape})){escape.target=overlay;overlay.SendEvent(escape);}
        yield return null;strike=window.Draft.Material.strikes[0];
        Check(Mathf.Abs(strike.radius-cancelledRadius)<.001f&&!overlay.HasPointerCapture(PointerId.mousePointerId),"Esc restores a dragged shape and releases pointer capture");
        var details=root.Q<Foldout>();details.value=true;yield return null;
        var vector=root.Q<Vector3Field>("boss-origin");
        Check(vector.Query<FloatField>().ToList().All(f=>f.resolvedStyle.width>22f),"Expanded center XYZ inputs remain usable");
        details.value=false;
        Click(root.Q<Button>("boss-tab-1"));yield return null;
        float beforeImpact=strike.impact;var timeline=root.Q("boss-timeline");float width=timeline.contentRect.width-64;
        Pointer(timeline,EventType.MouseDown,new Vector2(52+beforeImpact*width,27));Pointer(timeline,EventType.MouseDrag,new Vector2(52+(beforeImpact+.02f)*width,27));Pointer(timeline,EventType.MouseUp,new Vector2(52+(beforeImpact+.02f)*width,27));yield return null;
        Check(Mathf.Abs(strike.impact-beforeImpact-.02f)<.003f&&Mathf.Abs(window.Draft.Ability.HitNormalizedTime-strike.impact)<.00001f,"Timeline drag synchronizes material impact and ability hit");
        Change(root.Q<Toggle>("boss-strike-parry"),false);yield return null;
        Change(root.Q<DropdownField>("boss-strike"),"2타");yield return null;Change(root.Q<Toggle>("boss-custom-parry"),true);yield return null;
        var second=window.Draft.Material.strikes[1];Change(root.Q<FloatField>("boss-parry-start-frame"),(second.impact-.12f)*window.Draft.FrameCount);Change(root.Q<FloatField>("boss-parry-end-frame"),(second.impact-.04f)*window.Draft.FrameCount);yield return null;
        Check(!window.Draft.Material.IsStrikeParryable(0)&&window.Draft.Material.IsStrikeParryable(1),"Each combo strike has its own parry eligibility");
        Check(window.Draft.Material.IsParryWindowOpen(1,second.impact-.08f,9f)&&!window.Draft.Material.IsParryWindowOpen(1,second.impact-.02f,.1f),"Custom frame window replaces default seconds gate");
        float frame=window.Preview.Progress;Change(root.Q<Toggle>("boss-show-source"),true);yield return null;
        Check(window.Preview.ActiveDraft.Material.DamageMultiplier==1f&&Mathf.Abs(window.Preview.Progress-frame)<.001f,"Original comparison retains the inspected frame");
        Change(root.Q<Toggle>("boss-show-source"),false);yield return null;
        window.OpenReview();Check(root.Q("boss-save-review").resolvedStyle.display!=DisplayStyle.None&&root.Q<Button>("boss-save-confirm").enabledSelf,"Changed values reviewed before explicit save");
        Check(BossMakerReview.Changes(window.Draft).Any(s=>s.Contains("→")),"Save review lists actual before and after values");
        window.CommitReview();yield return null;
        Check(!window.Session.Dirty&&combo.DamageMultiplier==1.75f&&combo.AnimationSpeedMultiplier==.8f,"Explicit save persists edited material and clears dirty state");
        Undo.PerformUndo();yield return null;
        Check(combo.DamageMultiplier==1f&&window.Draft.Material.DamageMultiplier==1f&&!window.Session.Dirty&&!window.Draft.Conflict,"Undo of saved tuning refreshes clean working copy");
        Undo.PerformRedo();yield return null;
        Check(combo.DamageMultiplier==1.75f&&window.Draft.Material.DamageMultiplier==1.75f&&!window.Session.Dirty&&!window.Draft.Conflict,"Redo of saved tuning refreshes clean working copy");
        AssetDatabase.SaveAssetIfDirty(combo);AssetDatabase.SaveAssetIfDirty(combo.ability);
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(combo),ImportAssetOptions.ForceUpdate);yield return null;
        var saved=AssetDatabase.LoadAssetAtPath<EnemyBossAttackMaterial>(AssetDatabase.GetAssetPath(combo));
        Check(saved.IsValid&&saved.ability==AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(AssetDatabase.GetAssetPath(combo.ability))&&EditorUtility.IsPersistent(saved.ability),"Saved material references persistent ability after native reload");
        var fresh = new BossMakerSession(collection);
        try { Check(fresh.Drafts[0].Material.DamageMultiplier==1.75f&&!fresh.Dirty,"Saved tuning loads into a new clean editing session"); }
        finally { fresh.Dispose(); }
        var restored = new BossMakerSession(collection);
        try
        {
            restored.Drafts[0].Edit("Recovery fixture",()=>restored.Drafts[0].Material.tuning.damageMultiplier=1.9f);
            var captured=restored.Capture();var recovered=new BossMakerSession(collection,captured);
            try { Check(recovered.Drafts[0].Dirty&&recovered.Drafts[0].Material.DamageMultiplier==1.9f&&!recovered.Drafts[0].Conflict,"Unsaved tuning survives session recovery"); }
            finally { recovered.Dispose(); }
        }
        finally { restored.Dispose(); }
        window.SelectAttack(1);yield return null;Click(root.Q<Button>("boss-tab-2"));yield return null;
        Change(root.Q<FloatField>("boss-damage-multiplier"),2f);Change(root.Q<FloatField>("boss-animation-speed"),1.2f);window.OpenReview();window.CommitReview();yield return null;
        window.SelectAttack(2);yield return null;Click(root.Q<Button>("boss-tab-3"));yield return null;
        Change(root.Q<FloatField>("boss-flight-seconds"),.8f);Change(root.Q<FloatField>("boss-arc-height"),3f);yield return null;
        Click(root.Q<Button>("boss-tab-2"));yield return null;Change(root.Q<FloatField>("boss-damage-multiplier"),1.3f);window.OpenReview();window.CommitReview();yield return null;
        Check(spit.DamageMultiplier==2f&&rock.flightSeconds==.8f&&rock.arcHeight==3f,"Ranged damage and flight fields persist to runtime data");
        window.SelectAttack(0);yield return null;Click(root.Q<Button>("boss-tab-0"));yield return null;
        float good=window.Draft.Material.strikes[0].radius;Change(root.Q<FloatField>("boss-radius"),-1f);window.OpenReview();yield return null;
        Check(!root.Q<Button>("boss-save-confirm").enabledSelf,"Invalid geometry blocks saving");
        Change(root.Q<FloatField>("boss-radius"),good);yield return null;
        Check(!window.Session.Dirty,"Restoring invalid input returns clean draft");
        window.Seek(window.Draft.Material.strikes[0].impact);yield return null;Capture(window,Path.Combine(output,"native-authoring.png"));
        Check(JToken.DeepEquals(scenes,Scenes()),"Preview and editing preserve user scenes and dirtiness");
        foreach(var row in hashes)Check(Hash(row.Key)==row.Value,"Original asset unchanged: "+Path.GetFileName(row.Key));
        CloseWindow();Check(BossMakerWindow.OpenPreviews==previews,"Verifier preview resources returned to baseline");
        Persist("PASS");
        File.WriteAllText(Path.Combine(output,"runtime-plan.json"),new JObject{["collection"]=AssetDatabase.GetAssetPath(collection),["fixtureFolder"]=folder,["folderGuid"]=folderGuid,["scenes"]=scenes}.ToString());
        routine=null;EditorApplication.update-=Tick;
    }
    static void Tick()
    {
        if(routine==null)return;
        try { if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Native authoring interrupted.");if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Native authoring deadline.");if(!routine.MoveNext())routine=null; }
        catch(Exception e){Persist("FAIL",e.ToString());CloseWindow();CleanupFixture();routine=null;EditorApplication.update-=Tick;}
    }
    static void Persist(string state,string error=null)=>File.WriteAllText(Path.Combine(output,"result.json"),new JObject{["status"]=state,["failure"]=error,["cases"]=cases,["fixtureFolder"]=folder,["scenesBefore"]=scenes,["scenesAfter"]=Scenes()}.ToString());
    static void Reload(){if(routine!=null){Persist("FAIL","Interrupted by domain reload.");CloseWindow();CleanupFixture();routine=null;EditorApplication.update-=Tick;}}
    static void CloseWindow(){if(window==null)return;window.ClearVerificationDrafts();window.Close();window=null;}
    public static bool CleanupFixture()
    {
        if(string.IsNullOrEmpty(folder)) return true;
        try { RecycleFixture(folder,folderGuid); return true; }
        catch(Exception error) { Persist("FAIL",error.Message); Debug.LogError(error.Message); return false; }
    }
    static void Change<T>(BaseField<T> field,T value)
    {if(field==null)throw new InvalidOperationException("Expected Toolkit field missing.");var old=field.value;field.SetValueWithoutNotify(value);using(var evt=ChangeEvent<T>.GetPooled(old,value)){evt.target=field;field.SendEvent(evt);}}
    static void Click(Button button){Pointer(button,EventType.MouseDown,button.contentRect.center);Pointer(button,EventType.MouseUp,button.contentRect.center);}
    static void Pointer(VisualElement element,EventType kind,Vector2 local)
    {var input=new Event{type=kind,button=0,mousePosition=element.LocalToWorld(local)};EventBase evt=kind==EventType.MouseDown?(EventBase)PointerDownEvent.GetPooled(input):kind==EventType.MouseUp?PointerUpEvent.GetPooled(input):PointerMoveEvent.GetPooled(input);using(evt){evt.target=element;element.SendEvent(evt);}}
    internal static void Capture(EditorWindow w,string path)
    {
        var view=typeof(EditorWindow).GetField("m_Parent",Flags).GetValue(w);var type=view.GetType();int width=(int)w.position.width,height=(int)w.position.height;
        RenderTexture surface=null;Texture2D texture=null;var previous=RenderTexture.active;
        try
        {
            type.GetMethod("RepaintImmediately",Flags).Invoke(view,null);surface=new RenderTexture(width,height,24);surface.Create();
            type.GetMethod("GrabPixels",Flags,null,new[]{typeof(RenderTexture),typeof(Rect)},null).Invoke(view,new object[]{surface,new Rect(0,0,width,height)});
            RenderTexture.active=surface;texture=new Texture2D(width,height,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,width,height),0,0);texture.Apply();var pixels=texture.GetPixels32();
            for(int y=0;y<height/2;y++)for(int x=0;x<width;x++){int a=y*width+x,b=(height-1-y)*width+x;var t=pixels[a];pixels[a]=pixels[b];pixels[b]=t;}texture.SetPixels32(pixels);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
        }
        finally{RenderTexture.active=previous;if(texture!=null)Object.DestroyImmediate(texture);if(surface!=null){surface.Release();Object.DestroyImmediate(surface);}}
    }
    private static void RecycleFixture(string path, string guid)
    {
        string current = AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets);
        if (string.IsNullOrEmpty(current)) return;
        if (string.IsNullOrEmpty(guid) || current != guid || !path.StartsWith("Assets/Editor/Testers/Bosses/BossMakerFixture_", StringComparison.Ordinal))
            throw new InvalidOperationException("Fixture ownership changed; preserved: " + path);
        if (!AssetDatabase.MoveAssetToTrash(path))
            throw new InvalidOperationException("Could not recycle owned fixture; preserved: " + path);
    }

}
