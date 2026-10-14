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
        CharacterAmbientLightVisibility.Unregister(ambientLight);
    }

    private void RefreshLight()
    {
        CharacterAmbientLightVisibility.Unregister(ambientLight);
        var map = GetComponent<EnemyRank>()?.Encounter.Map;
        if (map != null && map.terrain == Overburst.Persistence.MapTerrainKind.Desert)
        {
            // Preserve reused/authored lights without creating a light the desert never uses.
            if (ambientLight == null) ambientLight = transform.Find(LightName)?.GetComponent<Light>();
            if (ambientLight != null) ambientLight.enabled = false;
            return;
        }

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
        // Center the light in the body to brighten the surroundings evenly.
        ambientLight.transform.position = body.transform.TransformPoint(body.center);
        ambientLight.type = LightType.Point;
        ambientLight.color = new Color(1f, .88f, .70f, 1f);
        ambientLight.intensity = .15f;
        ambientLight.range = Mathf.Clamp(radius + 1.2f, 1.6f, 3f);
        ambientLight.cullingMask = ~LayerMask.GetMask("Enemy", "Player");
        if (health != null && !health.IsDead)
            CharacterAmbientLightVisibility.Register(ambientLight, body);
        else
            ambientLight.enabled = false;
    }

    private void OnDead(CombatHealth _, DamageInfo __)
    {
        CharacterAmbientLightVisibility.Unregister(ambientLight);
    }

    private void OnReset(CombatHealth _) => RefreshLight();
}
