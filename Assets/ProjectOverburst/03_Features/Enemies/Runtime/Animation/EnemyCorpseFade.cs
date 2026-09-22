using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// Presentation only. EnemyController owns timing and the single pool return.
// Copies are local to a pooled instance; vendor materials are never modified.
[DisallowMultipleComponent]
public sealed class EnemyCorpseFade : MonoBehaviour
{
    [SerializeField, Min(.05f)] private float duration = .35f;
    private Renderer[] targets;
    private Material[][] original, fading;
    private MaterialPropertyBlock[] saved, working;
    private ShadowCastingMode[] shadows;
    private Color[] colors;
    private bool applied;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public IEnumerator Fade()
    {
        Prepare();
        applied = true;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            targets[i].GetPropertyBlock(saved[i]);
            targets[i].GetPropertyBlock(working[i]);
            colors[i] = saved[i].HasColor(BaseColor) ? saved[i].GetColor(BaseColor)
                : original[i].Length > 0 && original[i][0].HasProperty(BaseColor)
                ? original[i][0].GetColor(BaseColor) : Color.white;
            shadows[i] = targets[i].shadowCastingMode;
            targets[i].sharedMaterials = fading[i];
            targets[i].shadowCastingMode = ShadowCastingMode.Off;
        }
        float elapsed = 0f;
        while (elapsed < duration)
        {
            SetAlpha(1f - elapsed / duration);
            yield return null;
            elapsed += Time.deltaTime;
        }
        SetAlpha(0f);
    }

    private void SetAlpha(float alpha)
    {
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            Color color = colors[i]; color.a *= alpha;
            working[i].SetColor(BaseColor, color);
            targets[i].SetPropertyBlock(working[i]);
        }
    }

    private void Prepare()
    {
        if (targets != null) return;
        var actor = GetComponent<EnemyActor>();
        targets = (actor != null ? actor.VisualRoot : transform).GetComponentsInChildren<Renderer>(true);
        original = new Material[targets.Length][]; fading = new Material[targets.Length][];
        saved = new MaterialPropertyBlock[targets.Length]; working = new MaterialPropertyBlock[targets.Length];
        colors = new Color[targets.Length]; shadows = new ShadowCastingMode[targets.Length];
        for (int i = 0; i < targets.Length; i++)
        {
            saved[i] = new MaterialPropertyBlock(); working[i] = new MaterialPropertyBlock();
            original[i] = targets[i].sharedMaterials;
            fading[i] = new Material[original[i].Length];
            for (int j = 0; j < original[i].Length; j++)
            {
                Material source = original[i][j];
                if (source == null || source.shader.name != "Universal Render Pipeline/Lit")
                { fading[i][j] = source; continue; }
                var copy = new Material(source) { name = source.name + " (pooled corpse)", hideFlags = HideFlags.DontSave };
                copy.SetFloat("_Surface",1); copy.SetFloat("_Blend",0);
                copy.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);
                copy.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
                copy.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);
                copy.SetFloat("_DstBlendAlpha",(float)BlendMode.OneMinusSrcAlpha);
                copy.SetFloat("_ZWrite",0);
                copy.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                copy.SetOverrideTag("RenderType","Transparent");
                copy.renderQueue = (int)RenderQueue.Transparent;
                fading[i][j] = copy;
            }
        }
    }

    private void OnDisable()
    {
        if (!applied) return;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;
            targets[i].sharedMaterials = original[i];
            targets[i].SetPropertyBlock(saved[i]);
            targets[i].shadowCastingMode = shadows[i];
        }
        applied = false;
    }

    private void OnDestroy()
    {
        if (fading == null) return;
        for (int i = 0; i < fading.Length; i++)
            for (int j = 0; j < fading[i].Length; j++)
                if (fading[i][j] != null && fading[i][j] != original[i][j]) Destroy(fading[i][j]);
    }
}
