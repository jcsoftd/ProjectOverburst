using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class TutorialBuilder
{
    public const string Root = "Assets/ProjectOverburst/Resources/Tutorial";
    public const string UiPath = Root + "/PF_OverburstTutorialUI.prefab";
    public const string ArenaPath = Root + "/PF_DiamondTutorialArena.prefab";
    private static TMP_FontAsset serif, body;
    private const string PendingKey = "Overburst.TutorialBuilder.Pending";
    static TutorialBuilder() { if (SessionState.GetString(PendingKey, "") != "") EditorApplication.update += BuildQueued; }
    public static void Queue(string resultFile, bool keyGuideOnly = false)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        SessionState.SetString(PendingKey, Path.GetFullPath(resultFile));
        SessionState.SetBool(PendingKey+".keyGuideOnly", keyGuideOnly);
        SessionState.SetFloat(PendingKey + ".deadline", (float)EditorApplication.timeSinceStartup + 180);
        EditorApplication.update -= BuildQueued; EditorApplication.update += BuildQueued;
    }
    private static void BuildQueued()
    {
        string path = SessionState.GetString(PendingKey, "");
        if (path == "") { EditorApplication.update -= BuildQueued; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))
        {
            if (EditorApplication.timeSinceStartup < SessionState.GetFloat(PendingKey + ".deadline", 0)) return;
            File.WriteAllText(path, "{\"status\":\"BLOCKED_EDITOR_BUSY\"}");
        }
        else
        {
            try { if(SessionState.GetBool(PendingKey+".keyGuideOnly",false)) BuildKeyGuideOnly(); else Build(); File.WriteAllText(path, "{\"status\":\"PASS\"}"); }
            catch (Exception error) { File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "FAIL", error = error.ToString() })); }
        }
        SessionState.EraseBool(PendingKey+".keyGuideOnly"); SessionState.EraseString(PendingKey); SessionState.EraseFloat(PendingKey + ".deadline"); EditorApplication.update -= BuildQueued;
    }
    [MenuItem("OVERBURST/튜토리얼/사용자 지정 튜토리얼 제작")]
    public static void Build()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        string before = HideoutCatalogLayoutBuilder.EditorSnapshot();
        Folder(Root);
        serif = Font("TutorialSerif", "Assets/ProjectOverburst/Resources/UI/Fonts/Quest/NotoSerifKR_Semibold.ttf");
        body = Font("TutorialBody", "Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject arena = null, ui = null;
        try
        {
            arena = BuildArena(scene);
            var arenaAsset = PrefabUtility.SaveAsPrefabAsset(arena, ArenaPath).GetComponent<TutorialArena>();
            ui = BuildUi(scene, arenaAsset);
            PrefabUtility.SaveAsPrefabAsset(ui, UiPath);
        }
        finally
        {
            if (ui) Object.DestroyImmediate(ui); if (arena) Object.DestroyImmediate(arena);
            EditorSceneManager.ClosePreviewScene(scene);
        }
        if (before != HideoutCatalogLayoutBuilder.EditorSnapshot()) throw new InvalidOperationException("사용자 씬 상태가 변경되었습니다.");
    }
    private static GameObject BuildArena(Scene scene)
    {
        var root = new GameObject("PF_DiamondTutorialArena"); SceneManager.MoveGameObjectToScene(root, scene);
        var arena = root.AddComponent<TutorialArena>();
        arena.definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath("aae5d59e28022e1418398a4e8af7f685"));
        arena.strongAbility = AssetDatabase.LoadAssetAtPath<EnemyAbilityDefinition>(AssetDatabase.GUIDToAssetPath("929d21612a010fb4491e43642b5fb04c"));
        if (!arena.definition || !arena.strongAbility) throw new InvalidOperationException("현재 게임의 훈련 적/패링 가능 강공 자산이 없습니다.");
        var floor = new Mesh { name = "DiamondFloor" };
        floor.vertices = new[] { new Vector3(0,0,12), new Vector3(12,0,0), new Vector3(0,0,-12), new Vector3(-12,0,0) };
        floor.triangles = new[] { 0, 1, 2, 0, 2, 3 }; floor.RecalculateNormals(); floor.RecalculateBounds();
        floor = SaveMesh(floor);
        var floorMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ProjectOverburst/Resources/Enemies/Themes/Arena/Floor.mat");
        arena.skyMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/06_VFX/Vefects/Trails VFX URP/Demo/Resources/Environment/Materials/M_Sky.mat");
        if (!floorMaterial || !arena.skyMaterial || !arena.skyMaterial.shader.isSupported)
            throw new InvalidOperationException("기존 시험장 바닥/하늘 재질이 없습니다.");
        var floorObject = MeshObject(root.transform, "Diamond floor", floor, floorMaterial);
        var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "Platform body"; platform.transform.SetParent(root.transform, false);
        platform.transform.localPosition = new Vector3(0, -.62f, 0);
        platform.transform.localRotation = Quaternion.Euler(0, 45, 0);
        platform.transform.localScale = new Vector3(12 * Mathf.Sqrt(2), 1.2f, 12 * Mathf.Sqrt(2));
        platform.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
        Object.DestroyImmediate(platform.GetComponent<Collider>());
        floorObject.AddComponent<MeshCollider>().sharedMesh = floor;
        int groundLayer = LayerMask.NameToLayer("Ground"); if (groundLayer >= 0) floorObject.layer = groundLayer;
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int n = -12; n <= 12; n += 2)
        {
            Ribbon(new Vector3((n-12)/2f,.014f,(n+12)/2f), new Vector3((n+12)/2f,.014f,(n-12)/2f), .025f, vertices, triangles);
            Ribbon(new Vector3((-12+n)/2f,.014f,(-12-n)/2f), new Vector3((12+n)/2f,.014f,(12-n)/2f), .025f, vertices, triangles);
        }
        var grid = new Mesh { name = "DiamondGrid", vertices = vertices.ToArray(), triangles = triangles.ToArray() }; grid.RecalculateNormals(); grid.RecalculateBounds();
        MeshObject(root.transform, "Diamond cells", SaveMesh(grid), Material("Grid", "#939DA4"));
        Vector3[] corners = { new Vector3(0,0,12), new Vector3(12,0,0), new Vector3(0,0,-12), new Vector3(-12,0,0) };
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = corners[i], b = corners[(i + 1) % 4];
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Boundary " + i; wall.transform.SetParent(root.transform, false);
            wall.transform.localPosition = (a+b)/2 + Vector3.up*1.5f;
            wall.transform.localRotation = Quaternion.LookRotation(b-a);
            wall.transform.localScale = new Vector3(.18f, 3, Vector3.Distance(a,b));
            wall.GetComponent<MeshRenderer>().enabled = false;
            int boundaryLayer = LayerMask.NameToLayer("PlayerBoundary"); if (boundaryLayer >= 0) wall.layer = boundaryLayer;
        }
        arena.playerSpawn = Marker(root.transform, "Player Spawn", new Vector3(-1.6f,.12f,-1.4f));
        arena.moveTarget = Marker(root.transform, "Move Target", new Vector3(2,.03f,-1));
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); ring.name = "Move target disc"; ring.transform.SetParent(arena.moveTarget, false);
        ring.transform.localScale = new Vector3(1.5f,.008f,1.5f); Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.GetComponent<MeshRenderer>().sharedMaterial = Material("Target", "#B49B69");
        arena.targets = new[] { Marker(root.transform, "Target 1", new Vector3(1.6f,.12f,1.1f)), Marker(root.transform, "Target 2", new Vector3(-4,.12f,2)), Marker(root.transform, "Target 3", new Vector3(4,.12f,2)) };
        arena.playerSpawn.localRotation = Quaternion.LookRotation(arena.targets[0].localPosition - arena.playerSpawn.localPosition);
        foreach (var target in arena.targets) target.localRotation = Quaternion.LookRotation(arena.playerSpawn.localPosition - target.localPosition);
        var poolRoot = Marker(root.transform, "Inactive Pool", Vector3.zero); poolRoot.gameObject.SetActive(false);
        arena.pool = root.AddComponent<EnemyPoolService>(); arena.pool.Configure(poolRoot, 0);
        var lightObject = new GameObject("Training Light"); lightObject.transform.SetParent(root.transform, false);
        lightObject.transform.localRotation = Quaternion.Euler(50,-30,0); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.shadows = LightShadows.Soft;
        return root;
    }
    private static GameObject BuildUi(Scene scene, TutorialArena arena)
    {
        var root = new GameObject("PF_OverburstTutorialUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, scene); root.SetActive(false); root.layer = 5;
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 450;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var stage = At(root.transform, "Stage", 0,0,1920,1080); stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(.5f,.5f); stage.anchoredPosition = Vector2.zero;
        var owner = root.AddComponent<OptionalTutorialController>(); var view = root.AddComponent<TutorialView>(); owner.view = view; owner.arenaPrefab = arena;
        var goal = Panel(stage,"Goal",24,24,720,176); view.goal = goal.gameObject;
        view.progress = Text(goal,"Progress","조작 튜토리얼",20,10,680,22,15,false,"#B48D55");
        view.heading = Text(goal,"Heading","완벽 패링",20,34,680,34,26,true,"#EDE3D1");
        view.instruction = Text(goal,"Instruction","",20,74,680,52,18,false,"#EDE3D1");
        view.keys = Text(goal,"Keys","",20,134,680,28,18,false,"#E3CFAF");
        BuildKeyGuide(view,goal);
        view.feedback = Text(goal,"Feedback","",20,174,680,22,15,false,"#B48D55");
        foreach (var label in new[] { view.progress, view.heading, view.instruction, view.keys, view.feedback })
            label.alignment = TextAlignmentOptions.Left;
        view.worldConfirm = Button(goal,"확인했습니다",20,182,250,40,18);
        var tools = At(stage,"Tools",1488,26,402,40); view.tools=tools.gameObject;
        view.retry = Button(tools,"다시 연습",0,0,126,40,17); view.skip = Button(tools,"건너뛰기",132,0,126,40,17); view.exit=Button(tools,"종료",264,0,126,40,17);
        var comparison = Panel(stage,"Parry Comparison",24,216,720,145); view.comparison=comparison.gameObject;
        string[] grades={"불완전","일반","완벽"}, ranges={"30% 미만","30% 이상 ~ 80% 미만","80% 이상"}, effects={"피해 20% 남음 · 약공 반격","피해 없음 · 강화 반격","피해 없음 · 강화 반격 · 기절"};
        view.gradeBackgrounds = new Image[3];
        for(int i=0;i<3;i++)
        {
            var column=At(comparison,"Grade "+i,14+i*230,7,230,94); view.gradeBackgrounds[i]=column.gameObject.AddComponent<Image>(); view.gradeBackgrounds[i].sprite=Resources.Load<Sprite>("UI/HudMenu/Skins/row-hover"); view.gradeBackgrounds[i].raycastTarget=false;
            Text(column,"Grade",grades[i],4,4,222,28,21,true,"#B48D55"); Text(column,"Range",ranges[i],4,35,222,22,15,false,"#EDE3D1"); Text(column,"Effect",effects[i],4,63,222,22,15,false,"#EDE3D1");
        }
        Text(comparison,"Note","강공 시작 때의 게이지로 등급 결정 · 기절은 일반 적 기준",14,111,692,24,14,false,"#EDE3D1");
        var practice=At(stage,"Practice Energy",24,216,720,52); view.practice=practice.gameObject; view.energyButtons=new Button[3];
        for(int i=0;i<3;i++) view.energyButtons[i]=Button(practice,grades[i]+" 준비 "+new[]{"20%","50%","85%"}[i],i*243,0,234,44,18);
        var shade=At(stage,"Modal Shade",0,0,1920,1080).gameObject.AddComponent<Image>(); shade.color=C("#000000B8"); view.shade=shade.gameObject;
        var modal=Panel(stage,"Modal",470,280,980,420); view.modal=modal.gameObject; modal.GetComponent<Image>().raycastTarget=true;
        view.modalTitle=Text(modal,"Title","",48,55,884,60,36,true,"#EDE3D1");
        var separator=At(modal,"Separator",110,132,760,3).gameObject.AddComponent<Image>(); separator.sprite=AssetDatabase.LoadAssetAtPath<Sprite>("Assets/ThirdParty/RPG and MMO UI 11/Textures/Controls/Separators/Separator_Horizontal.png"); separator.color=new Color(1,1,1,.5f); separator.raycastTarget=false;
        view.modalBody=Text(modal,"Body","",48,153,884,82,23,false,"#EDE3D1");
        var chapters=At(modal,"Chapters",48,247,884,160); view.chapters=chapters.gameObject; view.chapterButtons=new Button[7];
        string[] chapterNames={"약공·강공","대시·구르기","대시 공격","불완전 패링","일반 패링","완벽 패링","장비·물약"};
        for(int i=0;i<7;i++)view.chapterButtons[i]=Button(chapters,chapterNames[i],i%3*296,i/3*50,280,42,18);
        var actions=At(modal,"Actions",48,0,884,52); actions.anchorMin=actions.anchorMax=new Vector2(0,0); actions.pivot=new Vector2(0,0); actions.anchoredPosition=new Vector2(48,75);
        view.primary=Button(actions,"처음부터",24,0,260,52,20); view.secondary=Button(actions,"자유 연습",312,0,260,52,20); view.tertiary=Button(actions,"닫기",600,0,260,52,20);
        view.modalNote=Text(modal,"Note","",48,0,884,28,18,false,"#A8A09B"); var note=(RectTransform)view.modalNote.transform; note.anchorMin=note.anchorMax=new Vector2(0,0); note.pivot=new Vector2(0,0); note.anchoredPosition=new Vector2(48,24);
        view.footer=Text(stage,"Footer","훈련 장비·소모품은 임시 제공됩니다. 실제 소유품과 진행은 유지됩니다.",300,1010,1320,28,18,false,"#A8A09B").gameObject;
        view.footer.SetActive(false); root.SetActive(true); return root;
    }
    public static void BuildKeyGuideOnly()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new InvalidOperationException("다른 Editor 실행 준비 중입니다.");
        string before=HideoutCatalogLayoutBuilder.EditorSnapshot();
        var root=PrefabUtility.LoadPrefabContents(UiPath);
        try
        {
            var view=root.GetComponent<TutorialView>();
            if(view.keyGuide) throw new InvalidOperationException("키 가이드가 이미 반영되어 있습니다.");
            body=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/TutorialBody.asset");
            BuildKeyGuide(view,(RectTransform)view.goal.transform);
            view.keys.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,UiPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        if(before!=HideoutCatalogLayoutBuilder.EditorSnapshot())throw new InvalidOperationException("사용자 씬 상태가 변경되었습니다.");
    }
    private static void BuildKeyGuide(TutorialView view, RectTransform goal)
    {
        var row=At(goal,"Key Guide",20,134,680,36);
        row.gameObject.AddComponent<RectMask2D>();
        var guide=row.gameObject.AddComponent<TutorialKeyGuide>(); view.keyGuide=guide;
        guide.keycap=Resources.Load<Sprite>("Tutorial/Keys/Keycap");
        guide.mouseLeft=Resources.Load<Sprite>("Tutorial/Keys/MouseLeft");
        guide.mouseRight=Resources.Load<Sprite>("Tutorial/Keys/MouseRight");
        if(!guide.keycap||!guide.mouseLeft||!guide.mouseRight)throw new InvalidOperationException("승인 키 가이드 이미지가 없습니다.");
        guide.content=At(row,"Tokens",0,0,680,36);
        guide.slots=new RectTransform[8]; guide.icons=new Image[8];
        guide.glyphs=new TMP_Text[8]; guide.captions=new TMP_Text[8];
        for(int i=0;i<8;i++)
        {
            var slot=At(guide.content,"Key "+i,0,0,36,36); guide.slots[i]=slot;
            var icon=At(slot,"Icon",18,0,52,52).gameObject.AddComponent<Image>();
            icon.rectTransform.anchorMin=icon.rectTransform.anchorMax=new Vector2(0,.5f);icon.rectTransform.pivot=new Vector2(.5f,.5f);
            icon.color=Color.white;icon.raycastTarget=false;guide.icons[i]=icon;
            guide.glyphs[i]=Text(slot,"Key Text","",0,0,36,36,16,false,"#EDE3D1");
            guide.captions[i]=Text(slot,"Action","",45,0,200,36,17,false,"#E3CFAF");
            guide.captions[i].alignment=TextAlignmentOptions.Left;
            guide.glyphs[i].textWrappingMode=guide.captions[i].textWrappingMode=TextWrappingModes.NoWrap;
            slot.gameObject.SetActive(false);
        }
    }
    private static RectTransform At(Transform parent,string name,float x,float y,float w,float h)
    { var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);r.gameObject.layer=5;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r; }
    private static RectTransform Stretch(Transform parent,string name)
    {var r=At(parent,name,0,0,0,0);r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;return r;}
    private static RectTransform Panel(Transform p,string name,float x,float y,float w,float h)
    {
        var r=At(p,name,x,y,w,h);
        var hit=r.gameObject.AddComponent<UnityEngine.UI.Image>();hit.color=Color.clear;hit.raycastTarget=false;
        var frame=Stretch(r,"Existing HUD Panel").gameObject.AddComponent<UnityEngine.UI.Image>();
        frame.sprite=Resources.Load<Sprite>("UI/HudMenu/Skins/panel");
        if(!frame.sprite)throw new InvalidOperationException("기존 HUD 메뉴 패널 스킨이 없습니다.");
        frame.type=UnityEngine.UI.Image.Type.Sliced;frame.color=Color.white;frame.raycastTarget=false;
        frame.rectTransform.offsetMin=new Vector2(-24,-24);frame.rectTransform.offsetMax=new Vector2(24,24);
        return r;
    }
    private static TMP_Text Text(Transform p,string name,string value,float x,float y,float w,float h,float size,bool heading,string color)
    {var t=At(p,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();t.font=heading?serif:body;t.text=value;t.fontSize=size;t.color=C(color);t.alignment=TextAlignmentOptions.Center;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=heading?TextOverflowModes.Overflow:TextOverflowModes.Truncate;t.raycastTarget=false;return t;}
    private static Button Button(Transform p,string label,float x,float y,float w,float h,int size)
    {
        var r=At(p,label,x,y,w,h);r.gameObject.AddComponent<Image>().color=Color.clear;
        var fill=Stretch(r,"Reviewed Fill").gameObject.AddComponent<QuestUiGradient>();fill.top=C("#4F2721");fill.bottom=C("#24130F");fill.raycastTarget=false;
        var texture=Stretch(r,"Reviewed Texture").gameObject.AddComponent<Image>();texture.sprite=Resources.Load<Sprite>("UI/Reviewed/simple");texture.type=Image.Type.Tiled;texture.raycastTarget=false;
        var border=Stretch(r,"Reviewed Border").gameObject.AddComponent<Image>();border.sprite=Resources.Load<Sprite>("UI/Reviewed/border");border.type=Image.Type.Sliced;border.pixelsPerUnitMultiplier=2;border.color=new Color(1,1,1,.72f);border.raycastTarget=false;
        if(!texture.sprite||!border.sprite)throw new InvalidOperationException("승인 버튼 자산 누락");
        var b=r.gameObject.AddComponent<Button>();b.targetGraphic=fill;var c=b.colors;c.highlightedColor=new Color(1.2f,1.2f,1.2f);c.pressedColor=new Color(.85f,.85f,.85f);c.selectedColor=new Color(1.2f,1.14f,.95f);b.colors=c;
        r.gameObject.AddComponent<CanvasGroup>();r.gameObject.AddComponent<QuestUiButtonOpacity>();Text(r,"Label",label,6,0,w-12,h,size,true,"#E3CFAF");return b;
    }
    private static TMP_FontAsset Font(string name,string source)
    {
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root+"/"+name+".asset");if(font)return font;
        font=TMP_FontAsset.CreateFontAsset(AssetDatabase.LoadAssetAtPath<Font>(source),96,8,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,false);
        font.name=name;AssetDatabase.CreateAsset(font,Root+"/"+name+".asset");foreach(var t in font.atlasTextures)AssetDatabase.AddObjectToAsset(t,font);AssetDatabase.AddObjectToAsset(font.material,font);
        string glyphs=string.Join("",Directory.GetFiles("Assets/ProjectOverburst/03_Features/Tutorial/Runtime","*.cs").Select(File.ReadAllText))+File.ReadAllText("Assets/Editor/Builders/TutorialBuilder.cs")+"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789[]()%· →←↑↓~";
        font.TryAddCharacters(new string(glyphs.Where(c=>!char.IsControl(c)).Distinct().ToArray()));font.atlasPopulationMode=AtlasPopulationMode.Static;EditorUtility.SetDirty(font);AssetDatabase.SaveAssetIfDirty(font);return font;
    }
    private static Material Material(string name,string color)
    {string path=Root+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(m,path);}m.SetColor("_BaseColor",C(color));EditorUtility.SetDirty(m);AssetDatabase.SaveAssetIfDirty(m);return m;}
    private static Mesh SaveMesh(Mesh mesh)
    {string path=Root+"/"+mesh.name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);Object.DestroyImmediate(mesh);EditorUtility.SetDirty(old);AssetDatabase.SaveAssetIfDirty(old);return old;}AssetDatabase.CreateAsset(mesh,path);return mesh;}
    private static GameObject MeshObject(Transform parent,string name,Mesh mesh,Material material)
    {var go=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));go.transform.SetParent(parent,false);go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=material;return go;}
    private static Transform Marker(Transform parent,string name,Vector3 position)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=position;return go.transform;}
    private static void Ribbon(Vector3 a,Vector3 b,float width,List<Vector3> v,List<int> t)
    {Vector3 side=Vector3.Cross((b-a).normalized,Vector3.up)*width*.5f;int n=v.Count;v.Add(a-side);v.Add(a+side);v.Add(b+side);v.Add(b-side);t.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});}
    private static Color C(string value){ColorUtility.TryParseHtmlString(value,out var c);return c;}
    private static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');Folder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));}
}
