using System;
using System.Collections.Generic;
using UnityEngine;
using Overburst.Persistence;

// Arrivals share one validated runtime transaction. Only then are their views returned.
public static class CurrencyPickupBatch
{
    private readonly struct Entry
    {
        public readonly CurrencyWorldPickup Pickup;
        public readonly uint Version;
        public Entry(CurrencyWorldPickup pickup, uint version) { Pickup = pickup; Version = version; }
    }
    private const int MaxBatchSize = 128;
    private static readonly Queue<Entry> Pending = new Queue<Entry>(128);
    private static readonly List<Entry> Work = new List<Entry>(128);
    private static readonly List<bool> Accepted = new List<bool>(128);
    private static Host host;
    private static bool processing;
    private static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("Overburst.Currency.Batch");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { Pending.Clear(); Work.Clear(); Accepted.Clear(); host = null; processing = false; }

    internal static void Enqueue(CurrencyWorldPickup pickup, uint version)
    {
        if (host == null)
        {
            var root = new GameObject("Currency pickup batch");
            UnityEngine.Object.DontDestroyOnLoad(root);
            host = root.AddComponent<Host>();
        }
        Pending.Enqueue(new Entry(pickup, version));
    }

    public static void DrainPending()
    {
        // Checkpoints include arrivals already queued before this boundary.
        int batches = (Pending.Count + MaxBatchSize - 1) / MaxBatchSize;
        for (int i = 0; i < batches; i++) FlushPending();
    }

    public static void FlushPending()
    {
        if (processing || Pending.Count == 0 || AccountGameplaySession.Current?.IsEditing == true) return;
        using (Marker.Auto())
        {
            processing = true;
            Work.Clear(); Accepted.Clear();
            while (Pending.Count > 0 && Work.Count < MaxBatchSize) { Work.Add(Pending.Dequeue()); Accepted.Add(false); }
            bool committed = false;
            try
            {
                committed = AccountGameplaySession.RunCurrencyAcquisition(() =>
                {
                    bool any = false;
                    for (int i = 0; i < Work.Count; i++)
                    {
                        var entry = Work[i];
                        Accepted[i] = entry.Pickup != null && entry.Pickup.TryAddQueued(entry.Version);
                        any |= Accepted[i];
                    }
                    return any;
                });
            }
            catch (Exception error) { Debug.LogException(error); }
            finally
            {
                for (int i = 0; i < Work.Count; i++)
                {
                    var entry = Work[i];
                    if (entry.Pickup != null)
                        try { entry.Pickup.CompleteQueued(entry.Version, committed && Accepted[i]); }
                        catch (Exception error) { Debug.LogException(error); }
                }
                Work.Clear(); Accepted.Clear(); processing = false;
            }
        }
    }
    private sealed class Host : MonoBehaviour
    {
        private void LateUpdate() { FlushPending(); }
        private void OnDestroy() { if (host == this) host = null; }
    }
}
