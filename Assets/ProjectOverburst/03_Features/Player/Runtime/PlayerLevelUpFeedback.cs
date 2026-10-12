using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PlayerLevelUpFeedback : MonoBehaviour
{
    private const float Radius = 4f, MaximumHeightDifference = 1.5f, ReactionSeconds = .25f;
    private PlayerProgression progression;
    private PlayerContext context;
    private PlayerActorRuntime actor;
    private PlayerLevelUpNotification notification;
    private readonly List<CombatTarget> targets = new List<CombatTarget>(64);
    private readonly HashSet<int> processed = new HashSet<int>();
    private RaycastHit[] visibilityHits = new RaycastHit[32];
    private int pendingLevel, shownAtFrame, runRevision;
    private int pendingPulses;
    private bool hasFeedback;
    private Scene actorScene;

    public void Bind(PlayerProgression nextProgression, PlayerContext nextContext)
    {
        Unbind(); progression = nextProgression; context = nextContext;
        if (progression != null) progression.LeveledUp += Play;
        if (context != null) context.CurrentActorChanged += ActorChanged;
        ActorChanged(context != null ? context.CurrentActor : null);
    }
    private void ActorChanged(PlayerActorRuntime nextActor) { Clear(); actor = nextActor; }
    private bool UsableActor => actor != null && actor.isActiveAndEnabled && actor.Health != null && !actor.Health.IsDead;
    private void Play(int previousLevel, int nextLevel)
    {
        if (nextLevel <= previousLevel || !UsableActor || PersistentSceneFlow.Instance?.IsSwitching == true) return;
        actorScene = actor.gameObject.scene; runRevision = RunWalkableContext.Revision;
        hasFeedback = true;
        pendingLevel = nextLevel; shownAtFrame = Time.frameCount;
        CombatActionSfxService.PlayLevelUp(actor.transform.position);
        if (Time.timeScale > 0f) Pulse(); else pendingPulses++;
        // A card reward closes its panel after its commit callback; show on the next frame.
    }
    private void Update()
    {
        if (!UsableActor || PersistentSceneFlow.Instance?.IsSwitching == true
            || hasFeedback
                && (actor.gameObject.scene != actorScene || RunWalkableContext.Revision != runRevision))
        { Clear(); return; }
        if (pendingLevel != 0 && Time.frameCount > shownAtFrame)
        {
            if (notification == null) InstallNotification();
            if (notification != null) notification.Show(pendingLevel);
            pendingLevel = 0;
        }
        while (pendingPulses > 0 && Time.timeScale > 0f) { pendingPulses--; Pulse(); }
    }
    private void InstallNotification()
    {
        var hud = FindFirstObjectByType<PlayerHealthHud>(FindObjectsInactive.Include);
        Canvas canvas = hud != null ? hud.GetComponentInParent<Canvas>(true) : null;
        var prefab = Resources.Load<GameObject>(PlayerLevelUpNotification.ResourcePath);
        if (canvas != null && prefab != null)
            notification = Instantiate(prefab, canvas.transform, false).GetComponent<PlayerLevelUpNotification>();
    }
    private void Clear()
    {
        pendingLevel = 0; pendingPulses = 0; hasFeedback = false;
        if (notification != null) notification.Hide();
        CombatActionSfxService.StopLevelUp();
    }
    private void Unbind()
    {
        if (progression != null) progression.LeveledUp -= Play;
        if (context != null) context.CurrentActorChanged -= ActorChanged;
        Clear(); progression = null; context = null; actor = null;
    }
    private void OnDisable() => Unbind();
    private void OnDestroy() { if (notification != null) Destroy(notification.gameObject); }

    private void Pulse()
    {
        if (!UsableActor) return;
        Vector3 origin = actor.transform.position;
        CombatTargetRegistry.CollectPotentialTargets(origin, Radius, targets);
        processed.Clear();
        foreach (CombatTarget target in targets)
        {
            if (target == null || !target.IsAlive || target.Team != CombatTeam.Enemy || !processed.Add(target.TargetId)) continue;
            Vector3 delta = target.transform.position - origin;
            if (Mathf.Abs(delta.y) > MaximumHeightDifference) continue;
            delta.y = 0f;
            if (delta.sqrMagnitude > Radius * Radius) continue;
            var reaction = target.GetComponent<EnemyMovementReaction>();
            var motor = target.GetComponent<EnemyMotor>();
            var rank = target.GetComponent<EnemyRank>();
            var motion = target.GetComponent<EnemyAnimationBridge>();
            if (reaction == null || !reaction.isActiveAndEnabled || motor == null || motor.IsFrozen
                || motion != null && motion.IsFrozen || rank != null && rank.GradeType == EnemyGradeType.Boss
                || !VisibleFromPlayer(target, origin + Vector3.up * .8f)) continue;
            Vector3 direction = delta.sqrMagnitude > .0001f ? delta.normalized
                : EnemyCrowdPrioritySolver.ResolveStablePairDirection(target.TargetId, actor.GetInstanceID());
            float distance = rank != null && rank.GradeType == EnemyGradeType.GreaterElite ? .25f
                : rank != null && rank.GradeType == EnemyGradeType.Elite ? .4f
                : reaction.HitWeightProfile == null ? .8f
                : reaction.HitWeightProfile.Weight == EnemyHitWeight.Light ? 1.2f
                : reaction.HitWeightProfile.Weight == EnemyHitWeight.Heavy ? .5f : .8f;
            var abilities = target.GetComponent<EnemyAbilityController>();
            if (abilities != null) abilities.Cancel(); else target.GetComponent<EnemyMeleeAttackController>()?.CancelAttack();
            reaction.ApplyKnockbackDistance(direction, distance, false, true);
            reaction.ApplyHitStun(ReactionSeconds, false);
            reaction.ExtendKnockbackReaction(ReactionSeconds, false);
            if (motion != null && !reaction.IsParryStunned && !motion.IsParryStunAnimating && !motion.IsNormalParryReacting)
                motion.PlayResolvedHit(new DamageInfo(0f, target.WorldCenter, actor.gameObject,
                    direction: direction, triggersOnHitEffects: false, suppressDefaultHitVfx: true));
        }
        targets.Clear(); processed.Clear();
    }
    private bool VisibleFromPlayer(CombatTarget target, Vector3 origin)
    {
        Vector3 delta = target.WorldCenter - origin;
        float distance = delta.magnitude;
        if (distance < .001f) return true;
        PhysicsScene physics = target.gameObject.scene.GetPhysicsScene();
        int count;
        while ((count = physics.Raycast(origin, delta / distance, visibilityHits, distance, ~0, QueryTriggerInteraction.Ignore)) == visibilityHits.Length)
            System.Array.Resize(ref visibilityHits, visibilityHits.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider hit = visibilityHits[i].collider;
            if (hit == null || hit.transform.IsChildOf(actor.transform) || hit.transform.IsChildOf(target.transform)
                || CombatTarget.Resolve(hit) != null) continue;
            return false;
        }
        return true;
    }
}
