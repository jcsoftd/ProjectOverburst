using UnityEngine;
using UnityEngine.UI;

namespace Overburst.Appearance
{
    // Optional tabs own all their controls and data beneath one authored child.
    public abstract class AppearanceOptionalTab:MonoBehaviour
    {
        public Button tabButton;
        public GameObject page;
        public AppearanceCustomizationPanel Owner {get;private set;}
        public void Bind(AppearanceCustomizationPanel owner)
        {
            Owner=owner;if(tabButton){tabButton.onClick.RemoveListener(Open);tabButton.onClick.AddListener(Open);}SetVisible(false);
        }
        private void Open(){if(Owner&&Owner.Session!=null)Owner.ShowExtension(this);}
        public void SetVisible(bool visible)
        {
            if(page)page.SetActive(visible);
            if(tabButton)tabButton.GetComponent<AppearanceButtonVisual>().SetSelected(visible);
            if(visible)OnShown();else OnHidden();
        }
        protected virtual void OnShown(){}
        protected virtual void OnHidden(){}
        public virtual void SessionClosed(){}
        protected virtual void OnDestroy(){if(tabButton)tabButton.onClick.RemoveListener(Open);}
    }
}
