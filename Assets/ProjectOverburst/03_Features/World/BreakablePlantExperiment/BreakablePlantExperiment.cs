using UnityEngine;
using UnityEngine.InputSystem;

// Experiment-only health adapter: the existing target registry sees a neutral target,
// while interface damage breaks scenery without emitting actor HP/on-hit events.
[DisallowMultipleComponent]
public sealed class BreakablePlantExperiment : CombatHealth, IDamageable
{
    [SerializeField] Rigidbody[] stones;
    [SerializeField] Collider intactCollider;
    [SerializeField] CombatTarget target;
    [SerializeField, Min(.1f)] float debrisLifetime = 3f;
    [SerializeField] bool showOverlay = true;
    Vector3[] positions, scales;
    Quaternion[] rotations;
    float brokenAt;
    bool debrisCleared;

    public bool Broken { get; private set; }
    public bool DebrisCleared => debrisCleared;
    public int BreakCount { get; private set; }
    public int FragmentCount => stones != null ? stones.Length : 0;
    public bool BlocksMovement => intactCollider != null && intactCollider.enabled;

    void CachePose()
    {
        if (positions != null) return;
        positions = new Vector3[stones.Length];
        scales = new Vector3[stones.Length];
        rotations = new Quaternion[stones.Length];
        for (int i = 0; i < stones.Length; i++)
        {
            positions[i] = stones[i].transform.localPosition;
            scales[i] = stones[i].transform.localScale;
            rotations[i] = stones[i].transform.localRotation;
        }
    }

    void IDamageable.TakeDamage(DamageInfo info)
    {
        if (!Application.isPlaying || Broken || info.damage <= 0f || info.isDamageOverTime)
            return;
        CachePose();
        Broken = true;
        debrisCleared = false;
        brokenAt = Time.time;
        BreakCount++;
        target.enabled = false;
        intactCollider.enabled = false;
        Vector3 direction = info.direction;
        direction.y = 0;
        direction = direction.sqrMagnitude > .001f ? direction.normalized : transform.forward;
        var player = PlayerContext.Instance?.CurrentActor;
        var playerColliders = player != null ? player.GetComponentsInChildren<Collider>() : null;
        for (int i = 0; i < stones.Length; i++)
        {
            Rigidbody body = stones[i];
            body.isKinematic = false;
            body.useGravity = true;
            var stoneCollider = body.GetComponent<Collider>();
            stoneCollider.enabled = true;
            if (playerColliders != null)
                foreach (var collider in playerColliders)
                    if (collider != null) Physics.IgnoreCollision(stoneCollider, collider);
            Vector3 away = body.worldCenterOfMass - info.hitPoint;
            away.y = 0;
            body.linearVelocity = direction * Random.Range(1.7f, 2.8f)
                + away.normalized * Random.Range(.3f, 1.1f) + Vector3.up * Random.Range(1.4f, 3.1f);
            body.angularVelocity = Random.insideUnitSphere * 6f;
        }
    }

    public void ResetPlant()
    {
        CachePose();
        for (int i = 0; i < stones.Length; i++)
        {
            Rigidbody body = stones[i];
            if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.isKinematic = true;
            body.useGravity = false;
            body.gameObject.SetActive(true);
            body.transform.localPosition = positions[i];
            body.transform.localRotation = rotations[i];
            body.transform.localScale = scales[i];
            body.GetComponent<Collider>().enabled = false;
        }
        Broken = false;
        debrisCleared = false;
        intactCollider.enabled = true;
        target.enabled = true;
        ResetHealth();
        Physics.SyncTransforms();
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetPlant();
        if (!Broken || debrisCleared) return;
        float age = Time.time - brokenAt;
        if (age < debrisLifetime - .6f) return;
        float scale = Mathf.Clamp01((debrisLifetime - age) / .6f);
        for (int i = 0; i < stones.Length; i++)
        {
            stones[i].transform.localScale = scales[i] * Mathf.Max(.001f, scale);
            if (age >= debrisLifetime) stones[i].gameObject.SetActive(false);
        }
        debrisCleared = age >= debrisLifetime;
    }

    void OnDisable()
    {
        if (stones == null) return;
        foreach (var stone in stones)
            if (stone != null) { stone.isKinematic = true; stone.useGravity = false; }
    }

    void OnGUI()
    {
        if (!showOverlay) return;
        GUI.Box(new Rect(16, 16, 360, 78), "식생 파괴 실험 · 기존 대검 약공 / 일반 강공\n식생을 향해 공격 · R: 모든 식생 복원 · Play 종료: 실험 종료\n" + (Broken ? "부러짐 — 조각 3초 후 정리 · 그루터기 유지" : "한 번의 유효 타격으로 파괴"));
    }
}
