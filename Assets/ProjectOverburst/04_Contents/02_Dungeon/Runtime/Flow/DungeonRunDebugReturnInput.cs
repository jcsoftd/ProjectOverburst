using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DungeonRunDebugReturnInput : MonoBehaviour
{
    [SerializeField] private DungeonRunFlow runFlow;
    [SerializeField] private bool enableQuickExtract = true;
    [SerializeField] private Key quickExtractKey = Key.F8;

    public DungeonRunFlow RunFlow => runFlow;

    public void Configure(DungeonRunFlow owner)
    {
        runFlow = owner;
    }

    private void Update()
    {
        if (!enableQuickExtract
            || runFlow == null
            || GameplayInputBlocker.IsGameplayInputBlocked
            || IsTextInputFocused())
        {
            return;
        }

        Keyboard keyboard = Keyboard.current;
        KeyControl control = keyboard != null
            ? keyboard[quickExtractKey]
            : null;
        if (control != null && control.wasPressedThisFrame)
            RequestQuickExtract();
    }

    public bool RequestQuickExtract()
    {
        if (runFlow == null)
            return false;

        Debug.Log("[DungeonRun] F8 quick extract requested.");
        return runFlow.RequestReturnToSourceHub(true);
    }

    private static bool IsTextInputFocused()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null
            || eventSystem.currentSelectedGameObject == null)
        {
            return false;
        }

        GameObject selected = eventSystem.currentSelectedGameObject;
        return selected.GetComponent<TMP_InputField>() != null
            || selected.GetComponent<InputField>() != null;
    }
}
