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
        public virtual bool IsAvailable=>isActiveAndEnabled&&Owner&&Owner.Session!=null;
        public void Bind(AppearanceCustomizationPanel owner)
        {
            Owner=owner;if(tabButton){tabButton.onClick.RemoveListener(Open);tabButton.onClick.AddListener(Open);}SetVisible(false);RefreshAvailability();
        }
        private void Open(){if(IsAvailable)Owner.ShowExtension(this);}
        public void RefreshAvailability()
        {
            bool available=IsAvailable;
            if(tabButton){tabButton.interactable=available;tabButton.gameObject.SetActive(available);}
            if(!available&&page&&page.activeSelf)SetVisible(false);
        }
        public void SetVisible(bool visible)
        {
            visible=visible&&IsAvailable;
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
