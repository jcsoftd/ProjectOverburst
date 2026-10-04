using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Builds a separate supplier-effect review and captures existing item artwork with native shaders.</summary>
public static class EdgeEffectsIconReviewBuilder
{
    public const string VendorRoot = "Assets/ThirdParty/06_VFX/RVFX/UIShaderEffects-EdgeEffects";
    public const string ReviewScene = "Assets/ProjectOverburst/00_Scenes/Preview/SCN_EdgeEffectsIconReview.unity";
    const string Output = "../개인파일/코덱스산출/UI/20261004_EdgeEffectsIconReview";
    const string SlotPrefab = "Assets/ProjectOverburst/02_Shared/UI/Prefabs/RpgMmo11/Slots/PF_OverburstItemSlot_Rpg11.prefab";
    const string MaterialsRoot = "Assets/ProjectOverburst/02_Shared/UI/Materials/EdgeEffectsReview";
    const string DerivedTextureRoot = VendorRoot+"/ReviewDerived/Textures";
    static readonly string[] Candidates = {"EdgeGlow_01","EdgeGlow_05","EdgeGlow_13","EdgeGlow_15","EdgeGlow_16"};
    static readonly ItemGrade[] Grades = {ItemGrade.Uncommon,ItemGrade.Rare,ItemGrade.Epic,ItemGrade.Legendary,ItemGrade.Artifact,ItemGrade.Mythic,ItemGrade.Cursed};
    static readonly string[] GradeLabels = {"UNCOMMON","RARE","EPIC","LEGENDARY","ARTIFACT","MYTHIC","CURSED"};

    [MenuItem("OVERBURST/UI/Edge Effects/Build Review Scene")]
    public static void BuildReviewMenu(){Debug.Log(BuildReview());}
    [MenuItem("OVERBURST/UI/Edge Effects/Open Review Scene")]
    public static void OpenReview(){
        if(!AssetDatabase.LoadAssetAtPath<SceneAsset>(ReviewScene))throw new InvalidOperationException("Build the review first.");
        // Additive preserves any open, unsaved production scene.
        EditorSceneManager.OpenScene(ReviewScene,OpenSceneMode.Additive);
    }
    public static string PreviewCatalog(){return Capture("catalog",1);}
    public static string CaptureCatalog(){return Capture("catalog",120);}
    public static string CaptureComparison(){return Capture("comparison",120);}
    public static string CaptureGrades(){return Capture("grades",120);}
    public static string FinishReview(){return CaptureComparison()+"\n"+CaptureGrades()+"\n"+BuildReview();}

    static bool Busy()=>EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating;
    static string[] SharedScenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return s.path+"|"+s.isDirty+"|"+s.rootCount;}).ToArray();
    static GameObject[] Effects()=>AssetDatabase.FindAssets("t:Prefab",new[]{VendorRoot}).Select(AssetDatabase.GUIDToAssetPath)
        .OrderBy(p=>int.Parse(Regex.Match(Path.GetFileName(p),@"\d+").Value)).ThenBy(p=>p)
        .Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
    static BaseItemData[] Items(){
        var all=AssetDatabase.FindAssets("t:BaseItemData",new[]{"Assets/ProjectOverburst"}).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p=>p)
            .Select(AssetDatabase.LoadAssetAtPath<BaseItemData>).Where(i=>i&&i.icon).ToArray();
        return new BaseItemData[]{
            AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset"),
            all.OfType<GearItemData>().First(i=>i.name.IndexOf("Helmet",StringComparison.OrdinalIgnoreCase)>=0),
            all.OfType<ElementGemItemData>().First(i=>i.element==WeaponElement.Fire&&i.fixedGrade==ItemGrade.Mythic),
            all.OfType<BagItemData>().First(),all.OfType<FlaskItemData>().First()};
    }

    sealed class Board : IDisposable {
        public Scene scene;
        public Camera camera;
        public Canvas canvas;
        public RenderTexture target;
        public int width,height;
        public bool persist;
        readonly RenderTexture previous;
        public readonly List<Object> owned=new List<Object>();
        public readonly List<Material> timed=new List<Material>();
        readonly Dictionary<Shader,Shader> clocks=new Dictionary<Shader,Shader>();
        readonly Dictionary<Texture,Texture> neutralTextures=new Dictionary<Texture,Texture>();
        readonly FieldInfo radial=typeof(SlotGradeEffect).GetField("radialGradientSprite",BindingFlags.NonPublic|BindingFlags.Static);
        readonly Sprite beforeRadial;
        public Board(int w,int h,bool persistent=false){
            width=w;height=h;persist=persistent;previous=RenderTexture.active;beforeRadial=(Sprite)radial.GetValue(null);
            scene=persist?EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive):EditorSceneManager.NewPreviewScene();
            var go=new GameObject("Review Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(go,scene);
            camera=go.GetComponent<Camera>();camera.scene=scene;camera.enabled=false;camera.orthographic=true;camera.orthographicSize=h*.5f;
            camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.045f,.06f);
            camera.cullingMask=1<<30;camera.allowHDR=false;camera.allowMSAA=false;
            go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderPostProcessing=false;
            go=new GameObject("Edge Effects Review",typeof(RectTransform),typeof(Canvas));SceneManager.MoveGameObjectToScene(go,scene);
            canvas=go.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;
            canvas.GetComponent<RectTransform>().sizeDelta=new Vector2(w,h);
            if(!persist){target=new RenderTexture(w,h,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;}
        }
        public void Caption(string text,float x,float y,float w,int size=16,Color? c=null){
            var go=new GameObject(text,typeof(RectTransform),typeof(Text));go.transform.SetParent(canvas.transform,false);
            var t=go.GetComponent<Text>();t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");t.text=text;t.fontSize=size;t.color=c??new Color(.78f,.8f,.82f);
            t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Overflow;
            Top(t.rectTransform,x,y,w,32);
        }
        public void Panel(float x,float y,float w,float h){
            var go=new GameObject("Sample Card",typeof(RectTransform),typeof(Image));go.transform.SetParent(canvas.transform,false);
            var image=go.GetComponent<Image>();image.color=new Color(.062f,.075f,.096f);image.raycastTarget=false;Top(image.rectTransform,x,y,w,h);
        }
        public Transform Clip(float x,float y,float w,float h){
            var go=new GameObject("Effect Sample Viewport",typeof(RectTransform),typeof(RectMask2D));go.transform.SetParent(canvas.transform,false);
            Top(go.GetComponent<RectTransform>(),x,y,w,h);return go.transform;
        }
        Texture GradeTexture(Texture source){
            if(!source)return source;
            if(neutralTextures.TryGetValue(source,out var cached))return cached;
            var previousActive=RenderTexture.active;
            var readback=RenderTexture.GetTemporary(source.width,source.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            Texture2D neutral=null;
            try{
                Graphics.Blit(source,readback);RenderTexture.active=readback;
                neutral=new Texture2D(source.width,source.height,TextureFormat.RGBA32,false,false);
                neutral.ReadPixels(new Rect(0,0,source.width,source.height),0,0);neutral.Apply();
                var pixels=neutral.GetPixels();bool colored=false;
                for(int i=0;i<pixels.Length;i++){
                    var p=pixels[i];float value=Mathf.Max(p.r,Mathf.Max(p.g,p.b));
                    if(value-Mathf.Min(p.r,Mathf.Min(p.g,p.b))>.025f)colored=true;
                    pixels[i]=new Color(value,value,value,p.a);
                }
                if(!colored){Object.DestroyImmediate(neutral);neutralTextures[source]=source;return source;}
                neutral.SetPixels(pixels);neutral.Apply();neutral.wrapMode=source.wrapMode;neutral.filterMode=source.filterMode;neutral.name=source.name+"_GradeNeutral";
                Texture result=neutral;
                if(persist){
                    EnsureFolder(DerivedTextureRoot);string path=DerivedTextureRoot+"/"+neutral.name+".png";
                    File.WriteAllBytes(path,neutral.EncodeToPNG());AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Default;importer.sRGBTexture=true;importer.wrapMode=source.wrapMode;importer.filterMode=source.filterMode;importer.mipmapEnabled=false;importer.SaveAndReimport();
                    result=AssetDatabase.LoadAssetAtPath<Texture2D>(path);Object.DestroyImmediate(neutral);
                }else owned.Add(neutral);
                neutralTextures[source]=result;return result;
            }finally{RenderTexture.active=previousActive;RenderTexture.ReleaseTemporary(readback);}
        }
        public GameObject Slot(Sprite sprite,ItemGrade grade,float x,float y,float scale,bool current,bool keepGradeBackground=false){
            var go=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefab),canvas.transform,false);
            go.name=(current?"CURRENT ":"CANDIDATE ")+grade+" "+sprite.name;var rect=go.GetComponent<RectTransform>();Top(rect,x,y,84,84);rect.localScale=Vector3.one*scale;
            var view=go.GetComponent<OverburstUIItemSlotView>();view.Present(sprite,grade);
            var preview=go.GetComponent<OverburstUISlotGradePreview>();
            if(preview){var frame=(GameObject)new SerializedObject(preview).FindProperty("editorGradeFrame").objectReferenceValue;if(frame)frame.SetActive(false);preview.enabled=false;}
            if(current||keepGradeBackground){
                var root=new GameObject("Current Production Grade",typeof(RectTransform));root.transform.SetParent(go.transform,false);root.transform.SetSiblingIndex(1);
                var r=root.GetComponent<RectTransform>();r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=Vector2.one*6;r.offsetMax=-Vector2.one*6;
                var effect=root.AddComponent<SlotGradeEffect>();effect.Init((Image)new SerializedObject(view).FindProperty("icon").objectReferenceValue,null);effect.SetGrade(grade,GradeConfig.GetGradeColor(grade));
                if(!current){var old=root.transform.Find("ExperimentalOutline");if(!old)old=root.GetComponentInChildren<ExperimentalSlotOutlineEffect>(true)?.transform;if(old)old.gameObject.SetActive(false);}
            }
            return go;
        }
        public GameObject Effect(GameObject prefab,float x,float y,float size,ItemGrade? grade=null,float intensity=1,bool square=false,Transform parent=null){
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,scene);go.transform.SetParent(parent??canvas.transform,false);go.name=prefab.name+(grade.HasValue?" - "+grade:" - Original");
            var r=go.GetComponent<RectTransform>();Top(r,x,y,size,size);r.localScale=Vector3.one;
            foreach(var image in go.GetComponentsInChildren<Image>(true)){
                image.raycastTarget=false;
                if(grade.HasValue)image.color=GradeConfig.GetGradeColor(grade.Value);
                if(grade.HasValue||intensity!=1||square){
                    var m=new Material(image.material);m.name="Review_"+prefab.name+"_"+image.name;
                    if(m.HasProperty("_HueShift"))m.SetFloat("_HueShift",0);
                    if(grade.HasValue&&m.HasProperty("_MainTex"))m.SetTexture("_MainTex",GradeTexture(m.GetTexture("_MainTex")));
                    if(m.HasProperty("_ColorIntensity"))m.SetFloat("_ColorIntensity",m.GetFloat("_ColorIntensity")*intensity);
                    if(square&&m.HasProperty("_IsCircular")){m.SetFloat("_IsCircular",0);m.DisableKeyword("_IsCircular");}
                    if(persist){
                        EnsureFolder(MaterialsRoot);string path=MaterialsRoot+"/"+m.name+".mat";
                        var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(existing){EditorUtility.CopySerialized(m,existing);Object.DestroyImmediate(m);m=existing;AssetDatabase.SaveAssetIfDirty(existing);}
                        else AssetDatabase.CreateAsset(m,path);
                    }else owned.Add(m);
                    image.material=m;
                }
            }
            return go;
        }
        public void ClockMaterials(){
            foreach(var image in canvas.GetComponentsInChildren<Image>(true)){
                if(!image.material||image.material.shader.name=="UI/Default")continue;
                var original=image.material;var shader=original.shader;string path=AssetDatabase.GetAssetPath(shader);
                if(string.IsNullOrEmpty(path)||!File.Exists(path))continue;
                if(!clocks.TryGetValue(shader,out var clock)){
                    string code=File.ReadAllText(path);
                    if(!Regex.IsMatch(code,@"\b_Time\b"))continue;
                    code=Regex.Replace(code,@"\b_Time\b","_EdgeCaptureTime");
                    code=Regex.Replace(code,@"Shader\s+""[^""]+""","Shader \"OVERBURST/ReviewClock/"+shader.GetInstanceID()+"\"");
                    code=code.Replace("CGPROGRAM","CGPROGRAM\nfloat4 _EdgeCaptureTime;");
                    clock=ShaderUtil.CreateShaderAsset(code,false);clock.hideFlags=HideFlags.HideAndDontSave;
                    if(ShaderUtil.ShaderHasError(clock))throw new InvalidOperationException("Clock shader failed: "+path);
                    clocks.Add(shader,clock);owned.Add(clock);
                }
                var mat=new Material(original){shader=clock,hideFlags=HideFlags.HideAndDontSave,name="Capture clock "+original.name};owned.Add(mat);timed.Add(mat);image.material=mat;
            }
        }
        public void SaveFrame(string path,float seconds){
            foreach(var mat in timed)mat.SetVector("_EdgeCaptureTime",new Vector4(seconds/20,seconds,seconds*2,seconds*3));
            foreach(var t in canvas.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            Canvas.ForceUpdateCanvases();camera.Render();
            Texture2D pixels=null;
            try{RenderTexture.active=target;pixels=new Texture2D(width,height,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,width,height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;if(pixels)Object.DestroyImmediate(pixels);}
        }
        public void Dispose(){
            RenderTexture.active=previous;if(camera)camera.targetTexture=null;
            if(target){target.Release();Object.DestroyImmediate(target);}
            if(scene.IsValid()){if(persist)EditorSceneManager.CloseScene(scene,true);else EditorSceneManager.ClosePreviewScene(scene);}
            foreach(var o in owned)if(o)Object.DestroyImmediate(o);owned.Clear();timed.Clear();clocks.Clear();neutralTextures.Clear();
            if(!beforeRadial){var sprite=(Sprite)radial.GetValue(null);if(sprite){var tex=sprite.texture;Object.DestroyImmediate(sprite);if(tex)Object.DestroyImmediate(tex);}radial.SetValue(null,null);}
        }
    }
    static void Top(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
    static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;EnsureFolder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}

    static void Catalog(Board board,GameObject[] effects,BaseItemData[] items){
        board.Caption("EDGE EFFECTS  /  20 ORIGINAL PRESETS",28,20,1400,27);
        board.Caption("LEFT: SUPPLIER ORIGINAL     RIGHT: OVERBURST 84px SLOT / EPIC COLOR / 45% INTENSITY",28,62,1450,15);
        for(int i=0;i<effects.Length;i++){
            float x=24+(i%5)*294,y=108+(i/5)*210;board.Panel(x,y,278,194);
            board.Caption(effects[i].name,x+14,y+12,250,18);
            var clip=board.Clip(x+6,y+43,266,121);
            board.Effect(effects[i],6,7,116,parent:clip);
            var slot=board.Slot(items[i%items.Length].icon,ItemGrade.Epic,166,25,1,false);
            slot.transform.SetParent(clip,false);
            // 96px effect covers the 84px slot with a 6px outer allowance.
            board.Effect(effects[i],-6,-6,96,ItemGrade.Epic,.45f,true,slot.transform);
            board.Caption("ORIGINAL",x+22,y+168,115,12);board.Caption("GAME ICON",x+156,y+168,115,12);
        }
    }
    static void Comparison(Board board,GameObject[] effects,BaseItemData[] items){
        board.Caption("OVERBURST  /  BORDER REPLACEMENT STUDY",28,20,1500,27);
        board.Caption("Same 84px slot and artwork. Candidate: grade color, neutral pattern RGB, intensity 45%.",28,62,1500,15);
        var labels=new[]{"CURRENT"}.Concat(Candidates).ToArray();
        for(int c=0;c<labels.Length;c++)board.Caption(labels[c],224+c*194,111,190,17);
        var rowNames=new[]{"GREATSWORD","HELMET","FIRE GEM","BAG","FLASK"};
        for(int r=0;r<items.Length;r++){
            float y=152+r*140;board.Caption(rowNames[r],28,y+30,180,17);
            var grade=new[]{ItemGrade.Rare,ItemGrade.Epic,ItemGrade.Mythic,ItemGrade.Legendary,ItemGrade.Artifact}[r];
            board.Caption(grade.ToString().ToUpperInvariant(),28,y+61,180,13,GradeConfig.GetGradeColor(grade));
            for(int c=0;c<labels.Length;c++){
                float x=220+c*194;board.Panel(x-12,y-8,170,124);
                var slot=board.Slot(items[r].icon,grade,x+25,y+10,1,c==0,true);
                if(c>0)board.Effect(effects.First(p=>p.name==labels[c]),-6,-6,96,grade,.45f,true,slot.transform);
            }
        }
    }
    static void GradeBoard(Board board,GameObject[] effects,BaseItemData[] items){
        board.Caption("OVERBURST  /  ONE PRESET ACROSS ALL GRADES",28,20,1500,27);
        board.Caption("A consistent border shape with the existing green / blue / purple / gold / teal / red / burgundy grade colors.",28,62,1500,15);
        for(int c=0;c<Grades.Length;c++)board.Caption(GradeLabels[c],205+c*166,118,165,14,GradeConfig.GetGradeColor(Grades[c]));
        var rows=new[]{"CURRENT","EdgeGlow_01","EdgeGlow_13","EdgeGlow_15"};
        for(int r=0;r<rows.Length;r++){
            float y=164+r*172;board.Caption(rows[r],28,y+48,170,17);
            for(int c=0;c<Grades.Length;c++){
                float x=204+c*166;board.Panel(x-8,y-10,150,142);
                var slot=board.Slot(items[c%items.Length].icon,Grades[c],x+25,y+18,1,r==0,true);
                if(r>0)board.Effect(effects.First(p=>p.name==rows[r]),-6,-6,96,Grades[c],.45f,true,slot.transform);
            }
        }
    }
    static string Capture(string kind,int frames){
        if(Busy())return "DEFERRED: Editor busy; nothing created";
        Directory.CreateDirectory(Output+"/media/"+kind+"-frames");
        var before=SharedScenes();var active=SceneManager.GetActiveScene();
        var effects=Effects();var items=Items();if(effects.Length!=20)throw new InvalidOperationException("Expected 20 supplier presets");
        int width=kind=="catalog"?1500:1400,height=kind=="catalog"?970:890;
        using(var board=new Board(width,height)){
            if(kind=="catalog")Catalog(board,effects,items);else if(kind=="comparison")Comparison(board,effects,items);else GradeBoard(board,effects,items);
            board.ClockMaterials();
            for(int i=0;i<frames;i++)board.SaveFrame(Output+"/media/"+kind+"-frames/"+i.ToString("D4")+".png",2f+i/20f);
        }
        if(SceneManager.GetActiveScene()!=active)SceneManager.SetActiveScene(active);
        if(!before.SequenceEqual(SharedScenes()))throw new InvalidOperationException("Shared scenes changed");
        File.WriteAllText(Output+"/data/"+kind+"-capture.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{status="PASS",kind,frames,fps=20,width,height,source="Native Unity preview scene with supplier shaders and material clock substitution only",items=items.Select(i=>new{name=i.itemName,path=AssetDatabase.GetAssetPath(i),icon=AssetDatabase.GetAssetPath(i.icon)}),currentMode=ExperimentalSlotOutlineModeState.CurrentMode.ToString(),sharedScenePreserved=true,cleanup=true},Newtonsoft.Json.Formatting.Indented));
        return "PASS: "+kind+" "+frames+" native frames; preview scene and temporary materials removed";
    }
    public static string BuildReview(){
        if(Busy())return "DEFERRED: Editor busy; nothing created";
        var before=SharedScenes();var active=SceneManager.GetActiveScene();EnsureFolder(Path.GetDirectoryName(ReviewScene).Replace('\\','/'));
        using(var board=new Board(1500,970,true)){
            Catalog(board,Effects(),Items());
            foreach(var t in board.canvas.GetComponentsInChildren<Transform>(true))t.gameObject.layer=30;
            board.camera.enabled=true;
            // Stored canvas uses the same logical reference size on any game view.
            var scaler=board.canvas.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1500,970);scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            if(!EditorSceneManager.SaveScene(board.scene,ReviewScene))throw new InvalidOperationException("Scene save failed");
        }
        SceneManager.SetActiveScene(active);
        if(!before.SequenceEqual(SharedScenes()))throw new InvalidOperationException("Shared scenes changed");
        foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{MaterialsRoot}))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
        return "PASS: "+ReviewScene+"; 20 original presets + 20 actual item samples";
    }
}
