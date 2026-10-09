using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyAmbientLight : MonoBehaviour
{
    private const string LightName = "MonsterAmbientLight";
    private Light ambientLight;
    private CombatHealth health;

    private void OnEnable()
    {
        health = GetComponent<CombatHealth>();
        if (health != null)
        {
            health.OnDead += OnDead;
            health.OnReset += OnReset;
        }
        RefreshLight();
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDead -= OnDead;
            health.OnReset -= OnReset;
        }
        if (ambientLight != null) ambientLight.enabled = false;
    }

    private void RefreshLight()
    {
        var actor = GetComponent<EnemyActor>();
        var body = actor != null && actor.CollisionRoot != null
            ? actor.CollisionRoot.GetComponentInChildren<CapsuleCollider>(true) : null;
        if (body == null) return;
        if (ambientLight == null)
        {
            var existing = transform.Find(LightName);
            var lightObject = existing != null ? existing.gameObject : new GameObject(LightName);
            lightObject.transform.SetParent(transform, false);
            ambientLight = lightObject.GetComponent<Light>();
            if (ambientLight == null) ambientLight = lightObject.AddComponent<Light>();
        }
        var scale = body.transform.lossyScale;
        float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        // Place the light outside the body, so a small monster does not get a light buried in its mesh.
        ambientLight.transform.position = body.transform.TransformPoint(body.center)
            + transform.up * (body.height * Mathf.Abs(scale.y) * .25f)
            + transform.forward * (radius + 1f);
        ambientLight.type = LightType.Point;
        ambientLight.color = new Color(1f, .88f, .70f, 1f);
        ambientLight.intensity = .3f;
        ambientLight.range = Mathf.Clamp(radius + 1.2f, 1.6f, 3f);
        ambientLight.shadows = LightShadows.None;
        ambientLight.bounceIntensity = 0f;
        ambientLight.lightmapBakeType = LightmapBakeType.Realtime;
        ambientLight.enabled = health != null && !health.IsDead;
    }

    private void OnDead(CombatHealth _, DamageInfo __)
    {
        if (ambientLight != null) ambientLight.enabled = false;
    }

    private void OnReset(CombatHealth _) => RefreshLight();
}
