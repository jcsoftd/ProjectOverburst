using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

// The first blast claims/consumes frozen stacks. Its committed bonus survives weapon changes.
public sealed class ShatterWaveScheduler : MonoBehaviour
{
    public const int RingCount = 5;
    public const float RingDelay = .06f;
    private struct Pending
    {
        public CombatHealth Target;
        public ElementalStatusController Status;
        public int Life;
        public GameObject Source, Prefab;
        public Vector3 Point, Direction, VfxPoint;
        public float Damage, Due, SfxEnergy;
        public bool VisualOnly;
    }
    private static ShatterWaveScheduler instance;
    private static readonly ProfilerMarker DispatchMarker = new ProfilerMarker("Overburst.Cost.Shatter.Dispatch");
    private readonly List<Pending> pending = new List<Pending>(256);
    private float clock;
    public static int PendingCount => instance != null ? instance.pending.Count : 0;
    public static float ResolveDelay(Vector3 center, Vector3 point, float radius)
    {
        Vector3 delta = point - center; delta.y = 0;
        int ring = Mathf.Clamp(Mathf.FloorToInt(delta.magnitude / Mathf.Max(.001f, radius) * RingCount), 0, RingCount - 1);
        return ring * RingDelay;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static void Submit(CombatHealth target, float damage, GameObject source,
        GameObject prefab, Vector3 center, Vector3 point, Vector3 direction, float radius,
        float sfxEnergy = MeleeElementSfxService.FullVolumeEnergy)
    {
        if (target == null) return;
        if (instance == null)
        {
            var root = new GameObject("ShatterWaveScheduler");
            DontDestroyOnLoad(root); instance = root.AddComponent<ShatterWaveScheduler>();
        }
        var status = target.GetComponent<ElementalStatusController>();
        float delay = ResolveDelay(center, point, radius);
        // 2026-09-30: 쇄빙 VFX는 맞은 지점이 아니라 각 몬스터 몸 중심에서 터진다.
        var combatTarget = target.GetComponent<CombatTarget>();
        Vector3 vfxPoint = combatTarget != null ? CombatTargetVfxPlacement.ResolveVolume(combatTarget).Center : point;
        var item = new Pending { Target = target, Status = status,
            Life = status != null ? status.LifecycleVersion : 0,
            Source = source, Prefab = prefab, Point = point, Direction = direction, VfxPoint = vfxPoint,
            Damage = damage, Due = instance.clock + delay, VisualOnly = target.IsDead, SfxEnergy = sfxEnergy };
        if (delay <= 0) Dispatch(item);
        else instance.pending.Add(item);
    }
    private void Update()
    {
        // As with fire/lightning, a hitch must not collapse every remaining ring into one burst.
        clock += Mathf.Clamp(Time.deltaTime, 0, RingDelay);
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var item = pending[i];
            if (item.Due > clock + .000001f) continue;
            pending.RemoveAt(i);
            Dispatch(item);
        }
    }
    private static void Dispatch(Pending item)
    {
        using var scope = DispatchMarker.Auto();
        if (!item.VisualOnly)
        {
            if (item.Target == null || !item.Target.isActiveAndEnabled || item.Target.IsDead
                || (item.Status != null && item.Status.LifecycleVersion != item.Life)) return;
            if (item.Damage > 0)
                item.Target.TakeDamage(new DamageInfo(item.Damage, item.Point, item.Source, item.Direction,
                    triggersOnHitEffects: false, suppressDefaultHitVfx: true,
                    element: WeaponElement.Ice, playerAttackKind: PlayerAttackKind.Elemental));
        }
        if (item.Prefab != null)
            TransientVfxPool.Spawn(item.Prefab, item.VfxPoint, Quaternion.identity, 0, MeleeHeavyVfxPreparation.RetainedCapacity(item.Prefab));
        MeleeElementSfxService.TryPlayFollowUp(WeaponElement.Ice, item.VfxPoint, item.SfxEnergy); // A21 대상별 쇄빙음
    }
    private void OnDestroy()
    {
        pending.Clear();
        if (instance == this) instance = null;
    }
}
