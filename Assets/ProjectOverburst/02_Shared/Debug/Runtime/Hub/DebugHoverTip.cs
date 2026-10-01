using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Overburst.DebugTools
{
    /// <summary>마우스를 올리면 창의 툴팁에 설명을 띄운다.</summary>
    public sealed class DebugHoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public static event Action<string> Shown;
        public static event Action Hidden;

        public string Text;
        private bool showing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            Shown = null;
            Hidden = null;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(Text))
                return;
            showing = true;
            Shown?.Invoke(Text);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            HideIfShowing();
        }

        private void OnDisable()
        {
            HideIfShowing();
        }

        private void HideIfShowing()
        {
            if (!showing)
                return;
            showing = false;
            Hidden?.Invoke();
        }
    }
}
