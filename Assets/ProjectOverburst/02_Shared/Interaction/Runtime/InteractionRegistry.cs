using System.Collections.Generic;
using UnityEngine;

public static class InteractionRegistry
{
    private static readonly List<IInteractable> Entries = new List<IInteractable>(32);

    public static int Count
    {
        get
        {
            RemoveInvalidEntries();
            return Entries.Count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Entries.Clear();

    public static void Register(IInteractable interactable)
    {
        if (!IsAlive(interactable) || Entries.Contains(interactable))
            return;
        Entries.Add(interactable);
    }

    public static void Unregister(IInteractable interactable)
    {
        if (interactable != null)
            Entries.Remove(interactable);
    }

    public static void CopyTo(List<IInteractable> destination)
    {
        if (destination == null)
            return;

        destination.Clear();
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            IInteractable entry = Entries[i];
            if (!IsAlive(entry))
            {
                Entries.RemoveAt(i);
                continue;
            }
            destination.Add(entry);
        }
    }

    private static void RemoveInvalidEntries()
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (!IsAlive(Entries[i]))
                Entries.RemoveAt(i);
        }
    }

    private static bool IsAlive(IInteractable interactable)
    {
        return interactable != null && interactable.InteractionComponent != null;
    }
}
