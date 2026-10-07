using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Overburst.Appearance
{
    [DisallowMultipleComponent]
    public sealed class AppearanceButtonVisual : MonoBehaviour
    {
        public static readonly Color NormalFill=new Color(.045f,.037f,.030f,1);
        public static readonly Color SelectedFill=new Color(.16f,.035f,.024f,1);
        public Image backing,skin,selectedMark;
        public TMP_Text label;
        public Graphic selectedOutline;
        public Color normalSkin=Color.white,selectedSkin=Color.white;
        public void SetSelected(bool selected)
        {
            if(backing)backing.color=selected?SelectedFill:NormalFill;
            if(skin)skin.color=selected?selectedSkin:normalSkin;
            if(selectedMark)selectedMark.enabled=selected;
            if(selectedOutline)selectedOutline.enabled=selected;
            if(label)label.color=selected?new Color(.96f,.80f,.48f,1):new Color(.90f,.85f,.73f,1);
        }
    }
}
