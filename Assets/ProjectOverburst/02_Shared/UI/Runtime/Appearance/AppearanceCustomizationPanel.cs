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
        private Func<CharacterAppearanceSnapshot,bool> creationCommit;
        private Action creationClosed;
        public static int LastClosedFrame {get;private set;}=-1;
        public bool IsCreation => creationCommit != null;
        public GameObject creationIdentity;
        public Button creationRename;
        public RawImage creationConfirmationBackdrop;
        private Action renameCreation;
        private string creationName;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics(){Instance=null;LastClosedFrame=-1;}
        public static AppearanceCustomizationPanel Install(Transform canvasRoot)
        {
            if(Instance)return Instance;
            var prefab=Resources.Load<AppearanceCustomizationPanel>(ResourcePath);
            return prefab?Instantiate(prefab,canvasRoot,false):null;
        }
        public bool managementPreview;
        private void Awake()
        {
            if(managementPreview)return;
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
            foreach(var button in GetComponentsInChildren<Button>(true))
                if(button!=apply)button.onClick.AddListener(PlaySelectionClick);
            apply.onClick.AddListener(()=>{if(!IsCreation)PlaySelectionClick();});
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
            continueEditing.onClick.AddListener(()=>ShowDiscardConfirmation(false));
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
        public bool OpenCreation(CharacterAppearanceSnapshot draft, Func<CharacterAppearanceSnapshot,bool> complete, Action closed)
        {
            if(Session!=null||complete==null)return false;
            creationCommit=complete;creationClosed=closed;
            return Begin(null,false,null,draft);
        }
        private void SetContextLabels()
        {
            var title=surface.transform.Find("Title")?.GetComponent<TMP_Text>();
            var location=surface.transform.Find("Location")?.GetComponent<TMP_Text>();
            if(title)title.text=IsCreation?"캐릭터 생성":"외모 변경";
            if(location)
            {
                location.text=IsCreation?creationName??"새로운 캐릭터":"은신처";
                if(IsCreation&&creationIdentity)
                {
                    location.ForceMeshUpdate();var rect=(RectTransform)creationIdentity.transform;
                    rect.anchoredPosition=new Vector2(120+location.preferredWidth+28.8f,-102.9886f);
                }
            }
            if(creationIdentity)creationIdentity.SetActive(IsCreation);
            if(appearanceTab)appearanceTab.gameObject.SetActive(!IsCreation);
            if(creationConfirmationBackdrop)creationConfirmationBackdrop.gameObject.SetActive(IsCreation);
            if(headgearToggle)headgearToggle.gameObject.SetActive(!IsCreation);
            var maleLabel=surface.transform.Find("Appearance Options/Male Label");if(maleLabel)maleLabel.GetComponent<TMP_Text>().text=IsCreation?"남성":"남성 · 준비 중";
            var maleLock=maleLocked.transform.Find("Lock");if(maleLock)maleLock.gameObject.SetActive(!IsCreation);
            foreach(var name in new[]{"View Hint","Temporary Preview Extension","Editor Preview Extensions"}){var control=surface.transform.Find(name);if(control)control.gameObject.SetActive(!IsCreation);}
            foreach(var tab in optionalTabs)if(tab)tab.RefreshAvailability();
            var stage=(RectTransform)surface.transform;stage.localScale=Vector3.one;
            stage.anchorMin=stage.anchorMax=IsCreation?new Vector2(.5f,.5f):Vector2.zero;
            if(IsCreation){stage.pivot=new Vector2(.5f,.5f);stage.anchoredPosition=Vector2.zero;stage.sizeDelta=new Vector2(1920,1080);}
            else{stage.anchorMax=Vector2.one;stage.pivot=new Vector2(.5f,.5f);stage.offsetMin=stage.offsetMax=Vector2.zero;}
            var mask=surface.GetComponent<RectMask2D>();if(mask)mask.enabled=IsCreation;
            if(title){var rect=title.rectTransform;rect.anchoredPosition=new Vector2(118,IsCreation?-28.6534f:-26);rect.sizeDelta=new Vector2(360,IsCreation?52.5f:72);title.overflowMode=TextOverflowModes.Overflow;}
            if(location){var rect=location.rectTransform;rect.anchoredPosition=new Vector2(120,IsCreation?-104.6079f:-98);rect.sizeDelta=new Vector2(300,IsCreation?28.5852f:32);}
            SetFooterY(back,IsCreation?27.0854f:32);SetFooterY(resetAppearance,IsCreation?27.0854f:32);SetFooterY(apply,IsCreation?20.091f:28);
            var backLabel=back.GetComponentInChildren<TMP_Text>();backLabel.text=IsCreation?"돌아가기":"‹  돌아가기";backLabel.fontSize=IsCreation?21:20;backLabel.margin=IsCreation?new Vector4(24,0,0,0):Vector4.zero;
            var backIcon=back.transform.Find("Creation Back Icon");if(backIcon)backIcon.gameObject.SetActive(IsCreation);
            SetLocalPosition(hairPrevious,IsCreation?47.983f:48,IsCreation?475.705f:471);SetLocalPosition(hairNext,IsCreation?461.966f:488,IsCreation?475.705f:471);
            SetLocalPosition(equipmentPrevious,IsCreation?-16.011f:-10,IsCreation?564.511f:560);SetLocalPosition(equipmentNext,IsCreation?397.971f:416,IsCreation?564.511f:560);
            foreach(var control in new[]{faceFrame,upperFrame,fullFrame,resetView})SetFooterY(control,IsCreation?29.091f:28);
            foreach(var control in new[]{resetAppearance,faceFrame,upperFrame,fullFrame,resetView})control.GetComponentInChildren<TMP_Text>().fontSize=IsCreation?21:20;
            foreach(var control in new[]{hairPrevious,hairNext,equipmentPrevious,equipmentNext})control.GetComponentInChildren<TMP_Text>().fontSize=IsCreation?25.2f:20;
            if(creationRename)creationRename.GetComponentInChildren<TMP_Text>().color=new Color(.478f,.459f,.420f);
            var label=apply.GetComponentInChildren<TMP_Text>();if(label)label.text=IsCreation?"캐릭터 생성":"외모 적용";
            var question=discardConfirmation.transform.Find("Confirmation/Question")?.GetComponent<TMP_Text>();
            var explanation=discardConfirmation.transform.Find("Confirmation/Explanation")?.GetComponent<TMP_Text>();
            if(question)question.text=IsCreation?"캐릭터 생성을 취소할까요?":"변경 내용을 버릴까요?";
            if(explanation)explanation.text=IsCreation?"이름과 외모 선택은 저장되지 않습니다.":"변경한 외모는 저장되지 않습니다.";
            continueEditing.GetComponentInChildren<TMP_Text>().text=IsCreation?"계속 만들기":"계속 편집";
            discardAndClose.GetComponentInChildren<TMP_Text>().text=IsCreation?"생성 취소":"돌아가기";
        }
        static void SetFooterY(Button button,float y){if(button){var rect=(RectTransform)button.transform;rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,y);}}
        static void SetLocalPosition(Button button,float x,float y){if(button)((RectTransform)button.transform).anchoredPosition=new Vector2(x,-y);}
        private void FitCreationStage()
        {
            if(!IsCreation)return;var viewport=((RectTransform)transform).rect.size;
            float scale=Mathf.Min(viewport.x/1920f,viewport.y/1080f);var targetScale=Vector3.one*Mathf.Max(scale,.001f);if(surface.transform.localScale!=targetScale)surface.transform.localScale=targetScale;
        }
        public void SetCreationName(string name,Action rename=null)
        {
            creationName=name;if(rename!=null)renameCreation=rename;
            if(creationRename){creationRename.onClick.RemoveAllListeners();creationRename.onClick.AddListener(PlaySelectionClick);creationRename.onClick.AddListener(()=>renameCreation?.Invoke());}
            SetContextLabels();
        }
        private bool Begin(AppearanceStylistInteractable owner,bool exhibition,AccountGameplaySession current,CharacterAppearanceSnapshot initial=null)
        {
            if(Session!=null)return true;
            Wire();
            try
            {
                account=current;Owner=owner;IsExhibition=exhibition;saving=false;projectionRetry=false;
                Session=new AppearanceCustomizationSession(catalog,initial??current?.ReadAppearance());
                SetContextLabels();
                returnSelection=EventSystem.current?EventSystem.current.currentSelectedGameObject:null;
                NormalizeCanvasRoot();surface.SetActive(true);transform.SetAsLastSibling();
                discardConfirmation.SetActive(false);Canvas.ForceUpdateCanvases();
                FitCreationStage();int height=Mathf.Max(1,Mathf.RoundToInt(Mathf.Min(Screen.height,Screen.width*9f/16f)*Mathf.Min(2f,2160f/Mathf.Max(1,Screen.height))));
                preview.Open(catalog,Session,characterTarget,IsCreation?new Vector2Int(Mathf.RoundToInt(height*16f/9f),height):default);
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
                if(EventSystem.current)EventSystem.current.SetSelectedGameObject(IsCreation?choices.First(c=>c.kind==AppearanceChoiceKind.Face).button.gameObject:appearanceTab.gameObject);
                return true;
            }
            catch(Exception error){Close();Debug.LogException(error);return false;}
        }
        private void Update()
        {
            if(managementPreview)return;
            if(Session==null)return;
            FitCreationStage();
            if(Application.isPlaying)
            {
                bool invalid=!IsExhibition&&!IsCreation&&(account!=AccountGameplaySession.Current||!WorldSessionState.IsHideout
                    || !PlayerContext.Instance?.CurrentActor||PlayerContext.Instance.CurrentActor.Health.IsDead
                    || (PersistentSceneFlow.Instance&&PersistentSceneFlow.Instance.IsSwitching));
                if(invalid){Close();return;}
                bool nameModal=IsCreation&&TitleCharacterPanel.Instance&&(TitleCharacterPanel.Instance.NameDialogOpen||TitleCharacterPanel.Instance.ModalClosedFrame==Time.frameCount);
                if(PlayerInputFacade.Current?.UiCancelPressedThisFrame==true&&!saving&&!nameModal)
                {
                    if(discardConfirmation.activeSelf)ShowDiscardConfirmation(false);
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
        private void PlaySelectionClick()
        {
            if(Session!=null&&!IsExhibition)OverburstGameMenu.PlaySelectionClick();
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
            if(IsCreation)RefreshCreationNavigation();
        }
        private void RefreshCreationNavigation()
        {
            Canvas.ForceUpdateCanvases();var controls=surface.GetComponentsInChildren<Selectable>(false).Where(c=>c.IsInteractable()).ToArray();
            var centers=controls.Select(c=>c.transform.TransformPoint(((RectTransform)c.transform).rect.center)).ToArray();
            Selectable Neighbor(int source,Vector3 direction){float score=0;Selectable selected=null;for(int i=0;i<controls.Length;i++){if(i==source)continue;var delta=centers[i]-centers[source];float dot=Vector3.Dot(direction,delta);if(dot<=.001f)continue;float value=dot/delta.sqrMagnitude;if(value>score){score=value;selected=controls[i];}}return selected;}
            for(int i=0;i<controls.Length;i++)controls[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=Neighbor(i,Vector3.left),selectOnRight=Neighbor(i,Vector3.right),selectOnUp=Neighbor(i,Vector3.up),selectOnDown=Neighbor(i,Vector3.down)};
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
            if(IsCreation||(Session.HasAppearanceChanges&&!IsExhibition)){ShowDiscardConfirmation(true);return;}
            Close();
        }
        private void ShowDiscardConfirmation(bool show)
        {
            if(IsCreation&&TitleCharacterPanel.Instance&&creationConfirmationBackdrop){if(show)TitleCharacterPanel.Instance.CaptureCreationConfirmation(creationConfirmationBackdrop);else TitleCharacterPanel.Instance.ReleaseCreationConfirmation();}
            discardConfirmation.SetActive(show);
            if(show)discardConfirmation.transform.SetAsLastSibling();
            continueEditing.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnRight=discardAndClose};discardAndClose.navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnLeft=continueEditing};
            if(EventSystem.current)EventSystem.current.SetSelectedGameObject(show?continueEditing.gameObject:back.gameObject);
        }
        public void Apply()
        {
            if(Session!=null&&!saving&&IsCreation)
            {
                saving=true;apply.interactable=false;
                try{if(creationCommit(Session.CandidateForSave()))Close();}
                catch(Exception error){SetFeedback("캐릭터를 생성하지 못했습니다. "+error.Message);}
                finally{saving=false;if(Session!=null)RefreshChoices();}
                return;
            }
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
            if(IsCreation&&discardConfirmation&&discardConfirmation.activeSelf)ShowDiscardConfirmation(false);
            var closed=creationClosed;creationClosed=null;creationCommit=null;
            renameCreation=null;creationName=null;
            if(Session!=null)LastClosedFrame=Time.frameCount;
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
            if(surface&&apply)SetContextLabels();
            closed?.Invoke();
        }
        private void OnDisable()=>Close();
        private void OnDestroy(){Close();if(Instance==this)Instance=null;}
    }
}
