using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static class QuestMerchantWorkshopBuilder
{
    public const string GroupName="08_QuestsMerchant • 상인·퀘스트";
    public const string LiveName="Quest Merchant • Connected Production Preview";
    [MenuItem("OVERBURST/UI/Register Merchant Quests In Management Scene")]
    public static void Register()
    {
        AppearanceCustomizationBuilder.RequireIdle();
        var existing=SceneManager.GetSceneByPath(OverburstUIWorkshopBuilder.ScenePath);
        if(existing.IsValid()&&existing.isLoaded&&existing.isDirty)throw new InvalidOperationException("Preserve unsaved management-scene changes first");
        bool opened=!existing.IsValid()||!existing.isLoaded;
        var scene=opened?EditorSceneManager.OpenScene(OverburstUIWorkshopBuilder.ScenePath,OpenSceneMode.Additive):existing;
        try{Install(scene);if(!EditorSceneManager.SaveScene(scene))throw new IOException("Management-scene save failed");}
        finally{if(opened)EditorSceneManager.CloseScene(scene,true);}
    }
    public static void Install(Scene scene)
    {
        var roots=scene.GetRootGameObjects();var gallery=roots.Single(r=>r.name==OverburstUIWorkshopBuilder.GalleryName);
        var workshop=roots.SelectMany(r=>r.GetComponentsInChildren<OverburstUIWorkshop>(true)).Single();
        if(gallery.transform.Find(GroupName)||workshop.transform.Find(LiveName))throw new InvalidOperationException("Quest management registration already exists; inspect instead of rebuilding");
        var group=new GameObject(GroupName);SceneManager.MoveGameObjectToScene(group,scene);group.transform.SetParent(gallery.transform,false);
        float bottom=gallery.GetComponentsInChildren<Canvas>(true).Select(c=>c.transform.localPosition.y).DefaultIfEmpty(0).Min()-1320;
        for(int i=0;i<8;i++)
        {
            var board=Rect((18+i).ToString("00")+" "+((OverburstUIQuestMerchantPreview.Screen)i),group.transform,new Vector2(i%3*2120,bottom-i/3*1320),new Vector2(1920,1080));
            var canvas=board.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
            var backdrop=Rect("Backdrop",board,Vector2.zero,new Vector2(1920,1080)).gameObject.AddComponent<Image>();backdrop.color=new Color(.055f,.048f,.043f);backdrop.raycastTarget=false;
            Label(board,"Board Title",new Vector2(-940,600),board.name+" · 정식 프리팹",28);
            Label(board,"Board Note",new Vector2(-940,562),"1920 × 1080 · Connected · 수치/선택/표시 상태만 전시 override",16);
            var preview=Create(board,null);preview.galleryOnly=true;preview.Show((OverburstUIQuestMerchantPreview.Screen)i);
            foreach(var child in board.GetComponentsInChildren<Transform>(true)){child.gameObject.layer=31;child.gameObject.tag="EditorOnly";Record(child.gameObject);foreach(var component in child.GetComponents<Component>())if(component)Record(component);}
        }
        var live=Rect(LiveName,workshop.transform,Vector2.zero,new Vector2(1920,1080));Stretch(live);var livePreview=Create(live,workshop);
        var serialized=new SerializedObject(workshop);serialized.FindProperty("questPreview").objectReferenceValue=livePreview;serialized.ApplyModifiedPropertiesWithoutUndo();
        livePreview.Hide();InstallInventoryStyles(scene);RefreshInventorySamples(scene);EditorSceneManager.MarkSceneDirty(scene);
    }
    public static void RefreshInventorySamples(Scene scene)
    {
        foreach(var style in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<QuestInventoryStyle>(true)))
        {
            var inventory=style.GetComponentInParent<OverburstUIWindow>(true);var capacity=inventory.GetComponentsInChildren<Text>(true).First(c=>c.name=="Capacity"&&c.gameObject.activeSelf);var existing=style.transform.Find("Layout/Sample Capacity");
            if(!existing){var label=Object.Instantiate(capacity,style.transform.Find("Layout"),false);label.name="Sample Capacity";label.rectTransform.anchorMin=label.rectTransform.anchorMax=label.rectTransform.pivot=new Vector2(0,1);label.rectTransform.anchoredPosition=new Vector2(466,-96);label.rectTransform.sizeDelta=new Vector2(110,22);label.rectTransform.localScale=Vector3.one;label.font=style.bodyFont;label.fontSize=14;label.alignment=TextAnchor.MiddleRight;label.enabled=true;label.raycastTarget=false;}
            capacity.enabled=false;Record(capacity);var equipment=inventory.GetComponentsInChildren<Button>(true).First(x=>x.name=="Equipment Button");foreach(var label in equipment.GetComponentsInChildren<Text>(true)){label.text="장비창 열기";Record(label);}foreach(var label in equipment.GetComponentsInChildren<TMPro.TMP_Text>(true)){label.text="장비창 열기";Record(label);}
        }EditorSceneManager.MarkSceneDirty(scene);
    }
    public static int InstallInventoryStyles(Scene scene)
    {
        int count=0;
        foreach(var inventory in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<OverburstUIWindow>(true)))
        {
            if(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(inventory.gameObject)!=OverburstUIWorkshopBuilder.Root+"/PF_OverburstInventory_Rpg11.prefab")continue;
            if(inventory.GetComponentInChildren<QuestInventoryStyle>(true))continue;
            var style=((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(QuestMerchantUIBuilder.Folder+"/PF_QuestInventoryStyle.prefab"),inventory.transform)).GetComponent<QuestInventoryStyle>();
            var sort=inventory.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
            var direction=inventory.GetComponentsInChildren<Button>(true).Single(b=>b.name=="InventorySortRefreshButton");
            var equipment=inventory.GetComponentsInChildren<Button>(true).Single(b=>b.name=="Equipment Button");
            var capacity=inventory.GetComponentsInChildren<Text>(true).First(c=>c.name=="Capacity"&&c.gameObject.activeSelf);
            style.Bind(sort,direction,equipment,capacity);style.PresentSort("등급순",false,true);
            foreach(var label in inventory.GetComponentsInChildren<Text>(true).Where(c=>c.name=="Currency Label"))label.enabled=false;
            foreach(var label in inventory.GetComponentsInChildren<TMPro.TMP_Text>(true).Where(c=>c.name=="Currency Value"))label.enabled=false;
            foreach(var child in inventory.GetComponentsInChildren<Transform>(true)){Record(child.gameObject);foreach(var component in child.GetComponents<Component>())if(component)Record(component);}
            count++;
        }
        if(count>0)EditorSceneManager.MarkSceneDirty(scene);return count;
    }
    static OverburstUIQuestMerchantPreview Create(RectTransform parent,OverburstUIWorkshop workshop)
    {
        var preview=parent.gameObject.AddComponent<OverburstUIQuestMerchantPreview>();preview.workshop=workshop;
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(QuestMerchantUIBuilder.Prefab);
        preview.view=((GameObject)PrefabUtility.InstantiatePrefab(source,parent)).GetComponent<QuestMerchantUI>();
        var so=new SerializedObject(preview.view);so.FindProperty("previewOnly").boolValue=true;so.ApplyModifiedPropertiesWithoutUndo();preview.view.enabled=false;Record(preview.view);
        preview.availableRow=Row(preview.view.merchantRowTemplate,preview.view.merchantRows,"Sample Available",-2);
        preview.lockedRow=Row(preview.view.merchantRowTemplate,preview.view.merchantRows,"Sample Locked",-132);
        preview.personalRow=Row(preview.view.journalRowTemplate,preview.view.journalRows,"Sample Personal",-2);
        preview.view.merchantRowTemplate.gameObject.SetActive(false);preview.view.journalRowTemplate.gameObject.SetActive(false);
        var menuSource=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstHudMenuBuilder.PrefabPath);
        preview.menu=((GameObject)PrefabUtility.InstantiatePrefab(menuSource,parent)).GetComponent<OverburstHudMenu>();preview.menu.enabled=workshop!=null;
        if(!workshop)preview.menu.Initialize();preview.menu.CloseImmediate();Record(preview.menu);
        if(workshop)preview.menu.gameObject.SetActive(false);
        return preview;
    }
    static QuestRowView Row(QuestRowView template,RectTransform parent,string name,float y)
    {var row=Object.Instantiate(template,parent,false);row.name=name;((RectTransform)row.transform).anchoredPosition=new Vector2(2,y);return row;}
    static void Record(Object target){if(PrefabUtility.IsPartOfPrefabInstance(target))PrefabUtility.RecordPrefabInstancePropertyModifications(target);}
    static RectTransform Rect(string name,Transform parent,Vector2 position,Vector2 size)
    {var go=new GameObject(name,typeof(RectTransform));go.layer=5;var rect=(RectTransform)go.transform;rect.SetParent(parent,false);rect.localScale=Vector3.one;rect.anchoredPosition=position;rect.sizeDelta=size;return rect;}
    static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static void Label(Transform parent,string name,Vector2 position,string text,int size)
    {var label=Rect(name,parent,position,new Vector2(1900,40)).gameObject.AddComponent<Text>();label.rectTransform.pivot=new Vector2(0,.5f);label.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/DamageFloating/Pretendard_Medium.ttf");label.text=text;label.fontSize=size;label.color=new Color32(224,189,122,255);label.raycastTarget=false;}
}
