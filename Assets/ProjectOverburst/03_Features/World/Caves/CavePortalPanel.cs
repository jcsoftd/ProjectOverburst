using UnityEngine;

namespace Overburst.Caves
{
    public sealed class CavePortalPanel : MonoBehaviour
    {
        public UnityEngine.UI.Text countLabel, status;
        public UnityEngine.UI.Button previous, next, enter, close;
        CaveDungeonPortal owner;
        public void Configure(CaveDungeonPortal portal)
        {
            owner = portal;
            previous.onClick.AddListener(() => Select(-1)); next.onClick.AddListener(() => Select(1));
            close.onClick.AddListener(owner.ClosePanel); enter.onClick.AddListener(Enter);
            Refresh(); gameObject.SetActive(true); transform.SetAsLastSibling();
        }
        void Select(int delta) { owner.SelectIndex(owner.SelectedIndex + delta); Refresh(); }
        void Refresh()
        {
            countLabel.text = owner.SelectedCount + "개";
            previous.interactable = owner.SelectedIndex > 0;
            next.interactable = owner.SelectedIndex + 1 < owner.destinations.Length;
            status.text = "준비된 동굴로 입장합니다.\n입구의 포탈에서 마을로 돌아올 수 있습니다.";
        }
        void Enter()
        {
            enter.interactable = false;
            if (!owner.EnterSelected(out var error)) { status.text = error; enter.interactable = true; }
        }
        void OnDisable() { if (owner) owner.ClosePanel(); }
    }
}
