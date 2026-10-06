using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Samples the animated blade after the combat animator (371), before the camera (520).
[DefaultExecutionOrder(480), DisallowMultipleComponent]
public sealed class PerfectParryContactPresenter : MonoBehaviour
{
    private PlayerParryController owner;
    private PlayerEquipment equipment;
    private CombatHealth health;
    private EnemyActor target;
    private uint targetLease;
    private Transform weapon, bladeGrip;
    private Vector3 savedContact, incomingDirection;
    private int contentScene, extras;
    private readonly List<Vector3> initialContacts = new List<Vector3>(2);
    private Scene capturedScene;
    private bool captured, queued;
    private System.Action<GameObject> prepareMain, prepareExtra;
    private float additionalDelay;
    public Vector3 LastPosition { get; private set; }
    public int MainCount { get; private set; }
    public int AdditionalCount { get; private set; }
    public Scene ContentScene => capturedScene;
    public PerfectParryContactProfile Profile => PerfectParryContactProfile.Current;
    public bool CanPresent => captured && Profile != null && Profile.IsReady && equipment != null
        && equipment.CurrentWeaponRoot == weapon && weapon != null && target != null
        && target.IsLeased && target.LeaseVersion == targetLease && capturedScene.isLoaded;
    private void Awake()
    {
        owner = GetComponent<PlayerParryController>(); equipment = GetComponent<PlayerEquipment>(); health = GetComponent<CombatHealth>();
        if (GetComponent<PerfectParryWeaponAfterimage>() == null) gameObject.AddComponent<PerfectParryWeaponAfterimage>();
        prepareMain = value => value.GetComponent<PerfectParryContactVfx>().Prepare(Profile, 0);
        prepareExtra = value => value.GetComponent<PerfectParryContactVfx>().Prepare(Profile, additionalDelay);
    }
    public void BeginAction() { Cancel(); extras = 0; }
    public void Capture(EnemyActor enemy, Vector3 contact, Vector3 direction)
    {
        target = enemy; targetLease = enemy != null ? enemy.LeaseVersion : 0;
        weapon = equipment != null ? equipment.CurrentWeaponRoot : null;
        bladeGrip = weapon != null ? weapon.GetComponent<WeaponGripMount>()?.RightHandGripPoint : null;
        savedContact = contact; incomingDirection = direction;
        capturedScene = enemy != null ? enemy.gameObject.scene : default;
        contentScene = capturedScene.handle; captured = enemy != null;
    }
    public void QueuePresentation() { queued = true; }
    public void AddInitialContact(Vector3 contact) { if (initialContacts.Count < 2) initialContacts.Add(contact); }
    private void LateUpdate()
    {
        if (!queued || Time.timeScale <= 0f || GameplayInputBlocker.IsGameplayInputBlocked) return;
        queued = false;
        if (owner == null || !owner.isActiveAndEnabled || health == null || health.IsDead) return;
        if (!PlayMain()) return;
        foreach (var point in initialContacts) PlayAdditional(point, .12f);
        initialContacts.Clear();
    }
    public void Cancel() { queued = false; initialContacts.Clear(); captured = false; target = null; weapon = bladeGrip = null; }
    public bool PlayMain()
    {
        if (!CanPresent) return false;
        LastPosition = ResolvePosition(); MainCount++;
        if (OverburstGameSettings.HitEffectScale > 0f)
            TransientVfxPool.Spawn(Profile.mainPrefab, LastPosition, ResolveRotation(), 0f, 8,
                prepareBeforeActivation: prepareMain, returnMode: TransientVfxReturnMode.NaturalParticleCompletion,
                useUnscaledTime: true, contentSceneHandle: contentScene);
        return true;
    }
    public void PlayAdditional(Vector3 contact, float delay)
    {
        if (!CanPresent || extras >= 2) return;
        extras++; AdditionalCount++;
        if (OverburstGameSettings.HitEffectScale <= 0f) return;
        additionalDelay = delay;
        TransientVfxPool.Spawn(Profile.additionalPrefab, contact, ResolveRotation(), 0f, 16,
            prepareBeforeActivation: prepareExtra, returnMode: TransientVfxReturnMode.NaturalParticleCompletion,
            useUnscaledTime: true, contentSceneHandle: contentScene);
    }
    public Vector3 ResolvePosition()
    {
        if (!TryGetBladeSegment(out Vector3 a, out Vector3 b)) return savedContact;
        Vector3 blade = b - a;
        float u = Mathf.Clamp(Vector3.Dot(savedContact - a, blade) / blade.sqrMagnitude, Profile.bladeMin, Profile.bladeMax);
        return a + blade * u + weapon.transform.TransformVector(Profile.localOffset);
    }
    public bool TryGetBladeSegment(out Vector3 a, out Vector3 b)
    {
        Transform tip = equipment != null ? equipment.CurrentWeaponTraceBinding?.WeaponTip : null;
        a = bladeGrip != null ? bladeGrip.position : weapon != null ? weapon.position : Vector3.zero;
        b = tip != null ? tip.position : a;
        return weapon != null && tip != null && (b - a).sqrMagnitude >= .01f;
    }
    private Quaternion ResolveRotation()
    {
        Vector3 direction = incomingDirection; direction.y = 0f;
        if (direction.sqrMagnitude < .0001f) direction = transform.forward;
        return Quaternion.LookRotation(-direction.normalized, Vector3.up);
    }
    private void OnDisable() { Cancel(); }
}
