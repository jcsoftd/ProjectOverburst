using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class HitFlashFeedback : MonoBehaviour // 피격 flash
{
    [SerializeField] private CombatHealth health;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float flashDuration = 0.08f;
    [SerializeField, Min(1f)] private float flashBrightness = 2.5f;

    private MaterialPropertyBlock[] propertyBlocks; // 재질 속성 묶음
    private Color[] baseColors; // 원본 색
    private MaterialPropertyBlock[] beforeFlash;
    private Renderer[] slotRenderers;
    private int[] slotIndices;
    private Material[] slotMaterials;
    private Shader[] slotShaders;
    private bool[] hasBaseColor, hasColor;
    private readonly List<Material> materialScratch = new List<Material>(8);
    private bool flashApplied;
    private bool corpseTintActive;
    private int elementColdStacks;
    public void SetElementColdStacks(int stacks)
    {
        stacks = Mathf.Clamp(stacks, 0, 5);
        if (elementColdStacks == stacks || corpseTintActive) return;
        if (!flashApplied) CaptureBeforeFlash();
        elementColdStacks = stacks;
        flashApplied = true;
        if (flashRoutine == null) RestoreColors();
    }
    private Color StatusColor(Color original) => Color.Lerp(original,
        new Color(.22f, .55f, 1f, original.a), elementColdStacks * .15f);
    private Coroutine flashRoutine; // flash 루틴
    private const string BaseColorProperty = "_BaseColor"; // URP 색상
    private const string ColorProperty = "_Color"; // 기본 색상

    private void Awake()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();

        CacheRenderers();
    }

    private void OnEnable()
    {
        corpseTintActive = false;
        if (health != null)
        {
            health.OnDamaged += HandleDamaged;
            health.OnDead += HandleDead;
            health.OnReset += HandleReset;
        }
    }

    private void OnDisable()
    {
        elementColdStacks = 0;
        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDead -= HandleDead;
            health.OnReset -= HandleReset;
        }

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        RestoreColors();
        corpseTintActive = false;
        flashRoutine = null;
    }

    private void HandleReset(CombatHealth source)
    {
        elementColdStacks = 0;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        RestoreColors();
        corpseTintActive = false;
        flashRoutine = null;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        // This component already owns per-material hit colors. Keep corpse tone in
        // the same owner so the hit flash cannot restore a live color over a death.
        if (GetComponent<EnemyDeathPresentation>() == null) return;
        if (!flashApplied) CaptureBeforeFlash();
        flashApplied = true;
        corpseTintActive = true;
        if (flashRoutine == null) ApplyCorpseTint();
    }

    // 피해 이벤트 없이 번쩍이는 연출(패링 성공, 파동 넉백)용. 피격과 같은 경로라 색 복원이 보장된다.
    public void FlashOnce()
    {
        if (corpseTintActive || !isActiveAndEnabled) return;
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        if (!flashApplied) CaptureBeforeFlash();
        flashApplied = true;
        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (corpseTintActive || info.isDamageOverTime || !info.triggersOnHitEffects)
            return;

        if (flashRoutine != null)
            StopCoroutine(flashRoutine);

        // Preserve tint and other presentation properties, even on repeated hits.
        if (!flashApplied) CaptureBeforeFlash();
        flashApplied = true;

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        ApplyFlashColor();
        float elapsed = 0f;
        while (elapsed < flashDuration)
        {
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            float weight = 1f - Mathf.Clamp01(elapsed / Mathf.Max(.001f, flashDuration));
            for (int i = 0; i < slotRenderers.Length; i++)
                ApplyColor(i, Color.Lerp(corpseTintActive ? CorpseColor(baseColors[i]) : StatusColor(baseColors[i]),
                    BrightFlash(), weight));
        }

        if (corpseTintActive) ApplyCorpseTint();
        else RestoreColors();
        flashRoutine = null;
    }

    private void CaptureBeforeFlash()
    {
        if (!MaterialsMatchCache())
        {
            RestoreColors();
            CacheRenderers();
        }
        for (int i = 0; i < slotRenderers.Length; i++)
            if (slotRenderers[i] != null)
            {
                beforeFlash[i].Clear();
                propertyBlocks[i].Clear();
                slotRenderers[i].GetPropertyBlock(beforeFlash[i], slotIndices[i]);
                slotRenderers[i].GetPropertyBlock(propertyBlocks[i], slotIndices[i]);
                if (propertyBlocks[i].isEmpty) slotRenderers[i].GetPropertyBlock(propertyBlocks[i]);
                var block = propertyBlocks[i];
                baseColors[i] = block.HasColor(Shader.PropertyToID(BaseColorProperty))
                    ? block.GetColor(BaseColorProperty) : block.HasColor(Shader.PropertyToID(ColorProperty))
                    ? block.GetColor(ColorProperty) : GetRendererColor(i);
            }
    }

    private static Color CorpseColor(Color baseColor)
    {
        float luminance = baseColor.grayscale;
        return Color.Lerp(baseColor,
            new Color(luminance * .43f, luminance * .46f, luminance * .50f, baseColor.a), .86f);
    }

    private void ApplyCorpseTint()
    {
        for (int i = 0; i < slotRenderers.Length; i++)
            ApplyColor(i, CorpseColor(baseColors[i]));
    }

    private void CacheRenderers()
    {
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>(true);

        // EnemyActor tint uses indexed blocks. A renderer-wide block cannot override them.
        var targets = new List<Renderer>(); var indices = new List<int>(); var materials = new List<Material>();
        foreach (var target in renderers)
            if (target != null)
            {
                target.GetSharedMaterials(materialScratch);
                for (int index = 0; index < materialScratch.Count; index++)
                { targets.Add(target); indices.Add(index); materials.Add(materialScratch[index]); }
            }
        slotRenderers = targets.ToArray(); slotIndices = indices.ToArray();
        slotMaterials = materials.ToArray();
        slotShaders = new Shader[slotMaterials.Length];
        hasBaseColor = new bool[slotMaterials.Length]; hasColor = new bool[slotMaterials.Length];
        propertyBlocks = new MaterialPropertyBlock[slotRenderers.Length];
        beforeFlash = new MaterialPropertyBlock[slotRenderers.Length];
        baseColors = new Color[slotRenderers.Length];

        for (int i = 0; i < slotRenderers.Length; i++)
        {
            Material material = slotMaterials[i];
            slotShaders[i] = material != null ? material.shader : null;
            hasBaseColor[i] = material != null && material.HasProperty(BaseColorProperty);
            hasColor[i] = material != null && material.HasProperty(ColorProperty);
            propertyBlocks[i] = new MaterialPropertyBlock();
            beforeFlash[i] = new MaterialPropertyBlock();
            baseColors[i] = GetRendererColor(i);
        }
    }

    private Color GetRendererColor(int index)
    {
        var targetRenderer = slotRenderers[index];
        if (targetRenderer == null)
            return Color.white;

        Material material = slotMaterials[index];
        if (material == null) return Color.white;
        if (hasBaseColor[index])
            return material.GetColor(BaseColorProperty);

        if (hasColor[index])
            return material.GetColor(ColorProperty);

        return Color.white;
    }

    private void ApplyFlashColor()
    {
        ApplyColor(BrightFlash());
    }

    private Color BrightFlash() => new Color(flashColor.r * flashBrightness,
        flashColor.g * flashBrightness, flashColor.b * flashBrightness, flashColor.a);

    private void RestoreColors()
    {
        if (!flashApplied || renderers == null || baseColors == null)
            return;

        for (int i = 0; i < slotRenderers.Length; i++)
            if (slotRenderers[i] != null) slotRenderers[i].SetPropertyBlock(beforeFlash[i].isEmpty ? null : beforeFlash[i], slotIndices[i]);
        flashApplied = false;
        if (elementColdStacks > 0 && !corpseTintActive)
        {
            for (int i = 0; i < slotRenderers.Length; i++) ApplyColor(i, StatusColor(baseColors[i]));
            flashApplied = true;
        }
    }

    private void ApplyColor(Color color)
    {
        if (renderers == null)
            return;

        for (int i = 0; i < slotRenderers.Length; i++)
            ApplyColor(i, color);
    }

    private void ApplyColor(int index, Color color)
    {
        if (slotRenderers == null || index < 0 || index >= slotRenderers.Length || slotRenderers[index] == null)
            return;

        Material material = slotMaterials[index];
        if (material == null)
            return;

        MaterialPropertyBlock block = propertyBlocks != null && index < propertyBlocks.Length ? propertyBlocks[index] : null;
        if (block == null)
            block = new MaterialPropertyBlock();

        if (hasBaseColor[index])
            block.SetColor(BaseColorProperty, color);

        if (hasColor[index])
            block.SetColor(ColorProperty, color);

        slotRenderers[index].SetPropertyBlock(block, slotIndices[index]);
    }

    private bool MaterialsMatchCache()
    {
        if (slotMaterials == null || renderers == null) return false;
        int slot = 0;
        foreach (Renderer target in renderers)
        {
            if (target == null) continue;
            target.GetSharedMaterials(materialScratch);
            for (int i = 0; i < materialScratch.Count; i++, slot++)
            {
                Material material = materialScratch[i];
                if (slot >= slotMaterials.Length || slotRenderers[slot] != target
                    || slotIndices[slot] != i || slotMaterials[slot] != material
                    || slotShaders[slot] != (material != null ? material.shader : null)) return false;
            }
        }
        return slot == slotMaterials.Length;
    }
}
