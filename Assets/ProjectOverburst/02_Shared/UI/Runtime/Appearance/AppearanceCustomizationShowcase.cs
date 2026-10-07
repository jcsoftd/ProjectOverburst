using UnityEngine;

namespace Overburst.Appearance
{
    public sealed class AppearanceCustomizationShowcase:MonoBehaviour
    {
        public AppearanceCustomizationPanel panel;
        private void Start(){if(panel)panel.OpenExhibition();}
    }
}
