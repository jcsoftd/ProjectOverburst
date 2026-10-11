using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Overburst.Appearance
{
    [DisallowMultipleComponent]
    public sealed class AppearanceButtonVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public static readonly Color NormalFill=new Color(.045f,.037f,.030f,1);
        public static readonly Color SelectedFill=new Color(.16f,.035f,.024f,1);
        public Image backing,skin,selectedMark;
        public TMP_Text label;
        public Text legacyLabel;
        public Image hoverBorder;
        public Graphic selectedOutline;
        public Color normalSkin=Color.white,selectedSkin=Color.white;
        public Color normalBacking=NormalFill,selectedBacking=SelectedFill;
        public Color normalLabel=new Color(.90f,.85f,.73f,1),selectedLabel=new Color(.96f,.80f,.48f,1);
        public Color hoverFill=new Color(.129f,.086f,.051f,1),hoverSkin=new Color(.98f,.81f,.43f,1);
        public Color hoverLabel=new Color(.96f,.80f,.48f,1),normalBorder=Color.white;
        private Selectable selectable;
        private bool selected,hovered,focused,pressed,interactable=true,fading;
        private Color fillTarget,skinTarget,labelTarget,borderTarget;
        public bool IsHovered=>hovered;

        private void Awake(){selectable=GetComponent<Selectable>();if(selectable)selectable.transition=Selectable.Transition.None;}
        private void OnEnable(){hovered=focused=pressed=false;Refresh(true);}
        private void OnDisable(){hovered=focused=pressed=false;Refresh(true);}
        public void SetSelected(bool selected)
        {
            this.selected=selected;
            if(selectedMark)selectedMark.enabled=selected;
            if(selectedOutline)selectedOutline.enabled=selected;
            Refresh(!Application.isPlaying);
        }
        private void Refresh(bool immediate=false)
        {
            if(!selectable)selectable=GetComponent<Selectable>();
            interactable=!selectable||selectable.IsInteractable();
            bool highlight=interactable&&(hovered||focused);
            fillTarget=selected?selectedBacking:highlight?hoverFill:normalBacking;
            skinTarget=highlight?hoverSkin:selected?selectedSkin:normalSkin;
            labelTarget=highlight?hoverLabel:selected?selectedLabel:normalLabel;
            borderTarget=highlight?hoverSkin:normalBorder;
            if(interactable&&pressed){fillTarget*=.7f;skinTarget*=.8f;labelTarget*=.86f;}
            if(!interactable){fillTarget*=.55f;skinTarget*=.4f;labelTarget*=.45f;borderTarget*=.4f;}
            fading=true;if(immediate||!Application.isPlaying)Step(1);
        }
        private void LateUpdate()
        {
            bool canSelect=!selectable||selectable.IsInteractable();
            if(canSelect!=interactable)Refresh();
            if(fading)Step(Mathf.Clamp01(Time.unscaledDeltaTime/.12f));
        }
        private void Step(float amount)
        {
            bool moving=false;
            moving|=Tint(backing,fillTarget,amount);moving|=Tint(skin,skinTarget,amount);
            moving|=Tint(label?label:(Graphic)legacyLabel,labelTarget,amount);
            moving|=Tint(hoverBorder,borderTarget,amount);fading=moving;
        }
        private static bool Tint(Graphic graphic,Color target,float amount)
        {
            if(!graphic)return false;
            var next=Color.Lerp(graphic.color,target,amount);
            bool moving=Mathf.Abs(next.r-target.r)+Mathf.Abs(next.g-target.g)+Mathf.Abs(next.b-target.b)+Mathf.Abs(next.a-target.a)>.002f;
            graphic.color=moving?next:target;return moving;
        }
        public void OnPointerEnter(PointerEventData e){hovered=true;Refresh();}
        public void OnPointerExit(PointerEventData e){hovered=pressed=false;Refresh();}
        public void OnPointerDown(PointerEventData e){if(e.button==PointerEventData.InputButton.Left){pressed=true;Refresh();}}
        public void OnPointerUp(PointerEventData e){pressed=false;Refresh();}
        public void OnSelect(BaseEventData e){focused=e is AxisEventData||OverburstGameMenu.NavigationRequestedThisFrame;Refresh();}
        public void OnDeselect(BaseEventData e){focused=false;Refresh();}
    }
}
