using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class EnemyDeathFadeController : MonoBehaviour
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private EnemyMovement movement;
    [SerializeField] private EnemyAnimationBridge animationBridge;
    [SerializeField] private Rigidbody rb;
    [SerializeField] private Collider[] colliders;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private float deathPoseHoldTime = 2.4f;
    [SerializeField] private float deathFadeDuration = 2f;
    [SerializeField] private bool freezeRigidbodyOnDeath = true;
    [SerializeField] private bool disableCollidersAfterDeath = true;
    [SerializeField] private bool destroyAfterFade = true;

    private bool deathStarted;
    private Material[][] materialInstances;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        deathStarted = false;
        ResolveReferences();
        SetCollidersEnabled(true); // 재사용 초기화

        if (health == null)
            health = GetComponent<CombatHealth>();

        if (health != null)
            health.OnDead += HandleDead;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDead -= HandleDead;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (deathStarted)
            return;

        deathStarted = true;
        ResolveReferences();
        FreezeAtDeathPosition();

        if (disableCollidersAfterDeath)
            SetCollidersEnabled(false); // 시체 통과

        if (movement != null && GetComponent<EnemyAIController>() == null)
            movement.StopForDeath(); // 상태 AI가 없는 안전 fallback

        if (animationBridge != null)
            animationBridge.PlayDeath(); // 사망 동작

        StartCoroutine(DeathFadeRoutine());
    }

    private IEnumerator DeathFadeRoutine()
    {
        if (deathPoseHoldTime > 0f)
            yield return new WaitForSeconds(deathPoseHoldTime);

        PrepareMaterialInstances();

        float duration = Mathf.Max(0.01f, deathFadeDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(1f - Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        SetAlpha(0f);

        if (destroyAfterFade)
            Destroy(gameObject);
        else
            gameObject.SetActive(false);
    }

    private void FreezeAtDeathPosition()
    {
        if (rb == null)
            return;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (freezeRigidbodyOnDeath)
            rb.isKinematic = true; // 사망 위치 고정
    }

    private void ResolveReferences()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (movement == null)
            movement = GetComponent<EnemyMovement>();
        if (animationBridge == null)
            animationBridge = GetComponent<EnemyAnimationBridge>();
        if (rb == null)
            rb = GetComponent<Rigidbody>();
        if (colliders == null || colliders.Length == 0)
            colliders = GetComponentsInChildren<Collider>(true);
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void PrepareMaterialInstances()
    {
        if (renderers == null)
            return;

        materialInstances = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null)
                continue;

            Material[] materials = targetRenderer.materials; // 실행 중 재질 인스턴스
            materialInstances[i] = materials;
            for (int j = 0; j < materials.Length; j++)
                ConfigureTransparentMaterial(materials[j]);
        }
    }

    private void ConfigureTransparentMaterial(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void SetAlpha(float alpha)
    {
        if (materialInstances == null)
            return;

        for (int i = 0; i < materialInstances.Length; i++)
        {
            Material[] materials = materialInstances[i];
            if (materials == null)
                continue;

            for (int j = 0; j < materials.Length; j++)
                SetMaterialAlpha(materials[j], alpha);
        }
    }

    private void SetMaterialAlpha(Material material, float alpha)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
        {
            Color color = material.GetColor("_BaseColor");
            color.a = alpha;
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            Color color = material.GetColor("_Color");
            color.a = alpha;
            material.SetColor("_Color", color);
        }
    }

    private void SetCollidersEnabled(bool isEnabled)
    {
        if (colliders == null)
            return;

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = isEnabled;
        }
    }
}
