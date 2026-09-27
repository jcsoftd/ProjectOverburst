using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

[DisallowMultipleComponent]
public sealed class PlayerLevelUpVfx : MonoBehaviour
{
    private const string PrefabResourcePath = "VFX/PF_PlayerLevelUpVfx";
    private const float EffectLifetimeSeconds = 5f;

    private PlayerProgression progression;
    private PlayerContext context;
    private GameObject prefab;
    private GameObject activeEffect;
    private VisualEffect activeVisualEffect;
    private Transform activeAnchor;
    private Coroutine releaseRoutine;
    private bool warnedMissingPrefab;

    public void Bind(PlayerProgression nextProgression, PlayerContext nextContext)
    {
        if (progression != null)
            progression.LeveledUp -= Play;
        progression = nextProgression;
        context = nextContext;
        if (progression != null)
            progression.LeveledUp += Play;
    }

    private void LateUpdate()
    {
        if (activeVisualEffect != null && activeAnchor != null)
            SyncTransform(activeVisualEffect, activeAnchor);
    }

    private void OnDisable()
    {
        if (progression != null)
            progression.LeveledUp -= Play;
        progression = null;
        context = null;
        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);
        releaseRoutine = null;
        if (activeEffect != null)
            Destroy(activeEffect);
        activeEffect = null;
        activeVisualEffect = null;
        activeAnchor = null;
    }

    private void Play(int previousLevel, int currentLevel)
    {
        PlayerActorRuntime actor = context != null ? context.CurrentActor : null;
        if (actor == null || !actor.isActiveAndEnabled)
            return;

        if (prefab == null)
            prefab = Resources.Load<GameObject>(PrefabResourcePath);
        if (prefab == null)
        {
            if (!warnedMissingPrefab)
            {
                Debug.LogWarning("[PlayerLevelUpVfx] Missing prefab: " + PrefabResourcePath);
                warnedMissingPrefab = true;
            }
            return;
        }

        if (releaseRoutine != null)
            StopCoroutine(releaseRoutine);
        if (activeEffect != null)
            Destroy(activeEffect);
        activeEffect = null;
        activeVisualEffect = null;
        activeAnchor = null;

        SkinnedMeshRenderer body = ResolveBody(actor);
        if (body == null)
        {
            Debug.LogWarning("[PlayerLevelUpVfx] Active actor has no skinned body mesh: " + actor.name);
            return;
        }

        activeEffect = Instantiate(prefab, actor.transform);
        activeEffect.name = "PlayerLevelUpVfx";
        activeEffect.transform.localPosition = Vector3.zero;
        activeEffect.transform.localRotation = Quaternion.identity;
        activeEffect.transform.localScale = prefab.transform.localScale;

        activeVisualEffect = activeEffect.GetComponentInChildren<VisualEffect>(true);
        if (activeVisualEffect == null || activeVisualEffect.visualEffectAsset == null)
        {
            Debug.LogWarning("[PlayerLevelUpVfx] Replacement prefab has no usable VisualEffect.");
            Destroy(activeEffect);
            activeEffect = null;
            activeVisualEffect = null;
            return;
        }

        if (activeVisualEffect.HasSkinnedMeshRenderer("SkinnedMeshRenderer"))
            activeVisualEffect.SetSkinnedMeshRenderer("SkinnedMeshRenderer", body);
        activeAnchor = ResolveAnchor(body, actor.transform);
        SyncTransform(activeVisualEffect, activeAnchor);
        activeVisualEffect.Reinit();
        releaseRoutine = StartCoroutine(ReleaseAfterDelay(activeEffect));
    }

    private static SkinnedMeshRenderer ResolveBody(PlayerActorRuntime actor)
    {
        SkinnedMeshRenderer best = null;
        SkinnedMeshRenderer bestChest = null;
        int mostVertices = -1;
        int mostChestVertices = -1;
        foreach (SkinnedMeshRenderer candidate in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (candidate == null || !candidate.enabled || !candidate.gameObject.activeInHierarchy ||
                candidate.sharedMesh == null)
                continue;
            int vertices = candidate.sharedMesh.vertexCount;
            if (vertices > mostVertices)
            {
                best = candidate;
                mostVertices = vertices;
            }
            if (vertices > mostChestVertices &&
                candidate.name.EndsWith("_Chest", System.StringComparison.OrdinalIgnoreCase))
            {
                bestChest = candidate;
                mostChestVertices = vertices;
            }
        }
        return bestChest != null ? bestChest : best;
    }

    private static Transform ResolveAnchor(SkinnedMeshRenderer body, Transform fallback)
    {
        foreach (Transform bone in body.bones)
        {
            if (bone != null && (bone.name.Equals("Hips", System.StringComparison.OrdinalIgnoreCase) ||
                                 bone.name.Equals("Pelvis", System.StringComparison.OrdinalIgnoreCase)))
                return bone;
        }
        return body.rootBone != null ? body.rootBone : fallback;
    }

    private static void SyncTransform(VisualEffect effect, Transform anchor)
    {
        if (!effect.HasVector3("Transform_position") || !effect.HasVector3("Transform_angles") ||
            !effect.HasVector3("Transform_scale"))
            return;

        bool local = effect.visualEffectAsset.GetExposedSpace("Transform") == VFXSpace.Local;
        Matrix4x4 matrix = local
            ? effect.transform.worldToLocalMatrix * anchor.localToWorldMatrix
            : anchor.localToWorldMatrix;
        effect.SetVector3("Transform_position", matrix.GetPosition());
        effect.SetVector3("Transform_angles", matrix.rotation.eulerAngles);
        effect.SetVector3("Transform_scale", matrix.lossyScale);
    }

    private IEnumerator ReleaseAfterDelay(GameObject effect)
    {
        yield return new WaitForSeconds(EffectLifetimeSeconds);
        if (effect != null)
            Destroy(effect);
        if (activeEffect == effect)
        {
            activeEffect = null;
            activeVisualEffect = null;
            activeAnchor = null;
            releaseRoutine = null;
        }
    }
}
