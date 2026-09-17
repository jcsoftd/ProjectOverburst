using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class RuntimeUIDiagnostics // UI 진단
{
    public static void DestroyRunTransientUiRoots()
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None); // Canvas 목록
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas == null || !IsRunTransientCanvas(canvas))
                continue;

            canvas.gameObject.SetActive(false);
            Object.Destroy(canvas.gameObject);
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    public static void LogRuntimeState(string label)
    {
        StringBuilder builder = new StringBuilder(); // 진단 로그
        builder.Append("[RuntimeUIRebind] ").Append(label);
        builder.Append(" Player=").Append(CountPlayers());
        builder.Append(" MainCamera=").Append(CountMainCameras());
        builder.Append(" HUDCanvas=").Append(CountCanvasesByName("HUD"));
        builder.Append(" PlayerInventoryCanvas=").Append(CountCanvasesByName("PlayerInventoryCanvas"));
        builder.Append(" InventoryUI=").Append(CountActiveComponents<InventoryUI>());
        builder.Append("/").Append(CountAllComponents<InventoryUI>());
        builder.Append(" InventorySlotBridge=").Append(CountActiveComponents<InventorySlotBridge>());
        builder.Append("/").Append(CountAllComponents<InventorySlotBridge>());
        builder.Append(" StashUI=").Append(CountActiveComponents<StashUI>());
        builder.Append("/").Append(CountAllComponents<StashUI>());
        builder.Append(" StashSlotBridge=").Append(CountActiveComponents<StashSlotBridge>());
        builder.Append("/").Append(CountAllComponents<StashSlotBridge>());
        builder.Append(" TooltipManager=").Append(CountActiveComponents<TooltipManager>());
        builder.Append("/").Append(CountAllComponents<TooltipManager>());
        builder.Append(" EventSystem=").Append(CountActiveComponents<EventSystem>());
        builder.Append("/").Append(CountAllComponents<EventSystem>());
        builder.Append(" GraphicRaycaster=").Append(CountActiveComponents<GraphicRaycaster>());
        builder.Append("/").Append(CountAllComponents<GraphicRaycaster>());
        builder.Append(" WeaponSlotUI=").Append(CountWeaponSlotUis());
        builder.Append(" BrokenDropSlotRefs=").Append(CountBrokenDropSlotRefs());
        builder.Append(" InventoryUIActive=").Append(FormatObjectPath(FindFirstActive<InventoryUI>()));
        builder.Append(" StashUIActive=").Append(FormatObjectPath(FindFirstActive<StashUI>()));
        builder.Append(" TooltipInstance=").Append(FormatObjectPath(TooltipManager.Instance));
        Debug.Log(builder.ToString());
    }

    private static bool IsRunTransientCanvas(Canvas canvas)
    {
        if (canvas == null)
            return false;

        string canvasName = canvas.name; // Canvas 이름
        string rootName = canvas.transform.root != null ? canvas.transform.root.name : string.Empty; // root 이름
        return canvasName.StartsWith("Runtime_DebugMinimap")
            || canvasName.StartsWith("Runtime_DebugWorldMinimap")
            || rootName.StartsWith("Runtime_DebugWorldMinimap");
    }

    private static int CountPlayers()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        return players != null ? players.Length : 0;
    }

    private static int CountMainCameras()
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0; // 활성 수
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].CompareTag("MainCamera") && cameras[i].gameObject.activeInHierarchy)
                count++;
        }

        return count;
    }

    private static int CountCanvasesByName(string namePart)
    {
        Canvas[] canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0; // 활성 수
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null && canvas.gameObject.activeInHierarchy && canvas.name.Contains(namePart))
                count++;
        }

        return count;
    }

    private static int CountActiveComponents<T>() where T : Component
    {
        T[] components = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0; // 슬롯 수
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].gameObject.activeInHierarchy)
                count++;
        }

        return count;
    }

    private static int CountAllComponents<T>() where T : Component
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    }

    private static int CountWeaponSlotUis()
    {
        SlotUI[] slots = Object.FindObjectsByType<SlotUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0; // 깨진 참조 수
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].gameObject.activeInHierarchy && slots[i].IsWeaponSlot)
                count++;
        }

        return count;
    }

    private static int CountBrokenDropSlotRefs()
    {
        DropSlot[] dropSlots = Object.FindObjectsByType<DropSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < dropSlots.Length; i++)
        {
            DropSlot dropSlot = dropSlots[i];
            if (dropSlot == null || !dropSlot.gameObject.activeInHierarchy)
                continue;

            if (dropSlot.Slot == null || dropSlot.Slot.OwnerBridge == null)
                count++;
        }

        return count;
    }

    private static T FindFirstActive<T>() where T : Component
    {
        T[] components = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].gameObject.activeInHierarchy)
                return components[i];
        }

        return null;
    }

    private static string FormatObjectPath(Object target)
    {
        if (target == null)
            return "None";

        Component component = target as Component; // Component 경로
        GameObject gameObject = component != null ? component.gameObject : target as GameObject;
        if (gameObject == null)
            return target.name;

        return gameObject.scene.name + "/" + GetTransformPath(gameObject.transform);
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "None";

        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }
}
