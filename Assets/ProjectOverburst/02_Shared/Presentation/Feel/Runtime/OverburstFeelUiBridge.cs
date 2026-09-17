using UnityEngine;

[DisallowMultipleComponent]
public sealed class OverburstFeelUiBridge : MonoBehaviour
{
    public void PlayConfirm()
    {
        OverburstFeelFeedbackHub.Request(OverburstFeelCue.UiConfirm, Vector3.zero);
    }
}
