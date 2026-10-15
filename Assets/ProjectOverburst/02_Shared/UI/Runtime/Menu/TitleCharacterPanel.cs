using System;
using System.Collections;
using System.IO;
using System.Linq;
using Overburst.Appearance;
using Overburst.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;

public sealed class TitleCharacterPanel : MonoBehaviour
{
    public const string ResourcePath="UI/Title/PF_OverburstTitle";
    public static TitleCharacterPanel Instance {get;private set;}
    public Canvas canvas;
    public bool managementPreview;
    public RawImage background;
    public Texture titleBackground,charactersBackground,classesBackground;
    public Material titleMaterial,charactersMaterial,classesMaterial;
    public GameObject titleScreen,charactersScreen,classesScreen,nameDialog;
    public Button start,settings,quit,back,create,enter,classNext,nameNext,nameBack,nameClose;
    public Button[] classButtons;
    public TMP_Text header,subtitle,className,classDescription,classAvailability,feedback,characterName,characterDetails;
    public TMP_Text classEnglish,classRole,classWeapon,rosterCount,characterLevel,characterLocation,characterTime,characterLast,characterElement,nameCounter,nameError;
    public TMP_Text[] classCaptions;
    public TMP_InputField nameInput;
    public RawImage modalBackdrop;
    public GameObject quitDialog;
    public Button quitCancel,quitConfirm;
    public RawImage quitBackdrop;
    public Material modalBlur;
    public RectTransform rosterContent;
    public Button rosterTemplate;
    public RawImage[] classTargets;
    public AppearanceCharacterPreview[] classPreviews;
    public RawImage rosterTarget;
    public AppearanceCharacterPreview rosterPreview;
    public CharacterAppearanceCatalog catalog;
    private readonly string[] classIds={"knight","mage","archer"},classNames={"기사","마법사","궁수"};
    private readonly int[] outfits={6,9,10};
    private EasySaveAccountStore store;
    private string saveDirectory;
    private AccountContentRegistry registry;
    private AccountSnapshot snapshot;
    private string selectedId,draftId,draftName;
    private int selectedClass;
    private string screen="title";
    private bool wired,entering,settingsOwned,renaming;
    private float rotation,rotationFrom,rotationTo,rotationElapsed;
    private bool rotating;
    private int modalClosedFrame=-1;
    private readonly float[] castDepth=new float[3];
    private RenderTexture modalTexture;
    private RawImage modalTarget;
    private InputAction navigate;
    private Vector2 previousNavigate;
    public bool ReadyForGameplay=>entering;
    public string Screen=>screen;
    public string SelectedId=>selectedId;
    public bool NameDialogOpen=>nameDialog&&nameDialog.activeSelf;
    public int ModalClosedFrame=>modalClosedFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()=>Instance=null;
    public static TitleCharacterPanel Install()
    {
        if(Instance)return Instance;
        var prefab=Resources.Load<TitleCharacterPanel>(ResourcePath);
        return prefab?Instantiate(prefab):null;
    }
    void Awake(){if(managementPreview)return;Instance=this;Wire();}
    void Wire()
    {
        if(wired)return;wired=true;
        foreach(var button in new[]{start,settings,quit,back,create,classNext,nameNext,nameBack,nameClose,quitCancel,quitConfirm}.Concat(classButtons))
            if(button)button.onClick.AddListener(OverburstGameMenu.PlaySelectionClick);
        start.onClick.AddListener(()=>{if(Reload())Show("characters");});
        create.onClick.AddListener(()=>{draftId=null;nameInput.text="";selectedClass=0;Show("classes");});
        back.onClick.AddListener(Back);
        enter.onClick.AddListener(Enter);
        classNext.onClick.AddListener(OpenName);
        nameNext.onClick.AddListener(OpenAppearance);
        nameBack.onClick.AddListener(CloseName);
        nameClose.onClick.AddListener(()=>nameBack.onClick.Invoke());
        nameInput.onSubmit.AddListener(_=>{OverburstGameMenu.PlaySelectionClick();OpenAppearance();});
        nameInput.onValueChanged.AddListener(value=>{nameCounter.text=value.Length+" / 12";nameError.text="";});
        nameInput.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnDown=nameNext,selectOnUp=nameClose};
        nameNext.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=nameInput,selectOnLeft=nameBack};nameBack.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=nameInput,selectOnRight=nameNext};nameClose.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnDown=nameInput};
        quitCancel.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnRight=quitConfirm};quitConfirm.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=quitCancel};
        for(int i=0;i<classButtons.Length;i++){int index=i;classButtons[i].onClick.AddListener(()=>SelectClass(index));}
        settings.onClick.AddListener(OpenSettings);
        quit.onClick.AddListener(OpenQuit);
        quitCancel.onClick.AddListener(CloseQuit);
        quitConfirm.onClick.AddListener(()=>{
#if UNITY_EDITOR
            if(Application.isPlaying)UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit();
#endif
        });
    }
    public void Open(string directory)
    {
        Wire();registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        saveDirectory=directory;store=new EasySaveAccountStore(directory);entering=false;
        navigate=PlayerInputFacade.Current?.RuntimeAsset?.FindAction("UI/Navigate",false);previousNavigate=Vector2.zero;
        if(Application.isPlaying){GameplayInputBlocker.Block(this);PlayerInputFacade.Current?.SetGameplayMapDisabled(this,true);}
        Reload();Show("title");
    }
    public IEnumerator WaitForSelection()
    {
        Open(AccountBootstrap.ResolveSaveDirectory());
        while(!entering)yield return null;
        CloseVisuals();GameplayInputBlocker.Unblock(this);PlayerInputFacade.ReleaseGameplayMapDisabled(this);
        store=null;snapshot=null;
        gameObject.SetActive(false);Destroy(gameObject);
    }
    bool Reload()
    {
        try
        {
            snapshot=MapAccountMigration.Load(store,registry);
            if(snapshot!=null)
            {
                AccountInvariants.Validate(snapshot,registry);
                if(snapshot.run!=null&&snapshot.run.phase!=RunPhase.Extracted&&snapshot.run.phase!=RunPhase.Failed)
                {
                    var recovery=new AccountTransactions(snapshot,store,registry);
                    recovery.Execute("recover-"+snapshot.run.runId,snapshot.revision,c=>AccountRunCommands.RecoverInterrupted(c),true);snapshot=recovery.Read();
                }
            }
            SetFeedback("");return true;
        }
        catch(Exception error){SetFeedback("저장을 불러오지 못했습니다. 파일을 보존했습니다. 다시 시도해 주세요.\n"+error.Message);return false;}
    }
    void SetFeedback(string message)
    {
        feedback.text=message;
        if(nameDialog.activeSelf&&nameError)nameError.text=message;
        if(AppearanceCustomizationPanel.Instance?.IsCreation==true)AppearanceCustomizationPanel.Instance.SetFeedback(message);
    }
    public void Show(string next)
    {
        CloseVisuals();screen=next;nameDialog.SetActive(false);
        titleScreen.SetActive(next=="title");charactersScreen.SetActive(next=="characters");classesScreen.SetActive(next=="classes");
        back.gameObject.SetActive(next!="title");
        ((RectTransform)back.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,next=="characters"?48:52);
        header.transform.parent.gameObject.SetActive(next!="title");subtitle.gameObject.SetActive(next=="classes");
        header.text=next=="classes"?"클래스 선택":"캐릭터 선택";
        subtitle.text=next=="classes"?"당신의 모험을 시작할 캐릭터를 선택하세요.":"나의 캐릭터";
        background.texture=next=="title"?titleBackground:next=="classes"?classesBackground:charactersBackground;
        var image=background.texture;float visible=(image.width/(float)image.height)/(1920f/1080);
        float width=Mathf.Min(1,1/visible),height=Mathf.Min(1,visible);
        background.uvRect=next=="title"?new Rect((1+width)/2,(1-height)/2,-width,height):new Rect((1-width)/2,(1-height)/2,width,height);
        background.material=next=="title"?titleMaterial:next=="classes"?classesMaterial:charactersMaterial;background.color=Color.white;
        if(next=="characters")RenderRoster();
        if(next=="classes")
        {
            rotation=-selectedClass*120;rotating=false;
            for(int i=0;i<3;i++)
            {
                var session=new AppearanceCustomizationSession(catalog,null);session.ShowEquipment(catalog.equipmentExamples[outfits[i]-1].id);
                classPreviews[i].Open(catalog,session,classTargets[i],new Vector2Int(768,1152),new Vector3(512+i*32,512,512));
                classTargets[i].raycastTarget=false;
            }
            RefreshClass();PlaceCast();Select(classButtons[selectedClass]);
        }
        if(next=="title")Select(start);
    }
    void RenderRoster()
    {
        for(int i=rosterContent.childCount-1;i>=0;i--){var child=rosterContent.GetChild(i).gameObject;if(child!=rosterTemplate.gameObject){child.SetActive(false);if(Application.isPlaying)Destroy(child);else DestroyImmediate(child);}}
        var list=snapshot?.characters??(snapshot!=null?new System.Collections.Generic.List<AccountCharacterSnapshot>{new AccountCharacterSnapshot{id=AccountCharacterCommands.LegacyId,name="기존 캐릭터"}}:new System.Collections.Generic.List<AccountCharacterSnapshot>());
        if(!list.Exists(c=>c.id==selectedId))selectedId=list.FirstOrDefault()?.id;
        int index=0;
        foreach(var entry in list)
        {
            var captured=entry.id;var button=Instantiate(rosterTemplate,rosterContent);button.name="Character "+entry.id;button.gameObject.SetActive(true);
            var rect=(RectTransform)button.transform;rect.anchoredPosition=new Vector2(0,-index++*154f);
            var personal=entry.id==(snapshot.activeCharacterId??AccountCharacterCommands.LegacyId)?snapshot:entry.personal;
            button.transform.Find("Name").GetComponent<TMP_Text>().text=entry.name;
            button.transform.Find("Info").GetComponent<TMP_Text>().text="Lv. "+personal.level+"  ·  기사  ·  "+Element(personal);
            button.transform.Find("Avatar").GetComponent<Image>().sprite=catalog.faces.FirstOrDefault(f=>f.id==personal.appearance?.faceId)?.thumbnail??catalog.faces[0].thumbnail;
            var visual=button.GetComponent<AppearanceButtonVisual>();if(visual)visual.SetSelected(entry.id==selectedId);
            button.onClick.AddListener(OverburstGameMenu.PlaySelectionClick);
            button.onClick.AddListener(()=>{selectedId=captured;Show("characters");});
        }
        rosterContent.sizeDelta=new Vector2(rosterContent.sizeDelta.x,Mathf.Max(140f,index*154f-14f));
        rosterCount.text=index+"명";
        ((RectTransform)create.transform).anchoredPosition=new Vector2(92,-(251.34f+Mathf.Clamp(index*154f-14f,0,456)+14f));
        enter.interactable=selectedId!=null;characterName.text=list.FirstOrDefault(c=>c.id==selectedId)?.name??"새로운 모험";
        characterDetails.text=selectedId==null?"캐릭터를 생성해 모험을 시작하세요.":"기사 · 대검";
        characterLevel.text="";characterLocation.text="은신처";characterTime.text="—";characterLast.text="—";characterElement.text="없음";
        if(selectedId!=null)
        {
            var entry=list.Single(c=>c.id==selectedId);var personal=entry.id==(snapshot.activeCharacterId??AccountCharacterCommands.LegacyId)?snapshot:entry.personal;
            characterLevel.text="Lv. "+personal.level;characterElement.text=Element(personal);
            var session=new AppearanceCustomizationSession(catalog,personal.appearance);
            session.ShowEquipment(catalog.equipmentExamples[5].id);
            rosterPreview.Open(catalog,session,rosterTarget);rosterTarget.raycastTarget=true;
        }
        Select(selectedId==null?create:enter);
    }
    string Element(AccountSnapshot personal)
    {
        var item=personal.items.Find(i=>i.instanceId==personal.elementalGemInstanceId);
        if(item==null)return "없음";
        var gem=registry.Resolve<ElementGemItemData>(item.contentId);
        return gem.element switch {WeaponElement.Fire=>"화염",WeaponElement.Ice=>"빙결",WeaponElement.Electric=>"번개",WeaponElement.Light=>"빛",WeaponElement.Dark=>"어둠",_=>"없음"};
    }
    public void SelectClass(int index)
    {
        if(index<0||index>2)return;
        if(index!=selectedClass)OverburstGameMenu.PlaySelectionClick();
        selectedClass=index;
        rotationFrom=rotation;rotationTo=rotation+Mathf.DeltaAngle(rotation,-120*index);rotationElapsed=0;rotating=Mathf.Abs(rotationTo-rotation)>.01f;
        RefreshClass();
    }
    void RefreshClass()
    {
        className.text=classNames[selectedClass];
        classEnglish.text=selectedClass==0?"KNIGHT":selectedClass==1?"MAGE":"ARCHER";
        classRole.text=selectedClass==0?"근접 전투":selectedClass==1?"마법 전투":"원거리 전투";
        classWeapon.text=selectedClass==0?"대검":selectedClass==1?"마법":"활";
        for(int i=0;i<3;i++)
        {
            var visual=classButtons[i].GetComponent<AppearanceButtonVisual>();
            if(visual){visual.normalLabel=new Color(.72f,.635f,.506f,.72f);visual.selectedLabel=new Color(.82f,.68f,.40f);visual.SetSelected(i==selectedClass);}
            else classCaptions[i].color=i==selectedClass?new Color(.82f,.68f,.40f):new Color(.72f,.635f,.506f,.72f);
        }
        classDescription.text=selectedClass==0?"강철 갑옷을 입고 대검을 휘두르는 근접 전투 클래스.":selectedClass==1?"마법으로 전장에 맞서는 클래스. 추후 업데이트로 추가됩니다.":"활을 사용하는 원거리 전투 클래스. 추후 업데이트로 추가됩니다.";
        classAvailability.text=selectedClass==0?"현재 생성 가능":"개발 중 · 추후 추가 예정";
        classNext.interactable=selectedClass==0;classNext.GetComponentInChildren<TMP_Text>().text=selectedClass==0?"기사로 생성":"개발 중";
        foreach(var button in classButtons)button.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnDown=classNext.interactable?classNext:back,selectOnUp=back};
        classNext.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=classButtons[selectedClass],selectOnLeft=back};back.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=classButtons[selectedClass],selectOnRight=classNext.interactable?classNext:classButtons[selectedClass]};
    }
    void PlaceCast()
    {
        for(int i=0;i<3;i++)
        {
            float angle=(120*i+rotation)*Mathf.Deg2Rad,depth=Mathf.Cos(angle),scale=.74f+.13f*depth;
            var rect=(RectTransform)classButtons[i].transform;
            castDepth[i]=depth;
            rect.anchoredPosition=new Vector2(744+396*Mathf.Sin(angle),-(835.2f+42*depth+44.4f*scale));rect.localScale=Vector3.one*scale;
            classTargets[i].color=new Color(.72f+.3f*depth,.72f+.3f*depth,.72f+.3f*depth,1);
            classPreviews[i].SetViewYaw(180-35*Mathf.Sin(angle));
        }
        int a=0,b=1,c=2;
        if(castDepth[a]>castDepth[b]){int swap=a;a=b;b=swap;}
        if(castDepth[b]>castDepth[c]){int swap=b;b=c;c=swap;}
        if(castDepth[a]>castDepth[b]){int swap=a;a=b;b=swap;}
        classButtons[a].transform.SetAsLastSibling();classButtons[b].transform.SetAsLastSibling();classButtons[c].transform.SetAsLastSibling();
    }
    void Update()
    {
        if(managementPreview)return;
        if(entering)return;
        if(Application.isPlaying)PlayerInputFacade.Current?.SetGameplayMapDisabled(this,true);
        var movement=navigate?.ReadValue<Vector2>()??Vector2.zero;
        if(screen=="classes"&&!NameDialogOpen&&!AppearanceCustomizationPanel.IsOpen&&Mathf.Abs(movement.x)>.5f&&Mathf.Abs(previousNavigate.x)<=.5f)
        {
            SelectClass((selectedClass+(movement.x>0?1:2))%3);Select(classButtons[selectedClass]);
        }
        previousNavigate=movement;
        if(screen=="classes"&&rotating)
        {
            rotationElapsed+=Time.unscaledDeltaTime;float t=Mathf.Clamp01(rotationElapsed/1.45f),ease=t*t*t*(t*(t*6-15)+10);
            rotation=Mathf.LerpUnclamped(rotationFrom,rotationTo,ease);rotating=t<1;PlaceCast();
        }
        if(PlayerInputFacade.Current?.UiCancelPressedThisFrame==true&&Time.frameCount!=modalClosedFrame
            &&Time.frameCount!=AppearanceCustomizationPanel.LastClosedFrame&&!OverburstGameMenu.IsOpen&&(NameDialogOpen||!AppearanceCustomizationPanel.IsOpen))Back();
    }
    void OpenName(){if(selectedClass!=0)return;renaming=false;ShowName();}
    void RenameCreation(){renaming=true;nameInput.text=draftName;ShowName();}
    void ShowName(){CaptureModalBackdrop();nameDialog.SetActive(true);var modalCanvas=nameDialog.GetComponent<Canvas>();modalCanvas.overrideSorting=true;modalCanvas.sortingOrder=400;var window=nameDialog.transform.Find("Window");window.Find("English").GetComponent<TMP_Text>().text=renaming?"CHARACTER NAME":"NEW CHARACTER";window.Find("Heading").GetComponent<TMP_Text>().text=renaming?"이름 변경":"이름 지정";window.Find("Intro").GetComponent<TMP_Text>().text=renaming?"캐릭터 이름을 변경해 주세요.":"기사 캐릭터의 이름을 정해 주세요.";nameBack.GetComponentInChildren<TMP_Text>().text=renaming?"취소":"클래스 선택으로";nameNext.GetComponentInChildren<TMP_Text>().text=renaming?"이름 변경  ›":"외모 설정으로  ›";nameError.text="";Select(nameInput);nameInput.ActivateInputField();}
    void CloseName(){nameDialog.SetActive(false);ReleaseModalBackdrop();modalClosedFrame=Time.frameCount;if(renaming)Select(AppearanceCustomizationPanel.Instance?.creationRename);else Select(classNext);renaming=false;}
    void OpenQuit(){CaptureModalBackdrop(quitBackdrop);quitDialog.SetActive(true);var modalCanvas=quitDialog.GetComponent<Canvas>();modalCanvas.overrideSorting=true;modalCanvas.sortingOrder=400;Select(quitCancel);}
    void CloseQuit(){quitDialog.SetActive(false);ReleaseModalBackdrop();modalClosedFrame=Time.frameCount;Select(quit);}
    public void CaptureCreationConfirmation(RawImage target)=>CaptureModalBackdrop(target);
    public void ReleaseCreationConfirmation(){ReleaseModalBackdrop();modalClosedFrame=Time.frameCount;}
    void CaptureModalBackdrop(RawImage target=null)
    {
        ReleaseModalBackdrop();modalTarget=target?target:modalBackdrop;var savedMode=canvas.renderMode;var savedCamera=canvas.worldCamera;float savedDistance=canvas.planeDistance;
        var appearance=AppearanceCustomizationPanel.Instance??GetComponentInChildren<AppearanceCustomizationPanel>(true);bool captureAppearance=appearance&&appearance.IsCreation&&appearance.Session!=null;
        var appearanceMode=captureAppearance?appearance.rootCanvas.renderMode:RenderMode.ScreenSpaceOverlay;var appearanceCamera=captureAppearance?appearance.rootCanvas.worldCamera:null;float appearanceDistance=captureAppearance?appearance.rootCanvas.planeDistance:0;
        GameObject cameraRoot=null;RenderTexture source=null,intermediate=null;var savedActive=RenderTexture.active;
        try
        {
            int width=savedCamera&&savedCamera.targetTexture?savedCamera.targetTexture.width:UnityEngine.Screen.width;
            int height=savedCamera&&savedCamera.targetTexture?savedCamera.targetTexture.height:UnityEngine.Screen.height;
            width=Mathf.Max(width,1);height=Mathf.Max(height,1);
            cameraRoot=new GameObject("Owned Title Modal Capture",typeof(Camera)){hideFlags=HideFlags.DontSave};UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot,gameObject.scene);
            var capture=cameraRoot.GetComponent<Camera>();capture.enabled=false;capture.scene=gameObject.scene;capture.transform.position=new Vector3(0,0,-20);capture.orthographic=true;capture.orthographicSize=5;capture.nearClipPlane=.01f;capture.farClipPlane=100;capture.cullingMask=1<<5;capture.clearFlags=CameraClearFlags.SolidColor;capture.backgroundColor=Color.black;
            source=RenderTexture.GetTemporary(width,height,24);capture.targetTexture=source;
            canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=capture;canvas.planeDistance=10;
            if(captureAppearance){appearance.rootCanvas.renderMode=RenderMode.ScreenSpaceCamera;appearance.rootCanvas.worldCamera=capture;appearance.rootCanvas.planeDistance=10;}
            Canvas.ForceUpdateCanvases();var request=new UniversalRenderPipeline.SingleCameraRequest{destination=source};if(RenderPipeline.SupportsRenderRequest(capture,request))RenderPipeline.SubmitRenderRequest(capture,request);else capture.Render();
            intermediate=RenderTexture.GetTemporary(width,height,0);modalTexture=new RenderTexture(width,height,0){name="Owned Title Modal Backdrop",hideFlags=HideFlags.DontSave};modalTexture.Create();
            Graphics.Blit(source,intermediate,modalBlur,0);Graphics.Blit(intermediate,modalTexture,modalBlur,1);modalTarget.texture=modalTexture;
        }
        finally
        {
            canvas.renderMode=savedMode;canvas.worldCamera=savedCamera;canvas.planeDistance=savedDistance;
            if(captureAppearance){appearance.rootCanvas.renderMode=appearanceMode;appearance.rootCanvas.worldCamera=appearanceCamera;appearance.rootCanvas.planeDistance=appearanceDistance;}
            RenderTexture.active=savedActive;
            if(source)RenderTexture.ReleaseTemporary(source);if(intermediate)RenderTexture.ReleaseTemporary(intermediate);
            if(cameraRoot){cameraRoot.SetActive(false);if(Application.isPlaying)Destroy(cameraRoot);else DestroyImmediate(cameraRoot);}Canvas.ForceUpdateCanvases();
        }
    }
    void ReleaseModalBackdrop(){if(modalTarget)modalTarget.texture=null;modalTarget=null;if(modalTexture){if(RenderTexture.active==modalTexture)RenderTexture.active=null;modalTexture.Release();if(Application.isPlaying)Destroy(modalTexture);else DestroyImmediate(modalTexture);modalTexture=null;}}
    void OpenAppearance()
    {
        if(!nameDialog.activeSelf||selectedClass!=0)return;
        try
        {
            draftName=AccountCharacterCommands.ValidateName(nameInput.text);
            if(snapshot?.characters?.Any(c=>string.Equals(c.name,draftName,StringComparison.OrdinalIgnoreCase))==true)throw new InvalidOperationException("이미 사용 중인 이름입니다.");
            if(renaming){AppearanceCustomizationPanel.Instance.SetCreationName(draftName);CloseName();return;}
            draftId??=Guid.NewGuid().ToString("N");
            var panel=AppearanceCustomizationPanel.Install(transform);
            if(!panel||!panel.OpenCreation(null,CompleteCreation,()=>{modalClosedFrame=Time.frameCount;if(this&&isActiveAndEnabled){Reload();Show("characters");}}))throw new InvalidOperationException("외모 설정을 열지 못했습니다.");
            panel.SetCreationName(draftName,RenameCreation);
            nameDialog.SetActive(false);ReleaseModalBackdrop();CloseVisuals();SetFeedback("");
        }
        catch(Exception error){SetFeedback(error.Message);Select(nameInput);nameInput.ActivateInputField();}
    }
    bool CompleteCreation(CharacterAppearanceSnapshot appearance)
    {
        try
        {
            if(!Reload())return false;
            if(snapshot==null)
            {
                var first=AccountCharacterCommands.First(draftId,draftName,"knight",appearance,registry);first.lastTransactionId="create-"+draftId;
                store.Save(first,first.lastTransactionId);snapshot=first;
                try{TutorialProgress.RecordFirstCharacter(saveDirectory,draftId);}
                catch(Exception error){Debug.LogWarning("[Tutorial] 최초 캐릭터 안내 기록을 저장하지 못했습니다. "+error.Message);}
            }
            else
            {
                var existing=snapshot.characters?.Find(c=>c.id==draftId);
                if(existing!=null)
                {
                    var saved=existing.id==snapshot.activeCharacterId?snapshot.appearance:existing.personal?.appearance;
                    if(existing.name!=draftName||existing.classId!="knight"||!appearance.Equals(saved))
                        throw new InvalidOperationException("이미 다른 내용으로 생성된 캐릭터입니다.");
                }
                else
                {
                    var transactions=new AccountTransactions(snapshot,store,registry);
                    transactions.Execute("create-"+draftId,snapshot.revision,c=>AccountCharacterCommands.Create(c,draftId,draftName,"knight",appearance,registry),true);snapshot=transactions.Read();
                }
            }
            selectedId=draftId;draftId=null;OverburstGameMenu.PlaySelectionConfirm();return true;
        }
        catch(Exception error){SetFeedback("저장하지 못했습니다. 외모를 유지했습니다. 다시 시도해 주세요. "+error.Message);return false;}
    }
    void Enter()
    {
        if(entering||selectedId==null)return;
        try
        {
            if(!Reload()||snapshot==null)return;
            var transactions=new AccountTransactions(snapshot,store,registry);
            transactions.Execute("select-"+Guid.NewGuid().ToString("N"),snapshot.revision,c=>AccountCharacterCommands.Select(c,selectedId),true);
            OverburstGameMenu.PlaySelectionConfirm();
            entering=true;enter.interactable=false;CloseVisuals();
        }
        catch(Exception error){SetFeedback("모험을 시작하지 못했습니다. "+error.Message);}
    }
    void Back(){if(quitDialog.activeSelf)CloseQuit();else if(nameDialog.activeSelf)CloseName();else Show(screen=="classes"?"characters":"title");}
    void OpenSettings()
    {
        var menu=OverburstGameMenu.Instance??OverburstGameMenu.Install(transform);
        settingsOwned=menu&&menu.OpenStandaloneSettings(()=>{modalClosedFrame=Time.frameCount;settingsOwned=false;if(this&&isActiveAndEnabled)Select(settings);});
    }
    void CloseVisuals()
    {
        foreach(var preview in classPreviews.Concat(new[]{rosterPreview}))if(preview){if(preview.Model)preview.Model.transform.root.gameObject.SetActive(false);preview.Close();}
    }
    static void Select(Selectable target){if(EventSystem.current&&target&&target.isActiveAndEnabled)EventSystem.current.SetSelectedGameObject(target.gameObject);}
    void OnDisable(){ReleaseModalBackdrop();CloseVisuals();GameplayInputBlocker.Unblock(this);PlayerInputFacade.ReleaseGameplayMapDisabled(this);if(settingsOwned&&OverburstGameMenu.Instance)OverburstGameMenu.Instance.Close();}
    void OnDestroy(){if(Instance==this)Instance=null;}
}
