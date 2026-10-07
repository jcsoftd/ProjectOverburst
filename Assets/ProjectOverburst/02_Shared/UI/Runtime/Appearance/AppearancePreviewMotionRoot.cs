using UnityEngine;

namespace Overburst.Appearance
{
    // Receives root motion on the visual-only animator. Horizontal travel remains in place.
    public sealed class AppearancePreviewMotionRoot : MonoBehaviour
    {
        private AppearanceCharacterPreview owner;
        private Animator animator;
        public void Initialize(AppearanceCharacterPreview preview,Animator source){owner=preview;animator=source;}
        private void OnAnimatorMove(){if(owner&&animator)owner.ReceiveRootMotion(animator.deltaPosition,animator.deltaRotation);}
    }
}
