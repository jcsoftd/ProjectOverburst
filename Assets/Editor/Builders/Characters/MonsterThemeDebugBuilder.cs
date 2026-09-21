using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class MonsterThemeDebugBuilder
{
    [MenuItem("OVERBURST/Enemies/Themes/Connect Debug Buttons")]
    public static void Connect()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play first.");
        var current=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if(current.isDirty)throw new InvalidOperationException("Save current scene first.");
        var scene=EditorSceneManager.OpenScene("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        var toggle=UnityEngine.Object.FindFirstObjectByType<DebugPanelToggleUI>(FindObjectsInactive.Include);
        var so=new SerializedObject(toggle);var font=so.FindProperty("fontAsset").objectReferenceValue as TMP_FontAsset;
        if(font==null)font=toggle.GetComponentInChildren<TextMeshProUGUI>(true).font;
        var previous=toggle.transform.parent.Find("Monster Theme Controls");if(previous!=null)UnityEngine.Object.DestroyImmediate(previous.gameObject);
        var root=Rect("Monster Theme Controls",toggle.transform.parent,168,235,310,346);
        root.gameObject.AddComponent<Image>().color=new Color(.035f,.055f,.08f,.95f);
        var ui=root.gameObject.AddComponent<EnemyThemeDebugUI>();
        ui.tables=new[]{"SpiderBrood","VenomBrood","PrimalHunt"}.Select(id=>AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(MonsterThemeCombatBuilder.Root+"/Tables/"+id+".asset")).ToArray();
        ui.warningMaterials=ui.tables.Select(t=>AssetDatabase.LoadAssetAtPath<Material>(MonsterThemeCombatBuilder.Root+"/Materials/"+t.ThemeId+".mat")).ToArray();
        ui.spawnButtons=new Button[3];ui.waveButtons=new Button[3];
        ui.arenaPrefab=BuildArena(ui.tables,ui.warningMaterials,font);
        ui.arenaButton=Button(root,12,10,286,34,"독립 시험장 입장",font,new Color(.12f,.32f,.36f));
        Label("Header",root,12,311,286,28,"몬스터 테마 시험",font,19);
        for(int i=0;i<3;i++)
        {
            float y=261-i*49;
            Label("Theme name",root,12,y,137,33,ui.tables[i].DisplayName,font,14);
            ui.spawnButtons[i]=Button(root,149,y,72,33,"50마리",font,ui.tables[i].Accent*.5f);
            ui.waveButtons[i]=Button(root,227,y,71,33,"공세",font,new Color(.18f,.28f,.34f));
        }
        ui.statusLabel=Label("Status",root,12,95,286,59,"테마별 소형 40 · 중형 9 · 정예 1",font,13);
        ui.clearButton=Button(root,12,54,286,31,"시험 소환 정리 / 공세 중지",font,new Color(.36f,.16f,.18f));
        var controlled=so.FindProperty("controlledObjects");
        var controls=Enumerable.Range(0,controlled.arraySize).Select(i=>controlled.GetArrayElementAtIndex(i).objectReferenceValue).Where(o=>o!=null).Append(root.gameObject).ToArray();
        controlled.arraySize=controls.Length;for(int i=0;i<controls.Length;i++)controlled.GetArrayElementAtIndex(i).objectReferenceValue=controls[i];so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(scene);Debug.Log("[MonsterThemeDebug] Connected 3 exact-count buttons, 3 onslaught buttons and owned-spawn cleanup.");
    }
    private static EnemyThemeDebugArena BuildArena(EnemyThemeTable[] tables,Material[] signals,TMP_FontAsset font)
    {
        string folder=MonsterThemeCombatBuilder.Root+"/Arena";
        if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder(MonsterThemeCombatBuilder.Root,"Arena");
        string path=folder+"/Floor.mat";
        var floorMaterial=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(floorMaterial==null){floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(floorMaterial,path);}
        floorMaterial.SetColor("_BaseColor",new Color(.16f,.20f,.22f));EditorUtility.SetDirty(floorMaterial);
        var root=new GameObject("Monster Theme Arena");
        try
        {
            var arena=root.AddComponent<EnemyThemeDebugArena>();root.AddComponent<EnemyMapTheme>().Configure(tables[0]);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.name="Training ground";floor.transform.SetParent(root.transform,false);
            floor.transform.localPosition=Vector3.down*.5f;floor.transform.localScale=new Vector3(180,1,180);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            var entry=new GameObject("Entry").transform;entry.SetParent(root.transform,false);entry.localPosition=new Vector3(0,.15f,-32);arena.entry=entry;
            for(int i=0;i<3;i++)
            {
                var zone=new GameObject(tables[i].DisplayName+" trigger");zone.transform.SetParent(root.transform,false);zone.transform.localPosition=new Vector3((i-1)*48,0,0);
                var box=zone.AddComponent<BoxCollider>();box.center=Vector3.up;box.size=new Vector3(6,2,6);box.isTrigger=true;
                var encounter=zone.AddComponent<EnemyThemeEncounter>();encounter.Configure(i==0?null:tables[i],null,signals[i]);
                zone.AddComponent<EnemyThemeTriggerZone>().Configure(encounter,true);
                var pad=GameObject.CreatePrimitive(PrimitiveType.Cube);pad.name="Entry marker";pad.transform.SetParent(zone.transform,false);
                pad.transform.localPosition=new Vector3(0,.025f,0);pad.transform.localScale=new Vector3(6,.04f,6);UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());pad.GetComponent<Renderer>().sharedMaterial=signals[i];
                var label=new GameObject("Zone label").AddComponent<TextMeshPro>();label.transform.SetParent(zone.transform,false);
                label.transform.localPosition=new Vector3(0,.08f,-5);label.transform.localRotation=Quaternion.Euler(90,0,0);label.font=font;label.fontSize=4;
                label.alignment=TextAlignmentOptions.Center;label.text=tables[i].DisplayName+"\n진입하면 3회 공세";label.rectTransform.sizeDelta=new Vector2(22,6);
            }
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,folder+"/PF_EnemyThemeDebugArena.prefab");AssetDatabase.SaveAssets();
            return prefab.GetComponent<EnemyThemeDebugArena>();
        }
        finally{UnityEngine.Object.DestroyImmediate(root);}
    }
    private static RectTransform Rect(string name,Transform parent,float x,float y,float width,float height)
    {var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.zero;rect.anchoredPosition=new Vector2(x,y);rect.sizeDelta=new Vector2(width,height);return rect;}
    private static TextMeshProUGUI Label(string name,Transform parent,float x,float y,float width,float height,string text,TMP_FontAsset font,int size)
    {var label=Rect(name,parent,x,y,width,height).gameObject.AddComponent<TextMeshProUGUI>();label.text=text;label.font=font;label.fontSize=size;label.color=Color.white;label.alignment=TextAlignmentOptions.MidlineLeft;label.raycastTarget=false;return label;}
    private static Button Button(Transform parent,float x,float y,float width,float height,string text,TMP_FontAsset font,Color color)
    {var root=Rect(text,parent,x,y,width,height);var image=root.gameObject.AddComponent<Image>();image.color=color;var button=root.gameObject.AddComponent<Button>();button.targetGraphic=image;var label=Label("Label",root,4,0,width-8,height,text,font,14);label.alignment=TextAlignmentOptions.Center;return button;}
}
