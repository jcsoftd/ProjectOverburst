using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class OverburstShopUIVerifier
{
    public const string Output = OverburstShopUIBuilder.Output;
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    [MenuItem("OVERBURST/UI/상점 공용 디자인 검증 및 캡처")]
    public static void Run()
    {
        ItemTypeIconBuilder.RequireIdle(); checks.Clear(); errors.Clear(); Application.logMessageReceived += Log;
        string scenes = Scenes(); var random = UnityEngine.Random.state;
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(OverburstShopUIBuilder.PrefabPath);
            var slots = asset.GetComponentsInChildren<SlotUI>(true);
            Check(slots.Length == 77, "Stock 35 and two offer lists of 21 preserved");
            Check(asset.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "No missing scripts");
            foreach (var slot in slots)
            {
                Check(((RectTransform)slot.transform).sizeDelta == Vector2.one * 84, "84 cell " + slot.name);
                var visual = slot.transform.Find("Shared Visual");
                Check(visual && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(visual.gameObject) == ItemTypeIconBuilder.SharedSlot, "Shared prefab connection " + slot.name);
                Check(slot.GetComponent<OverburstUIItemSlotView>() && !visual.GetComponent<OverburstUIItemSlotView>(), "Single presentation owner " + slot.name);
                foreach (string badge in new[] { "Item Type Badge", "Weapon Element Badge" })
                {
                    var rect = (RectTransform)visual.Find(badge);
                    Check(rect.sizeDelta == Vector2.one * 23 && rect.anchoredPosition == new Vector2(-7, 7), "23 badge and inset " + slot.name + "/" + badge);
                }
                var serialized = new SerializedObject(slot);
                Check(serialized.FindProperty("iconImage").objectReferenceValue == visual.Find("Icon").GetComponent<Image>(), "Live icon reference " + slot.name);
            }
            var scrolls = asset.GetComponentsInChildren<ScrollRect>(true);
            Check(scrolls.Length == 3 && scrolls.All(s => s.vertical && !s.horizontal && s.content && s.viewport && s.viewport.GetComponent<RectMask2D>()), "Three clipped vertical scrolls");
            foreach (var scroll in scrolls)
                Check(scroll.content.rect.height > scroll.viewport.rect.height && scroll.content.GetComponent<GridLayoutGroup>().cellSize == Vector2.one * 84, "Scrollable preserved capacity " + scroll.name);
            Check(scrolls.Single(s=>s.name=="Stock Viewport").content.GetComponent<GridLayoutGroup>().constraintCount==6,"Merchant stock aligned to inventory six columns");
            foreach(var scroll in scrolls.Where(s=>s.name!="Stock Viewport"))
            {
                Check(scroll.content.GetComponent<GridLayoutGroup>().constraintCount==2,"Each trade offer uses two columns "+scroll.name);
                Check(Mathf.Approximately(scroll.content.rect.height,1004) && Mathf.Approximately(scroll.viewport.rect.height,360),"All 21 offer slots retained across eleven scroll rows "+scroll.name);
            }
            Check(asset.GetComponent<OverburstUIShopSkin>(), "Presentation bridge persisted");
            Check(asset.transform.Find("Approved Tabs").childCount==4 && asset.transform.Find("ShopContextBlocker/ShopContextMenu/TradeButton"),"Four tabs distinct from context trade button");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponElementIconBuilder.UiRoot + "PF_OverburstInventory_Rpg11.prefab");
            foreach (var window in asset.transform.CastChildren().Where(t => t.name.EndsWith("Window")))
            {
                var chrome = window.Find("Window Chrome");
                Check(chrome && chrome.GetComponent<Image>().sprite == source.GetComponent<Image>().sprite, "Approved frame " + window.name);
                Check(((RectTransform)window).sizeDelta.y == 776, "Consistent window height " + window.name);
            }
            Check(((RectTransform)asset.transform.Find("MerchantInventoryWindow")).sizeDelta.x==608 && ((RectTransform)source.transform).rect.width*source.transform.localScale.x==608,"Shop and inventory share 608 displayed width");
            Check(((RectTransform)asset.transform.Find("TradeWindow")).sizeDelta.x==448,"Compact 448 trade width");
            Check(OverburstUIShopSkin.TradeX-224-(OverburstUIShopSkin.MerchantX+304)==16 && OverburstUIShopSkin.InventoryX-304-(OverburstUIShopSkin.TradeX+224)==16,"Three windows have even 16 gaps");
            var trade=asset.transform.Find("TradeWindow");
            foreach(string name in new[]{"ConfirmButton","ClearButton","CloseButton"})
            {
                var button=trade.Find(name).GetComponent<Button>();var art=button.transform.Find("Approved Button Artwork");
                Check(art && button.GetComponents<DuloGames.UI.UIHighlightTransition>().Length>=1 && button.GetComponent<DuloGames.UI.UIPressTransition>(),"Native asset hover and press reused "+name);
                Check(!art.GetComponent<Button>() && button.targetGraphic.transform.IsChildOf(art),"Existing click owner with native artwork "+name);
                Check(((RectTransform)button.transform).rect.height>=(name=="ConfirmButton"?60:48) && art.Find("Text").GetComponent<Text>().fontSize>=(name=="ConfirmButton"?19:17),"Readable button and hit area "+name);
            }
            Check(((RectTransform)trade.Find("ConfirmButton")).sizeDelta.x==384 && ((RectTransform)trade.Find("ClearButton")).sizeDelta.x==186,"Full width primary and equal secondary buttons");
            foreach(var entry in new[]{new{button="OkButton",text="확인"},new{button="CancelButton",text="취소"}})
            {
                var button=asset.transform.Find("ShopContextBlocker/ShopSplitTradePopup/ButtonRow/"+entry.button);
                Check(button.GetComponentInChildren<TMP_Text>(true).text==entry.text && button.Find("Approved Button Artwork/Text").GetComponent<Text>().text==entry.text,"Korean split action source and presentation "+entry.button);
            }
            // Check the actual persistent controller after native save, including scene overrides.
            foreach (var shop in Resources.FindObjectsOfTypeAll<ShopUI>().Where(s => !EditorUtility.IsPersistent(s) && s.gameObject.scene.IsValid()))
            {
                var so = new SerializedObject(shop);
                foreach (var entry in new[] { new { name = "merchantInventorySlots", count = 35 }, new { name = "merchantOfferSlots", count = 21 }, new { name = "playerOfferSlots", count = 21 } })
                {
                    var array = so.FindProperty(entry.name);
                    Check(array.arraySize == entry.count && Enumerable.Range(0,array.arraySize).All(i => array.GetArrayElementAtIndex(i).objectReferenceValue), "PersistentScene references " + entry.name);
                }
                foreach (string field in new[] { "shopPanel", "merchantInventoryWindowRoot", "tradeWindowRoot", "confirmButton", "clearButton", "closeButton", "contextMenu" })
                    Check(so.FindProperty(field).objectReferenceValue, "PersistentScene reference " + field);
            }
            Render("native-trade.png", false);
            Render("native-quest.png", true);
            Render("native-split.png",false,true);
            Check(errors.Count==0,"Preview renders without errors or assertions");
            Check(scenes == Scenes(), "Open scenes and dirty state unchanged by previews");
            Write("edit-results.json", new { status = "PASS", checks, errors });
        }
        catch (Exception e) { Write("edit-results.json", new { status = "FAIL", checks, errors, error = e.ToString() }); throw; }
        finally { UnityEngine.Random.state = random; Application.logMessageReceived -= Log; }
    }

    static void Render(string file, bool quest, bool split=false)
    {
        const int width = 1920, height = 1080, layer = 30;
        var preview = EditorSceneManager.NewPreviewScene();
        GameObject cameraObject = null, canvasObject = null; RenderTexture target = null; Texture2D pixels = null;
        var previous = RenderTexture.active;
        try
        {
            cameraObject = new GameObject("Shop preview camera", typeof(Camera));
            canvasObject = new GameObject("Shop preview canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            SceneManager.MoveGameObjectToScene(cameraObject, preview); SceneManager.MoveGameObjectToScene(canvasObject, preview);
            var camera = cameraObject.GetComponent<Camera>(); camera.scene = preview; camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true; camera.orthographicSize = height / 2f; camera.cullingMask = 1 << layer;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f,.065f,.075f);
            target = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32); camera.targetTexture = target;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OverburstShopUIBuilder.PrefabPath), canvas.transform);
            root.SetActive(true); var rootRect = (RectTransform)root.transform; rootRect.anchorMin = Vector2.zero; rootRect.anchorMax = Vector2.one; rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
            foreach (string name in new[] { "MerchantInventoryWindow", "TradeWindow" }) root.transform.Find(name).gameObject.SetActive(!quest);
            foreach (string name in new[] { "QuestListWindow", "QuestDetailWindow" }) root.transform.Find(name).gameObject.SetActive(quest);
            foreach (string name in new[] { "SpecialtyListWindow", "SpecialtyDetailWindow", "TradeFailurePopup", "ShopContextBlocker" }) root.transform.Find(name).gameObject.SetActive(false);
            var inventory = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(WeaponElementIconBuilder.UiRoot + "PF_OverburstInventory_Rpg11.prefab"), canvas.transform);
            inventory.SetActive(!quest); ((RectTransform)inventory.transform).anchoredPosition = new Vector2(OverburstUIShopSkin.InventoryX,OverburstUIShopSkin.WindowY);
            var samples = ItemTypeIconVerifier.Samples();
            foreach (var slot in root.GetComponentsInChildren<SlotUI>(true)) slot.SetDisplayItem(samples[slot.transform.GetSiblingIndex() % samples.Length]);
            var inventorySlots = inventory.GetComponentsInChildren<OverburstUIItemSlotView>(true);
            for (int i = 0; i < inventorySlots.Length; i++)
            { var slot = inventorySlots[i].GetComponent<SlotUI>() ?? inventorySlots[i].gameObject.AddComponent<SlotUI>(); slot.SetDisplayItem(i < 24 ? samples[i % samples.Length] : null); }
            SetText(root,"MerchantNameText","무기 상인"); SetText(root,"DescriptionText","새로운 장비와 재료를 거래합니다.");
            SetText(root,"ReputationLevelText","평판 Lv. 1"); SetText(root,"ReputationGradeText","중립"); SetText(root,"ExpPercentText","다음 평판까지 0%");
            SetText(root,"ReputationDiscountText","가격 할인 0%"); SetText(root,"MerchantGoldInfoText","상인 보유 골드 1,000G"); SetText(root,"ReputationStockGradeText","취급 등급: 일반 · 고급 · 희귀");
            SetText(root,"MerchantValueText","상인 제안 가치: 3,450G"); SetText(root,"PlayerValueText","플레이어 제안 가치: 1,200G");
            SetText(root,"AutoGoldText","내가 지불: 2,250G"); SetText(root,"GoldSummaryText","인벤토리 5,000G · 창고 12,000G");
            SetText(root,"StatusText","더블클릭, 드래그, 우클릭 메뉴로 거래창에 올립니다.");
            root.GetComponent<OverburstUIShopSkin>().RefreshPresentation();
            if(split)
            {
                root.transform.SetAsLastSibling();
                root.transform.Find("ShopContextBlocker").gameObject.SetActive(true);
                root.transform.Find("ShopContextBlocker/ShopContextMenu").gameObject.SetActive(false);
                root.transform.Find("ShopContextBlocker/ShopSplitTradePopup").gameObject.SetActive(true);
                ((RectTransform)root.transform.Find("ShopContextBlocker/ShopSplitTradePopup")).anchoredPosition=Vector2.zero;
            }
            var skin = new SerializedObject(root.GetComponent<OverburstUIShopSkin>()); var tabs = skin.FindProperty("tabs");
            for (int i=0;i<tabs.arraySize;i++) ((GameObject)tabs.GetArrayElementAtIndex(i).FindPropertyRelative("selected").objectReferenceValue).SetActive(quest ? i==1 : i==0);
            foreach (Transform t in canvasObject.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            Canvas.ForceUpdateCanvases();
            foreach (var grid in canvasObject.GetComponentsInChildren<GridLayoutGroup>(true)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)grid.transform);
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(width,height,TextureFormat.RGBA32,false); pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output,file),pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            if (cameraObject) cameraObject.GetComponent<Camera>().targetTexture = null;
            if (pixels) Object.DestroyImmediate(pixels);
            if (target) { target.Release(); Object.DestroyImmediate(target); }
            if (canvasObject) Object.DestroyImmediate(canvasObject);
            if (cameraObject) Object.DestroyImmediate(cameraObject);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }
    static void SetText(GameObject root,string name,string value)
    { var text = root.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t=>t.name==name); if(text)text.text=value; }
    public static string Scenes() => JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=> { var s=SceneManager.GetSceneAt(i); return new{s.path,s.isDirty}; }));
    public static void Write(string name,object value) { Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output,name),JsonConvert.SerializeObject(value,Formatting.Indented)); }
    static void Check(bool pass,string message) { checks.Add((pass?"PASS ":"FAIL ")+message); if(!pass)throw new InvalidOperationException(message); }
    static void Log(string message,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message); }
}

[InitializeOnLoad]
public static class OverburstShopUIPlayVerifier
{
    const string Key = "OverburstShopUIPlayVerifier.";
    static IEnumerator work;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static double deadline;
    static int lastFrame;
    static string VerificationOutput => SessionState.GetString(Key+"output",OverburstShopUIVerifier.Output);
    static void Write(string name,object value) { Directory.CreateDirectory(VerificationOutput); File.WriteAllText(Path.Combine(VerificationOutput,name),JsonConvert.SerializeObject(value,Formatting.Indented)); }
    static string Account => Path.GetFullPath(Path.Combine(VerificationOutput,"IsolatedAccount06"));
    static string Report => SessionState.GetBool(Key+"restore",false) ? "play02-results.json" : "play01-results.json";
    public static string Status => SessionState.GetString(Key+"status","NOT_RUN");
    static OverburstShopUIPlayVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if(SessionState.GetBool(Key+"return",false)) EditorApplication.update += ReturnAccount;
        if(SessionState.GetBool(Key+"pending",false)) EditorApplication.update += BootWatch;
    }
    public static void Run(bool restore) => Run(restore,OverburstShopUIVerifier.Output);

    public static void Run(bool restore,string outputDirectory,bool verifyFlaskBindings=false)
    {
        ItemTypeIconBuilder.RequireIdle();
        if(SessionState.GetBool(Key+"pending",false) || SessionState.GetBool(Key+"return",false))
            throw new InvalidOperationException("Previous shop verification has not returned");
        string output=Path.GetFullPath(outputDirectory);
        string allowed=Path.GetFullPath("../개인파일/코덱스산출").TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Verification output must stay under Codex artifacts");
        SessionState.SetString(Key+"output",output);
        SessionState.SetBool(Key+"flaskBindings",verifyFlaskBindings);
        if(SceneManager.GetActiveScene().name!="PersistentScene") throw new InvalidOperationException("PersistentScene required");
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)) || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))) throw new InvalidOperationException("Another account is selected");
        SessionState.SetInt(Key+"pid",System.Diagnostics.Process.GetCurrentProcess().Id);
        SessionState.SetBool(Key+"restore",restore); SessionState.SetBool(Key+"pending",true); SessionState.SetString(Key+"status","RUNNING");
        SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key+"scenes",OverburstShopUIVerifier.Scenes());
        SessionState.SetString(Key+"deadline",(EditorApplication.timeSinceStartup+240).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= BootWatch; EditorApplication.update += BootWatch;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Account); }
        catch { ScheduleReturn(); throw; }
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key+"pending",false)) return;
        if(state==PlayModeStateChange.EnteredPlayMode)
        {
            checks.Clear(); errors.Clear(); deadline=EditorApplication.timeSinceStartup+180; lastFrame=-1;
            SessionState.SetBool(Key+"background",Application.runInBackground); Application.runInBackground=true;
            work=Verify(); Application.logMessageReceived += Log; EditorApplication.update += Tick;
        }
        if(state==PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= Tick; Application.logMessageReceived -= Log; (work as IDisposable)?.Dispose(); work=null;
            Application.runInBackground=SessionState.GetBool(Key+"background",false);
            if(Status=="RUNNING") { SessionState.SetString(Key+"status","INTERRUPTED"); Write(Report,new{status="INTERRUPTED",checks,errors}); }
        }
        if(state==PlayModeStateChange.EnteredEditMode) ScheduleReturn();
    }
    static void ScheduleReturn()
    {
        SessionState.SetBool(Key+"return",true);
        SessionState.SetString(Key+"returnDeadline",(EditorApplication.timeSinceStartup+180).ToString(System.Globalization.CultureInfo.InvariantCulture));
        EditorApplication.update -= ReturnAccount; EditorApplication.update += ReturnAccount;
    }
    static bool Expired(string suffix) => double.TryParse(SessionState.GetString(Key+suffix,""),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double time) && EditorApplication.timeSinceStartup>time;
    static bool OwnPlay => EditorApplication.isPlaying && Same(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),Account) && Same(IsolatedSavePlayGuard.ActiveDirectory,Account);
    static bool Same(string left,string right) => !string.IsNullOrEmpty(left) && string.Equals(Path.GetFullPath(left),right,StringComparison.OrdinalIgnoreCase);
    static void BootWatch()
    {
        if(!SessionState.GetBool(Key+"pending",false)) { EditorApplication.update-=BootWatch; return; }
        if(!Expired("deadline")) return;
        EditorApplication.update-=BootWatch;
        if(OwnPlay) { checks.Add("FAIL Verification boot/return deadline"); Finish(); }
        else if(!EditorApplication.isPlayingOrWillChangePlaymode) ScheduleReturn();
    }
    static void ReturnAccount()
    {
        if(Expired("returnDeadline")) { SessionState.SetString(Key+"status","RETURN_TIMEOUT"); EditorApplication.update-=ReturnAccount; return; }
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if(System.Diagnostics.Process.GetCurrentProcess().Id!=SessionState.GetInt(Key+"pid",0)) { EditorApplication.update-=ReturnAccount; return; }
        string environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if((!string.IsNullOrEmpty(environment)&&!Same(environment,Account)) || (!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)&&!Same(IsolatedSavePlayGuard.ActiveDirectory,Account))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))
        { SessionState.SetString(Key+"status","RETURN_DEFERRED"); EditorApplication.update-=ReturnAccount; return; }
        IsolatedSavePlayGuard.UseRealAccount();
        bool ready=!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))
            && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""))
            && SessionState.GetString(Key+"startScene","")==AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)
            && SessionState.GetString(Key+"scenes","")==OverburstShopUIVerifier.Scenes();
        SessionState.SetBool(Key+"pending",false); SessionState.SetBool(Key+"return",false);
        EditorApplication.update-=ReturnAccount; EditorApplication.update-=BootWatch;
        foreach(string key in new[]{"startScene","scenes","deadline","returnDeadline"})SessionState.EraseString(Key+key); SessionState.EraseInt(Key+"pid"); SessionState.EraseBool(Key+"flaskBindings");
        Write(Report.Replace("results","return"),new{status=ready?"PASS":"FAIL",ready,guard=IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),pending=false,callback=false,playing=EditorApplication.isPlayingOrWillChangePlaymode});
        if(!ready)SessionState.SetString(Key+"status","RETURN_FAILED");
    }
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate(); if(!OwnPlay || lastFrame==Time.frameCount)return; lastFrame=Time.frameCount;
        try { if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Shop verification timed out"); if(!work.MoveNext())Finish(); }
        catch(Exception e) { checks.Add("FAIL "+e); Finish(); }
    }
    static void Finish()
    {
        string status=errors.Count==0 && checks.All(c=>c.StartsWith("PASS "))?"PASS":"FAIL";
        SessionState.SetString(Key+"status",status); Write(Report,new{status,checks,errors}); EditorApplication.update-=Tick;
        if(OwnPlay)EditorApplication.ExitPlaymode(); else ScheduleReturn();
    }
    static IEnumerator Verify()
    {
        while(!PlayerContext.Instance || !PlayerContext.Instance.CurrentActorInventory || !WorldSessionState.IsHideout)yield return null;
        for(int i=0;i<20;i++)yield return null;
        if(SessionState.GetBool(Key+"flaskBindings",false)) VerifyFlaskBindings();
        var game=Object.FindFirstObjectByType<OverburstGameUI>(); var shop=Object.FindFirstObjectByType<ShopUI>(FindObjectsInactive.Include);
        Check(game && shop,"Persistent product UI loaded");
        var merchant=AssetDatabase.FindAssets("t:MerchantDefinition",new[]{"Assets/ProjectOverburst"}).Select(g=>AssetDatabase.LoadAssetAtPath<MerchantDefinition>(AssetDatabase.GUIDToAssetPath(g))).First(m=>m.Category==ShopCategory.Weapon);
        var npc=Object.FindObjectsByType<GeneralGoodsMerchantInteractable>(FindObjectsSortMode.None).First(n=>Field<MerchantDefinition>(n,"merchantDefinition")==merchant);
        var actor=PlayerContext.Instance.CurrentActor;
        var near=npc.transform.position+Vector3.forward;
        actor.transform.position=near; var rigidbody=actor.GetComponent<Rigidbody>(); if(rigidbody){rigidbody.position=near;rigidbody.linearVelocity=Vector3.zero;}
        for(int i=0;i<4;i++)yield return null;
        var inventory=PlayerContext.Instance.CurrentActorInventory; var currency=Object.FindFirstObjectByType<StashCurrencyService>();
        if(!SessionState.GetBool(Key+"restore",false))
        {
            foreach(var sample in ItemTypeIconVerifier.Samples().Take(8)) Check(inventory.AddItem(sample),"Isolated sample added "+sample.baseData.name);
            var data=AssetDatabase.FindAssets("t:ConsumableItemData",new[]{"Assets/ProjectOverburst"}).Select(g=>AssetDatabase.LoadAssetAtPath<ConsumableItemData>(AssetDatabase.GUIDToAssetPath(g))).First(d=>d.name=="MoveSpeedPotion" && !d.IsPermanentSingleItem && d.maxStack>=8);
            Check(inventory.AddItem(new ItemData(data,1,ItemGrade.Common,8)),"Split test stack added");
            SessionState.SetString(Key+"stack",AssetDatabase.GetAssetPath(data));
            Check(currency.TryAddCurrency(CurrencyType.Gold,100000),"Isolated purchase funds added");
        }
        var stackData=AssetDatabase.LoadAssetAtPath<ConsumableItemData>(SessionState.GetString(Key+"stack",""));
        var originalPosition=((RectTransform)game.inventoryWindow.transform).anchoredPosition;
        Check(npc.TryInteract(actor)==InteractionExecutionResult.Succeeded,"Actual merchant interaction opens shop within range"); for(int i=0;i<12;i++)yield return null;
        Check(shop.IsOpen && shop.IsTradeTabActive && game.inventory.IsVisible && game.inventory.InputToggleLocked,"Shop opens existing inventory and locks toggle");
        Check(((RectTransform)game.inventoryWindow.transform).anchoredPosition==new Vector2(OverburstUIShopSkin.InventoryX,OverburstUIShopSkin.WindowY),"Existing inventory at balanced third column");
        var panel=Field<GameObject>(shop,"shopPanel"); var stock=FieldArray<SlotUI>(shop,"merchantInventorySlots"); var mOffers=FieldArray<SlotUI>(shop,"merchantOfferSlots"); var pOffers=FieldArray<SlotUI>(shop,"playerOfferSlots");
        var service=Field<MerchantTradeService>(shop,"tradeService"); var menu=Field<ShopContextMenuController>(shop,"contextMenu");
        Check(stock.Length==35 && mOffers.Length==21 && pOffers.Length==21 && stock.Concat(mOffers).Concat(pOffers).All(s=>s&&s.GetComponent<OverburstUIItemSlotView>()),"All 77 native runtime slots bound");
        Check(stock[0].transform.parent.GetComponent<GridLayoutGroup>().constraintCount==6 && mOffers[0].transform.parent.GetComponent<GridLayoutGroup>().constraintCount==2 && pOffers[0].transform.parent.GetComponent<GridLayoutGroup>().constraintCount==2,"Runtime stock six columns and trade two columns");
        var main=Field<Button>(shop,"confirmButton");
        foreach(string buttonName in new[]{"ConfirmButton","ClearButton","CloseButton"})
        {
            var button=panel.transform.Find("TradeWindow/"+buttonName).GetComponent<Button>(); var hits=new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,button.transform.position)},hits);
            Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Button>()==button,"Action button receives top raycast "+buttonName);
        }
        var mainPointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,main.transform.position),button=PointerEventData.InputButton.Left};
        var hover=main.transform.Find("Approved Button Artwork/Hover Overlay").GetComponent<Image>();
        var press=main.transform.Find("Approved Button Artwork/Press Overlay").GetComponent<Image>();
        ExecuteEvents.Execute(main.gameObject,mainPointer,ExecuteEvents.pointerEnterHandler); float feedbackEnd=Time.realtimeSinceStartup+.2f;while(Time.realtimeSinceStartup<feedbackEnd)yield return null;
        Check(hover.canvasRenderer.GetColor().a>.5f,"Native hover feedback becomes visible"); Capture("product-button-hover.png");yield return null;
        ExecuteEvents.Execute(main.gameObject,mainPointer,ExecuteEvents.pointerDownHandler);feedbackEnd=Time.realtimeSinceStartup+.2f;while(Time.realtimeSinceStartup<feedbackEnd)yield return null;
        Check(press.canvasRenderer.GetColor().a>.5f,"Native press feedback becomes visible"); Capture("product-button-press.png");yield return null;
        ExecuteEvents.Execute(main.gameObject,mainPointer,ExecuteEvents.pointerUpHandler);EventSystem.current.SetSelectedGameObject(null);ExecuteEvents.Execute(main.gameObject,mainPointer,ExecuteEvents.pointerExitHandler);feedbackEnd=Time.realtimeSinceStartup+.2f;while(Time.realtimeSinceStartup<feedbackEnd)yield return null;
        Check(hover.canvasRenderer.GetColor().a<.1f && press.canvasRenderer.GetColor().a<.1f && service.Session.MerchantOffers.Count==0,"Native pointer feedback resets without creating a trade");
        var buy=stock.First(s=>s.DisplayItem!=null && shop.CanShopContextTrade(ShopContextMenuTarget.MerchantInventory,s));
        Check(shop.HandleShopSlotClicked(buy) && service.Session.MerchantOffers.Count==1,"Double-click stock adds offer");
        Check(Field<InventorySlotBridge>(game.inventory,"slotBridge").CanSortInventory(out _)
            && Field<TMP_Dropdown>(game.inventory,"sortDropdown").interactable
            && Field<Button>(game.inventory,"sortRefreshButton").interactable,"Merchant-only offer permits inventory sort");
        Check(mOffers[0].DisplayItem!=null && mOffers[0].GetComponentInChildren<ItemTypeIconView>(true).IsVisible,"Offer uses shared type icon");
        Check(shop.HandleShopSlotDrop(mOffers[0],buy) && service.Session.MerchantOffers.Count==0,"Drag offer back removes it");
        var inventorySlots=game.inventoryWindow.GetComponentsInChildren<SlotUI>(true);
        var stackSlot=inventorySlots.First(s=>s.DisplayItem?.baseData==stackData);
        int stackBefore=stackSlot.DisplayItem.stackCount;
        if(SessionState.GetBool(Key+"restore",false))Check(stackBefore==SessionState.GetInt(Key+"expectedStack",0),"Saved split source stack restored across Play");
        var pointer=new PointerEventData(EventSystem.current){position=new Vector2(Screen.width*.83f,Screen.height*.52f),button=PointerEventData.InputButton.Right};
        Check(menu.Open(ShopContextMenuTarget.PlayerInventory,stackSlot,pointer),"Right-click existing inventory opens shop menu");
        game.inventoryWindow.Focus(); for(int i=0;i<3;i++)yield return null;
        Check(shop.transform.GetSiblingIndex()>game.inventoryWindow.transform.GetSiblingIndex(),"Shop menu stays above refocused existing inventory");
        Capture("product-context.png"); for(int i=0;i<3;i++)yield return null;
        Field<Button>(menu,"splitTradeButton").onClick.Invoke(); for(int i=0;i<3;i++)yield return null;
        Check(Field<RectTransform>(menu,"splitPopupRoot").gameObject.activeInHierarchy,"Split popup bound");
        var results=new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,Field<Button>(menu,"splitOkButton").transform.position)},results);
        Check(results.Count>0 && results[0].gameObject.GetComponentInParent<Button>()==Field<Button>(menu,"splitOkButton"),"Split confirm receives top raycast");
        Field<TMP_InputField>(menu,"splitAmountInput").text="3"; Capture("product-split.png"); for(int i=0;i<3;i++)yield return null;
        Field<Button>(menu,"splitOkButton").onClick.Invoke();
        Check(service.Session.PlayerOffers.Count==1 && service.Session.PlayerOffers[0].StackCount==3 && stackSlot.DisplayItem.stackCount==stackBefore,"Split reserves 3 without changing source stack");
        Check(pOffers[0].DisplayItem!=null,"Player offer shown in shared slot");
        VerifyPendingOfferSortRestriction(game, shop, service);
        for(int i=0;i<3;i++)yield return null;
        Field<Button>(shop,"confirmButton").onClick.Invoke();
        Check(service.Session.PlayerOffers.Count==0 && inventory.Items.Where(i=>i?.baseData==stackData).Sum(i=>i.stackCount)==stackBefore-3,"Confirm sells split and updates inventory");
        Check(Field<TMP_Dropdown>(game.inventory,"sortDropdown").interactable
            && Field<Button>(game.inventory,"sortRefreshButton").interactable,"Completing sell restores sort controls");
        int totalBefore=service.GetTotalGoldAmount(); int boughtBefore=inventory.Items.Count(i=>i?.baseData==buy.DisplayItem.baseData); var boughtData=buy.DisplayItem.baseData;
        Check(shop.HandleShopSlotDrop(buy,mOffers[0]),"Drag stock to merchant offer"); int cost=service.GetAutoGoldCost();
        Check(cost>0 && service.ValidateTrade().success,"Valid purchase against isolated funds");
        Field<Button>(shop,"confirmButton").onClick.Invoke();
        Check(service.Session.MerchantOffers.Count==0 && service.GetTotalGoldAmount()==totalBefore-cost && inventory.Items.Count(i=>i?.baseData==boughtData)==boughtBefore+1,"Confirm purchase commits item and payment");
        foreach(var scroll in panel.GetComponentsInChildren<ScrollRect>(true))
        {
            scroll.verticalNormalizedPosition=0; for(int i=0;i<3;i++)yield return null;
            Check(Mathf.Abs(scroll.content.anchoredPosition.y-(scroll.content.rect.height-scroll.viewport.rect.height))<2,"Scroll reaches last row "+scroll.name);
            scroll.verticalNormalizedPosition=1;
        }
        for(int i=0;i<3;i++)yield return null; Capture(SessionState.GetBool(Key+"restore",false)?"product-play02.png":"product-trade.png"); for(int i=0;i<3;i++)yield return null;
        Field<Button>(shop,"confirmButton").onClick.Invoke(); for(int i=0;i<3;i++)yield return null;
        Check(Field<GameObject>(shop,"failurePopupRoot").activeInHierarchy && !Field<Button>(shop,"confirmButton").interactable,"Validation failure opens modal and disables trade confirm");
        Capture("product-failure.png"); for(int i=0;i<3;i++)yield return null;
        Field<Button>(shop,"failurePopupConfirmButton").onClick.Invoke();
        Check(!Field<GameObject>(shop,"failurePopupRoot").activeSelf && Field<Button>(shop,"confirmButton").interactable,"Failure popup confirm restores trade");
        Field<Button>(shop,"questTabButton").onClick.Invoke(); for(int i=0;i<3;i++)yield return null;
        Check(!shop.IsTradeTabActive && Field<GameObject>(shop,"questListRoot").activeInHierarchy && panel.transform.Find("Approved Tabs").gameObject.activeInHierarchy,"Quest keeps common tabs visible"); Capture("product-quest.png"); for(int i=0;i<3;i++)yield return null;
        Field<Button>(shop,"firstSpecialtyTabButton").onClick.Invoke(); for(int i=0;i<3;i++)yield return null;
        Check(Field<GameObject>(shop,"specialtyListRoot").activeInHierarchy,"Weapon specialty tab bound"); Capture("product-specialty.png"); for(int i=0;i<3;i++)yield return null;
        Field<Button>(shop,"tradeTabButton").onClick.Invoke(); for(int i=0;i<3;i++)yield return null;
        var close=panel.transform.Find("TradeWindow/Window Chrome/Header/Button (Close)").GetComponent<Button>(); close.onClick.Invoke();
        for(int i=0;i<3;i++)yield return null;
        Check(!shop.IsOpen && !game.inventory.InputToggleLocked && ((RectTransform)game.inventoryWindow.transform).anchoredPosition==originalPosition,"Header close restores inventory and position");
        for(int run=0;run<3;run++)
        {
            shop.Open(merchant); for(int i=0;i<3;i++)yield return null;
            Check(shop.IsOpen && shop.IsTradeTabActive,"Repeated shop open "+run);
            Field<Button>(shop,"clearButton").onClick.Invoke(); close.onClick.Invoke();
            Check(!shop.IsOpen && service.Session.MerchantOffers.Count==0 && service.Session.PlayerOffers.Count==0,"Repeated close clears session "+run);
            Check(Field<InventorySlotBridge>(game.inventory,"slotBridge").CanSortInventory(out _)
                && Field<TMP_Dropdown>(game.inventory,"sortDropdown").interactable
                && Field<Button>(game.inventory,"sortRefreshButton").interactable,"Shop close leaves sort unlocked "+run);
        }
        // Persisted account restores the same surviving stack on the second Play.
        SessionState.SetInt(Key+"expectedStack",inventory.Items.Where(i=>i?.baseData==stackData).Sum(i=>i.stackCount));
        for(int i=0;i<15;i++)yield return null;
    }
    static void VerifyFlaskBindings()
    {
        var account=Overburst.Persistence.AccountGameplaySession.Current;
        var inventory=PlayerContext.Instance.CurrentActorInventory;
        var controller=PlayerFlaskController.Current;
        var registry=Resources.Load<Overburst.Persistence.AccountContentRegistry>(Overburst.Persistence.AccountContentRegistry.ResourcePath);
        Check(account!=null && controller && inventory,"Flask regression uses actual account actor and controller");
        ItemData Flask(FlaskKind kind) => new ItemData(registry.Entries.Select(e=>e.asset).OfType<FlaskItemData>().First(d=>d.kind==kind),1,ItemGrade.Common);
        var first=Flask(FlaskKind.Berserker); var second=Flask(FlaskKind.Giant);
        Check(inventory.AddItem(first) && inventory.AddItem(second),"Flask fixture items committed to isolated account");
        Check(controller.TryEquip(0,first,out _) && controller.TryEquip(1,second,out _),"Flasks equipped through actual account commands");
        string firstId=first.runtimeInstanceId,secondId=second.runtimeInstanceId;
        Check(controller.TryUse(0,out string firstReason),"First actual flask use: "+firstReason);
        Check(controller.TryUse(1,out string secondReason),"Second actual flask use: "+secondReason);
        Check(controller.Effects.Contains(firstId) && controller.Effects.Contains(secondId),"Both actual effects active before removal");
        Check(!account.Execute(()=>{inventory.RemoveItem(controller.GetItem(0));return false;}),"Failed account removal rolls back");
        Check(controller.GetItem(0)?.runtimeInstanceId==firstId && controller.Effects.Contains(firstId) && controller.Effects.Contains(secondId),"Rollback retains binding and both active effects");
        var service=Object.FindFirstObjectByType<MerchantTradeService>();
        var merchant=AssetDatabase.FindAssets("t:MerchantDefinition",new[]{"Assets/ProjectOverburst"}).Select(g=>AssetDatabase.LoadAssetAtPath<MerchantDefinition>(AssetDatabase.GUIDToAssetPath(g))).First(m=>m.Category==ShopCategory.GeneralGoods);
        service.Open(merchant);
        var selling=controller.GetItem(0); int slot=Enumerable.Range(0,inventory.Capacity).First(i=>inventory.GetItemAt(i)==selling);
        var plan=MerchantTradeTransactionPlan.Create(new List<MerchantTradeOffer>(),new List<MerchantTradeOffer>{new MerchantTradeOffer(MerchantTradeOfferSide.Player,slot,selling)},0,1);
        var currency=Object.FindFirstObjectByType<StashCurrencyService>();
        Check(new MerchantTradeValidator().ValidateCurrent(plan,inventory,service.MerchantInventory,currency).success,"Active flask sale preflight accepts actual stock");
        Check(new MerchantTradeCommitter().Commit(plan,inventory,service.MerchantInventory,currency).success,"Actual merchant commit sells active flask");
        Check(controller.GetItem(0)==null && !controller.Effects.Contains(firstId),"Committed cleared binding removes sold flask effect");
        Check(controller.GetItem(1)?.runtimeInstanceId==secondId && controller.Effects.Contains(secondId),"Other equipped flask and active effect survive sale");
        var replacement=Flask(FlaskKind.Berserker);
        Check(inventory.AddItem(replacement) && controller.TryEquip(0,replacement,out _) && controller.TryUse(0,out _),"Vacated slot supports equip and use again");
        Check(controller.TryUnequip(0,out _) && !controller.Effects.Contains(replacement.runtimeInstanceId) && controller.Effects.Contains(secondId),"Explicit unequip clears only its own effect");
        inventory.RemoveItem(controller.GetItem(1)); inventory.RemoveItem(inventory.Items.First(i=>i?.runtimeInstanceId==replacement.runtimeInstanceId));
        Check(controller.GetItem(1)==null && controller.Effects.Count==0,"Actual inventory removal clears final active flask");
        service.Close();
    }
    static void VerifyPendingOfferSortRestriction(OverburstGameUI game, ShopUI shop, MerchantTradeService service)
    {
        var ui=game.inventory;
        var bridge=Field<InventorySlotBridge>(ui,"slotBridge");
        var dropdown=Field<TMP_Dropdown>(ui,"sortDropdown");
        var direction=Field<Button>(ui,"sortRefreshButton");
        var inventory=service.PlayerInventory;
        var account=Overburst.Persistence.AccountGameplaySession.Current;
        Check(bridge && dropdown && direction && account!=null,"Sort regression uses product controls and account");
        var items=inventory.Items.ToArray();
        var stacks=items.Select(i=>i!=null?i.stackCount:0).ToArray();
        long revision=account.Revision;
        int mode=dropdown.value;
        var directionField=typeof(InventoryUI).GetField("sortDirection",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        object sortDirection=directionField.GetValue(ui);
        int changed=0;
        Action changedHandler=()=>changed++;
        inventory.Changed+=changedHandler;
        try
        {
            Check(ShopUI.HasPendingPlayerOffers(inventory) && !bridge.CanSortInventory(out string reason)
                && reason==ShopUI.PendingPlayerOffersSortMessage,"Partial sell blocks this inventory sort");
            Check(!dropdown.interactable && !direction.interactable,"Both sort controls disabled during sell");
            dropdown.value=(mode+1)%dropdown.options.Count;
            direction.onClick.Invoke();
            Check(dropdown.value==mode && sortDirection.Equals(directionField.GetValue(ui)),"Queued sort callbacks retain mode and direction");
            Check(!bridge.SortInventory(ItemSortMode.Grade,ItemSortDirection.Ascending),"Bridge direct sort rejected");
            ui.SetVisible(false); ui.SetVisible(true);
            shop.ShowQuestTab();
            Check(ShopUI.HasPendingPlayerOffers(inventory) && !bridge.CanSortInventory(out _),"Clearing quest-tab selection retains offer sort lock");
            shop.ShowTradeTab();
            Check(!dropdown.interactable && !direction.interactable,"Trade-tab reopen retains sort lock");
            Check(Field<TextMeshProUGUI>(shop,"statusText").text.Contains(ShopUI.PendingPlayerOffersSortMessage),"Visible trade status explains sort lock");
            shop.ShowShopContextStatus("SORT_REGRESSION_ERROR");
            ui.RefreshSortAvailability();
            Check(Field<TextMeshProUGUI>(shop,"statusText").text=="SORT_REGRESSION_ERROR","Availability refresh preserves trade error status");
            Check(account.Revision==revision && changed==0,"Rejected sorts emit no account revision or inventory change");
            Check(inventory.Items.Count==items.Length && Enumerable.Range(0,items.Length).All(i=>
                ReferenceEquals(items[i],inventory.Items[i]) && (inventory.Items[i]!=null?inventory.Items[i].stackCount:0)==stacks[i]),"Rejected sorts preserve source slots and stack amounts");
            var offer=service.Session.PlayerOffers[0];
            var sourceSlot=FieldArray<SlotUI>(bridge,"inventorySlots")[offer.SourceSlotIndex];
            Check(shop.HandleShopContextRemoveOffer(ShopContextMenuTarget.PlayerOffer,FieldArray<SlotUI>(shop,"playerOfferSlots")[0])
                && bridge.CanSortInventory(out _) && dropdown.interactable && direction.interactable,"Removing last sell offer restores sort controls");
            Check(shop.HandleShopContextSplitTrade(ShopContextMenuTarget.PlayerInventory,sourceSlot,offer.StackCount),"Re-add partial sell for clear test");
            Field<Button>(shop,"clearButton").onClick.Invoke();
            Check(bridge.CanSortInventory(out _) && dropdown.interactable && direction.interactable,"Clearing trade restores sort controls");
            Check(shop.HandleShopContextSplitTrade(ShopContextMenuTarget.PlayerInventory,sourceSlot,offer.StackCount)
                && !bridge.CanSortInventory(out _),"Re-add original partial sell for normal confirmation");
            shop.ShowTradeTab();
        }
        finally { inventory.Changed-=changedHandler; }
    }

    static T Field<T>(Object source,string name) where T:Object => (T)new SerializedObject(source).FindProperty(name).objectReferenceValue;
    static T[] FieldArray<T>(Object source,string name) where T:Object { var array=new SerializedObject(source).FindProperty(name); return Enumerable.Range(0,array.arraySize).Select(i=>(T)array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray(); }
    static void Capture(string file) => ScreenCapture.CaptureScreenshot(Path.Combine(VerificationOutput,file));
    static void Check(bool pass,string message) { checks.Add((pass?"PASS ":"FAIL ")+message); if(!pass)throw new InvalidOperationException(message); }
    static void Log(string message,string trace,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message); }
}
