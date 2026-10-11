using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class UIPrefabStructureWindow : EditorWindow
{
    Vector2 scroll;
    string search="";
    string[] paths=Array.Empty<string>();
    readonly Dictionary<string,bool> expanded=new Dictionary<string,bool>();
    readonly Dictionary<string,string[]> shared=new Dictionary<string,string[]>();
    void OnEnable()=>RefreshOwners();
    void RefreshOwners(){paths=UIPrefabStructureBuilder.OwnerPaths().OrderBy(Group).ThenBy(p=>p).ToArray();shared.Clear();}
    [MenuItem("Overburst/UI/정식 UI/화면별 원본과 공용 프리팹")]
    public static void Open(){GetWindow<UIPrefabStructureWindow>("UI Prefabs");}
    void OnGUI()
    {
        EditorGUILayout.LabelField("정식 UI 원본 · 화면별 중첩 프리팹",EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("게임과 관리씬이 참조하는 기존 원본입니다. 화면 본체의 데이터·입력 연결을 유지하고 큰 뷰는 중첩 프리팹에서 편집합니다.",MessageType.None);
        search=EditorGUILayout.TextField("찾기",search);
        if(GUILayout.Button("UI 관리씬 열기"))OverburstUIWorkshopBuilder.Open();
        if(GUILayout.Button("원본 목록 새로고침"))RefreshOwners();
        scroll=EditorGUILayout.BeginScrollView(scroll);
        foreach(var group in paths.GroupBy(Group))
        {
            var visible=group.Where(p=>p.IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0).ToArray();if(visible.Length==0)continue;
            GUILayout.Space(10);EditorGUILayout.LabelField(group.Key,EditorStyles.boldLabel);
            foreach(var path in visible)
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                EditorGUILayout.BeginHorizontal();EditorGUILayout.ObjectField(asset,typeof(GameObject),false);
                if(GUILayout.Button("원본 열기",GUILayout.Width(76)))AssetDatabase.OpenAsset(asset);EditorGUILayout.EndHorizontal();
                foreach(var part in UIPrefabStructureBuilder.Plan.Where(p=>p.owner==path))
                {
                    var view=AssetDatabase.LoadAssetAtPath<GameObject>(UIPrefabStructureBuilder.PartsRoot+"/"+part.group+"/PF_"+part.name+".prefab");
                    EditorGUILayout.BeginHorizontal();GUILayout.Space(22);EditorGUILayout.ObjectField(part.hierarchy,view,typeof(GameObject),false);
                    if(view&&GUILayout.Button("뷰 열기",GUILayout.Width(76)))AssetDatabase.OpenAsset(view);EditorGUILayout.EndHorizontal();
                }
                expanded.TryGetValue(path,out bool open);
                open=EditorGUILayout.Foldout(open,"이 화면에서 사용하는 공용 · 중첩 원본",true);expanded[path]=open;
                if(open)
                {
                    if(!shared.TryGetValue(path,out var sources))shared[path]=sources=NestedSources(asset);
                    foreach(var source in sources)
                    {
                        var nested=AssetDatabase.LoadAssetAtPath<GameObject>(source);
                        EditorGUILayout.BeginHorizontal();GUILayout.Space(22);EditorGUILayout.ObjectField(nested,typeof(GameObject),false);
                        if(nested&&GUILayout.Button("공용 열기",GUILayout.Width(76)))AssetDatabase.OpenAsset(nested);EditorGUILayout.EndHorizontal();
                    }
                    if(sources.Length==0)EditorGUILayout.LabelField("    단독 화면 원본");
                }
            }
        }
        EditorGUILayout.EndScrollView();
    }
    public static string[] NestedSources(GameObject asset)
    {
        if(!asset)return Array.Empty<string>();
        string owner=AssetDatabase.GetAssetPath(asset);
        return asset.GetComponentsInChildren<Transform>(true).Select(t=>PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject))
            .Where(p=>!string.IsNullOrEmpty(p)&&p!=owner).Distinct().OrderBy(p=>p,StringComparer.Ordinal).ToArray();
    }
    public static string Group(string path)
    {
        if(path.Contains("/Views/"))return "12 중첩 뷰";
        if(path.Contains("/Debug/")||path.Contains("DebugArena")||path.Contains("/Feel/"))return "12 개발 UI";
        if(path.Contains("Concept")||path.Contains("Specimen")||path.Contains("PreviewModel")||path.Contains("EditorPreview")||path.Contains("TemporaryBloodComparison")||path.Contains("OiiaCat"))return "11 검수 표본 · 미리보기";
        if(path.Contains("/Title/"))return "01 타이틀 · 캐릭터 선택";
        if(path.Contains("/Appearance/")||path.Contains("AppearanceStylist"))return "02 공용 캐릭터 생성 · 외모";
        if(path.Contains("/Menu/"))return "03 ESC · 설정 · 확인창";
        if(path.Contains("/HudMenu/")||path.Contains("/HUD/")||path.Contains("HUD_Rpg"))return "04 HUD · 게임 메뉴";
        if(path.Contains("/Quests/")||path.Contains("/Merchants/"))return "06 퀘스트 · 상인 접근";
        if(path.Contains("Inventory")||path.Contains("Equipment")||path.Contains("Stash")||path.Contains("Slots/")||path.Contains("QuickKey"))return "05 인벤토리 · 장비 · 창고 · 슬롯";
        if(path.Contains("ShopPanel"))return "07 상점 · 거래";
        if(path.Contains("/SkillTree/"))return "08 스킬트리";
        if(path.Contains("/Run/")||path.Contains("/MapDungeon/")||path.Contains("Loading")||path.Contains("CavePortalPanel")||path.Contains("HubScenePortal"))return "09 던전 · 전송 · 로딩";
        return "10 월드 표시 · 미니맵 · 툴팁";
    }
}
