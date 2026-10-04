using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Uses the combat roster, not scene-wide searches. Wounds never change HP or account state.
[DefaultExecutionOrder(-780)]
public sealed class LowHealthBloodTrailService : MonoBehaviour
{
    public const float HealthThreshold = .30f;
    public const float DropInterval = .65f;
    public const float DropSpacing = .55f;
    public const int Capacity = 96, PerTickLimit = 4;
    struct Wound { public CombatHealth health; public Vector3 last; public float next; public bool active; }
    readonly Wound[] wounds = new Wound[Capacity];
    readonly List<CombatTarget> roster = new List<CombatTarget>(128);
    float nextRoster, nextTick;
    int cursor;
    public int PlayerDropCount { get; private set; }
    public int MonsterDropCount { get; private set; }
    public int TrackedCount { get; private set; }
    public static BloodHitProfile ProfileFor(CombatHealth health)
    {
        if (health == null) return null;
        if (CombatTeamUtility.IsPlayerActorHealth(health)) return Resources.Load<BloodHitProfile>(BloodHitVfxService.PlayerProfilePath);
        return health.TryGetComponent<BloodHitTarget>(out var target) ? target.Profile : null;
    }
    public static bool ShouldBleed(CombatHealth health)
    {
        if (health == null || !health.isActiveAndEnabled || health.IsDead || health.CurrentHp <= 0f || health.NormalizedHp > HealthThreshold) return false;
        var profile = ProfileFor(health);
        return profile != null && !profile.suppressBlood;
    }
    void Update()
    {
        float now = Time.time;
        if (now < nextTick) return;
        nextTick = now + .1f;
        for (int i = 0; i < wounds.Length; i++)
            if (wounds[i].active && !ShouldBleed(wounds[i].health)) { wounds[i] = default; TrackedCount--; }
        if (now >= nextRoster)
        {
            nextRoster = now + .5f;
            CombatTargetRegistry.CollectTeamTargets(CombatTeam.Enemy, roster);
            foreach (var target in roster) Track(target.DamageReceiver, now);
            CombatTargetRegistry.CollectTeamTargets(CombatTeam.PlayerParty, roster);
            foreach (var target in roster) Track(target.DamageReceiver, now);
            if (PlayerInputFacade.Current != null) Track(PlayerInputFacade.Current.GetComponent<CombatHealth>(), now);
        }
        int sent = 0;
        for (int n = 0; n < wounds.Length && sent < PerTickLimit; n++)
        {
            int i = (cursor + n) % wounds.Length;
            var wound = wounds[i];
            if (wound.health == null || now < wound.next) continue;
            Vector3 position = wound.health.transform.position;
            Vector3 delta = Vector3.ProjectOnPlane(position - wound.last, Vector3.up);
            // Teleports restart the trail instead of drawing a streak between distant maps.
            if (delta.sqrMagnitude > 25f) { wounds[i].last = position; continue; }
            if (delta.sqrMagnitude < DropSpacing * DropSpacing) continue;
            if (BloodHitVfxService.RequestBleed(wound.health, ProfileFor(wound.health), position, delta.normalized))
            {
                if (CombatTeamUtility.IsPlayerActorHealth(wound.health)) PlayerDropCount++; else MonsterDropCount++;
                sent++;
            }
            wounds[i].last = position; wounds[i].next = now + DropInterval;
        }
        cursor = (cursor + PerTickLimit) % wounds.Length;
    }
    void Track(CombatHealth health, float now)
    {
        if (!ShouldBleed(health)) return;
        int free = -1;
        for (int i = 0; i < wounds.Length; i++)
        {
            if (wounds[i].active && wounds[i].health == health) return;
            if (!wounds[i].active && free < 0) free = i;
        }
        if (free < 0) return;
        wounds[free] = new Wound { health = health, last = health.transform.position, next = now + DropInterval, active = true };
        TrackedCount++;
    }
    void Clear() { System.Array.Clear(wounds, 0, wounds.Length); TrackedCount = 0; nextRoster = nextTick = 0f; }
    void SceneChanged(Scene previous, Scene current) => Clear();
    void SceneUnloaded(Scene scene) => Clear();
    void OnEnable() { SceneManager.activeSceneChanged += SceneChanged; SceneManager.sceneUnloaded += SceneUnloaded; }
    void OnDisable() { SceneManager.activeSceneChanged -= SceneChanged; SceneManager.sceneUnloaded -= SceneUnloaded; Clear(); }
}
