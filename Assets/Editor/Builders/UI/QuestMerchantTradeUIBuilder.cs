using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using B = QuestMerchantUIBuilder;

public static class QuestMerchantTradeUIBuilder
{
    private static readonly List<OverburstUIShopSkin.Label> labels=new List<OverburstUIShopSkin.Label>();
    public static string Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)throw new InvalidOperationException("Idle editor required.");
        string path=OverburstShopUIBuilder.PrefabPath;
        var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var prior=OverburstShopUIBuilder.SlotIdentities(asset);
        string evidence=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/Implementation/20261011_QuestMerchant"));
        string backup=Path.Combine(evidence,"Before/G6",path);Directory.CreateDirectory(Path.GetDirectoryName(backup));
        if(!File.Exists(backup)){File.Copy(path,backup);File.Copy(path+".meta",backup+".meta");}
        var root=PrefabUtility.LoadPrefabContents(path);
        try
        {
            var skin=root.GetComponent<OverburstUIShopSkin>();var serialized=new SerializedObject(skin);
            var old=serialized.FindProperty("labels");labels.Clear();
            for(int i=0;i<old.arraySize;i++){var item=old.GetArrayElementAtIndex(i);labels.Add(new OverburstUIShopSkin.Label{source=item.FindPropertyRelative("source").objectReferenceValue as TMP_Text,target=item.FindPropertyRelative("target").objectReferenceValue as Text});}
            root.transform.Find("Approved Tabs").gameObject.SetActive(false);
            var window=root.transform.Find("MerchantInventoryWindow");
            Hide(window.Find("Stock Heading"));Hide(window.Find("Stock Rule"));
            var top=window.Find("TopPanel");
            B.Rect(top.parent,top.name,0,0,608,328);
            var info=top.Find("MerchantInfoBox/ReputationInfoPanel");
            B.Rect(info.parent,info.name,0,0,608,328);B.Rect(info.parent.parent,info.parent.name,0,0,608,328);
            Hide(info.Find("ReputationStockGradeText"));Hide(info.Find("ReputationEffectsTitleText"));Hide(info.Find("Divider"));
            var photoBorder=B.SpriteRect(window,"Merchant Portrait Border",32,97,118,176,"e4a991a559ef25445ae21424b7a86668",Color.white,3.6f);
            var photoRect=B.Rect(window,"Merchant Portrait",37,102,108,166);
            var portrait=photoRect.GetComponent<RawImage>() ?? photoRect.gameObject.AddComponent<RawImage>();
            portrait.material=AssetDatabase.LoadAssetAtPath<Material>(B.Folder+"/MerchantPortrait.mat");portrait.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(B.Folder+"/WeaponMerchantPortrait.png");portrait.raycastTarget=false;
            portrait.uvRect=new Rect(0,0,1,1);
            var fade=B.Rect(photoRect,"Fade",0,91.3f,108,74.7f);var gradient=fade.GetComponent<QuestUiGradient>() ?? fade.gameObject.AddComponent<QuestUiGradient>();gradient.top=new Color(.035f,.035f,.024f,0);gradient.bottom=B.C("090906B8");gradient.raycastTarget=false;
            Mirror(top.Find("MerchantNameText"),window,"Merchant Name",173,99,320,33.8f,26,"D7B477");
            Mirror(top.Find("DescriptionText"),window,"Merchant Location",173,140.8f,403,22,15,"A99F8B");
            var category=top.Find("PortraitCategoryText");
            B.Solid(window,"Category Fill",532,104,44,25,B.C("211A11"));Outline(window,"Category Border",532,104,44,25,B.C("4D432E"));
            Mirror(category,window,"Merchant Category",532,104,44,25,13,"948872",false,TextAnchor.MiddleCenter);
            B.Rule(window,"Identity Rule",173,175,403);
            Mirror(info.Find("ReputationLevelText"),window,"Reputation Level",173,194,260,24,17,"CFB077");
            B.Solid(window,"Grade Fill",529,194,47,21,B.C("151411"));Outline(window,"Grade Border",529,194,47,21,B.C("4B4232"));
            Mirror(info.Find("ReputationGradeText"),window,"Reputation Grade",529,194,47,21,12,"A99B80",false,TextAnchor.MiddleCenter);
            var gauge=info.Find("ReputationExpBar") ?? window.Find("ReputationExpBar");gauge.SetParent(window,false);B.Rect(window,gauge.name,173,228.7f,403,4);
            gauge.GetComponent<Image>().color=B.C("28291E");gauge.Find("Fill").GetComponent<Image>().color=B.C("AD8850");
            B.Label(window,"Reputation Caption",173,243.7f,210,18,"우호도 경험치",12,B.C("7A705C"));
            Mirror(info.Find("ExpPercentText"),window,"Reputation Experience",383,242.7f,193,20,13,"C0B294",false,TextAnchor.MiddleRight,true);
            var viewport=window.Find("Stock Viewport") as RectTransform;B.Rect(window,viewport.name,32,328,544,360);
            var track=window.Find("Stock Viewport Scrollbar");B.Rect(window,track.name,588,328,4,360);
            B.Solid(window,"Wallet Fill",24,714,568,46,B.C("0C0D0C"));B.Rule(window,"Wallet Rule",24,714,568);
            B.Label(window,"Discount Caption",32,730,45,24,"할인율",14,B.C("A99F89"));
            Mirror(info.Find("ReputationDiscountText"),window,"Discount Value",84,730,100,24,18,"D9C08B",false,TextAnchor.MiddleLeft,true);
            B.Label(window,"Gold Caption",350,730,121,24,"상인 보유 골드",14,B.C("A99F89"));
            B.SpriteRect(window,"Gold Coin",478,731.5f,19,19,"7c19bcb2e2aa3234da6abac993ba4688",Color.white,1,false);
            Mirror(info.Find("MerchantGoldInfoText"),window,"Merchant Wallet",506,728,78,26,20,"D9C08B",false,TextAnchor.MiddleRight,true);
            Wallet(window,window.Find("Merchant Wallet").GetComponent<Text>(),584,true);
            var trade=root.transform.Find("TradeWindow");
            StyleButton(trade.Find("ConfirmButton"),32,632,384,56,true);
            StyleButton(trade.Find("ClearButton"),32,704,186,48,false);
            StyleButton(trade.Find("CloseButton"),230,704,186,48,false);
            foreach(var header in root.GetComponentsInChildren<Text>(true).Where(t=>t.name=="Text" && t.transform.parent.name=="Header"))header.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/Quest/NotoSerifKR_Semibold.ttf");
            foreach(var image in root.GetComponentsInChildren<Image>(true).Where(i=>i.transform.IsChildOf(window.Find("Window Chrome")) || i.transform.IsChildOf(trade.Find("Window Chrome"))))
                ApplyTint(image);
            labels.RemoveAll(l=>!l.source || !l.target);
            old.arraySize=labels.Count;
            for(int i=0;i<labels.Count;i++){var item=old.GetArrayElementAtIndex(i);item.FindPropertyRelative("source").objectReferenceValue=labels[i].source;item.FindPropertyRelative("target").objectReferenceValue=labels[i].target;}
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if(!PrefabUtility.SaveAsPrefabAsset(root,path))throw new InvalidOperationException("Shop prefab save failed.");
        }
        finally{PrefabUtility.UnloadPrefabContents(root);labels.Clear();}
        var after=OverburstShopUIBuilder.SlotIdentities(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        if(!prior.OrderBy(x=>x.Key).SequenceEqual(after.OrderBy(x=>x.Key)))throw new InvalidOperationException("Existing slot identities changed.");
        InventoryStyle();
        string result=Path.Combine(evidence,"Evidence/trade-ui-assets.json");File.WriteAllText(result,JsonConvert.SerializeObject(new{status="BUILT",slotIdentitiesPreserved=true,slots=after.Count,stock=new{x=32,y=328,w=544,h=360},wallet=new{y=714,h=46}},Formatting.Indented));return result;
    }
    private static void InventoryStyle()
    {
        string path=B.Folder+"/PF_QuestInventoryStyle.prefab";
        bool existing=AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var root=existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject("Quest Inventory Style",typeof(RectTransform));
        try
        {
            var rect=(RectTransform)root.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            var view=root.GetComponent<QuestInventoryStyle>() ?? root.AddComponent<QuestInventoryStyle>();
            var layout=B.Rect(root.transform,"Layout",0,0,608,776);layout.localScale=Vector3.one*2;
            B.Solid(layout,"Toolbar Fill",32,90,544,42,B.C("0C0D0C"));
            view.modeHost=Control(layout,"Sort Mode",32,154,"등급순  ▾",out view.modeLabel);
            view.directionHost=Control(layout,"Sort Direction",194,90,"정렬 ▼",out view.directionLabel);
            view.equipmentHost=Control(layout,"Equipment",292,132,"장비창 열기",out view.equipmentLabel);
            B.Solid(layout,"Wallet Fill",24,714,560,46,B.C("0C0D0C"));B.Rule(layout,"Wallet Rule",24,714,560);
            B.Label(layout,"Gold Caption",32,730,220,24,"보유 골드",14,B.C("A99F89"));
            B.SpriteRect(layout,"Gold Coin",480,731.5f,19,19,"7c19bcb2e2aa3234da6abac993ba4688",Color.white,1,false);
            view.gold=B.Label(layout,"Gold",504,728,72,26,"55 <size=13>G</size>",20,B.C("D9C08B"));NormalScale(view.gold,72,26,20);view.gold.text="55 <size=13>G</size>";view.gold.alignment=TextAnchor.MiddleRight;
            Wallet(layout,view.gold,576,false);
            view.bodyFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");
            if(!PrefabUtility.SaveAsPrefabAsset(root,path))throw new InvalidOperationException("Inventory style save failed.");
        }
        finally{if(existing)PrefabUtility.UnloadPrefabContents(root);else UnityEngine.Object.DestroyImmediate(root);}
    }
    private static void Wallet(Transform root,Text value,float right,bool moveCaption)
    {
        var wallet=root.GetComponent<QuestUiWalletLayout>() ?? root.gameObject.AddComponent<QuestUiWalletLayout>();
        wallet.value=value;wallet.coin=(RectTransform)root.Find("Gold Coin");wallet.rightEdge=right;
        wallet.caption=moveCaption ? root.Find("Gold Caption").GetComponent<Text>() : null;wallet.Refresh();
    }
    private static RectTransform Control(Transform parent,string name,float x,float width,string text,out Text label)
    {
        var rect=B.Rect(parent,name,x,90,width,36);var action=B.Action(rect,"Artwork",0,0,width,36,text,false,out label);action.enabled=false;foreach(var graphic in action.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;label.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");label.fontSize=28;
        if(name=="Sort Mode"){label.alignment=TextAnchor.MiddleLeft;label.rectTransform.anchoredPosition=new Vector2(14,0);label.rectTransform.sizeDelta=new Vector2((width-40)*2,72);}
        var host=B.Rect(rect,"Native Control",0,0,width,36);return host;
    }
    private static void Mirror(Transform source,Transform parent,string name,float x,float y,float w,float h,int size,string color,bool title=false,TextAnchor align=TextAnchor.UpperLeft,bool normal=false)
    {
        if(!source)throw new InvalidOperationException("Missing source for "+name);
        var tmp=source.GetComponent<TMP_Text>();tmp.enabled=false;
        var target=B.Label(parent,name,x,y,w,h,tmp.text,size,B.C(color),title);target.alignment=align;
        if(normal){NormalScale(target,w,h,size);target.text=tmp.text;}
        foreach(var old in source.GetComponentsInChildren<Text>(true))old.enabled=false;
        labels.RemoveAll(l=>l.source==tmp);labels.Add(new OverburstUIShopSkin.Label{source=tmp,target=target});
    }
    private static void NormalScale(Text text,float w,float h,int size){text.rectTransform.localScale=Vector3.one;text.rectTransform.sizeDelta=new Vector2(w,h);text.fontSize=size;}
    private static void StyleButton(Transform transform,float x,float y,float w,float h,bool primary)
    {
        B.Rect(transform.parent,transform.name,x,y,w,h);
        foreach(Transform child in transform)if(child.name!="Quest Artwork")child.gameObject.SetActive(false);
        var original=transform.GetComponent<Button>();var originalImage=transform.GetComponent<Image>();if(originalImage)originalImage.color=Color.clear;
        var artwork=B.Action(transform,"Quest Artwork",0,0,w,h,primary ? "거래 확정" : transform.name=="ClearButton" ? "비우기" : "닫기",primary,out _);
        artwork.enabled=false;foreach(var graphic in artwork.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
        originalImage.raycastTarget=true;original.targetGraphic=artwork.targetGraphic;original.colors=artwork.colors;
    }
    private static void Hide(Transform t){if(t)t.gameObject.SetActive(false);}
    private static void Outline(Transform p,string n,float x,float y,float w,float h,Color c){B.Solid(p,n+" Top",x,y,w,1,c);B.Solid(p,n+" Bottom",x,y+h-1,w,1,c);B.Solid(p,n+" Left",x,y,1,h,c);B.Solid(p,n+" Right",x+w-1,y,1,h,c);}
    public static void ApplyTint(Image image){var c=image.color;if(image.sprite && (c.r!=1 || c.g!=1 || c.b!=1)){image.material=B.TintMaterial(c);image.color=new Color(1,1,1,c.a);}}
}
