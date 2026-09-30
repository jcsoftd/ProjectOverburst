using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Object=UnityEngine.Object;

public static partial class OverburstUIWorkshopBuilder
{
    public const string GalleryName="UI_Gallery • UI별 전시";
    public static readonly string[] BoardNames={"01 HUD · 기본","02 HUD · 체력 감소","03 몬스터 · 체력 단계","04 인벤토리 · 아이템","05 인벤토리 · 빈 상태","06 장비 · 능력치","07 창고 · 보관함 1","08 창고 · 보관함 2","09 창고 · 보관함 3","10 알림 · 발견과 레벨","11 조합 · 장비와 가방","12 조합 · 창고와 가방","13 등급 · 크기와 효과","14 툴팁 · 무기·물약·소비품"};
    private static void BuildGallery()
    {
        var root=new GameObject(GalleryName);root.tag="EditorOnly";root.transform.position=new Vector3(10000,0,0);
        string[] groups={"01_HUD • 전투·탐험","02_Windows • 소지품·장비","03_Stash • 보관함 상태","04_Overlays • 알림·화면 조합","05_Grades • 크기·등급 검수"};
        for(int row=0;row<5;row++){
            var group=new GameObject(groups[row]);group.transform.SetParent(root.transform,false);
            for(int col=0;col<3;col++){
                int index=row*3+col;if(index>=BoardNames.Length)continue;var board=Rect(BoardNames[index],group.transform,new Vector2(col*2120,-row*1320),new Vector2(1920,1080));
                var canvas=board.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;
                var bg=Image("Backdrop",board,null,new Color(.055f,.048f,.043f),new Vector2(1920,1080));
                var backgroundObject=bg.gameObject;Object.DestroyImmediate(bg);var raw=backgroundObject.AddComponent<RawImage>();raw.texture=AssetDatabase.LoadAssetAtPath<Texture2D>(Vendor+"Textures/Demo/Background.png");raw.color=new Color(.5f,.5f,.5f,1);raw.raycastTarget=false;
                Label("Board Title",board,new Vector2(0,600),new Vector2(1920,50),BoardNames[index],32,Gold,TextAnchor.MiddleLeft);
                Label("Board Note",board,new Vector2(0,560),new Vector2(1920,28),"1920 × 1080   /   Connected Prefab   /   표시값은 검토용",18,Muted,TextAnchor.MiddleLeft);
                if(index==0||index==1||index==10||index==11){var hud=GalleryPrefab("HUD",board);if(index==1){var image=hud.transform.Find("Action Bar Unit Frame/Bar (Health)/Fill").GetComponent<Image>();image.type=UnityEngine.UI.Image.Type.Filled;image.fillMethod=UnityEngine.UI.Image.FillMethod.Horizontal;image.fillAmount=.28f;hud.transform.Find("Action Bar Unit Frame/Bar (Health)/Text Group/Percentage Text").GetComponent<Text>().text="336 / 1,200";}}
                if(index==2){for(int i=0;i<3;i++){var hp=GalleryPrefab("EnemyHealthBar",board);var r=(RectTransform)hp.transform;r.localScale=Vector3.one*.62f;r.anchoredPosition=new Vector2(-450+i*450,0);hp.GetComponent<OverburstEnemyHealthBarView>().Present(new[]{"유적의 감시자","독낭 추적자","암굴 파괴자"}[i],18+i,new[]{1,.45f,.1f}[i]);}}
                if(index==3||index==4){var bag=GalleryPrefab("Inventory",board);((RectTransform)bag.transform).anchoredPosition=Vector2.zero;if(index==4){foreach(Transform slot in bag.transform.Find("Content/Slot Viewport/Slots Grid"))FillSlot(slot,null,"");bag.transform.Find("Layout/Capacity").GetComponent<Text>().text="0 / 42";}}
                if(index==5){var gear=GalleryPrefab("Equipment",board);((RectTransform)gear.transform).anchoredPosition=Vector2.zero;}
                if(index>=6&&index<=8){var stash=GalleryPrefab("Stash",board);((RectTransform)stash.transform).anchoredPosition=Vector2.zero;var p=stash.GetComponent<OverburstUIStashPreview>();if(index==7)p.Second();if(index==8)p.Third();}
                if(index==13)BuildTooltipSamples(board);
                if(index==12)GalleryPrefab("GradeSpecimen",board);
                if(index==9)GalleryPrefab("Notifications",board);
                if(index==10){GalleryPrefab("Equipment",board);GalleryPrefab("Inventory",board);}
                if(index==11){GalleryPrefab("Stash",board);GalleryPrefab("Inventory",board);}
                foreach(var t in board.GetComponentsInChildren<Transform>(true)){t.gameObject.layer=31;t.gameObject.tag="EditorOnly";}
                Canvas.ForceUpdateCanvases();
                foreach(var scroll in board.GetComponentsInChildren<ScrollRect>(true)){scroll.verticalNormalizedPosition=1;scroll.content.anchoredPosition=Vector2.zero;scroll.verticalScrollbar.SetValueWithoutNotify(1);scroll.enabled=false;}
                foreach(var grade in board.GetComponentsInChildren<OverburstUISlotGradePreview>(true))grade.Refresh();
                // Static inspection copies never receive pointer input or register UI events.
                foreach(var drag in board.GetComponentsInChildren<DuloGames.UI.UIDragObject>(true))drag.enabled=false;
                foreach(var t in board.GetComponentsInChildren<Transform>(true)){if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject)){PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);foreach(var component in t.GetComponents<Component>())if(component)PrefabUtility.RecordPrefabInstancePropertyModifications(component);}}
            }
        }
    }
    private static GameObject GalleryPrefab(string name,Transform parent)
    {var source=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/PF_Overburst"+name+"_Rpg11.prefab");var go=(GameObject)PrefabUtility.InstantiatePrefab(source,parent);go.SetActive(true);return go;}
}

public sealed class OverburstUIWorkshopPanel : EditorWindow
{
    private Vector2 scroll;
    [MenuItem("OVERBURST/UI/UI Workshop • 관리 패널")]
    public static void OpenPanel(){GetWindow<OverburstUIWorkshopPanel>("UI Workshop");}
    private void OnGUI()
    {
        GUILayout.Space(12);GUILayout.Label("OVERBURST  /  UI WORKSHOP",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("개별 전시: Scene View에서 연결된 프리팹을 비교합니다.\n게임 미리보기: HUD와 창의 겹침·드래그를 확인합니다.\n제작 씬의 예시값이며 게임 데이터에 반영되지 않습니다.",MessageType.None);
        if(GUILayout.Button("UI 관리 씬 열기",GUILayout.Height(28)))OverburstUIWorkshopBuilder.Open();
        var root=GameObject.Find(OverburstUIWorkshopBuilder.GalleryName);
        if(!root){EditorGUILayout.HelpBox("UI 관리 씬을 열어주세요.",MessageType.Info);return;}
        if(GUILayout.Button("전체 전시 한눈에 보기",GUILayout.Height(28))){Selection.activeGameObject=root;Frame(root);}
        scroll=EditorGUILayout.BeginScrollView(scroll);
        foreach(Transform group in root.transform){GUILayout.Space(12);GUILayout.Label(group.name,EditorStyles.boldLabel);foreach(Transform board in group){GUILayout.BeginHorizontal();GUILayout.Label(board.name);if(GUILayout.Button("선택·확대",GUILayout.Width(80))){Selection.activeGameObject=board.gameObject;Frame(board.gameObject);}GUILayout.EndHorizontal();}}
        GUILayout.Space(18);GUILayout.Label("게임 화면 미리보기",EditorStyles.boldLabel);
        var w=Object.FindFirstObjectByType<OverburstUIWorkshop>();
        if(w){string[] names={"HUD","인벤토리","장비·능력치","창고 + 인벤토리","장비 + 인벤토리","몬스터 체력바","알림","등급·크기 비교","아이템 툴팁","창 위치 초기화"};System.Action[] actions={w.ShowHud,w.ShowInventory,w.ShowEquipment,w.ShowStash,w.ShowComparison,w.ShowEnemies,w.ShowNotifications,w.ShowGrades,w.ShowTooltips,w.ResetLayout};for(int i=0;i<names.Length;i++)if(GUILayout.Button(names[i],GUILayout.Height(25)))actions[i]();}
        GUILayout.Space(12);EditorGUILayout.LabelField("규격",EditorStyles.boldLabel);EditorGUILayout.HelpBox("기준 1920×1080 · 창 높이 776\n제목 26 / 소제목 17 / 본문 16 / 보조 14\n제목 Noto Serif KR · 본문 Pretendard Medium\n공용 슬롯 84×84 · 간격 8 · 슬롯 프리팹 하나",MessageType.None);
        EditorGUILayout.EndScrollView();
    }
    private static void Frame(GameObject go){var view=SceneView.lastActiveSceneView;if(!view)view=GetWindow<SceneView>();view.in2DMode=true;view.FrameSelected();view.Repaint();}
}
