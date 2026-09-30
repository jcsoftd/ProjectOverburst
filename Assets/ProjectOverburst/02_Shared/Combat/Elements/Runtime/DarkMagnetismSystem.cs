using System.Collections.Generic;
using UnityEngine;

// 60D corrosion magnet: corroded enemies drift toward the weighted centre of nearby corroded enemies.
// One central tick for every participant; movement is applied by EnemyMovement only on its normal
// locomotion path, so attacks, stagger, knockback, freeze and death never receive magnet displacement.
public sealed class DarkMagnetismSystem : MonoBehaviour
{
    private struct Participant
    {
        public ElementalStatusController Status;
        public EnemyMovement Movement;
        public EnemyGradeType Grade;
        public float BodyRadius;
        public int Stacks;
        public Vector3 Position;
    }

    private static DarkMagnetismSystem instance;
    private readonly List<Participant> participants = new List<Participant>(64);
    private readonly HashSet<ElementalStatusController> registered = new HashSet<ElementalStatusController>();
    private float accumulator;

    public static int ParticipantCount => instance != null ? instance.participants.Count : 0;
    public static int TickCount { get; private set; }
    public static int LastMovingCount { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        TickCount = 0;
        LastMovingCount = 0;
    }

    public static void Register(ElementalStatusController status)
    {
        if (status == null || !Application.isPlaying) return;
        if (instance == null)
        {
            var root = new GameObject("DarkMagnetismSystem");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<DarkMagnetismSystem>();
        }
        if (!instance.registered.Add(status)) return;
        EnemyCrowdAgent crowd = status.GetComponent<EnemyCrowdAgent>();
        CombatTarget target = status.GetComponent<CombatTarget>();
        instance.participants.Add(new Participant
        {
            Status = status,
            Movement = status.Movement != null ? status.Movement : status.GetComponent<EnemyMovement>(),
            Grade = status.GradeType,
            BodyRadius = crowd != null ? crowd.BodyRadius : target != null ? target.CurrentHurtVolume.Radius : 0.4f
        });
    }

    private void Update()
    {
        if (participants.Count == 0) return;
        OverburstElementTuning tuning = OverburstElementTuning.Current;
        float interval = tuning.SafeDarkMagnetTickInterval;
        // A hitch advances at most one interval: the magnet never runs several catch-up ticks at once.
        accumulator += Mathf.Min(Mathf.Max(0f, Time.deltaTime), interval);
        if (accumulator < interval) return;
        accumulator = 0f;
        using var costScope = ElementCombatCostMarkers.Dark_Magnet_Tick.Auto();
        Tick(tuning, interval);
    }

    private void Tick(OverburstElementTuning tuning, float interval)
    {
        for (int i = participants.Count - 1; i >= 0; i--)
        {
            Participant p = participants[i];
            int stacks = p.Status != null ? p.Status.RawStackCount(WeaponElement.Dark) : 0;
            bool alive = p.Status != null && p.Status.Health != null && !p.Status.Health.IsDead && p.Movement != null;
            if (!alive || stacks <= 0)
            {
                // Remove by reference: a destroyed controller compares equal to null but is still a set key.
                if ((object)p.Status != null) registered.Remove(p.Status);
                if (p.Movement != null) p.Movement.SetStatusMagnetVelocity(Vector3.zero, 0f);
                int last = participants.Count - 1;
                participants[i] = participants[last];
                participants.RemoveAt(last);
                continue;
            }
            p.Stacks = stacks;
            p.Position = p.Movement.transform.position;
            participants[i] = p;
        }

        float radius = tuning.SafeDarkMagnetRadius;
        float radiusSquared = radius * radius;
        float perStack = tuning.SafeDarkMagnetSpeedPerStack;
        int moving = 0;
        for (int i = 0; i < participants.Count; i++)
        {
            Participant self = participants[i];
            float resistance = OverburstElementTuning.GradeMoveResistance(self.Grade);
            if (resistance <= 0f) { self.Movement.SetStatusMagnetVelocity(Vector3.zero, 0f); continue; }
            Vector3 weighted = Vector3.zero;
            float weightSum = 0f;
            for (int j = 0; j < participants.Count; j++)
            {
                if (j == i) continue;
                Participant other = participants[j];
                Vector3 delta = other.Position - self.Position;
                delta.y = 0f;
                if (delta.sqrMagnitude > radiusSquared) continue;
                float weight = other.Stacks * tuning.DarkMagnetMass(other.Grade);
                weighted += other.Position * weight;
                weightSum += weight;
            }
            if (weightSum <= 0f) { self.Movement.SetStatusMagnetVelocity(Vector3.zero, 0f); continue; }
            Vector3 toCentre = weighted / weightSum - self.Position;
            toCentre.y = 0f;
            float distance = toCentre.magnitude;
            float contact = self.BodyRadius * 2f + 0.2f;
            if (distance <= contact) { self.Movement.SetStatusMagnetVelocity(Vector3.zero, 0f); continue; }
            float speed = self.Movement.ActiveMoveSpeed * perStack * self.Stacks * resistance;
            // Never overshoot the contact distance within one interval.
            speed = Mathf.Min(speed, (distance - contact) / Mathf.Max(0.01f, interval));
            self.Movement.SetStatusMagnetVelocity(toCentre / distance * speed, interval * 1.5f);
            moving++;
        }
        TickCount++;
        LastMovingCount = moving;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
