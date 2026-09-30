using UnityEngine;

// 창이 뒤로 갔을 때 소리 끄기(설정 > 소리). 설정 부트스트랩이 숨긴 오브젝트에 붙인다.
public sealed class OverburstSettingsFocusWatcher : MonoBehaviour
{
    private void OnApplicationFocus(bool hasFocus) => OverburstGameSettings.SetFocused(hasFocus);
    private void OnApplicationQuit() => OverburstGameSettings.SaveIfDirty();
}
