using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class MagicLightningChainProjectile : MonoBehaviour, IMagicProjectile // Lightning 투사체
{
    private const float DamageOverTimeTickInterval = 1f; // 지속 피해 주기

    [Header("Projectile")]
    [SerializeField] private float speed = 14f;
    [SerializeField] private int damage = 1;
    [SerializeField] private float baseDamage = 1f;
    [SerializeField] private float critChance = 6f;
    [SerializeField] private float critDamageMultiplier = 1.45f;
    [SerializeField] private float range = 8.5f;
    [SerializeField] private float lifetime = 3f;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField] private bool destroyOnAnyHit = true;

    [Header("Chain")]
    [SerializeField] private float chainRange = 4f;
    [SerializeField] private int maxChainDepth = 3;
    [SerializeField] private int chainBranchCount = 2;
    [SerializeField] private float chainDelay = 0.06f;
    [SerializeField, Range(0f, 1f)] private float chainDamageFalloff = 0.5f;

    [Header("VFX")]
    [SerializeField] private bool useRuntimeTemporaryVfx = true;
    [SerializeField] private GameObject projectileVfxPrefab;
    [SerializeField] private GameObject hitVfxPrefab;
    [SerializeField] private GameObject chainBeamVfxPrefab;
    [SerializeField] private GameObject castVfxPrefab;

    private Vector3 direction; // 이동 방향
    private float dieTime; // 삭제 시간
    private float traveledDistance; // 이동 누적
    private GameObject source; // 시전자
    private float knockback; // 넉백
    private bool isCritical; // 치명타
    private float dotDamageFlat;
    private float dotDamagePercent;
    private float dotDuration;
    private float hitHeal;
    private float lifeStealPercent;
    private Collider projectileCollider; // 충돌체
    private Rigidbody projectileRigidbody; // 물리체
    private Renderer[] projectileRenderers; // 본체 렌더러
    private bool hasResolved; // 체인 시작
    private GameObject projectileVfxInstance; // 본체 VFX
    private readonly HashSet<int> damagedTargets = new HashSet<int>(); // 중복 타격

    private struct ChainCandidate
    {
        public IDamageable damageable; // 대상
        public int id; // 대상 id
        public Vector3 center; // 중심점
        public float weight; // 선택 가중치
    }

    private void Awake()
    {
        projectileCollider = GetComponent<Collider>(); // 충돌체
        projectileCollider.isTrigger = true; // trigger 충돌
        projectileRenderers = GetComponentsInChildren<Renderer>(true);

        projectileRigidbody = GetComponent<Rigidbody>();
        if (projectileRigidbody == null)
            projectileRigidbody = gameObject.AddComponent<Rigidbody>(); // Rigidbody 보강

        projectileRigidbody.isKinematic = true; // 직접 이동
        projectileRigidbody.useGravity = false; // 중력 없음
        projectileRigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        projectileRigidbody.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void OnEnable()
    {
        dieTime = Time.time + lifetime; // 수명
        traveledDistance = 0f; // 거리 초기화
        hasResolved = false; // 상태 초기화
        damagedTargets.Clear(); // 중복 초기화
        SetProjectileVisible(true);
    }

    private void Update()
    {
        if (hasResolved)
            return;

        Vector3 move = GetClampedMove(direction * speed * Time.deltaTime); // 거리 제한
        if (move.sqrMagnitude <= 0.0001f)
        {
            Destroy(gameObject);
            return;
        }

        if (TryHitAlongMove(move))
            return;

        transform.position += move; // 이동
        traveledDistance += move.magnitude; // 거리 누적

        if (Time.time >= dieTime || range > 0f && traveledDistance >= range)
            Destroy(gameObject);
    }

    public void Configure(MagicProjectileConfig config)
    {
        speed = Mathf.Max(0f, config.speed); // 속도
        damage = Mathf.Max(0, config.damage); // 첫 피해
        baseDamage = Mathf.Max(0f, config.baseDamage); // 체인 기준 피해
        critChance = Mathf.Clamp(config.critChance, 0f, 100f); // 치명 확률
        critDamageMultiplier = Mathf.Max(1f, config.critDamageMultiplier); // 치명 배율
        range = Mathf.Max(0.1f, config.range); // 사거리
        source = config.source; // 시전자
        knockback = Mathf.Max(0f, config.knockback); // 넉백
        isCritical = config.isCritical; // 첫 치명타
        dotDamageFlat = Mathf.Max(0f, config.dotDamageFlat);
        dotDamagePercent = Mathf.Max(0f, config.dotDamagePercent);
        dotDuration = Mathf.Max(0f, config.dotDuration);
        hitHeal = Mathf.Max(0f, config.hitHeal);
        lifeStealPercent = Mathf.Max(0f, config.lifeStealPercent);
        chainRange = Mathf.Max(0.1f, config.chainRange); // 체인 거리
        maxChainDepth = Mathf.Max(0, config.maxChainDepth); // 체인 깊이
        chainBranchCount = Mathf.Max(1, config.chainBranchCount); // 체인 분기
        chainDelay = Mathf.Max(0f, config.chainDelay); // 체인 지연
        chainDamageFalloff = Mathf.Clamp01(config.chainDamageFalloff); // 체인 감쇠
        damagedTargets.Clear(); // 중복 초기화

        if (range > 0f && speed > 0f)
            lifetime = Mathf.Max(lifetime, range / speed + 0.1f); // 수명 보정
    }

    public void Launch(Vector3 launchDirection)
    {
        launchDirection.y = 0f; // XZ 평면

        if (launchDirection.sqrMagnitude <= 0.0001f)
            launchDirection = transform.forward; // 방향 fallback

        direction = launchDirection.normalized; // 발사 방향
        transform.rotation = Quaternion.LookRotation(direction, Vector3.up); // 방향 회전
        dieTime = Time.time + lifetime; // 수명 갱신
        traveledDistance = 0f; // 거리 초기화
        EnsureProjectileVfx();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (hasResolved)
            return;

        HandleCollision(other, transform.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (hasResolved || collision == null)
            return;

        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        HandleCollision(collision.collider, point);
    }

    private bool TryHitAlongMove(Vector3 move)
    {
        float distance = move.magnitude;
        if (distance <= 0.0001f)
            return false;

        float radius = GetSweepRadius();
        Ray ray = new Ray(transform.position, move.normalized);
        RaycastHit[] hits = Physics.SphereCastAll(ray, radius, distance, hitMask, QueryTriggerInteraction.Collide);

        if (hits == null || hits.Length == 0)
            return false;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || hitCollider == projectileCollider)
                continue;

            if (source != null && hitCollider.transform.IsChildOf(source.transform))
                continue;

            if (HandleCollision(hitCollider, hits[i].point))
            {
                transform.position = hits[i].point;
                return true;
            }
        }

        return false;
    }

    private bool HandleCollision(Collider other, Vector3 fallbackHitPoint)
    {
        if (other == null || !IsInHitMask(other.gameObject.layer))
            return false;

        if (source != null && other.transform.IsChildOf(source.transform))
            return false;

        IDamageable damageable = FindDamageable(other);
        if (damageable != null)
        {
            Vector3 hitPoint = other.ClosestPoint(fallbackHitPoint); // 첫 타격점
            StartChainFromFirstTarget(damageable, hitPoint);
            return true;
        }

        if (destroyOnAnyHit && !other.isTrigger)
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }

    private void StartChainFromFirstTarget(IDamageable firstTarget, Vector3 hitPoint)
    {
        if (hasResolved)
            return;

        hasResolved = true; // 이동 종료
        if (projectileCollider != null)
            projectileCollider.enabled = false;

        SetProjectileVisible(false);
        if (projectileVfxInstance != null)
            Destroy(projectileVfxInstance);

        int firstTargetId = GetDamageableId(firstTarget); // 첫 대상 id
        damagedTargets.Add(firstTargetId);
        ApplyDamage(firstTarget, hitPoint, damage, isCritical, direction);
        SpawnHitVfx(hitPoint);
        StartCoroutine(ChainRoutine(firstTarget, 1));
        Destroy(gameObject, Mathf.Max(0.35f, chainDelay * (maxChainDepth + 2) + 0.35f));
    }

    private IEnumerator ChainRoutine(IDamageable sourceTarget, int depth)
    {
        if (depth > maxChainDepth)
        {
            yield break;
        }

        if (chainDelay > 0f)
            yield return new WaitForSeconds(chainDelay);

        Vector3 from = GetDamageableCenter(sourceTarget);
        List<ChainCandidate> targets = SelectChainTargets(from);
        if (targets.Count == 0)
        {
            yield break;
        }

        int chainDamage = CalculateChainDamage(depth);
        for (int i = 0; i < targets.Count; i++)
        {
            ChainCandidate target = targets[i];
            damagedTargets.Add(target.id); // 중복 기록
            bool chainCritical = RollCritical();
            int finalDamage = chainCritical
                ? Mathf.Max(0, Mathf.RoundToInt(chainDamage * critDamageMultiplier))
                : chainDamage; // 체인 피해
            Vector3 knockbackDirection = target.center - from;
            knockbackDirection.y = 0f;
            if (knockbackDirection.sqrMagnitude <= 0.0001f)
                knockbackDirection = direction;
            knockbackDirection.Normalize();

            LightningChainBeamVfx.Spawn(from, target.center, chainBeamVfxPrefab);
            ApplyDamage(target.damageable, target.center, finalDamage, chainCritical, knockbackDirection);
            SpawnHitVfx(target.center);
            StartCoroutine(ChainRoutine(target.damageable, depth + 1));
        }
    }

    private int CalculateChainDamage(int depth)
    {
        float multiplier = Mathf.Pow(chainDamageFalloff, Mathf.Max(1, depth)); // 단계 감쇠
        return Mathf.Max(0, Mathf.RoundToInt(baseDamage * multiplier));
    }

    private List<ChainCandidate> SelectChainTargets(Vector3 from)
    {
        List<ChainCandidate> candidates = new List<ChainCandidate>();
        Collider[] hits = Physics.OverlapSphere(from, chainRange, hitMask, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
            return candidates;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hit = hits[i];
            if (hit == null || hit == projectileCollider)
                continue;

            if (source != null && hit.transform.IsChildOf(source.transform))
                continue;

            IDamageable damageable = FindDamageable(hit);
            if (damageable == null)
                continue;

            int id = GetDamageableId(damageable);
            if (damagedTargets.Contains(id) || ContainsCandidate(candidates, id))
                continue;

            Vector3 center = GetDamageableCenter(damageable); // 대상 중심
            float distance = Vector3.Distance(from, center); // 거리 가중치
            candidates.Add(new ChainCandidate
            {
                damageable = damageable,
                id = id,
                center = center,
                weight = Mathf.Max(0.1f, distance)
            });
        }

        List<ChainCandidate> selected = new List<ChainCandidate>();
        int count = Mathf.Min(chainBranchCount, candidates.Count);
        for (int i = 0; i < count; i++)
        {
            int index = PickWeightedCandidate(candidates);
            if (index < 0)
                break;

            selected.Add(candidates[index]);
            candidates.RemoveAt(index);
        }

        return selected;
    }

    private bool ContainsCandidate(List<ChainCandidate> candidates, int id)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].id == id)
                return true;
        }

        return false;
    }

    private int PickWeightedCandidate(List<ChainCandidate> candidates)
    {
        float totalWeight = 0f;
        for (int i = 0; i < candidates.Count; i++)
            totalWeight += Mathf.Max(0f, candidates[i].weight);

        if (totalWeight <= 0f)
            return candidates.Count > 0 ? 0 : -1;

        float roll = Random.value * totalWeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= Mathf.Max(0f, candidates[i].weight);
            if (roll <= 0f)
                return i;
        }

        return candidates.Count - 1;
    }

    private void ApplyDamage(IDamageable damageable, Vector3 hitPoint, int finalDamage, bool critical, Vector3 knockbackDirection)
    {
        if (damageable == null || finalDamage <= 0)
            return;

        CombatHealth targetHealth = damageable as CombatHealth;
        float targetHpBeforeHit = targetHealth != null ? targetHealth.CurrentHp : -1f;
        DamageInfo info = new DamageInfo(finalDamage, hitPoint, source, knockbackDirection, knockback, critical); // 피해 정보
        damageable.TakeDamage(info);
        float actualDamage = GetActualDamageDealt(finalDamage, targetHealth, targetHpBeforeHit);
        ApplyOnHitEffects(targetHealth, actualDamage);
    }

    private float GetActualDamageDealt(int attemptedDamage, CombatHealth targetHealth, float targetHpBeforeHit)
    {
        if (targetHealth == null || targetHpBeforeHit < 0f)
            return Mathf.Max(0, attemptedDamage);

        return Mathf.Max(0f, targetHpBeforeHit - targetHealth.CurrentHp);
    }

    private void ApplyOnHitEffects(CombatHealth targetHealth, float actualDamage)
    {
        if (actualDamage <= 0f)
            return;

        ApplyDamageOverTime(targetHealth, actualDamage);
        HealSourceOnHit(actualDamage);
    }

    private void ApplyDamageOverTime(CombatHealth targetHealth, float actualDamage)
    {
        if (targetHealth == null || targetHealth.IsDead)
            return;

        float damagePerTick = dotDamageFlat + actualDamage * dotDamagePercent / 100f; // DoT 피해
        if (damagePerTick <= 0f)
            return;

        targetHealth.ApplyDamageOverTime(damagePerTick, dotDuration, DamageOverTimeTickInterval, source, direction);
    }

    private void HealSourceOnHit(float actualDamage)
    {
        if (source == null)
            return;

        float healAmount = hitHeal + actualDamage * lifeStealPercent / 100f; // 회복량
        if (healAmount <= 0f)
            return;

        CombatHealth sourceHealth = source.GetComponentInParent<CombatHealth>();
        if (sourceHealth == null)
            return;

        sourceHealth.Heal(healAmount);
    }

    private void EnsureProjectileVfx()
    {
        if (castVfxPrefab != null)
            VfxPrefabFactory.Spawn(castVfxPrefab, transform.position, transform.rotation);

        if (projectileVfxInstance != null)
            return;

        if (projectileVfxPrefab != null)
        {
            projectileVfxInstance = Instantiate(projectileVfxPrefab, transform.position, transform.rotation, transform);
            projectileVfxInstance.transform.localPosition = Vector3.zero;
            projectileVfxInstance.transform.localRotation = Quaternion.identity;
            DisableVfxPhysics(projectileVfxInstance);
            return;
        }

        if (useRuntimeTemporaryVfx)
            projectileVfxInstance = LightningChainRuntimeVfx.AttachProjectileVisual(transform, transform.lossyScale.x);
    }

    private void SpawnHitVfx(Vector3 position)
    {
        if (hitVfxPrefab != null)
        {
            GameObject instance = VfxPrefabFactory.Spawn(hitVfxPrefab, position, Quaternion.LookRotation(direction, Vector3.up));
            if (instance != null)
            {
                DisableVfxPhysics(instance);
                Destroy(instance, 1.5f);
            }

            return;
        }

        if (useRuntimeTemporaryVfx)
            LightningChainRuntimeVfx.SpawnHit(position, transform.lossyScale.x, isCritical);
    }

    private void DisableVfxPhysics(GameObject instance)
    {
        if (instance == null)
            return;

        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody[] rigidbodies = instance.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
            rigidbodies[i].isKinematic = true;

    }

    private void SetProjectileVisible(bool visible)
    {
        if (projectileRenderers == null)
            return;

        for (int i = 0; i < projectileRenderers.Length; i++)
        {
            if (projectileRenderers[i] != null)
                projectileRenderers[i].enabled = visible;
        }
    }

    private bool RollCritical()
    {
        return critChance > 0f && Random.value * 100f < critChance;
    }

    private Vector3 GetDamageableCenter(IDamageable damageable)
    {
        if (damageable is Component component)
        {
            Collider targetCollider = component.GetComponentInChildren<Collider>();
            if (targetCollider != null)
                return targetCollider.bounds.center;

            return component.transform.position + Vector3.up * 0.8f;
        }

        return transform.position;
    }

    private float GetSweepRadius()
    {
        if (projectileCollider == null)
            return 0.05f;

        Vector3 extents = projectileCollider.bounds.extents;
        float radius = Mathf.Min(extents.x, extents.z);
        return Mathf.Clamp(radius, 0.03f, 0.35f);
    }

    private Vector3 GetClampedMove(Vector3 desiredMove)
    {
        if (range <= 0f)
            return desiredMove;

        float remainingDistance = range - traveledDistance;
        if (remainingDistance <= 0f)
            return Vector3.zero;

        float desiredDistance = desiredMove.magnitude;
        if (desiredDistance <= remainingDistance)
            return desiredMove;

        return desiredMove.normalized * remainingDistance;
    }

    private bool IsInHitMask(int layer)
    {
        return (hitMask.value & (1 << layer)) != 0;
    }

    private IDamageable FindDamageable(Collider other)
    {
        if (other == null)
            return null;

        CombatHealth health = other.GetComponentInParent<CombatHealth>();
        if (health != null)
            return health;

        MonoBehaviour[] behaviours = other.GetComponentsInParent<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] == null || !behaviours[i].isActiveAndEnabled)
                continue;

            if (behaviours[i] is IDamageable damageable)
                return damageable;
        }

        return null;
    }

    private int GetDamageableId(IDamageable damageable)
    {
        if (damageable is Component component)
            return component.gameObject.GetInstanceID();

        return damageable.GetHashCode();
    }
}
