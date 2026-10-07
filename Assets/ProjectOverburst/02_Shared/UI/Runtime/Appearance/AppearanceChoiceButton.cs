using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overburst.Appearance
{
    public enum AppearanceChoiceKind { Face, Hair, HairColor, SkinColor, EyeColor, BodyStyle, Equipment }
    [DisallowMultipleComponent]
    public sealed class AppearanceChoiceButton : MonoBehaviour
    {
        public AppearanceChoiceKind kind;
        public string optionId;
        public Button button;
        public Graphic selectedMark;
        public Image artwork;
        public TMP_Text caption;
        private Action<AppearanceChoiceButton> select;
        public void Bind(Action<AppearanceChoiceButton> callback)
        {
            select=callback;button.onClick.RemoveListener(Select);button.onClick.AddListener(Select);
        }
        private void Select()=>select?.Invoke(this);
        public void ShowSelection(bool selected){if(selectedMark)selectedMark.enabled=selected;var visual=GetComponent<AppearanceButtonVisual>();if(visual)visual.SetSelected(selected);}
        private void OnDestroy(){if(button)button.onClick.RemoveListener(Select);select=null;}
    }
}

