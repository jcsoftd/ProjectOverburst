using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DeveloperNotepadUI : MonoBehaviour
{
    private const string PlayerPrefsKey = "ProjectVTP.DeveloperNotepad.Text";

    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject windowRoot;
    [SerializeField] private TMP_InputField memoInput;
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private bool showInEditorOrDevelopmentBuild = true;

    private bool openListenerRegistered;
    private bool closeListenerRegistered;
    private bool inputListenerRegistered;

    private void Awake()
    {
        if (!IsDebugUiAllowed())
        {
            gameObject.SetActive(false);
            return;
        }

        ResolveReferences();
        RegisterListeners();
        ApplyFont();
        LoadMemo();
        CloseWindow();
    }

    private void OnDestroy()
    {
        SaveMemo();
        UnregisterListeners();
    }

    private void ResolveReferences()
    {
        // 2026-10-01: 여는 버튼은 옛 HUD 디버그 패널에 있었고 패널과 함께 지웠다. 이제 디버그 창(F1 > 시스템 > 메모장)이 Toggle()로 연다.
        // 비어 있을 때 자식 버튼을 찾아 쓰면 창 안의 닫기 버튼에 '열기'가 붙으므로 찾지 않는다.

        if (memoInput == null)
            memoInput = GetComponentInChildren<TMP_InputField>(true);
    }

    private void RegisterListeners()
    {
        if (openButton != null && !openListenerRegistered)
        {
            openButton.onClick.AddListener(OpenWindow);
            openListenerRegistered = true;
        }

        if (closeButton != null && !closeListenerRegistered)
        {
            closeButton.onClick.AddListener(CloseWindow);
            closeListenerRegistered = true;
        }

        if (memoInput != null && !inputListenerRegistered)
        {
            memoInput.onEndEdit.AddListener(HandleMemoChanged);
            inputListenerRegistered = true;
        }
    }

    private void UnregisterListeners()
    {
        if (openButton != null && openListenerRegistered)
            openButton.onClick.RemoveListener(OpenWindow);

        if (closeButton != null && closeListenerRegistered)
            closeButton.onClick.RemoveListener(CloseWindow);

        if (memoInput != null && inputListenerRegistered)
            memoInput.onEndEdit.RemoveListener(HandleMemoChanged);
    }

    public bool IsWindowOpen => windowRoot != null && windowRoot.activeSelf;

    // 디버그 창의 '개발자 메모장 열기'가 부른다(90C 7.8). 메모장 자체 열기 버튼은 그대로다.
    public void Toggle()
    {
        if (IsWindowOpen)
            CloseWindow();
        else
            OpenWindow();
    }

    private void OpenWindow()
    {
        if (windowRoot == null)
            return;

        windowRoot.SetActive(true);
        windowRoot.transform.SetAsLastSibling();
        if (memoInput != null)
            memoInput.ActivateInputField();
    }

    private void CloseWindow()
    {
        SaveMemo();
        if (windowRoot != null)
            windowRoot.SetActive(false);
    }

    private void HandleMemoChanged(string value)
    {
        SaveMemo();
    }

    private void LoadMemo()
    {
        if (memoInput == null)
            return;

        memoInput.text = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
    }

    private void SaveMemo()
    {
        if (memoInput == null)
            return;

        PlayerPrefs.SetString(PlayerPrefsKey, memoInput.text);
        PlayerPrefs.Save();
    }

    private void ApplyFont()
    {
        if (fontAsset == null)
            return;

        TextMeshProUGUI[] texts = GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] == null)
                continue;

            texts[i].font = fontAsset;
            texts[i].fontSharedMaterial = fontAsset.material;
        }
    }

    private bool IsDebugUiAllowed()
    {
        if (!showInEditorOrDevelopmentBuild)
            return false;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return true;
#else
        return false;
#endif
    }
}
