using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstHudMenuBuilder
{
    public const string PrefabPath = "Assets/ProjectOverburst/Resources/UI/HudMenu/PF_OverburstHudMenu_Rpg11.prefab";
    public const string Icons = "Assets/ProjectOverburst/Resources/UI/HudMenu/Icons/";
    const string Skins = "Assets/ProjectOverburst/Resources/UI/HudMenu/Skins/";
    static Color Hex(string hex){ColorUtility.TryParseHtmlString(hex,out var color);return color;}
    static readonly Color Gold = new Color32(180,141,85,255), Ivory = new Color32(237,227,209,255);
    static Font font;
    [MenuItem("OVERBURST/UI/Build Vertical HUD Menu")]
    public static void BuildMenu() => Build();
    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required.");
        font = AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
        if (!font) throw new InvalidOperationException("Existing Korean UI font required.");
        foreach (var name in new[]{"menu","close","inventory","equipment","skill-tree","settings"})
        {
            Import(Icons+name+".png",128,Vector4.zero);Import(Icons+name+"-hover.png",128,Vector4.zero);
        }
        foreach(var name in new[]{"trigger-normal","trigger-hover","trigger-open","panel","row-hover","row-pressed"})Import(Skins+name+".png",512,name=="panel"?new Vector4(28,28,28,28):Vector4.zero);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("PF_OverburstHudMenu_Rpg11",typeof(RectTransform),typeof(Canvas),typeof(GraphicRaycaster));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        root.SetActive(false); root.layer = 5; Stretch((RectTransform)root.transform);
        try
        {
            var ui = root.AddComponent<OverburstHudMenu>(); ui.canvas = root.GetComponent<Canvas>();
            ui.canvas.overrideSorting = true; ui.canvas.sortingOrder = 210;
            ui.outside = Rect(root.transform,"Outside Click",0,0,1920,1080).gameObject.AddComponent<Button>();
            Stretch((RectTransform)ui.outside.transform); var hit = ui.outside.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true; ui.outside.targetGraphic=hit; ui.outside.transition=Selectable.Transition.None;
            ui.dock = Rect(root.transform,"Corner Dock",0,0,58,58); ui.dock.anchorMin=ui.dock.anchorMax=ui.dock.pivot=new Vector2(1,0); ui.dock.anchoredPosition=new Vector2(-24,24);
            ui.popup = Rect(ui.dock,"Popup",0,0,58,58).gameObject.AddComponent<CanvasGroup>(); Stretch((RectTransform)ui.popup.transform);
            ui.panel = Rect(ui.popup.transform,"Vertical Menu",0,0,244,226);ui.panel.anchorMin=ui.panel.anchorMax=new Vector2(1,0);ui.panel.pivot=new Vector2(1,0);ui.panel.anchoredPosition=new Vector2(0,70);
            var surface=Image(ui.panel,"Approved Panel Skin",Skin("panel"),Color.white,0,0,292,274,true);surface.type=UnityEngine.UI.Image.Type.Sliced;surface.rectTransform.offsetMin=new Vector2(-24,-24);surface.rectTransform.offsetMax=new Vector2(24,24);
            Diamond(ui.panel,"Top Diamond",new Vector2(.5f,1),new Vector2(0,0),6);
            Diamond(ui.panel,"Bottom Diamond",new Vector2(.5f,0),new Vector2(0,0),6);
            ui.viewport = Rect(ui.panel,"List Viewport",9,9,226,208); ui.viewport.anchorMin=Vector2.zero;ui.viewport.anchorMax=Vector2.one;ui.viewport.offsetMin=new Vector2(9,9);ui.viewport.offsetMax=new Vector2(-9,-9);
            var viewHit=ui.viewport.gameObject.AddComponent<Image>();viewHit.color=Color.clear;viewHit.raycastTarget=true;ui.viewport.gameObject.AddComponent<RectMask2D>();
            ui.content=Rect(ui.viewport,"Menu Rows",0,0,226,208);
            ui.scroll=ui.viewport.gameObject.AddComponent<ScrollRect>();ui.scroll.viewport=ui.viewport;ui.scroll.content=ui.content;ui.scroll.horizontal=false;ui.scroll.vertical=true;ui.scroll.movementType=ScrollRect.MovementType.Clamped;ui.scroll.inertia=false;ui.scroll.scrollSensitivity=32;
            var track=Rect(ui.panel,"Scroll Track",0,0,3,208);track.anchorMin=new Vector2(1,0);track.anchorMax=new Vector2(1,1);track.offsetMin=new Vector2(-7,9);track.offsetMax=new Vector2(-4,-9);track.gameObject.AddComponent<Image>().color=new Color(.21f,.17f,.12f,.3f);
            var thumb=Rect(track,"Scroll Thumb",0,0,3,40);Stretch(thumb);var thumbImage=thumb.gameObject.AddComponent<Image>();thumbImage.color=new Color(.64f,.50f,.31f);
            var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=thumb;bar.targetGraphic=thumbImage;bar.direction=Scrollbar.Direction.BottomToTop;ui.scroll.verticalScrollbar=bar;ui.scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            ui.trigger=Rect(ui.dock,"Menu Button",0,0,58,58).gameObject.AddComponent<Button>();Stretch((RectTransform)ui.trigger.transform);
            Hit(ui.trigger);var triggerBg=Image(ui.trigger.transform,"Approved Trigger Skin",Skin("trigger-normal"),Color.white,0,0,82,82,true);triggerBg.rectTransform.offsetMin=new Vector2(-12,-12);triggerBg.rectTransform.offsetMax=new Vector2(12,12);
            ui.menuIcon=Glyph("menu");ui.closeIcon=Glyph("close");ui.triggerIcon=Image(ui.trigger.transform,"Menu Glyph",ui.menuIcon,Color.white,14,14,30,30);ui.triggerIcon.preserveAspect=true;
            Diamond((RectTransform)ui.trigger.transform,"Button Diamond",new Vector2(.5f,0),new Vector2(0,5),4);
            ui.triggerVisual=ui.trigger.gameObject.AddComponent<OverburstHudMenuTrigger>();ui.triggerVisual.owner=ui;ui.triggerVisual.background=triggerBg;ui.triggerVisual.normal=Skin("trigger-normal");ui.triggerVisual.hover=Skin("trigger-hover");ui.triggerVisual.expanded=Skin("trigger-open");ui.triggerVisual.menuHover=Glyph("menu-hover");ui.triggerVisual.closeHover=Glyph("close-hover");
            ui.rowTemplate=Row(root.transform);ui.rowTemplate.gameObject.SetActive(false);
            var labels=new[]{"인벤토리","장비","스킬트리","설정"};var icons=new[]{"inventory","equipment","skill-tree","settings"};
            ui.entries=new OverburstHudMenu.Entry[4];for(int i=0;i<4;i++)ui.entries[i]=new OverburstHudMenu.Entry{label=labels[i],icon=Glyph(icons[i]),hoverIcon=Glyph(icons[i]+"-hover"),destination=(OverburstHudMenu.Destination)i};
            ui.outside.gameObject.SetActive(false);ui.popup.alpha=0;ui.popup.interactable=ui.popup.blocksRaycasts=false;ui.popup.gameObject.SetActive(false);
            root.SetActive(true);
            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAsset(root,PrefabPath,out bool saved);if(!saved)throw new InvalidOperationException("HUD menu prefab save failed.");
            return PrefabPath;
        }
        finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
    static OverburstHudMenuItem Row(Transform parent)
    {
        var rect=Rect(parent,"Menu Row Template",0,0,226,52);var row=rect.gameObject.AddComponent<OverburstHudMenuItem>();
        row.button=rect.gameObject.AddComponent<Button>();Hit(row.button);row.hoverBackground=Skin("row-hover");row.pressedBackground=Skin("row-pressed");row.highlight=Image(rect,"Approved Hover",row.hoverBackground,Color.white,0,0,226,52,true);row.highlight.enabled=false;
        row.icon=Image(rect,"Icon",null,Color.white,15,12,28,28);row.icon.preserveAspect=true;
        row.label=Label(rect,"Name","인벤토리",59,0,140,52,15,Ivory);row.arrow=Label(rect,"Open Mark","›",200,0,14,52,19,Hex("#9c7a4d"));row.arrow.alignment=TextAnchor.MiddleCenter;
        row.arrow.enabled=false;
        foreach(int direction in new[]{-1,1}){var line=Image(row.arrow.transform,"Approved Chevron "+direction,null,Hex("#9c7a4d"),0,0,Mathf.Sqrt(50),1.2f).rectTransform;line.pivot=new Vector2(.5f,.5f);line.anchoredPosition=new Vector2(3.5f,-26+direction*2.5f);line.localRotation=Quaternion.Euler(0,0,-direction*45);}
        row.separator=Image(rect,"Separator",null,Hex("#806a4936"),15,0,196,1);
        return row;
    }
    static Sprite Glyph(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(Icons+name+".png");
    static Sprite Skin(string name)=>AssetDatabase.LoadAssetAtPath<Sprite>(Skins+name+".png");
    static void Import(string path,int size,Vector4 border)
    {AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var importer=AssetImporter.GetAtPath(path) as TextureImporter;if(!importer)throw new InvalidOperationException("Missing approved menu texture: "+path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.spriteBorder=border;importer.mipmapEnabled=false;importer.alphaIsTransparency=true;importer.filterMode=FilterMode.Bilinear;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.maxTextureSize=size;importer.SaveAndReimport();}
    static void Hit(Button b){var i=b.gameObject.AddComponent<Image>();i.color=Color.clear;i.raycastTarget=true;b.targetGraphic=i;b.transition=Selectable.Transition.None;}
    static RectTransform Rect(Transform p,string name,float x,float y,float w,float h)
    {var go=new GameObject(name,typeof(RectTransform));go.layer=5;var r=(RectTransform)go.transform;r.SetParent(p,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);return r;}
    static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    static Image Image(Transform p,string name,Sprite s,Color c,float x,float y,float w,float h,bool stretch=false)
    {var r=Rect(p,name,x,y,w,h);if(stretch)Stretch(r);var i=r.gameObject.AddComponent<Image>();i.sprite=s;i.color=c;i.raycastTarget=false;return i;}
    static Text Label(Transform p,string name,string value,float x,float y,float w,float h,int size,Color c)
    {var r=Rect(p,name,x,y,w,h);var t=r.gameObject.AddComponent<Text>();t.font=font;t.text=value;t.fontSize=size;t.color=c;t.alignment=TextAnchor.MiddleLeft;t.raycastTarget=false;t.horizontalOverflow=HorizontalWrapMode.Overflow;t.verticalOverflow=VerticalWrapMode.Truncate;return t;}
    static void Colors(Button b,Color normal,Color hover,Color pressed)
    {var colors=b.colors;colors.normalColor=normal;colors.highlightedColor=colors.selectedColor=hover;colors.pressedColor=pressed;colors.disabledColor=new Color(.2f,.18f,.15f,.5f);colors.colorMultiplier=1;colors.fadeDuration=.09f;b.colors=colors;}
    static void Frame(RectTransform p,float inset,Color c)
    {
        foreach(var edge in new[]{"Top","Bottom","Left","Right"})
        {var i=Image(p,"Frame "+inset+" "+edge,null,c,0,0,1,1);var r=i.rectTransform;
            if(edge=="Top"||edge=="Bottom"){r.anchorMin=new Vector2(0,edge=="Top"?1:0);r.anchorMax=new Vector2(1,r.anchorMin.y);r.pivot=new Vector2(.5f,edge=="Top"?1:0);r.offsetMin=new Vector2(inset,edge=="Top"?-inset-1:inset);r.offsetMax=new Vector2(-inset,edge=="Top"?-inset:inset+1);}
            else{r.anchorMin=new Vector2(edge=="Right"?1:0,0);r.anchorMax=new Vector2(r.anchorMin.x,1);r.pivot=new Vector2(edge=="Right"?1:0,.5f);r.offsetMin=new Vector2(edge=="Right"?-inset-1:inset,inset);r.offsetMax=new Vector2(edge=="Right"?-inset:inset+1,-inset);}
        }
    }
    static void Diamond(RectTransform p,string name,Vector2 anchor,Vector2 position,float size)
    {var i=Image(p,name,null,name=="Button Diamond"?Hex("#967246"):Gold,0,0,size,size);var r=i.rectTransform;r.anchorMin=r.anchorMax=anchor;r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.localRotation=Quaternion.Euler(0,0,45);if(name!="Button Diamond"){var inner=Image(r,"Dark Center",null,Hex("#20160f"),0,0,size-2,size-2);inner.rectTransform.anchorMin=inner.rectTransform.anchorMax=inner.rectTransform.pivot=new Vector2(.5f,.5f);inner.rectTransform.anchoredPosition=Vector2.zero;}}
}
