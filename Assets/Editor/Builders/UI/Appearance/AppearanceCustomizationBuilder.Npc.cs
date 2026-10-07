using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Appearance;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

public static partial class AppearanceCustomizationBuilder
{
    public const string ShowcasePath="Assets/ProjectOverburst/00_Scenes/UI/AppearanceCustomizationShowcase.unity";
    const string HideoutPath="Assets/ProjectOverburst/00_Scenes/HideoutScene.unity";
    static void BuildNpc(CharacterAppearanceCatalog catalog)
    {
        Folder(Path.GetDirectoryName(NpcPath).Replace('\\','/'));
        var scene=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=new GameObject("PF_HideoutAppearanceStylist");SceneManager.MoveGameObjectToScene(root,scene);
            var npc=root.AddComponent<AppearanceStylistInteractable>();
            var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/Medieval_NPC_Pack_2/Peasant_woman/Prefab/SK_Peasant_woman skin2.prefab");
            if(!source)throw new InvalidOperationException("은신처 여성 NPC 원본이 없습니다.");
            var visual=(GameObject)PrefabUtility.InstantiatePrefab(source,scene);visual.transform.SetParent(root.transform,false);visual.name="Stylist Visual";
            var animator=visual.GetComponentInChildren<Animator>(true);if(!animator)animator=visual.AddComponent<Animator>();
            var avatar=AssetDatabase.LoadAllAssetsAtPath("Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/Peasant_woman/Base Mesh/SK_Peasant_woman.fbx").OfType<Avatar>().FirstOrDefault(a=>a.isHuman&&a.isValid);
            if(!avatar)avatar=source.GetComponentInChildren<Animator>(true)?.avatar;if(!avatar)throw new InvalidOperationException("치장사 Avatar가 없습니다.");
            string controllerPath=Root+"/AC_StylistIdle.controller";var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if(!controller){controller=AnimatorController.CreateAnimatorControllerAtPath(controllerPath);var state=controller.layers[0].stateMachine.AddState("Stylist Idle");state.motion=catalog.idleClip;controller.layers[0].stateMachine.defaultState=state;AssetDatabase.SaveAssetIfDirty(controller);}
            animator.avatar=avatar;animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.fireEvents=false;
            var collider=root.AddComponent<CapsuleCollider>();collider.center=new Vector3(0,.85f,0);collider.radius=.3f;collider.height=1.7f;
            var label=new GameObject("Stylist Name",typeof(TextMeshPro),typeof(AppearanceStylistLabel));label.transform.SetParent(root.transform,false);label.transform.localPosition=new Vector3(0,2.15f,0);
            var name=label.GetComponent<TextMeshPro>();name.font=headingFont;name.text="치장사";name.fontSize=2.6f;name.color=Gold;name.alignment=TextAlignmentOptions.Center;name.rectTransform.sizeDelta=new Vector2(2,.3f);
            var prompt=new GameObject("Interaction Prompt",typeof(TextMeshPro),typeof(AppearanceStylistLabel));prompt.transform.SetParent(root.transform,false);prompt.transform.localPosition=new Vector3(0,1.92f,0);
            var promptText=prompt.GetComponent<TextMeshPro>();promptText.font=bodyFont;promptText.fontSize=2f;promptText.color=Ivory;promptText.alignment=TextAlignmentOptions.Center;promptText.rectTransform.sizeDelta=new Vector2(3,.3f);promptText.text="F : 외모 변경";
            var serialized=new SerializedObject(npc);serialized.FindProperty("promptRoot").objectReferenceValue=prompt;serialized.FindProperty("promptText").objectReferenceValue=promptText;serialized.ApplyModifiedPropertiesWithoutUndo();
            prompt.SetActive(false);PrefabUtility.SaveAsPrefabAsset(root,NpcPath);
        }
        finally{if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
        BuildShowcase();
    }
    static void BuildShowcase()
    {
        Folder(Path.GetDirectoryName(ShowcasePath).Replace('\\','/'));
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        try
        {
            var camera=new GameObject("Showcase Camera",typeof(Camera));SceneManager.MoveGameObjectToScene(camera,scene);camera.tag="MainCamera";camera.GetComponent<Camera>().backgroundColor=Color.black;
            var events=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));SceneManager.MoveGameObjectToScene(events,scene);events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            var panel=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath),scene);
            var showcase=panel.AddComponent<AppearanceCustomizationShowcase>();showcase.panel=panel.GetComponent<AppearanceCustomizationPanel>();
            if(!EditorSceneManager.SaveScene(scene,ShowcasePath))throw new IOException("제품 프리뷰 씬 저장 실패");
        }
        finally{if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
    }
    [MenuItem("Overburst/UI/외모 커스터마이징/은신처 치장사 배치")]
    public static void PlaceStylist()
    {
        RequireIdle();if(SceneManager.GetSceneByPath(HideoutPath).IsValid())throw new InvalidOperationException("열린 은신처의 다른 변경을 보존하기 위해 배치를 보류했습니다.");
        var source=AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath);if(!source)throw new InvalidOperationException("치장사 프리팹부터 만들어 주세요.");
        string backup=Path.GetFullPath(Output+"/Before/StylistPlacement/HideoutScene.unity");Directory.CreateDirectory(Path.GetDirectoryName(backup));if(!File.Exists(backup))File.Copy(HideoutPath,backup);
        var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(HideoutPath,OpenSceneMode.Additive);
        try
        {
            var existing=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<AppearanceStylistInteractable>(true)).ToArray();
            if(existing.Length>1)throw new InvalidOperationException("치장사가 중복 배치되어 있습니다.");
            var npc=existing.Length==1?existing[0].gameObject:(GameObject)PrefabUtility.InstantiatePrefab(source,scene);
            npc.name="Appearance Stylist";
            // Open ground by the camp entrance; precise clearance is checked before saving.
            var point=new Vector3(-3.7f,0,3.4f);
            Physics.SyncTransforms();
            var ground=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Collider>(true)).SingleOrDefault(c=>c.name=="Camp Ground");
            if(!ground||!ground.Raycast(new Ray(point+Vector3.up*10,Vector3.down),out var hit,20))throw new InvalidOperationException("치장사 아래의 은신처 바닥을 찾지 못했습니다.");
            point.y=hit.point.y;
            var obstacles=Physics.OverlapCapsule(point+Vector3.up*.4f,point+Vector3.up*1.45f,.4f,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>c!=ground&&c.transform.root!=npc.transform&&c.bounds.max.y>point.y+.3f).ToArray();
            if(obstacles.Length>0)throw new InvalidOperationException("치장사 배치가 장애물과 겹칩니다: "+string.Join(",",obstacles.Select(c=>c.name)));
            npc.transform.position=point;npc.transform.rotation=Quaternion.LookRotation((new Vector3(0,0,6)-point).normalized);
            var approach=point+npc.transform.forward*1.35f;
            if(!ground.Raycast(new Ray(approach+Vector3.up*10,Vector3.down),out var approachHit,20))throw new InvalidOperationException("치장사 앞의 접근 바닥을 찾지 못했습니다.");
            approach.y=approachHit.point.y;
            var approachObstacles=Physics.OverlapCapsule(approach+Vector3.up*.4f,approach+Vector3.up*1.45f,.4f,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>c!=ground&&c.transform.root!=npc.transform&&c.bounds.max.y>approach.y+.3f).ToArray();
            if(approachObstacles.Length>0)throw new InvalidOperationException("치장사 앞의 접근 위치가 장애물과 겹칩니다: "+string.Join(",",approachObstacles.Select(c=>c.name)));
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("은신처 저장 실패");
            File.WriteAllText(Path.GetFullPath(Output+"/stylist-placement.json"),JsonConvert.SerializeObject(new{status="PASS_NATIVE_PLACEMENT",scene=HideoutPath,prefab=NpcPath,position=new[]{point.x,point.y,point.z},approachPosition=new[]{approach.x,approach.y,approach.z},ground=ground.name,npcCount=1,backup,utc=DateTime.UtcNow},Formatting.Indented));
        }
        finally{if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(scene,true);}
    }
}
