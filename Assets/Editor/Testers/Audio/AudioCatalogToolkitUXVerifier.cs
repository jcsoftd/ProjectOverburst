using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object=UnityEngine.Object;

public static class AudioCatalogToolkitUXVerifier
{
    const string Output="../개인파일/코덱스산출/Tools/AudioCatalogManager/20261005_ToolkitUX";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static AudioCatalogManagerWindow window;
    static ElementalReactionSfxCatalog clone;
    static readonly List<(Object source,string json,bool dirty,string path,byte[] bytes,byte[] meta)> originals=new List<(Object,string,bool,string,byte[],byte[])>();
    static string scenes,guard;
    static Object selection;
    static int stage,checks,previews,desired,standby,candidateCount;
    static double started,next;
    public static bool Running{get;private set;}
    [MenuItem("OVERBURST/테스트/오디오/Toolkit 디자인과 편집 UX")]
    public static void Run()
    {
        if(Running)throw new InvalidOperationException("Audio UX verifier already running");
        Idle();AudioCatalogManagerVerifier.Run(Output+"/Regression",false);
        Directory.CreateDirectory(Output+"/data");Directory.CreateDirectory(Output+"/captures");
        originals.Clear();stage=checks=0;scenes=Scenes();guard=Guard();selection=Selection.activeObject;previews=UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;desired=AssetDatabase.DesiredWorkerCount;standby=EditorUserSettings.standbyImportWorkerCount;
        var providers=AudioCatalogEditorProviderRegistry.CreateProviders();
        try{foreach(var p in providers){p.Refresh();originals.Add((p.CatalogAsset,EditorJsonUtility.ToJson(p.CatalogAsset),EditorUtility.IsDirty(p.CatalogAsset),p.AssetPath,File.ReadAllBytes(p.AssetPath),File.ReadAllBytes(p.AssetPath+".meta")));}}
        finally{foreach(var p in providers)(p as IDisposable)?.Dispose();}
        Running=true;started=EditorApplication.timeSinceStartup;next=started+.25;
        try{window=ScriptableObject.CreateInstance<AudioCatalogManagerWindow>();window.position=new Rect(40,40,1240,840);window.ShowUtility();EditorApplication.update+=Tick;AssemblyReloadEvents.beforeAssemblyReload+=BeforeReload;Save("RUNNING",null);}
        catch(Exception error){Finish("FAIL",error.ToString());}
    }
    static void Tick()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+.25;
        try
        {
            if(!AudioCatalogManagerWindow.CanEdit||IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))){Finish("DEFERRED_SHARED_USE","Another Editor use began");return;}
            if(EditorApplication.timeSinceStartup-started>120)throw new TimeoutException("Audio Toolkit UX timeout");
            var root=window.rootVisualElement;
            if(stage<8)
            {
                int index=stage/2;
                if(stage%2==0)Click(root.Q<Button>("catalog-"+index));
                else
                {
                    Check(root.Query<Foldout>(className:"audio-cue-card").ToList().Count>0,"Native cue cards "+index);
                    Check(!root.Query<IMGUIContainer>().ToList().Any(),"No IMGUI editor "+index);
                    Check(root.Query<ObjectField>("clip-field").ToList().Count>0,"Native clip ObjectFields "+index);
                    Check(root.Q<ToolbarSearchField>("catalog-search").worldBound.xMax<=root.Q<ToolbarSearchField>("catalog-search").parent.worldBound.xMax,"Catalog search stays in sidebar "+index);
                    var search=root.Q<ToolbarSearchField>("cue-search");search.value="__no_cue__";
                    Check(root.Query<Foldout>(className:"audio-cue-card").ToList().All(c=>c.style.display==DisplayStyle.None),"Native cue filter "+index);Check(root.Q<Label>("cue-empty")!=null,"Cue search empty explanation "+index);search.value="";
                    Capture("catalog-"+index);Check(!root.Q<Button>("save-catalog").enabledSelf,"Clean catalog disables save "+index);
                }
                stage++;return;
            }
            if(stage==8)
            {
                var search=root.Q<ToolbarSearchField>("catalog-search");search.value="전투";Check(root.Q<Button>("catalog-0")==null&&root.Q<Button>("catalog-1")!=null,"Native catalog search");search.value="";
                root.Q<Toggle>("missing-only").value=true;Check(root.Query<Button>().ToList().All(b=>!b.name.StartsWith("catalog-",StringComparison.Ordinal)),"Optional blanks are outside missing filter");root.Q<Toggle>("missing-only").value=false;
                clone=Object.Instantiate((ElementalReactionSfxCatalog)originals[2].source);clone.hideFlags=HideFlags.HideAndDontSave;candidateCount=clone.vaporize.clips.Length;
                var provider=new ElementalReactionSfxCatalogEditorProvider();typeof(AudioCatalogManagerVerifier).GetMethod("Bind",BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(typeof(ElementalReactionSfxCatalog)).Invoke(null,new object[]{provider,clone});
                var providers=Get<List<IAudioCatalogEditorProvider>>("providers");window.rootVisualElement.Unbind();(providers[2] as IDisposable)?.Dispose();providers[2]=provider;Call("SelectProvider",2);stage++;return;
            }
            if(stage==9)
            {
                Click(root.Q<Button>("add-0"));Check(clone.vaporize.clips.Length==candidateCount+1,"Native candidate add on copy");
                Check(clone.vaporize.clips[candidateCount]==null,"Added candidate is intentionally empty");
                var field=root.Query<ObjectField>("clip-field").ToList().First(f=>f.bindingPath=="vaporize.clips.Array.data["+candidateCount+"]");field.value=((ElementalReactionSfxCatalog)originals[2].source).vaporize.clips[0];stage++;return;
            }
            if(stage==10)
            {
                Check(clone.vaporize.clips[candidateCount]!=null,"Native bound ObjectField applies on copy");Click(root.Q<Button>("remove-0-"+candidateCount));Check(clone.vaporize.clips.Length==candidateCount,"Native candidate remove");
                Undo.PerformUndo();Check(clone.vaporize.clips.Length==candidateCount+1,"Candidate removal undo");Undo.PerformRedo();Check(clone.vaporize.clips.Length==candidateCount,"Candidate removal redo");
                window.position=new Rect(40,40,960,700);stage++;return;
            }
            if(stage==11)
            {
                Check(root.Q<Button>("save-catalog").worldBound.yMax<=root.worldBound.yMax,"Compact save stays reachable");Capture("compact");window.position=new Rect(40,40,1240,840);stage++;return;
            }
            if(stage==12){Capture("wide");Check(!root.Query<IMGUIContainer>().ToList().Any(),"Final window remains Toolkit");stage++;return;}
            Finish("PASS",null);
        }
        catch(Exception error){Finish("FAIL",error.ToString());}
    }
    static void Click(Button button){if(button==null||!button.enabledSelf)throw new InvalidOperationException("Expected enabled Toolkit button");typeof(Clickable).GetMethod("SimulateSingleClick",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Invoke(button.clickable,new object[]{null,0});}
    static T Get<T>(string field)=>(T)window.GetType().GetField(field,Private).GetValue(window);
    static object Call(string method,params object[] args)=>window.GetType().GetMethod(method,Private).Invoke(window,args);
    static void Capture(string file)=>typeof(AudioCatalogManagerVerifier).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{window,Output+"/captures/"+file+".png"});
    static void Finish(string status,string error)
    {
        EditorApplication.update-=Tick;AssemblyReloadEvents.beforeAssemblyReload-=BeforeReload;Running=false;
        try
        {
            if(window!=null){window.Close();window=null;}if(clone!=null){Undo.ClearUndo(clone);Object.DestroyImmediate(clone);clone=null;}
            foreach(var source in originals){Check(EditorJsonUtility.ToJson(source.source)==source.json,"Source memory preserved");Check(EditorUtility.IsDirty(source.source)==source.dirty,"Source dirty preserved");Check(File.ReadAllBytes(source.path).SequenceEqual(source.bytes)&&File.ReadAllBytes(source.path+".meta").SequenceEqual(source.meta),"Disk and GUID preserved");}
            if(!status.StartsWith("DEFERRED",StringComparison.Ordinal)){Check(Scenes()==scenes,"User scenes preserved");Check(Guard()==guard,"Guard/start scene preserved");Check(Selection.activeObject==selection,"Selection preserved");Check(UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount==previews,"Previews preserved");Check(AssetDatabase.DesiredWorkerCount==desired&&EditorUserSettings.standbyImportWorkerCount==standby,"Worker settings preserved");}
        }
        catch(Exception e){status="FAIL";error=(error??"")+"\nRETURN: "+e;}
        Save(status,error);originals.Clear();
    }
    static void Save(string status,string error)=>File.WriteAllText(Output+"/data/verification.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{utc=DateTime.UtcNow,status,checks,stage,error,scenes=Scenes(),guard=Guard(),pointer="NOT_RUN_NATIVE_TOOLKIT_CLICKABLE_AND_CHANGE_EVENTS",humanAudition="NOT_RUN",play="NOT_RUN",player="NOT_RUN"},Newtonsoft.Json.Formatting.Indented));
    static void Check(bool pass,string message){checks++;if(!pass)throw new InvalidOperationException(message);}
    static string Scenes()=>Newtonsoft.Json.JsonConvert.SerializeObject(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(UnityEngine.SceneManagement.SceneManager.GetSceneAt).Select(s=>new{s.path,s.isDirty,s.rootCount}));
    static string Guard()=>Newtonsoft.Json.JsonConvert.SerializeObject(new{env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),active=IsolatedSavePlayGuard.ActiveDirectory,choice=IsolatedSavePlayGuard.RequiresAccountChoice,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),start=AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene)});
    static void Idle()=>typeof(AudioCatalogManagerVerifier).GetMethod("RequireIdle",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
    static void BeforeReload()=>Finish("DEFERRED_DOMAIN_RELOAD","Owned callbacks removed before reload");
}
