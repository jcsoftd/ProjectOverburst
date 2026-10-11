using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class QuestMerchantWorkshopVerifier
{
    const string K="Overburst.QuestMerchantWorkshopVerifier.";
    static string Root=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/Implementation/20261011_HudQuestMerchantPortraits/Evidence"));
    static readonly List<string> checks=new List<string>();static int phase,lastFrame=-1;static double deadline;
    static QuestMerchantWorkshopVerifier(){EditorApplication.playModeStateChanged+=State;if(SessionState.GetBool(K+"pending",false))EditorApplication.update+=Tick;if(SessionState.GetBool(K+"return",false))EditorApplication.update+=Return;}
    static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
    static string Hash(string path){using(var sha=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));}
    static void Write(string folder,string name,object result){Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,name),JsonConvert.SerializeObject(result,Formatting.Indented));}
    public static string Native(string label="ManagementNative")
    {
        AppearanceCustomizationBuilder.RequireIdle();checks.Clear();string output=Path.Combine(Root,label);Directory.CreateDirectory(output);
        string[] sources={QuestMerchantUIBuilder.Prefab,OverburstHudMenuBuilder.PrefabPath,QuestMerchantUIBuilder.Folder+"/PF_QuestInventoryStyle.prefab",OverburstShopUIBuilder.PrefabPath,OverburstUIWorkshopBuilder.ScenePath};var before=sources.Select(Hash).ToArray();
        var scene=EditorSceneManager.OpenPreviewScene(OverburstUIWorkshopBuilder.ScenePath);
        try
        {
            var all=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            Check(all.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))==0,"management scene missing scripts 0");
            var previews=all.Select(t=>t.GetComponent<OverburstUIQuestMerchantPreview>()).Where(p=>p).ToArray();Check(previews.Length==10,"nine gallery boards and one live preview");
            Check(previews.Count(p=>p.galleryOnly)==9,"nine static gallery samples");
            foreach(var p in previews)
            {
                Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.view.gameObject)==QuestMerchantUIBuilder.Prefab,"quest source shared "+p.name);
                Check(PrefabUtility.GetPrefabInstanceStatus(p.view)==PrefabInstanceStatus.Connected,"quest instance connected "+p.name);
                Check(p.view.IsPreviewOnly&&!p.view.enabled,"scene preview cannot register gameplay handlers "+p.name);
                Check(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p.menu.gameObject)==OverburstHudMenuBuilder.PrefabPath,"HUD menu source shared "+p.name);
                Check(PrefabUtility.GetPrefabInstanceStatus(p.menu)==PrefabInstanceStatus.Connected,"HUD menu instance connected "+p.name);
            }
            Check(!AssetDatabase.LoadAssetAtPath<GameObject>(QuestMerchantUIBuilder.Prefab).GetComponent<QuestMerchantUI>().IsPreviewOnly,"production quest prefab remains gameplay mode");
            var styles=all.Select(t=>t.GetComponent<QuestInventoryStyle>()).Where(s=>s).ToArray();Check(styles.Length>0,"management inventory styles registered");
            foreach(var s in styles){Check(PrefabUtility.GetPrefabInstanceStatus(s)==PrefabInstanceStatus.Connected,"inventory style connected "+s.GetComponentInParent<OverburstUIWindow>(true).name);var inventory=s.GetComponentInParent<OverburstUIWindow>(true);var sample=s.transform.Find("Layout/Sample Capacity");if(sample)sample.gameObject.SetActive(false);foreach(var cap in inventory.GetComponentsInChildren<Text>(true).Where(c=>c.name=="Capacity"&&c.gameObject.activeSelf))cap.enabled=true;PrefabUtility.UnpackPrefabInstance(inventory.gameObject,PrefabUnpackMode.OutermostRoot,InteractionMode.AutomatedAction);var sort=inventory.GetComponentInChildren<TMPro.TMP_Dropdown>(true);Check(sort!=null,"original dropdown preserved for runtime binding");s.Bind(sort,inventory.GetComponentsInChildren<Button>(true).First(b=>b.name=="InventorySortRefreshButton"),inventory.GetComponentsInChildren<Button>(true).First(b=>b.name=="Equipment Button"),inventory.GetComponentsInChildren<Text>(true).First(c=>c.name=="Capacity"&&c.gameObject.activeSelf));}
            var live=previews.Single(p=>!p.galleryOnly);var w=live.workshop;Check(w!=null,"live preview bound to workshop");live.menu.Initialize();
            Check(live.menu.Rows.Length==5,"exactly five HUD actions");
            var camera=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).First(c=>c.name=="UI Preview Camera");camera.scene=scene;camera.enabled=false;camera.cullingMask&=~(1<<31);
            foreach(var size in new[]{new Vector2Int(1920,1080),new Vector2Int(1280,720)})
            {
                foreach(OverburstUIQuestMerchantPreview.Screen screen in Enum.GetValues(typeof(OverburstUIQuestMerchantPreview.Screen)))
                {w.ShowHud();live.Show(screen);Capture(camera,size,Path.Combine(output,screen+"-"+size.x+".png"));Check(live.IsVisible,"visible "+screen+" "+size.x);}
                w.ShowShop();Capture(camera,size,Path.Combine(output,"Trade-"+size.x+".png"));
                w.ShowInventory();Capture(camera,size,Path.Combine(output,"Inventory-"+size.x+".png"));
            }
        }
        catch(Exception e){Write(output,"failure.json",new{error=e.ToString(),checks});throw;}
        finally{EditorSceneManager.ClosePreviewScene(scene);}
        Check(before.SequenceEqual(sources.Select(Hash)),"native preview preserves source prefabs and saved scene bytes");Write(output,"result.json",new{success=true,checks,resolutions=new[]{"1920x1080","1280x720"},captures=20});return output;
    }
    static void Capture(Camera camera,Vector2Int size,string path)
    {
        var rt=new RenderTexture(size.x,size.y,24);var old=camera.targetTexture;var active=RenderTexture.active;Texture2D pixels=null;
        try{rt.Create();camera.targetTexture=rt;Canvas.ForceUpdateCanvases();foreach(var wallet in (camera.scene.IsValid()?camera.scene:camera.gameObject.scene).GetRootGameObjects().SelectMany(o=>o.GetComponentsInChildren<QuestUiWalletLayout>(true)))wallet.Refresh();Canvas.ForceUpdateCanvases();camera.Render();RenderTexture.active=rt;pixels=new Texture2D(size.x,size.y,TextureFormat.RGB24,false);pixels.ReadPixels(new Rect(0,0,size.x,size.y),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
        finally{camera.targetTexture=old;RenderTexture.active=active;if(pixels)Object.DestroyImmediate(pixels);rt.Release();Object.DestroyImmediate(rt);}
    }
    public static void StartPlay(string label)
    {
        AppearanceCustomizationBuilder.RequireIdle();string output=Path.Combine(Root,label);if(Directory.Exists(output))throw new InvalidOperationException("Use unique evidence directory");Directory.CreateDirectory(output);
        Write(output,"edit-before.json",new{input=UnityEngine.InputSystem.InputSystem.settings.GetInstanceID(),scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        SessionState.SetString(K+"output",output);SessionState.SetString(K+"start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(K+"pending",true);SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+180);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(OverburstUIWorkshopBuilder.ScenePath);EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"isolated-save"));}catch(Exception error){Finish(error);}
    }
    static object[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return (object)new{s.path,s.isDirty,roots=s.rootCount};}).ToArray();
    static bool Own()=>IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(SessionState.GetString(K+"output",""),"isolated-save");
    static void State(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredEditMode&&SessionState.GetBool(K+"return",false)){EditorApplication.update-=Return;EditorApplication.update+=Return;}}
    static void Tick()
    {
        if(!SessionState.GetBool(K+"pending",false)){EditorApplication.update-=Tick;return;}
        try
        {
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0))throw new Exception("Management Play timeout");
            if(!EditorApplication.isPlaying||!Own()||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;EditorApplication.QueuePlayerLoopUpdate();
            var p=Object.FindObjectsByType<OverburstUIQuestMerchantPreview>(FindObjectsInactive.Include,FindObjectsSortMode.None).Single(x=>!x.galleryOnly);var v=p.view;var w=p.workshop;var output=SessionState.GetString(K+"output","");
            switch(phase++)
            {
                case 0:checks.Clear();Check(QuestMerchantUI.Current==null,"management samples never register as gameplay quest owner");Check(AccountGameplaySession.Current==null,"management samples do not open an account session");Check(p.menu.Rows.Length==5,"five HUD rows after real Awake");Check(Object.FindObjectsByType<OverburstUIQuestMerchantPreview>(FindObjectsInactive.Include,FindObjectsSortMode.None).Where(x=>x.galleryOnly).All(x=>!x.gameObject.activeSelf),"gallery boards inactive in Play");w.ShowQuestApproach();break;
                case 1:Check(v.approach.activeInHierarchy,"workshop opens approach");v.approachButtons[0].onClick.Invoke();Check(v.dialogue.activeInHierarchy,"approach dialogue click");v.dialoguePrimary.onClick.Invoke();Check(v.merchantList.activeInHierarchy,"dialogue to list click");p.availableRow.button.onClick.Invoke();Check(v.merchantDetail.activeInHierarchy,"list row opens right detail");Check(!p.lockedRow.button.interactable,"locked prerequisite row disabled");break;
                case 2:CaptureScene(output,"merchant-detail");v.merchantInfo.primary.onClick.Invoke();Check(v.merchantInfo.primaryLabel.text!="의뢰 수락","preview acceptance changes sample only");w.ShowPersonalQuests();Check(v.journal.activeInHierarchy,"personal list opens");break;
                case 3:CaptureScene(output,"journal");w.ShowQuestReport();Check(v.reportPopup.activeInHierarchy,"report popup opens");v.popupConfirm.onClick.Invoke();Check(v.journal.activeInHierarchy&&p.personalRow.status.text=="완료","sample report shows completed state");w.ShowCornerMenu();break;
                case 4:Check(p.menu.IsOpen,"workshop opens corner menu");CaptureScene(output,"hud-menu");p.menu.Rows[3].button.onClick.Invoke();Check(v.journal.activeInHierarchy&&!p.menu.IsOpen,"corner quest click opens personal list");w.ShowCornerMenu();p.menu.Rows[0].button.onClick.Invoke();Check(w.Inventory.gameObject.activeInHierarchy,"corner inventory click opens formal window");break;
                case 5:var style=w.Inventory.GetComponentInChildren<QuestInventoryStyle>(true);Check(style!=null,"inventory uses connected quest artwork");Check(style.equipmentLabel.text=="장비창 열기","management equipment caption matches gameplay");var sort=style.modeHost.GetComponentInChildren<TMPro.TMP_Dropdown>(true);sort.value=(sort.value+1)%sort.options.Count;Check(style.modeLabel.text==sort.options[sort.value].text,"management sort label follows actual dropdown");var previous=style.directionLabel.text;style.directionHost.GetComponentInChildren<Button>(true).onClick.Invoke();Check(style.directionLabel.text!=previous,"management sort direction updates");w.ShowShop();break;
                case 6:CaptureScene(output,"trade");var portrait=w.GetComponentsInChildren<RawImage>(true).First(x=>x.name=="Merchant Portrait");Check(portrait.texture==Resources.Load<Texture2D>("UI/Quests/GeneralMerchantPortrait"),"shop sample identity and generated portrait agree");w.ShowHud();Check(!p.IsVisible,"HUD return closes all quest previews");Check(QuestMerchantUI.Current==null&&AccountGameplaySession.Current==null,"sample actions leave gameplay owners untouched");Check(!GameplayInputBlocker.IsGameplayInputBlocked,"no input owner leaked by previews");w.ShowStylistApproach();break;
                case 7:Check(v.approach.activeInHierarchy,"stylist uses formal shared approach");Check(v.approachButtons.Count(b=>b.gameObject.activeSelf)==1&&v.approachButtons[1].transform.Find("Label").GetComponent<Text>().text=="외모 변경","stylist approach exposes one appearance action");CaptureScene(output,"stylist-approach");v.approachButtons[1].onClick.Invoke();break;
                case 8:var appearance=Overburst.Appearance.AppearanceCustomizationPanel.Instance;Check(appearance&&Overburst.Appearance.AppearanceCustomizationPanel.IsOpen&&appearance.IsExhibition,"stylist click opens formal appearance exhibition");Check(!p.IsVisible&&AccountGameplaySession.Current==null&&QuestMerchantUI.Current==null,"stylist preview leaves gameplay account and quest owner untouched");CaptureScene(output,"stylist-appearance");appearance.Close();w.ShowHud();Check(!Overburst.Appearance.AppearanceCustomizationPanel.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"appearance preview close releases input");Finish(null);break;
            }
        }
        catch(Exception error){Finish(error);}
    }
    static void CaptureScene(string output,string name){var camera=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c=>c.name=="UI Preview Camera");Capture(camera,new Vector2Int(1920,1080),Path.Combine(output,name+".png"));}
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;SessionState.SetBool(K+"pending",false);SessionState.SetBool(K+"return",true);var output=SessionState.GetString(K+"output","");Write(output,"play-result.json",new{success=error==null,checks,error=error?.ToString()});if(EditorApplication.isPlaying&&Own())EditorApplication.ExitPlaymode();EditorApplication.update-=Return;EditorApplication.update+=Return;
    }
    static void Return()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        var output=SessionState.GetString(K+"output","");var env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);var prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");if(new[]{env,prepared}.Any(path=>!string.IsNullOrEmpty(path)&&!Path.GetFullPath(path).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))return;
        string start=SessionState.GetString(K+"start","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(start)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);IsolatedSavePlayGuard.UseRealAccount();SessionState.EraseBool(K+"pending");SessionState.EraseBool(K+"return");SessionState.EraseString(K+"output");SessionState.EraseString(K+"start");SessionState.EraseFloat(K+"deadline");EditorApplication.update-=Return;
        Write(output,"return-result.json",new{success=!IsolatedSavePlayGuard.RequiresAccountChoice&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),input=UnityEngine.InputSystem.InputSystem.settings.GetInstanceID(),guard=IsolatedSavePlayGuard.RequiresAccountChoice,active=IsolatedSavePlayGuard.ActiveDirectory,scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
    }
}
