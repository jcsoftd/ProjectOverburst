using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Overburst.EditorTools.ComboMaker
{
    public sealed partial class ComboMakerWindow : EditorWindow
    {
        private const string StylePath="Assets/Editor/Tools/Weapons/ComboMaker/ComboMaker.uss";
        [SerializeField] private WeaponItemData selectedWeapon;
        [SerializeField] private string recovery,baseline;
        [SerializeField] private int selectedStep,tab;
        [SerializeField] private bool heavyMode;
        [SerializeField] private ComboMakerAttackMode attackMode;
        private bool IsHeavyMode => session != null && session.IsHeavy;
        private ComboMakerSession session;
        private ComboMakerPreview preview;
        private MeleeComboDefinition previewData;
        private SerializedObject serialized;
        private WeaponItemData[] weapons=Array.Empty<WeaponItemData>();
        private List<string> validation=new List<string>();
        private double lastTick,rebuildAt,lastRender;
        private bool needsBake,renderDirty=true;
        private bool placeTargetAfterRebuild=true;
        private string search="",message="";
        private ScrollView library,editorScroll;
        private Label dirtyBadge,statusLabel,previewLabel,timeLabel,editorTitle;
        private HelpBox validationBox;
        private Button applyButton,playButton,elementFxButton;
        private DropdownField playbackMode;
        private Image previewImage;
        private VisualElement previewHost,geometry;
        private ComboMakerTimeline timeline;
        private readonly List<Button> tabButtons=new List<Button>();

        [MenuItem("OVERBURST/무기/콤보 메이커",false,10)]
        public static void Open()
        {
            var w=GetWindow<ComboMakerWindow>();
            w.titleContent=new GUIContent("콤보 메이커");w.minSize=new Vector2(1100,720);w.Show();
        }
        [MenuItem("Assets/OVERBURST/콤보 메이커에서 열기",true)]
        private static bool CanOpenAsset()=>Selection.activeObject is MeleeComboDefinition || Selection.activeObject is MeleeHeavyAttackDefinition || Selection.activeObject is WeaponItemData;
        [MenuItem("Assets/OVERBURST/콤보 메이커에서 열기",false,2000)]
        private static void OpenAsset()
        {
            Open();var w=GetWindow<ComboMakerWindow>();
            if (Selection.activeObject is WeaponItemData weapon) { w.SelectWeapon(weapon); return; }
            foreach (var item in w.weapons)
                foreach (ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
                    if (ComboMakerAttackBinding.Asset(item.GetMeleeDefinition(),mode)==Selection.activeObject)
                    {
                        if (!w.ResolvePending()) return;
                        w.selectedWeapon=item;w.attackMode=mode;w.selectedStep=0;w.LoadSource();return;
                    }
        }

        private void OnEnable()
        {
            titleContent=new GUIContent("콤보 메이커");minSize=new Vector2(1100,720);
            saveChangesMessage="작업 사본의 변경을 현재 무기의 콤보 자산에 적용할까요?";
            session=new ComboMakerSession();preview=new ComboMakerPreview();
            if(heavyMode){attackMode=ComboMakerAttackMode.Heavy;heavyMode=false;}
            weapons=AssetDatabase.FindAssets("t:WeaponItemData",new[]{"Assets/ProjectOverburst"})
                .Select(g=>AssetDatabase.LoadAssetAtPath<WeaponItemData>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(w=>w!=null && w.GetMeleeComboDefinition()!=null)
                .OrderByDescending(w=>AssetDatabase.GetAssetPath(w).Contains("WP02")).ThenBy(w=>w.name).ToArray();
            if(selectedWeapon==null) selectedWeapon=weapons.FirstOrDefault();
            if(selectedWeapon!=null)
            {
                LoadSession(string.IsNullOrEmpty(recovery)?null:recovery,string.IsNullOrEmpty(baseline)?null:baseline);
                serialized=new SerializedObject(session.Working);ClampStep();ScheduleRebuild();
            }
            lastTick=EditorApplication.timeSinceStartup;
            EditorApplication.update+=UpdatePreview;Undo.undoRedoPerformed+=UndoChanged;
            EditorApplication.playModeStateChanged+=PlayChanged;
            GreatswordElementFxTunerWindow.PrefabSaved+=ElementFxSaved;
        }
        private void OnDisable()
        {
            CaptureRecovery();EditorApplication.update-=UpdatePreview;Undo.undoRedoPerformed-=UndoChanged;
            EditorApplication.playModeStateChanged-=PlayChanged;
            GreatswordElementFxTunerWindow.PrefabSaved-=ElementFxSaved;
            preview?.Dispose();session?.Dispose();serialized?.Dispose();
            if(previewData!=null) DestroyImmediate(previewData);
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();rootVisualElement.AddToClassList("combo-maker");
            var style=AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if(style!=null) rootVisualElement.styleSheets.Add(style);
            BuildHeader();
            var outer=new TwoPaneSplitView(0,220,TwoPaneSplitViewOrientation.Horizontal) {viewDataKey="combo-library-split"};
            outer.AddToClassList("workspace");rootVisualElement.Add(outer);
            var left=new VisualElement();left.AddToClassList("library");left.style.minWidth=180;outer.Add(left);
            left.Add(Title("무기와 콤보"));
            var searchField=new TextField("검색") {value=search};searchField.AddToClassList("weapon-search");
            searchField.RegisterValueChangedCallback(e=>{search=e.newValue;BuildLibrary();});left.Add(searchField);
            library=new ScrollView();library.AddToClassList("library-scroll");left.Add(library);
            left.Add(ActionButton("원본 자산 보기",()=>EditorGUIUtility.PingObject(session.Asset)));
            elementFxButton=ActionButton("대검 원소 효과 조절",()=>GreatswordElementFxTunerWindow.OpenForElement(preview.Element));
            left.Add(elementFxButton);
            var inner=new TwoPaneSplitView(1,380,TwoPaneSplitViewOrientation.Horizontal) {viewDataKey="combo-editor-split"};
            outer.Add(inner);
            var center=new VisualElement();center.AddToClassList("preview-panel");center.style.minWidth=380;inner.Add(center);
            BuildPreviewUI(center);
            var right=new VisualElement();right.AddToClassList("editor-panel");right.style.minWidth=320;inner.Add(right);
            editorTitle=Title("타수 설정");right.Add(editorTitle);
            var tabs=Row();right.Add(tabs);tabButtons.Clear();
            string[] names={"동작","판정 · VFX","이동","프리뷰"};
            for(int i=0;i<names.Length;i++)
            {int index=i;var button=ActionButton(names[i],()=>{tab=index;BuildEditor();});button.AddToClassList("tab");tabs.Add(button);tabButtons.Add(button);}
            editorScroll=new ScrollView();editorScroll.AddToClassList("editor-scroll");right.Add(editorScroll);
            validationBox=new HelpBox("",HelpBoxMessageType.Error);validationBox.AddToClassList("validation");right.Add(validationBox);
            statusLabel=new Label();statusLabel.AddToClassList("status");rootVisualElement.Add(statusLabel);
            BuildLibrary();BuildEditor();UpdateStatus();
            rootVisualElement.RegisterCallback<KeyDownEvent>(e=>
            {
                if(e.target is TextElement || e.target is TextField || e.target is FloatField || e.target is IntegerField) return;
                if(e.keyCode==KeyCode.Space) {TogglePlay();e.StopPropagation();}
                if(e.keyCode==KeyCode.LeftArrow || e.keyCode==KeyCode.RightArrow)
                {
                    if(!preview.Ready) return;
                    var clip=session.Working.steps[preview.StepIndex].animationClip;
                    if(clip==null)return;
                    Seek(preview.Progress+(e.keyCode==KeyCode.LeftArrow?-1:1)/Mathf.Max(1,clip.length*clip.frameRate));e.StopPropagation();
                }
            });
        }

        private void BuildHeader()
        {
            var header=Row();header.AddToClassList("header");rootVisualElement.Add(header);
            var titles=new VisualElement();titles.style.flexGrow=1;
            var title=new Label("COMBO MAKER");title.AddToClassList("brand");titles.Add(title);
            var caption=new Label("약공 · 강공 · 패링 · 닷지 · 대시 / 현재 게임 자산 편집");caption.AddToClassList("muted");titles.Add(caption);header.Add(titles);
            dirtyBadge=new Label();dirtyBadge.AddToClassList("badge");header.Add(dirtyBadge);
            header.Add(ActionButton("원본 다시 읽기",ReloadSource));
            applyButton=ActionButton("검증 후 무기에 적용",()=>Apply());applyButton.AddToClassList("primary");header.Add(applyButton);
        }

        private void BuildLibrary()
        {
            if(library==null || session.Working==null) return;library.Clear();
            foreach(var item in weapons)
            {
                string label=WeaponLabel(item);
                if(!label.Contains(search,StringComparison.OrdinalIgnoreCase) && !item.name.Contains(search,StringComparison.OrdinalIgnoreCase)) continue;
                var b=ActionButton(label,()=>SelectWeapon(item));b.tooltip=item.name;b.AddToClassList("weapon-card");
                b.EnableInClassList("selected",item==selectedWeapon);library.Add(b);
            }
            library.Add(Title("공격 종류"));
            foreach(ComboMakerAttackMode mode in Enum.GetValues(typeof(ComboMakerAttackMode)))
            {
                var selected=mode;var asset=ComboMakerAttackBinding.Asset(selectedWeapon.GetMeleeDefinition(),mode);
                var button=ActionButton(ComboMakerAttackBinding.Label(mode),()=>SelectAttackMode(selected));
                button.name="attack-mode-"+mode;button.EnableInClassList("selected",attackMode==mode);button.SetEnabled(asset!=null);
                button.tooltip=asset!=null?AssetDatabase.GetAssetPath(asset):"이 무기에 연결된 자산이 없습니다.";library.Add(button);
            }
            library.Add(Title(ComboMakerAttackBinding.Label(attackMode)));
            for(int i=0;i<session.Working.StepCount;i++)
            {
                int index=i;var step=session.Working.steps[i];
                var card=ActionButton("",()=>SelectStep(index));card.AddToClassList("step-card");card.EnableInClassList("selected",i==selectedStep);
                card.Add(new Label($"{(IsHeavyMode?ComboMakerAttackBinding.Label(attackMode):(i+1).ToString("00")+"타")}    {step.attackPhases?.Length??0}회 타격"));
                var clip=new Label(step.animationClip!=null?step.animationClip.name:"클립 없음");clip.AddToClassList("clip-name");card.Add(clip);
                var detail=new Label($"{StepSeconds(i):0.00}초  ·  {step.attackName}");detail.AddToClassList("muted");detail.AddToClassList("clip-name");card.Add(detail);
                card.tooltip=step.attackName+"\n"+step.attackId;library.Add(card);
            }
            if(attackMode!=ComboMakerAttackMode.Light)return;
            var buttons=Row();library.Add(buttons);buttons.Add(ActionButton("복제",DuplicateStep));
            var remove=ActionButton("삭제",RemoveStep);remove.SetEnabled(session.Working.StepCount>1);buttons.Add(remove);
            var up=ActionButton("↑",()=>MoveStep(-1));up.SetEnabled(selectedStep>0);buttons.Add(up);
            var down=ActionButton("↓",()=>MoveStep(1));down.SetEnabled(selectedStep<session.Working.StepCount-1);buttons.Add(down);
        }

        private void BuildPreviewUI(VisualElement parent)
        {
            var toolbar=Row();toolbar.AddToClassList("transport");parent.Add(toolbar);
            playButton=ActionButton("▶ 재생",TogglePlay);toolbar.Add(playButton);
            toolbar.Add(ActionButton("정지",()=>{preview.Playing=false;preview.Begin(selectedStep,false,true);renderDirty=true;UpdateStatus();}));
            var mode=new DropdownField(new List<string>{"선택 동작 반복","전체 약공 연결"},0);mode.name="combo-play-mode";mode.style.flexGrow=1;
            mode.RegisterValueChangedCallback(e=>{preview.All=mode.index==1;preview.Playing=false;preview.Begin(preview.All?0:selectedStep,false,true);renderDirty=true;UpdateStatus();});toolbar.Add(mode);
            playbackMode=mode;
            var loop=new Toggle {text="반복",value=preview.Loop};loop.RegisterValueChangedCallback(e=>preview.Loop=e.newValue);toolbar.Add(loop);
            float[] speeds={.25f,.5f,1,1.5f,2};var speed=new DropdownField(new List<string>{"0.25×","0.5×","1×","1.5×","2×"},2);
            speed.style.width=70;speed.RegisterValueChangedCallback(e=>preview.Speed=speeds[speed.index]);toolbar.Add(speed);
            previewHost=new VisualElement {focusable=true};previewHost.AddToClassList("preview-host");parent.Add(previewHost);
            previewImage=new Image {scaleMode=ScaleMode.StretchToFill,pickingMode=PickingMode.Ignore};previewImage.AddToClassList("preview-image");previewHost.Add(previewImage);
            geometry=new VisualElement {pickingMode=PickingMode.Ignore};geometry.AddToClassList("overlay");previewHost.Add(geometry);
            geometry.generateVisualContent+=ctx=>
            {
                if(preview==null) return;var painter=ctx.painter2D;painter.lineWidth=1.5f;
                foreach(var line in preview.GeometryLines(geometry.contentRect.size))
                {if(line.points.Length<2) continue;painter.strokeColor=line.color;painter.BeginPath();painter.MoveTo(line.points[0]);for(int i=1;i<line.points.Length;i++)painter.LineTo(line.points[i]);painter.Stroke();}
            };
            previewLabel=new Label();previewLabel.AddToClassList("preview-label");previewLabel.pickingMode=PickingMode.Ignore;previewHost.Add(previewLabel);
            int dragging=-1;Vector2 previous=default;
            previewHost.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0 && e.button!=2)return;dragging=e.button;previous=e.position;previewHost.CapturePointer(e.pointerId);previewHost.Focus();e.StopPropagation();});
            previewHost.RegisterCallback<PointerMoveEvent>(e=>
            {if(dragging<0 || !previewHost.HasPointerCapture(e.pointerId))return;Vector2 delta=(Vector2)e.position-previous;previous=e.position;if(dragging==0)preview.Orbit(delta);else preview.Pan(delta);renderDirty=true;});
            previewHost.RegisterCallback<PointerUpEvent>(e=>{dragging=-1;previewHost.ReleasePointer(e.pointerId);});
            previewHost.RegisterCallback<PointerCaptureOutEvent>(e=>dragging=-1);
            previewHost.RegisterCallback<WheelEvent>(e=>{preview.Zoom(e.delta.y);renderDirty=true;e.StopPropagation();});
            previewHost.RegisterCallback<GeometryChangedEvent>(e=>renderDirty=true);
            var views=Row();views.AddToClassList("view-toolbar");parent.Add(views);
            string[] viewNames={"사선","정면","측면","탑뷰","인게임"};
            for(int i=0;i<viewNames.Length;i++)
            {int v=i;views.Add(ActionButton(viewNames[i],()=>{preview.SetView(v);renderDirty=true;}));}
            AddPreviewToggle(views,"판정",true,v=>{preview.ShowGeometry=v;geometry.MarkDirtyRepaint();});
            AddPreviewToggle(views,"VFX",true,v=>{preview.ShowEffects=v;Seek(preview.Progress);});
            AddPreviewToggle(views,"타격",true,v=>{preview.ShowHits=v;Seek(preview.Progress);});
            timeLabel=new Label();timeLabel.AddToClassList("time-label");parent.Add(timeLabel);
            timeline=new ComboMakerTimeline(Seek,EditTiming);timeline.AddToClassList("timeline");parent.Add(timeline);
            var footer=Row();footer.AddToClassList("preview-footer");parent.Add(footer);
            var elements=new[]{WeaponElement.None}.Concat(Enumerable.Range(0,OverburstElementRules.Count).Select(OverburstElementRules.At)).ToArray();
            var element=new DropdownField("프리뷰 원소",elements.Select(e=>e==WeaponElement.None?"없음":OverburstElementRules.Label(e)).ToList(),Mathf.Max(0,Array.IndexOf(elements,preview.Element)));element.style.flexGrow=1;
            element.RegisterValueChangedCallback(e=>{preview.Element=elements[element.index];Seek(preview.Progress);});footer.Add(element);
            footer.Add(ActionButton("이펙트 지우기",()=>{preview.ClearEffects();renderDirty=true;}));
            BuildPreviewOptions(parent);
            var help=new Label("Space 재생  ·  ← → 프레임  ·  드래그 회전  ·  휠 확대  ·  가운데 드래그 이동");help.AddToClassList("preview-help");parent.Add(help);
        }
        private static void AddPreviewToggle(VisualElement parent,string text,bool value,Action<bool> changed)
        {var toggle=new Toggle {text=text,value=value};toggle.RegisterValueChangedCallback(e=>changed(e.newValue));parent.Add(toggle);}

        private void UpdatePreview()
        {
            double now=EditorApplication.timeSinceStartup;float dt=(float)(now-lastTick);lastTick=now;
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if(needsBake && now>=rebuildAt)
            {
                needsBake=false;validation=session.Validate();
                try
                {
                    preview.Dispose();if(previewData!=null)DestroyImmediate(previewData);
                    previewData=Instantiate(session.Working);previewData.hideFlags=HideFlags.HideAndDontSave;
                    preview.HeavyDefinition=IsHeavyMode?session.HeavyWorking:null;
                    if(validation.Count==0)MeleeAttackVfxSlopeBakeUtility.BakeWorkingCopy(session.Source,previewData);
                    preview.Load(selectedWeapon,previewData);preview.Begin(selectedStep,false,true);
                    if(placeTargetAfterRebuild){preview.PlaceTargetAtImpact();placeTargetAfterRebuild=false;}
                    message=validation.Count>0?"설정 오류를 수정한 뒤 적용하세요.":"현재 무기 기본 능력치 기준 · 작업 사본 프리뷰";
                }
                catch(Exception e){preview.Dispose();message="프리뷰 준비 실패: "+e.Message;}
                CaptureRecovery();renderDirty=true;UpdateStatus();
            }
            if(preview.Playing){preview.Tick(dt);renderDirty=true;}
            if(preview.PendingEffects){preview.PrepareEffects();if(!preview.PendingEffects)renderDirty=true;}
            if(renderDirty && previewHost!=null && now-lastRender>=1d/60 && previewHost.contentRect.width>1 && previewHost.contentRect.height>1)
            {
                lastRender=now;renderDirty=false;
                try
                {
                    float pixels=EditorGUIUtility.pixelsPerPoint;
                    previewImage.image=preview.Render(new Rect(0,0,previewHost.contentRect.width*pixels,previewHost.contentRect.height*pixels));
                    geometry.MarkDirtyRepaint();UpdateTransport();
                }
                catch(Exception e){preview.Playing=false;message="프리뷰 렌더 오류: "+e.Message;UpdateStatus();}
            }
        }

        private void UpdateTransport()
        {
            if(playButton==null)return;
            var bloodStatus=rootVisualElement.Q<Label>("blood-preview-status");if(bloodStatus!=null)bloodStatus.text=preview.BloodStatus;
            playButton.text=preview.Playing?"Ⅱ 일시정지":"▶ 재생";playButton.SetEnabled(preview.Ready&&validation.Count==0&&!needsBake&&!EditorApplication.isPlayingOrWillChangePlaymode);
            previewLabel.text=preview.Ready?$"{WeaponLabel(selectedWeapon)} / {preview.StepIndex+1}타     적중 {preview.HitCount} · VFX {preview.CueCount}":needsBake?"프리뷰 준비 중…":message;
            if(preview.Ready)previewLabel.text=$"{WeaponLabel(selectedWeapon)} · {ComboMakerAttackBinding.Label(attackMode)} {(IsHeavyMode?"":(preview.StepIndex+1)+"타")} · 에너지 {preview.PreviewEnergyAmount:0} · 적중 {preview.HitCount}\n{(previewEnemy!=null?previewEnemy.DisplayName:"연습 표적")} · 밀림 {preview.TargetDisplacement:0.00}m";
            var mode=rootVisualElement.Q<DropdownField>("combo-play-mode");
            if(mode!=null){mode.SetEnabled(attackMode==ComboMakerAttackMode.Light);if(attackMode!=ComboMakerAttackMode.Light){mode.SetValueWithoutNotify(mode.choices[0]);preview.All=false;}}
            if(preview.Ready)
            {
                var step=previewData.steps[preview.StepIndex];
                timeLabel.text=$"{preview.Progress*100:0.0}%  ·  클립 {preview.Progress*(step.animationClip!=null?step.animationClip.length:0):0.000}s  ·  경과 {preview.Elapsed:0.000}s";
                timeline.SetData(previewData,preview.StepIndex,preview.Progress,(p,t)=>preview.CueTime(p,t),IsHeavyMode,IsHeavyMode?session.HeavyWorking.SafeDischargePhaseIndex:0);
            }
            else
            {
                timeline.SetData(null,0,0,null);
                timeLabel.text="";
                previewImage.image=null;
            }
        }
        private void UpdateStatus()
        {
            if(dirtyBadge==null)return;
            dirtyBadge.text=session.Dirty?"● 적용 전 변경":"원본과 동일";dirtyBadge.EnableInClassList("dirty",session.Dirty);
            if(elementFxButton!=null)
            {
                bool supported=GreatswordElementFxTunerWindow.Supports(selectedWeapon);
                elementFxButton.SetEnabled(supported&&!EditorApplication.isPlayingOrWillChangePlaymode);
                elementFxButton.tooltip=supported?"현재 프리뷰 원소의 검신·트레일을 모든 대검에 공통 적용합니다.":"원소 FX가 있는 대검에서 사용할 수 있습니다.";
            }
            applyButton.SetEnabled(session.Working!=null&&session.Dirty&&!session.SourceChanged&&validation.Count==0&&!needsBake&&!EditorApplication.isPlayingOrWillChangePlaymode);
            if(playbackMode!=null)playbackMode.SetEnabled(attackMode==ComboMakerAttackMode.Light);
            statusLabel.text=message;statusLabel.tooltip=message;
            string errors=string.Join("\n",validation);
            if(session.SourceChanged)errors+="\n원본이 외부에서 변경되었습니다. 다시 읽기 후 편집하세요.";
            validationBox.text=errors;validationBox.style.display=string.IsNullOrWhiteSpace(errors)?DisplayStyle.None:DisplayStyle.Flex;
            UpdateTransport();
        }
        private void TogglePlay()
        {if(!preview.Ready||needsBake||validation.Count>0)return;if(!preview.Playing&&preview.Progress>=1)preview.Begin(preview.All?0:selectedStep,false,true);preview.Playing=!preview.Playing;renderDirty=true;UpdateTransport();}
        private void Seek(float progress){preview.Seek(progress);renderDirty=true;UpdateTransport();}
        private void CaptureRecovery(){if(session?.Working==null)return;recovery=session.Capture();baseline=session.Baseline;hasUnsavedChanges=session.Dirty;}
        private void ScheduleRebuild(){needsBake=true;rebuildAt=EditorApplication.timeSinceStartup+.35;preview.Playing=false;UpdateStatus();}
        private void Changed(){CaptureRecovery();validation=session.Validate();ScheduleRebuild();BuildLibrary();}
        private void UndoChanged(){CaptureRecovery();ClampStep();BuildLibrary();BuildEditor();ScheduleRebuild();}
        private void PlayChanged(PlayModeStateChange state)
        {preview?.Dispose();if(previewImage!=null)previewImage.image=null;if(state==PlayModeStateChange.EnteredEditMode)ScheduleRebuild();else message="Play 종료 후 프리뷰를 재개합니다.";UpdateStatus();}
        private void ElementFxSaved()
        {
            if(!GreatswordElementFxTunerWindow.Supports(selectedWeapon))return;
            message="대검 공통 원소 효과 설정이 저장되어 프리뷰를 다시 불러옵니다.";
            ScheduleRebuild();
        }
        private void ClampStep()=>selectedStep=Mathf.Clamp(selectedStep,0,Mathf.Max(0,session.Working.StepCount-1));
        private void SelectStep(int index){selectedStep=index;preview.Playing=false;preview.Begin(index,false,true);renderDirty=true;BuildLibrary();BuildEditor();UpdateTransport();}
        private bool ResolvePending()
        {
            if(!session.Dirty)return true;
            int answer=EditorUtility.DisplayDialogComplex("미적용 콤보","현재 작업 사본의 변경을 어떻게 할까요?","적용","돌아가기","변경 버리기");
            return answer==2 || answer==0&&Apply();
        }
        private void SelectWeapon(WeaponItemData item){if(item==selectedWeapon||!ResolvePending())return;selectedWeapon=item;if(ComboMakerAttackBinding.Asset(item.GetMeleeDefinition(),attackMode)==null)attackMode=ComboMakerAttackMode.Light;selectedStep=0;placeTargetAfterRebuild=true;LoadSource();}
        private void SelectAttackMode(ComboMakerAttackMode mode){if(attackMode==mode||ComboMakerAttackBinding.Asset(selectedWeapon.GetMeleeDefinition(),mode)==null||!ResolvePending())return;attackMode=mode;selectedStep=0;placeTargetAfterRebuild=true;LoadSource();}
        private void LoadSession(string draft=null,string original=null)
        {
            if(ComboMakerAttackBinding.Asset(selectedWeapon.GetMeleeDefinition(),attackMode)==null)attackMode=ComboMakerAttackMode.Light;
            session.LoadWeapon(selectedWeapon,attackMode,draft,original);
            if(attackMode!=ComboMakerAttackMode.Light){preview.All=false;playbackMode?.SetValueWithoutNotify("선택 동작 반복");}
        }
        private void ReloadSource(){if(ResolvePending())LoadSource();}
        private void LoadSource()
        {preview.Dispose();serialized?.Dispose();LoadSession();serialized=new SerializedObject(session.Working);ClampStep();CaptureRecovery();BuildLibrary();BuildEditor();ScheduleRebuild();}
        private bool Apply()
        {try{session.Apply();CaptureRecovery();message="적용 완료 · 원본 자산 Undo 지원";ScheduleRebuild();return true;}catch(Exception e){message="적용하지 못했습니다: "+e.Message;UpdateStatus();return false;}}
        public override void SaveChanges(){if(Apply())base.SaveChanges();}
        public override void DiscardChanges(){if(session?.Source!=null)LoadSource();hasUnsavedChanges=false;base.DiscardChanges();}
        private void MoveStep(int direction)
        {Undo.RecordObject(session.Working,"콤보 순서 변경");var a=session.Working.steps;int next=selectedStep+direction;(a[selectedStep],a[next])=(a[next],a[selectedStep]);selectedStep=next;Changed();BuildEditor();}
        private void DuplicateStep()
        {serialized.Update();var a=serialized.FindProperty("steps");a.InsertArrayElementAtIndex(selectedStep);selectedStep++;a.GetArrayElementAtIndex(selectedStep).FindPropertyRelative("attackId").stringValue="attack_"+Guid.NewGuid().ToString("N").Substring(0,8);serialized.ApplyModifiedProperties();Changed();BuildEditor();}
        private void RemoveStep()
        {if(session.Working.StepCount<=1||!EditorUtility.DisplayDialog("타수 삭제","작업 사본에서 선택 타를 삭제합니다.","삭제","취소"))return;serialized.Update();serialized.FindProperty("steps").DeleteArrayElementAtIndex(selectedStep);serialized.ApplyModifiedProperties();ClampStep();Changed();BuildEditor();}
        private float StepSeconds(int i)=>ComboMakerSession.Duration(session.Working,i)*session.Working.steps[i].playbackAcceleration.ToElapsed(1);
        private static string WeaponLabel(WeaponItemData item)=>item==null?"무기":AssetDatabase.GetAssetPath(item).Contains("WP02")?"대검":AssetDatabase.GetAssetPath(item).Contains("WP01")?"한손검":item.name;
        private static VisualElement Row(){var row=new VisualElement();row.AddToClassList("row");return row;}
        private static Label Title(string text){var label=new Label(text);label.AddToClassList("section-title");return label;}
        private static Button ActionButton(string text,Action action)=>new Button(action){text=text};
    }
}
