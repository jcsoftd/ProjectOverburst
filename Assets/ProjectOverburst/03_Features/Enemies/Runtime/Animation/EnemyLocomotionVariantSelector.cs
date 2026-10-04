using UnityEngine;

// Selects a complete idle cycle or movement interval without changing combat state.
[DefaultExecutionOrder(300)]
[DisallowMultipleComponent]
public sealed class EnemyLocomotionVariantSelector : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private string moveParameter = "Locomotion";
    [SerializeField] private string moveChoiceParameter = "MoveVariant";
    [SerializeField] private string locomotionState = "Locomotion";
    [SerializeField] private string[] idleStates;
    [SerializeField] private float[] idleWeights;
    [SerializeField] private float[] moveWeights;
    private EnemyActor actor;
    private EnemyAnimationBridge bridge;
    private EnemyAbilityController abilities;
    private CombatHealth health;
    private bool wasMoving;
    private int idleIndex = -1, moveIndex = -1;
    private bool idleEntered;
    private float idleRequestedAt;
    public int IdleSelectionCount { get; private set; }
    public int MoveSelectionCount { get; private set; }
    public int IdleIndex => idleIndex;
    public int MoveIndex => moveIndex;
    public void Configure(Animator target, string[] states, float[] idle, float[] move)
    {
        animator = target;
        idleStates = states != null ? (string[])states.Clone() : new string[0];
        idleWeights = idle != null ? (float[])idle.Clone() : new float[0];
        moveWeights = move != null ? (float[])move.Clone() : new float[0];
    }
    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        actor = GetComponent<EnemyActor>(); bridge = GetComponent<EnemyAnimationBridge>();
        abilities = GetComponent<EnemyAbilityController>(); health = GetComponent<CombatHealth>();
    }
    private void OnEnable() { ResetSelection(); }
    private void OnDisable() { ResetSelection(); }
    private void ResetSelection()
    {
        wasMoving = false; idleEntered = false; idleIndex = moveIndex = -1;
        IdleSelectionCount = MoveSelectionCount = 0;
    }
    private static int Pick(float[] weights)
    {
        if (weights == null || weights.Length == 0) return -1;
        float total = 0f;
        foreach (float weight in weights) total += Mathf.Max(0f, weight);
        if (total <= 0f) return 0;
        float draw = Random.value * total;
        for (int i = 0; i < weights.Length; i++)
        { draw -= Mathf.Max(0f, weights[i]); if (draw <= 0f) return i; }
        return weights.Length - 1;
    }
    private void LateUpdate()
    {
        if (animator == null || !animator.isActiveAndEnabled || actor == null || !actor.IsLeased
            || health == null || health.IsDead || bridge == null || bridge.IsFrozen
            || bridge.IsBlockingActionActive || abilities != null && abilities.IsExecuting)
        { wasMoving = false; idleEntered = false; return; }
        bool moving = Mathf.Abs(animator.GetFloat(moveParameter)) > .08f;
        if (moving)
        {
            if (!wasMoving)
            {
                moveIndex = Pick(moveWeights);
                if (moveIndex >= 0) { animator.SetFloat(moveChoiceParameter, moveIndex); MoveSelectionCount++; }
            }
            wasMoving = true; idleEntered = false; return;
        }
        wasMoving = false;
        if (idleStates == null || idleWeights == null || idleStates.Length == 0 || idleStates.Length != idleWeights.Length
            || animator.IsInTransition(0)) return;
        var state = animator.GetCurrentAnimatorStateInfo(0);
        if (idleIndex >= 0 && state.IsName(idleStates[idleIndex]))
        {
            idleEntered = true;
            if (state.normalizedTime < 1f) return;
        }
        else
        {
            if (!state.IsName(locomotionState)) return;
            if (!idleEntered && Time.time - idleRequestedAt < .5f) return;
        }
        idleIndex = Pick(idleWeights); idleEntered = false; idleRequestedAt = Time.time;
        animator.CrossFadeInFixedTime(idleStates[idleIndex], .12f, 0, 0f);
        IdleSelectionCount++;
    }
}
