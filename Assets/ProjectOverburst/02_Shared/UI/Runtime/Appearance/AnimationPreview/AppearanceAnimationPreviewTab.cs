#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overburst.Appearance.AnimationPreview
{
    public sealed class AppearanceAnimationPreviewTab:AppearanceOptionalTab
    {
        public AppearanceAnimationLibrary library;
        public Button gameCategory,kawaiiCategory,expressionCategory,playPause,stop,excludeSelected,excludedList;
        public TMP_Text playPauseLabel,timeLabel,speedLabel,selectedLabel,excludedCount;
        public TMP_InputField search;
        public RectTransform list;
        public Button rowTemplate;
        public Slider timeline,speed;
        public Toggle loop;
        public AppearanceAnimationExclusions Exclusions {get;private set;}
        public AppearanceAnimationOption Selected {get;private set;}
        public bool ShowingExcluded {get;private set;}
        private readonly List<Button> rows=new List<Button>();
        private string category="Game";
        private bool wired,refreshing;
        public override bool IsAvailable=>base.IsAvailable&&Owner.Session.PreviewBody==AppearancePreviewBody.Nude;
        private bool CanOperate=>IsAvailable&&Owner.ActiveExtension==this;
        private AppearanceCharacterPreview Preview=>Owner?Owner.preview:null;
        protected override void OnShown()
        {
            if(!IsAvailable)return;
            Wire();
            if(Preview){speed.SetValueWithoutNotify(Preview.PlaybackSpeed);loop.SetIsOnWithoutNotify(Preview.Looping);}
            try{Exclusions??=new AppearanceAnimationExclusions();RefreshRows();}
            catch(Exception e){Owner.SetFeedback(e.Message);excludeSelected.interactable=false;}
        }
        public void SetExclusionStore(AppearanceAnimationExclusions store){Exclusions=store;RefreshRows();}
        private void Wire()
        {
            if(wired)return;wired=true;
            gameCategory.onClick.AddListener(()=>SetCategory("Game"));kawaiiCategory.onClick.AddListener(()=>SetCategory("Kawaii"));expressionCategory.onClick.AddListener(()=>SetCategory("Expression"));
            search.onValueChanged.AddListener(_=>RefreshRows());
            playPause.onClick.AddListener(()=>{if(CanOperate)Preview?.TogglePause();});stop.onClick.AddListener(()=>{if(!CanOperate)return;Preview?.StopAnimation();Selected=null;RefreshRows();});
            speed.onValueChanged.AddListener(v=>{if(CanOperate)Preview?.SetSpeed(v);});loop.onValueChanged.AddListener(v=>{if(CanOperate)Preview?.SetLooping(v);});
            timeline.onValueChanged.AddListener(v=>{if(CanOperate&&!refreshing)Preview?.Seek(v);});
            excludeSelected.onClick.AddListener(ExcludeOrRestore);
            excludedList.onClick.AddListener(()=>{if(!CanOperate)return;ShowingExcluded=!ShowingExcluded;Selected=null;Preview?.StopAnimation();RefreshRows();});
        }
        private void SetCategory(string value){if(!CanOperate)return;category=value;ShowingExcluded=false;Selected=null;Preview?.StopAnimation();RefreshRows();}
        private void Update()
        {
            if(!CanOperate||!Preview)return;
            float time=Preview.PlaybackTime,length=Preview.ActiveClip?Preview.ActiveClip.length:0;
            bool timed=length>.001f;timeline.interactable=timed;speed.interactable=timed;loop.interactable=timed;playPause.interactable=timed;
            timeLabel.text=timed?time.ToString("0.0")+" / "+length.ToString("0.0")+" s":"표정 미리보기";playPauseLabel.text=Preview.IsPaused?"재생":"일시정지";speedLabel.text=Preview.PlaybackSpeed.ToString("0.00")+"×";
            refreshing=true;timeline.maxValue=Mathf.Max(.001f,length);timeline.value=time;refreshing=false;
        }
        public void RefreshRows()
        {
            if(!library||Exclusions==null)return;
            foreach(var row in rows)if(row){row.gameObject.SetActive(false);row.onClick.RemoveAllListeners();}
            string query=search.text??"";int used=0;
            foreach(var option in library.animations.Where(x=>(ShowingExcluded?Exclusions.Contains(x.id):x.category==category&&!Exclusions.Contains(x.id))
                &&(string.IsNullOrEmpty(query)||x.displayName.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)))
            {
                if(!option.clip)continue;
                var row=used<rows.Count?rows[used]:Instantiate(rowTemplate,list,false);
                if(used==rows.Count)rows.Add(row);used++;row.gameObject.SetActive(true);
                row.GetComponentInChildren<TMP_Text>().text=option.displayName;
                var image=row.transform.Find("Motion Thumbnail")?.GetComponent<Image>();if(image){image.sprite=option.thumbnail;image.enabled=option.thumbnail;}
                row.GetComponent<AppearanceButtonVisual>().SetSelected(Selected==option);
                var selected=option;row.onClick.AddListener(()=>Select(selected));
            }
            selectedLabel.text=Selected?.displayName??(ShowingExcluded?"제외 목록":"애니메이션을 선택하세요");
            foreach(var entry in new[]{(gameCategory,"Game"),(kawaiiCategory,"Kawaii"),(expressionCategory,"Expression")})
                entry.Item1.GetComponent<AppearanceButtonVisual>().SetSelected(!ShowingExcluded&&category==entry.Item2);
            excludeSelected.interactable=Selected!=null;excludeSelected.GetComponentInChildren<TMP_Text>().text=ShowingExcluded?"목록에 복원":"목록에서 제외";
            excludedCount.text="제외 목록 ("+Exclusions.Document.excluded.Count+")";
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
        }
        public void Select(AppearanceAnimationOption option)
        {
            if(!CanOperate||option==null)return;
            Selected=option;Preview.SetFraming(option.category=="Expression"?AppearanceFraming.Face:AppearanceFraming.FullBody);Preview.Play(option.clip,loop.isOn);RefreshRows();
        }
        public void ExcludeOrRestore()
        {
            if(!CanOperate||Selected==null||Exclusions==null)return;
            try
            {
                if(ShowingExcluded)Exclusions.Restore(Selected.id);else Exclusions.Exclude(Selected);
                Selected=null;Preview.StopAnimation();RefreshRows();Owner.SetFeedback("");
            }
            catch(Exception e){Owner.SetFeedback("목록을 저장하지 못했습니다. "+e.Message);}
        }
        protected override void OnHidden()
        {
            Selected=null;
            if(Preview&&Owner.Session!=null)
            {
                if(Preview.ActiveClip!=Owner.catalog.idleClip)Preview.StopAnimation();
                Preview.SetFraming(AppearanceFraming.FullBody);
            }
        }
        public override void SessionClosed()
        {
            Selected=null;ShowingExcluded=false;Exclusions=null;category="Game";
            search.SetTextWithoutNotify("");speed.SetValueWithoutNotify(1);loop.SetIsOnWithoutNotify(true);
        }
    }
}

#endif
