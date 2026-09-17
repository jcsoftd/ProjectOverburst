using System.Collections.Generic;
using UnityEngine;

public static class ElementalStatusScheduler
{
    private const int MaxControllerAdvancesPerFrame = 256;
    private const int MaxDamageTicksPerFrame = 64;

    private sealed class Driver : MonoBehaviour
    {
        private readonly List<ElementalStatusController> controllers = new List<ElementalStatusController>(256);
        private readonly HashSet<ElementalStatusController> scheduled = new HashSet<ElementalStatusController>();
        private readonly HashSet<ElementalStatusController> listed = new HashSet<ElementalStatusController>();
        private int nextControllerIndex;

        public int ScheduledCount => scheduled.Count;
#if UNITY_EDITOR
        public int LastAdvancedControllerCountForValidation { get; private set; }
        public int LastExecutedTickCountForValidation { get; private set; }
#endif

        public void Register(ElementalStatusController controller)
        {
            if (controller == null)
                return;

            scheduled.Add(controller);
            if (listed.Add(controller))
                controllers.Add(controller);
        }

        public void Unregister(ElementalStatusController controller)
        {
            if (controller != null)
                scheduled.Remove(controller); // 순회 중 목록 변경을 피하고 다음 Advance에서 실제 제거
        }

        private void Update()
        {
            Advance(Time.time, MaxControllerAdvancesPerFrame, MaxDamageTicksPerFrame);
        }

        public void Advance(float now, int controllerBudget, int tickBudget)
        {
            int remainingTicks = Mathf.Max(0, tickBudget);
            int controllerLimit = Mathf.Min(Mathf.Max(0, controllerBudget), controllers.Count);
            int advancedControllers = 0;
            int initialTickBudget = remainingTicks;

            while (advancedControllers < controllerLimit && controllers.Count > 0)
            {
                if (nextControllerIndex >= controllers.Count)
                    nextControllerIndex = 0;

                ElementalStatusController controller = controllers[nextControllerIndex];
                bool keepScheduled = controller != null
                    && scheduled.Contains(controller)
                    && controller.isActiveAndEnabled
                    && controller.AdvanceScheduledStates(now, ref remainingTicks);
                advancedControllers++;

                if (keepScheduled)
                {
                    nextControllerIndex++;
                    if (initialTickBudget > 0 && remainingTicks == 0)
                        break; // 다음 대상부터 이어서 처리해 한 대상의 밀린 tick 독점을 방지
                    continue;
                }

                scheduled.Remove(controller);
                listed.Remove(controller);
                int lastIndex = controllers.Count - 1;
                controllers[nextControllerIndex] = controllers[lastIndex];
                controllers.RemoveAt(lastIndex);
                if (initialTickBudget > 0 && remainingTicks == 0)
                    break;
            }

#if UNITY_EDITOR
            LastAdvancedControllerCountForValidation = advancedControllers;
            LastExecutedTickCountForValidation = initialTickBudget - remainingTicks;
#endif
        }

        private void OnDestroy()
        {
            controllers.Clear();
            scheduled.Clear();
            listed.Clear();
            nextControllerIndex = 0;
            if (driver == this)
                driver = null;
        }
    }

    private static Driver driver;

    public static int ActiveControllerCount => driver != null ? driver.ScheduledCount : 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        driver = null;
    }

    public static void Register(ElementalStatusController controller)
    {
        if (controller == null)
            return;

        ResolveDriver().Register(controller);
    }

    public static void Unregister(ElementalStatusController controller)
    {
        driver?.Unregister(controller);
    }

#if UNITY_EDITOR
    public static int LastAdvancedControllerCountForValidation =>
        driver != null ? driver.LastAdvancedControllerCountForValidation : 0;
    public static int LastExecutedTickCountForValidation =>
        driver != null ? driver.LastExecutedTickCountForValidation : 0;

    public static void AdvanceForValidation(float now)
    {
        driver?.Advance(now, MaxControllerAdvancesPerFrame, MaxDamageTicksPerFrame);
    }

    public static void AdvanceForValidation(float now, int controllerBudget, int tickBudget)
    {
        driver?.Advance(now, controllerBudget, tickBudget);
    }

    public static void ClearForValidation()
    {
        if (driver == null)
            return;

        Driver current = driver;
        if (Application.isPlaying)
            Object.Destroy(current.gameObject);
        else
            Object.DestroyImmediate(current.gameObject);
        driver = null;
    }
#endif

    private static Driver ResolveDriver()
    {
        if (driver != null)
            return driver;

        GameObject root = new GameObject("ElementalStatusScheduler");
        root.hideFlags = HideFlags.HideAndDontSave;
        driver = root.AddComponent<Driver>();
        return driver;
    }
}
