using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

public static partial class OverburstUIWorkshopBuilder
{
    public const string TooltipPath=Root+"/PF_OverburstTooltip_Rpg11.prefab";
    private static BaseItemData[] TooltipCatalog()=>AssetDatabase.FindAssets("t:BaseItemData",new[]{"Assets/ProjectOverburst"}).Select(g=>AssetDatabase.LoadAssetAtPath<BaseItemData>(AssetDatabase.GUIDToAssetPath(g))).Where(d=>d&&d.icon).ToArray();
    private static TextMeshProUGUI TipText(Transform parent,string name,float x,float y,float width,float height,int size,bool heading=false){
        var r=Rect(name,parent,Vector2.zero,Vector2.one);r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(width,height);
        var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(heading?"Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght SDF.asset":"Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium SDF.asset");t.fontSize=size;t.color=heading?Gold:Ivory;t.raycastTarget=false;t.richText=true;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Overflow;t.lineSpacing=7;return t;
    }
    private static void BuildTooltipAsset(){
        var go=Instance("HUD/Tooltip",null);Clean(go);go.name="PF_OverburstTooltip_Rpg11";
        foreach(var component in go.GetComponents<Component>())if(component is LayoutGroup||component is ContentSizeFitter)Object.DestroyImmediate(component);
        foreach(var child in go.transform.CastChildren())Object.DestroyImmediate(child.gameObject);
        var r=(RectTransform)go.transform;r.localScale=Vector3.one;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(0,1);r.sizeDelta=new Vector2(432,440);r.anchoredPosition=Vector2.zero;
        var background=go.GetComponent<Image>();background.type=UnityEngine.UI.Image.Type.Sliced;background.color=Color.white;
        var surface=Image("Opaque Surface",r,null,new Color(.045f,.041f,.037f,1),Vector2.zero);Stretch(surface.rectTransform);surface.rectTransform.offsetMin=Vector2.one*22;surface.rectTransform.offsetMax=Vector2.one*-22;
        var slot=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SharedSlotPath),r);slot.name="Item Icon";var sr=(RectTransform)slot.transform;sr.anchorMin=sr.anchorMax=sr.pivot=new Vector2(0,1);sr.anchoredPosition=new Vector2(40,-32);
        var title=TipText(r,"Item Name",144,32,248,62,24,true);var subtitle=TipText(r,"Item Type",144,68,248,24,14);subtitle.color=Muted;var tier=TipText(r,"Item Grade",144,98,248,24,14);
        var rule=Image("Header Rule",r,null,RuleColor,new Vector2(352,1));rule.rectTransform.anchorMin=rule.rectTransform.anchorMax=rule.rectTransform.pivot=new Vector2(0,1);rule.rectTransform.anchoredPosition=new Vector2(40,-148);
        var body=TipText(r,"Item Details",40,166,352,300,16);body.lineSpacing=14;
        go.AddComponent<OverburstUITooltipView>().Configure(title,subtitle,tier,body,slot.GetComponent<OverburstUIItemSlotView>(),rule);
        foreach(var graphic in go.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
        var group=go.GetComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
        PrefabUtility.SaveAsPrefabAsset(go,TooltipPath);Object.DestroyImmediate(go);
        OverburstTooltipHybridApplier.ApplyToPrefab(TooltipPath);
    }
    private static GameObject BuildTooltipSamples(Transform parent){
        var root=Rect("Tooltip Samples",parent,Vector2.zero,new Vector2(1920,1080));var catalog=TooltipCatalog();
        for(int i=0;i<3;i++){
            var panel=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TooltipPath),root);panel.name=new[]{"무기 툴팁","물약 툴팁","소비품 툴팁"}[i];
            var data=catalog.FirstOrDefault(d=>d.icon==ItemIcon(new[]{0,1,4}[i]));
            if(!data)throw new System.InvalidOperationException("Tooltip sample item is missing: "+i);
            if(i==0&&!(data is WeaponItemData)||i==1&&!(data is FlaskItemData))
                throw new System.InvalidOperationException("Tooltip sample item type is wrong: "+i);
            var state=Random.state;
            try{
                Random.InitState(1907+i);
                var sample=i==0?new ItemData(data,18,ItemGrade.Rare,1,WeaponElement.Ice):new ItemData(data,18,(ItemGrade)(i+2));
                panel.GetComponent<OverburstUITooltipView>().Present(sample);
            }finally{Random.state=state;}
            ((RectTransform)panel.transform).anchoredPosition=new Vector2(-688+i*472,280);
            foreach(var t in panel.GetComponentsInChildren<Transform>(true)){PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);foreach(var c in t.GetComponents<Component>())if(c)PrefabUtility.RecordPrefabInstancePropertyModifications(c);}
        }
        return root.gameObject;
    }
    private static void BuildTooltipPreview(GameObject canvas,OverburstUIWorkshop workshop){
        BuildTooltipAsset();var root=Rect("80 Item Tooltip Hover",canvas.transform,Vector2.zero,Vector2.zero);Stretch(root);
        var overlay=root.gameObject.AddComponent<Canvas>();overlay.overrideSorting=true;overlay.sortingOrder=32760;
        var panel=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TooltipPath),root);
        root.gameObject.AddComponent<OverburstUITooltipHost>().Configure(panel.GetComponent<OverburstUITooltipView>(),TooltipCatalog());
        workshop.ConfigureTooltips(BuildTooltipSamples(canvas.transform));
    }
    public static string UpgradeTooltips(){
        if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play first");
        font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/05_Art/Fonts/NotoSerifKR-VariableFont_wght.ttf");
        var w=Object.FindFirstObjectByType<OverburstUIWorkshop>();if(!w)throw new System.InvalidOperationException("Open workshop scene");
        var slot=PrefabUtility.LoadPrefabContents(SharedSlotPath);if(!slot.GetComponent<OverburstUISlotTooltip>())slot.AddComponent<OverburstUISlotTooltip>();PrefabUtility.SaveAsPrefabAsset(slot,SharedSlotPath);PrefabUtility.UnloadPrefabContents(slot);
        foreach(var name in new[]{"80 Item Tooltip Hover","Tooltip Samples"}){var old=w.transform.Find(name);if(old)Object.DestroyImmediate(old.gameObject);}
        BuildTooltipPreview(w.gameObject,w);
        var gallery=GameObject.Find(GalleryName);var group=gallery.transform.GetChild(4);var oldBoard=group.Find(BoardNames[13]);if(oldBoard)Object.DestroyImmediate(oldBoard.gameObject);
        var board=Rect(BoardNames[13],group,new Vector2(2120,-5280),new Vector2(1920,1080));board.gameObject.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace;
        Image("Backdrop",board,null,new Color(.04f,.035f,.03f),new Vector2(1920,1080));Label("Board Title",board,new Vector2(0,600),new Vector2(1920,50),BoardNames[13],32,Gold,TextAnchor.MiddleLeft);BuildTooltipSamples(board);
        foreach(var t in board.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=31;t.gameObject.tag="EditorOnly";if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject))PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);}
        w.ShowComparison();UnityEditor.SceneManagement.EditorSceneManager.SaveScene(w.gameObject.scene);AssetDatabase.SaveAssets();return "Tooltip prefab, hover binding and board 14 saved";
    }
}
