using System;
using System.Reflection;
using UnityEngine;

public static partial class ElementGemPlayVerifier
{
    static void FeedbackPackets(CombatHealth health, ElementalStatusController statuses,
        OverburstElementEnergy energy, PlayerEquipment equipment, ElementGemAttackSnapshot snapshot, WeaponElement element)
    {
        health.ResetHealth(); statuses.ClearAllStatuses(); energy.Clear();
        DamageInfo packet = default;
        float actual = 0f;
        void Resolved(CombatHealth _, DamageInfo info, float damage, bool lethal) { packet = info; actual = damage; }
        health.OnDamageResolved += Resolved;
        try
        {
            float expected = CombatBalanceFormulas.ApplyPlayerOutgoing(100f, snapshot.Stats, false, EnemyGradeType.Normal,
                PlayerAttackKind.Heavy | PlayerAttackKind.Elemental, snapshot.RunAttack, snapshot.RunElemental);
            UpperElementCombatUtility.DealDerivedDamage(health, 100f, health.transform.position,
                equipment.gameObject, Vector3.forward, element, snapshot);
            Check(packet.playerAttackKind == PlayerAttackKind.Elemental && !packet.triggersOnHitEffects && !packet.isDamageOverTime,
                "derived metadata stays elemental " + element);
            Check(Mathf.Abs(actual - expected) < .02f, "derived heavy bonus preserved once " + element);
            Check(DamageNumberStyles.Classify(packet, 2).Kind == DamageNumberKind.Discharge,
                "derived damage uses discharge label " + element);
            Check(energy.Amount == 0f && statuses.GetStackCount(element) == 0,
                "derived feedback cannot charge energy or apply status " + element);
            Check(snapshot.IsCurrent, "derived damage keeps committed gem context " + element);
            Check(MeleeElementSfxService.TryPlayHit(element, equipment.transform.position, energy: 100f),
                "equipped element hit audio starts " + element);
            if (element == WeaponElement.Ice)
            {
                health.ResetHealth();
                ShatterWaveScheduler.Submit(health, 100f, equipment.gameObject, null, health.transform.position,
                    health.transform.position, Vector3.forward, 3f, gemAttack: snapshot);
                Check(packet.playerAttackKind == PlayerAttackKind.Elemental && Mathf.Abs(actual - expected) < .02f,
                    "actual immediate shatter writer preserves metadata and bonus");
            }
            if (element == WeaponElement.Electric)
            {
                var root = new GameObject("ElementGemElectricCounterFixture"); root.SetActive(false);
                try
                {
                    var display = root.AddComponent<DamageNumberSpawner>();
                    var count = typeof(DamageNumberSpawner).GetMethod("NextElectricChainCount", BindingFlags.Instance | BindingFlags.NonPublic);
                    Check((int)count.Invoke(display, new object[] { packet }) == 1
                        && (int)count.Invoke(display, new object[] { packet }) == 2,
                        "actual lightning discharge counter advances");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            health.ResetHealth();
            UpperElementCombatUtility.DealDerivedDamage(health, 100f, health.transform.position,
                equipment.gameObject, Vector3.forward, element);
            float legacy = CombatBalanceFormulas.ApplyPlayerOutgoing(100f, snapshot.Stats, false, EnemyGradeType.Normal,
                PlayerAttackKind.Elemental, snapshot.RunAttack, snapshot.RunElemental);
            Check(Mathf.Abs(actual - legacy) < .02f && !packet.gemAttack.HasValue,
                "legacy derived call does not gain heavy scaling " + element);
            var direct = new DamageInfo(1f, Vector3.zero, element: element, playerAttackKind: PlayerAttackKind.Heavy | PlayerAttackKind.Elemental);
            Check(DamageNumberStyles.Classify(direct, 1).Kind == DamageNumberKind.Normal,
                "direct heavy damage retains normal label " + element);
            direct.isCritical = true;
            Check(DamageNumberStyles.Classify(direct, 1).Kind == DamageNumberKind.Critical,
                "direct critical damage retains critical label " + element);
            direct.isDamageOverTime = true;
            Check(DamageNumberStyles.Classify(direct, 1).Kind == DamageNumberKind.DamageOverTime,
                "status ticks retain their label " + element);
        }
        finally { health.OnDamageResolved -= Resolved; health.ResetHealth(); statuses.ClearAllStatuses(); energy.Clear(); }
    }
}
