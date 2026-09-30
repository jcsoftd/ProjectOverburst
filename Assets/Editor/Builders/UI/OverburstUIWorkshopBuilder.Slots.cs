using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

public static partial class OverburstUIWorkshopBuilder
{
    public const float UnifiedSlotSize=84;
    public const string SharedSlotPath=Root+"/Slots/PF_OverburstItemSlot_Rpg11.prefab";
    private static GameObject sharedSlot;
    private static void BuildSharedSlot()
    {
        // 2026-10-01: 게임 공용 아이템 칸 프리팹은 만든 뒤 조정값(등급·효과 연결)이 원본이다. 이미 있으면 다시 만들지 않는다.
        var existingSlot=AssetDatabase.LoadAssetAtPath<GameObject>(SharedSlotPath);if(existingSlot!=null){sharedSlot=existingSlot;return;}
        System.IO.Directory.CreateDirectory(Root+"/Slots");AssetDatabase.Refresh();
        var root=Rect("PF_OverburstItemSlot_Rpg11",null,Vector2.zero,Vector2.one*UnifiedSlotSize);
        var hit=root.gameObject.AddComponent<Image>();hit.color=Color.clear;hit.raycastTarget=true;
        var visual=Instance("HUD/Icon Slots/Spell Slot (Action Bar)",root);Clean(visual);visual.name="Vendor Frame";
        var vr=(RectTransform)visual.transform;vr.anchorMin=vr.anchorMax=vr.pivot=new Vector2(.5f,.5f);vr.anchoredPosition=Vector2.zero;vr.sizeDelta=Vector2.one*144;vr.localScale=Vector3.one*(UnifiedSlotSize/144f);
        Off(visual,"Icon");Off(visual,"Hotkey");
        var icon=Image("Icon",root,null,Color.white,Vector2.zero);Stretch(icon.rectTransform);icon.rectTransform.offsetMin=new Vector2(8,14);icon.rectTransform.offsetMax=new Vector2(-8,-8);icon.preserveAspect=true;icon.gameObject.AddComponent<OverburstUIIconFraming>().Configure(BuildIconFraming());icon.gameObject.SetActive(false);
        var empty=Image("Slot Icon",root,null,new Color(.65f,.61f,.53f,.85f),Vector2.zero);Stretch(empty.rectTransform);empty.rectTransform.offsetMin=Vector2.one*18;empty.rectTransform.offsetMax=Vector2.one*-18;empty.preserveAspect=true;empty.gameObject.SetActive(false);
        var hotkey=Rect("Hotkey",root,Vector2.zero,Vector2.zero);Stretch(hotkey);hotkey.offsetMin=new Vector2(10,6);hotkey.offsetMax=new Vector2(-10,-58);
        var label=Label("Hotkey Text",hotkey,Vector2.zero,Vector2.zero,"",13,Ivory,TextAnchor.MiddleRight);Stretch(label.rectTransform);label.font=bodyFont;
        AttachGrade(root,ItemGrade.Common);
        hotkey.SetAsLastSibling();
        root.gameObject.AddComponent<OverburstUIItemSlotView>().Configure(icon,empty,label,root.GetComponent<OverburstUISlotGradePreview>());
        root.gameObject.AddComponent<OverburstUISlotTooltip>();
        sharedSlot=PrefabUtility.SaveAsPrefabAsset(root.gameObject,SharedSlotPath);Object.DestroyImmediate(root.gameObject);
    }
    private static OverburstUIIconFraming.Artwork[] BuildIconFraming()
    {
        var paths=AssetDatabase.FindAssets("t:Sprite",new[]{"Assets/ProjectOverburst/03_Features/Items/Art/Icons/Flasks","Assets/ProjectOverburst/05_Art/UI/Icons/Consumables"}).Select(AssetDatabase.GUIDToAssetPath).Distinct();
        return paths.Select(path=>{
            var texture=new Texture2D(2,2);texture.LoadImage(System.IO.File.ReadAllBytes(path));var pixels=texture.GetPixels32();int w=texture.width,h=texture.height,minX=w,minY=h,maxX=0,maxY=0;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(pixels[y*w+x].a>32){minX=Mathf.Min(minX,x);maxX=Mathf.Max(maxX,x);minY=Mathf.Min(minY,y);maxY=Mathf.Max(maxY,y);}
            Object.DestroyImmediate(texture);return new OverburstUIIconFraming.Artwork{sprite=Sprite(path),bounds=new Rect((float)minX/w,(float)minY/h,(float)(maxX-minX+1)/w,(float)(maxY-minY+1)/h)};
        }).ToArray();
    }
    private static GameObject SharedSlot(Transform parent,string name,Sprite icon,ItemGrade grade,string key="")
    {
        var go=(GameObject)PrefabUtility.InstantiatePrefab(sharedSlot,parent);go.name=name;go.GetComponent<OverburstUIItemSlotView>().Present(icon,grade,key);return go;
    }
    private static Transform SharedGrid(Transform oldGrid,int columns,float x,float y,int filled)
    {
        var owner=oldGrid.parent;var gridName=oldGrid.name;Object.DestroyImmediate(oldGrid.gameObject);
        var grid=Rect(gridName,owner,Vector2.zero,Vector2.one);grid.gameObject.AddComponent<GridLayoutGroup>();Box(grid,x,y,columns*84+(columns-1)*8,636);grid.localScale=Vector3.one*2;grid.sizeDelta=new Vector2(columns*84+(columns-1)*8,636);
        var layout=grid.GetComponent<GridLayoutGroup>();layout.cellSize=Vector2.one*84;layout.spacing=Vector2.one*8;layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=columns;layout.padding=new RectOffset();layout.childAlignment=TextAnchor.UpperLeft;
        for(int i=0;i<columns*7;i++)SharedSlot(grid,"Slot "+(i+1).ToString("00"),i<filled?ItemIcon(i):null,(ItemGrade)(i%8));
        // Six visible rows; the seventh remains available by scrolling. The original capacity is preserved.
        var parent=grid.parent;var viewport=Panel(parent,"Slot Viewport",x,y,columns*84+(columns-1)*8,544);viewport.gameObject.AddComponent<RectMask2D>();
        grid.SetParent(viewport,false);grid.anchorMin=grid.anchorMax=grid.pivot=new Vector2(0,1);grid.anchoredPosition=Vector2.zero;grid.localScale=Vector3.one*2;
        var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=grid;scroll.horizontal=false;scroll.vertical=true;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=42;scroll.inertia=true;
        var mouse=viewport.gameObject.AddComponent<Image>();mouse.color=Color.clear;mouse.raycastTarget=true;
        var track=Image("Scrollbar",parent,null,new Color(.25f,.23f,.19f,.45f),Vector2.one);Box(track.rectTransform,x+columns*84+(columns-1)*8+12,y,4,544);track.raycastTarget=true;
        var handle=Image("Handle",track.transform,null,new Color(.60f,.48f,.30f,.85f),Vector2.zero);Stretch(handle.rectTransform);handle.raycastTarget=true;
        var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle.rectTransform;bar.targetGraphic=handle;bar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;bar.SetValueWithoutNotify(1);
        return grid;
    }
}
internal static class OverburstUITransformSnapshot
{
    public static Transform[] CastChildren(this Transform transform){var children=new Transform[transform.childCount];for(int i=0;i<children.Length;i++)children[i]=transform.GetChild(i);return children;}
}
