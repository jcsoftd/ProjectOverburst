using System;
using System.Collections.Generic;
using System.Linq;
using Overburst.Persistence;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Overburst.Appearance
{
    [DisallowMultipleComponent]
    public sealed class AppearanceCustomizationPanel : MonoBehaviour
    {
        public const string ResourcePath="UI/Appearance/PF_OverburstAppearance_Rpg11";
#if UNITY_EDITOR
        public const string EditorPreviewPath="Assets/ProjectOverburst/05_Art/UI/Appearance/EditorPreview/PF_AppearanceEditorPreview.prefab";
#endif
        public static AppearanceCustomizationPanel Instance {get;private set;}
        public static bool IsOpen=>Instance&&Instance.Session!=null;
        public CharacterAppearanceCatalog catalog;
        public Canvas rootCanvas;
        public GameObject surface,appearanceOptions,discardConfirmation;
        public RawImage characterTarget;
        public AppearanceCharacterPreview preview;
        public AppearanceChoiceButton[] choices;
        public Button appearanceTab,back,apply,resetAppearance,faceFrame,upperFrame,fullFrame,resetView;
        public Button maleLocked,hairPrevious,hairNext,equipmentPrevious,equipmentNext;
        public Button continueEditing,discardAndClose,headgearToggle;
        public TMP_Text feedback,pageHair,pageEquipment;
        private AppearanceOptionalTab[] optionalTabs=Array.Empty<AppearanceOptionalTab>();
        public AppearanceOptionalTab ActiveExtension {get;private set;}
        public AppearanceCustomizationSession Session {get;private set;}
        public AppearanceStylistInteractable Owner {get;private set;}
        public bool IsExhibition {get;private set;}
        private AccountGameplaySession account;
        private GameObject returnSelection;
        private int hairPage,equipmentPage;
        private bool wired,saving;
        private bool projectionRetry;
        private InputAction ownerCloseInput;
        private int sessionOpenedFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()=>Instance=null;
        public static AppearanceCustomizationPanel Install(Transform canvasRoot)
        {
            if(Instance)return Instance;
            var prefab=Resources.Load<AppearanceCustomizationPanel>(ResourcePath);
            return prefab?Instantiate(prefab,canvasRoot,false):null;
        }
        private void Awake()
        {
            if(Instance&&Instance!=this){Destroy(gameObject);return;}
            Instance=this;NormalizeCanvasRoot();Wire();
            if(surface)surface.SetActive(false);
            if(rootCanvas){rootCanvas.overrideSorting=true;rootCanvas.sortingOrder=350;}
        }
        private void NormalizeCanvasRoot()
        {
            // A nested overlay canvas inherits its parent viewport; preview-scene canvas drivers
            // can serialize a zero root scale, so normalize before it becomes interactive.
            if(!transform.parent||!transform.parent.GetComponentInParent<Canvas>()||!(transform is RectTransform rect))return;
            rect.localScale=Vector3.one;rect.localRotation=Quaternion.identity;
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;
            rect.offsetMin=Vector2.zero;rect.offsetMax=Vector2.zero;rect.anchoredPosition3D=Vector3.zero;
        }
        private void Wire()
        {
            if(wired)return;
#if UNITY_EDITOR
            InstallEditorPreview();
#endif
            wired=true;
            foreach(var choice in choices)choice.Bind(SelectChoice);
            appearanceTab.onClick.AddListener(ShowAppearance);
            optionalTabs=GetComponentsInChildren<AppearanceOptionalTab>(true);
            foreach(var tab in optionalTabs)tab.Bind(this);
            back.onClick.AddListener(RequestClose);apply.onClick.AddListener(Apply);
            resetAppearance.onClick.AddListener(()=>Session?.ResetAppearance());
            faceFrame.onClick.AddListener(()=>SetFraming(AppearanceFraming.Face));
            upperFrame.onClick.AddListener(()=>SetFraming(AppearanceFraming.UpperBody));
            fullFrame.onClick.AddListener(()=>SetFraming(AppearanceFraming.FullBody));
            resetView.onClick.AddListener(preview.ResetView);
            headgearToggle.onClick.AddListener(()=>Session?.ToggleHeadgear());
            maleLocked.onClick.AddListener(()=>SetFeedback("현재는 여성 캐릭터만 사용할 수 있습니다."));
            hairPrevious.onClick.AddListener(()=>ChangeHairPage(-1));hairNext.onClick.AddListener(()=>ChangeHairPage(1));
            equipmentPrevious.onClick.AddListener(()=>ChangeEquipmentPage(-1));equipmentNext.onClick.AddListener(()=>ChangeEquipmentPage(1));
            continueEditing.onClick.AddListener(()=>discardConfirmation.SetActive(false));
            discardAndClose.onClick.AddListener(Close);
        }
#if UNITY_EDITOR
        private void InstallEditorPreview()
        {
            if(!surface||surface.transform.Find("Editor Preview Extensions"))return;
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(EditorPreviewPath);
            if(!prefab)return;
            var extension=Instantiate(prefab,surface.transform,false);extension.name="Editor Preview Extensions";
            foreach(var developer in extension.GetComponentsInChildren<AppearanceDeveloperPreview>(true))developer.panel=this;
        }
#endif
        public bool Open(AppearanceStylistInteractable owner)
        {
            if(Session!=null)return Owner==owner;
            if(!AccountBootstrap.Ready || AccountGameplaySession.Current==null || !WorldSessionState.IsHideout
                || GameplayInputBlocker.IsGameplayInputBlocked || !PlayerContext.Instance?.CurrentActor
                || PlayerContext.Instance.CurrentActor.Health.IsDead
                || (PersistentSceneFlow.Instance&&PersistentSceneFlow.Instance.IsSwitching))return false;
            return Begin(owner,false,AccountGameplaySession.Current);
        }
        public bool OpenExhibition()=>Begin(null,true,null);
        private bool Begin(AppearanceStylistInteractable owner,bool exhibition,AccountGameplaySession current)
        {
            if(Session!=null)return true;
            Wire();
            try
            {
                account=current;Owner=owner;IsExhibition=exhibition;saving=false;projectionRetry=false;
                Session=new AppearanceCustomizationSession(catalog,current?.ReadAppearance());
                returnSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
                NormalizeCanvasRoot();surface.SetActive(true);transform.SetAsLastSibling();
                discardConfirmation.SetActive(false);Canvas.ForceUpdateCanvases();
                preview.Open(catalog,Session,characterTarget);
                Session.Changed+=RefreshChoices;
                hairPage=Mathf.Max(0,Array.FindIndex(catalog.hairStyles,x=>x.id==Session.Draft.hairStyleId))/4;
                equipmentPage=0;
                SetFeedback(Session.UsedFallback?"일부 외모를 불러오지 못해 기본값으로 표시했습니다.":"");
                ShowAppearance();RefreshChoices();RefreshFraming();
                if(Application.isPlaying)
                {
                    GameplayInputBlocker.Block(this);
                    PlayerInputFacade.Current?.SetGameplayMapDisabled(this,true);
                    if(owner&&PlayerInputFacade.Current&&PlayerInputFacade.Current.TryGetGameplayAction("Interact",out var interact))
                    {
                        ownerCloseInput=interact.Clone();
                        ownerCloseInput.Enable();
                    }
                    sessionOpenedFrame=Time.frameCount;
                    TooltipManager.Instance?.HideTooltip();
                }
                if(EventSystem.current)EventSystem.current.SetSelectedGameObject(appearanceTab.gameObject);
                return true;
            }
            catch(Exception error){Close();Debug.LogException(error);return false;}
        }
        private void Update()
        {
            if(Session==null)return;
            if(Application.isPlaying)
            {
                bool invalid=!IsExhibition&&(account!=AccountGameplaySession.Current||!WorldSessionState.IsHideout
                    || !PlayerContext.Instance?.CurrentActor||PlayerContext.Instance.CurrentActor.Health.IsDead
                    || (PersistentSceneFlow.Instance&&PersistentSceneFlow.Instance.IsSwitching));
                if(invalid){Close();return;}
                if(PlayerInputFacade.Current?.UiCancelPressedThisFrame==true&&!saving)
                {
                    if(discardConfirmation.activeSelf)discardConfirmation.SetActive(false);
                    else RequestClose();
                }
                else if(!saving&&Owner&&Time.frameCount!=sessionOpenedFrame
                    &&ownerCloseInput?.WasPressedThisFrame()==true&&!IsEditingText())
                    RequestClose();
                if(Session==null)return;
                PlayerInputFacade.Current?.SetGameplayMapDisabled(this,true);
            }
        }
        private static bool IsEditingText()
        {
            var selected=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
            if(!selected)return false;
            var tmp=selected.GetComponentInParent<TMP_InputField>();
            if(tmp&&tmp.isFocused)return true;
            var legacy=selected.GetComponentInParent<InputField>();
            return legacy&&legacy.isFocused;
        }
        private void SelectChoice(AppearanceChoiceButton choice)
        {
            if(Session==null||saving)return;
            switch(choice.kind)
            {
                case AppearanceChoiceKind.Face:Session.Edit(v=>v.faceId=choice.optionId);break;
                case AppearanceChoiceKind.Hair:Session.Edit(v=>v.hairStyleId=choice.optionId);break;
                case AppearanceChoiceKind.HairColor:Session.Edit(v=>v.hairColorId=choice.optionId);break;
                case AppearanceChoiceKind.SkinColor:Session.Edit(v=>v.skinColorId=choice.optionId);break;
                case AppearanceChoiceKind.EyeColor:Session.Edit(v=>v.eyeColorId=choice.optionId);break;
                case AppearanceChoiceKind.BodyStyle:Session.Edit(v=>v.bodyShapeId=choice.optionId);break;
                case AppearanceChoiceKind.Equipment:if(string.IsNullOrEmpty(choice.optionId))Session.ShowUnderwear();else Session.ShowEquipment(choice.optionId);break;
            }
        }
        private void RefreshChoices()
        {
            if(Session==null)return;var v=Session.Draft;
            foreach(var c in choices)
            {
                string selected=c.kind switch{
                    AppearanceChoiceKind.Face=>v.faceId,AppearanceChoiceKind.Hair=>v.hairStyleId,
                    AppearanceChoiceKind.HairColor=>v.hairColorId,AppearanceChoiceKind.SkinColor=>v.skinColorId,
                    AppearanceChoiceKind.EyeColor=>v.eyeColorId,AppearanceChoiceKind.BodyStyle=>v.bodyShapeId,
                    _=>Session.PreviewBody==AppearancePreviewBody.Equipment?Session.EquipmentExampleId:
                       Session.PreviewBody==AppearancePreviewBody.Underwear?"":null};
                c.ShowSelection(string.Equals(c.optionId,selected,StringComparison.Ordinal));
                if(c.kind==AppearanceChoiceKind.Hair)
                    c.gameObject.SetActive(Array.FindIndex(catalog.hairStyles,x=>x.id==c.optionId)/4==hairPage);
                if(c.kind==AppearanceChoiceKind.Equipment)
                {
                    int index=string.IsNullOrEmpty(c.optionId)?0:Array.FindIndex(catalog.equipmentExamples,x=>x.id==c.optionId)+1;
                    c.gameObject.SetActive(index/4==equipmentPage);
                }
                if(c.kind==AppearanceChoiceKind.HairColor)c.button.interactable=v.hairStyleId!="p09.hair.none";
            }
            pageHair.text=(hairPage+1)+" / "+Mathf.CeilToInt(catalog.hairStyles.Length/4f);
            pageEquipment.text=(equipmentPage+1)+" / "+Mathf.CeilToInt((catalog.equipmentExamples.Length+1)/4f);
            headgearToggle.GetComponent<AppearanceButtonVisual>().SetSelected(Session.HeadgearVisible);
            headgearToggle.GetComponentInChildren<TMP_Text>().text=Session.HeadgearVisible?"모자 켜짐":"모자 꺼짐";
            RefreshOptionalTabs();
            apply.interactable=!saving&&!IsExhibition;
        }
        private void SetFraming(AppearanceFraming mode){preview.SetFraming(mode);RefreshFraming();}
        private void RefreshFraming()
        {
            foreach(var entry in new[]{(faceFrame,AppearanceFraming.Face),(upperFrame,AppearanceFraming.UpperBody),(fullFrame,AppearanceFraming.FullBody)})
            {
                var active=entry.Item1.transform.Find("Active").GetComponent<Image>();active.enabled=Session!=null&&Session.Framing==entry.Item2;
                entry.Item1.GetComponent<AppearanceButtonVisual>().SetSelected(active.enabled);
            }
        }
        private void ChangeHairPage(int delta){hairPage=(hairPage+delta+Mathf.CeilToInt(catalog.hairStyles.Length/4f))%Mathf.CeilToInt(catalog.hairStyles.Length/4f);RefreshChoices();}
        private void ChangeEquipmentPage(int delta){int count=Mathf.CeilToInt((catalog.equipmentExamples.Length+1)/4f);equipmentPage=(equipmentPage+delta+count)%count;RefreshChoices();}
        private void RefreshOptionalTabs()
        {
            if(ActiveExtension&&!ActiveExtension.IsAvailable)
            {
                ShowAppearance();
                if(EventSystem.current)EventSystem.current.SetSelectedGameObject(appearanceTab.gameObject);
            }
            foreach(var tab in optionalTabs)if(tab)tab.RefreshAvailability();
        }
        public void ShowAppearance()
        {
            ActiveExtension=null;appearanceOptions.SetActive(true);
            appearanceTab.GetComponent<AppearanceButtonVisual>().SetSelected(true);
            foreach(var tab in optionalTabs)if(tab)tab.SetVisible(false);
        }
        public void ShowExtension(AppearanceOptionalTab selected)
        {
            if(Session==null||selected==null||!selected.IsAvailable||!optionalTabs.Contains(selected))return;
            ActiveExtension=selected;appearanceOptions.SetActive(false);
            appearanceTab.GetComponent<AppearanceButtonVisual>().SetSelected(false);
            foreach(var tab in optionalTabs)if(tab)tab.SetVisible(tab==selected);
        }
        public void RequestClose()
        {
            if(Session==null||saving)return;
            if(Session.HasAppearanceChanges&&!IsExhibition){discardConfirmation.SetActive(true);return;}
            Close();
        }
        public void Apply()
        {
            if(Session==null||saving||IsExhibition||account==null)return;
            if(!Session.HasAppearanceChanges){
                if(projectionRetry){
                    var currentController=PlayerAppearanceController.Install();
                    if(!currentController||!currentController.TryApplyCurrent()){SetFeedback("저장된 외모를 표시하지 못했습니다. 다시 시도해 주세요.");return;}
                }
                Close();return;
            }
            saving=true;apply.interactable=false;
            try
            {
                var next=Session.CandidateForSave();
                var controller=PlayerAppearanceController.Install();
                if(!controller)throw new InvalidOperationException("플레이어 외모 적용기를 찾지 못했습니다.");
                controller.ValidateCurrent(next);
                if(!account.ApplyAppearance(Session.CommittedAtOpen,next))return;
                if(account.AppearanceProjectionError!=null)
                {
                    projectionRetry=true;SetFeedback("외모는 저장됐지만 표시를 갱신하지 못했습니다. 적용 버튼을 다시 눌러 주세요.");
                    var stored=account.ReadAppearance();Session.Changed-=RefreshChoices;
                    preview.Close();Session=new AppearanceCustomizationSession(catalog,stored);
                    preview.Open(catalog,Session,characterTarget);Session.Changed+=RefreshChoices;return;
                }
                Close();
            }
            catch(Exception error){SetFeedback("외모를 적용하지 못했습니다. "+error.Message);Debug.LogException(error);}
            finally{saving=false;if(Session!=null)RefreshChoices();}
        }
        public void SetFeedback(string text){if(feedback)feedback.text=text;}
        public void Close()
        {
            if(ownerCloseInput!=null){ownerCloseInput.Disable();ownerCloseInput.Dispose();ownerCloseInput=null;}
            if(Session!=null)Session.Changed-=RefreshChoices;
            foreach(var tab in optionalTabs)if(tab)tab.SessionClosed();
            ActiveExtension=null;preview?.Close();Session=null;Owner=null;account=null;saving=false;
            RefreshOptionalTabs();
            if(surface)surface.SetActive(false);
            GameplayInputBlocker.Unblock(this);PlayerInputFacade.ReleaseGameplayMapDisabled(this);
            if(EventSystem.current&&returnSelection&&returnSelection.activeInHierarchy)
                EventSystem.current.SetSelectedGameObject(returnSelection);
            returnSelection=null;
        }
        private void OnDisable()=>Close();
        private void OnDestroy(){Close();if(Instance==this)Instance=null;}
    }
}
