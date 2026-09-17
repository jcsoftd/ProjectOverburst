using UnityEngine;

public static class LightningChainRuntimeVfx // 번개 런타임 VFX
{
    private static Mesh sphereMesh; // 구체 메시
    private static Material coreMaterial; // 중심 재질
    private static Material glowMaterial; // 발광 재질
    private static Material particleMaterial; // 파티클 재질

    public static GameObject AttachProjectileVisual(Transform parent, float projectileScale)
    {
        if (parent == null)
            return null;

        GameObject root = new GameObject("LightningProjectileVfx_Runtime");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        CreateSphere(root.transform, "Core", Mathf.Max(0.08f, projectileScale * 0.34f), GetCoreMaterial());
        CreateSphere(root.transform, "Glow", Mathf.Max(0.14f, projectileScale * 0.62f), GetGlowMaterial());
        CreateSparkParticles(root.transform, Mathf.Max(0.12f, projectileScale * 0.45f));
        CreateTrail(root);
        return root;
    }

    public static void SpawnHit(Vector3 position, float scale, bool critical)
    {
        GameObject root = new GameObject("LightningHitVfx_Runtime");
        root.transform.position = position;
        float size = Mathf.Max(0.18f, scale) * (critical ? 1.25f : 1f); // hit 크기

        CreateFlashParticles(root.transform, size);
        CreateBurstSparks(root.transform, size, critical);
        Object.Destroy(root, critical ? 0.32f : 0.26f);
    }

    private static void CreateSphere(Transform parent, string name, float scale, Material material)
    {
        GameObject sphere = new GameObject(name);
        sphere.transform.SetParent(parent, false);
        sphere.transform.localScale = Vector3.one * scale;

        MeshFilter filter = sphere.AddComponent<MeshFilter>();
        filter.sharedMesh = GetSphereMesh();

        MeshRenderer renderer = sphere.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private static void CreateTrail(GameObject root)
    {
        TrailRenderer trail = root.AddComponent<TrailRenderer>();
        trail.time = 0.12f;
        trail.minVertexDistance = 0.025f;
        trail.startWidth = 0.16f;
        trail.endWidth = 0.015f;
        trail.startColor = new Color(0.15f, 0.95f, 1f, 0.8f);
        trail.endColor = new Color(0.05f, 0.35f, 1f, 0f);
        trail.material = GetParticleMaterial();
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.alignment = LineAlignment.View;
    }

    private static void CreateSparkParticles(Transform parent, float radius)
    {
        ParticleSystem ps = CreateParticleSystem(parent, "ProjectileSparks");
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.35f;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.045f, 0.11f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.65f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(0.05f, 0.85f, 1f, 0.9f));
        main.maxParticles = 32;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 70f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = Mathf.Max(0.04f, radius);

        ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.x = new ParticleSystem.MinMaxCurve(-1.3f, 1.3f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        velocity.z = new ParticleSystem.MinMaxCurve(-1.3f, 1.3f);

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetParticleMaterial();
        renderer.trailMaterial = GetParticleMaterial();
        ps.Play(true);
    }

    private static void CreateFlashParticles(Transform parent, float size)
    {
        ParticleSystem ps = CreateParticleSystem(parent, "HitFlash");
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.08f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.42f, size * 0.7f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.1f, 0.9f, 1f, 0.72f));
        main.maxParticles = 16;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5, 8) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.12f;

        ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.material = GetParticleMaterial();
        ps.Play(true);
    }

    private static void CreateBurstSparks(Transform parent, float size, bool critical)
    {
        ParticleSystem ps = CreateParticleSystem(parent, "HitSparks");
        ParticleSystem.MainModule main = ps.main;
        main.duration = 0.12f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, critical ? 6.2f : 4.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, critical ? 0.075f : 0.055f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(0.05f, 0.75f, 1f, 0.95f));
        main.maxParticles = critical ? 42 : 30;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, critical ? 28 : 20) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = size * 0.08f;

        ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.42f;

        ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.lengthScale = 2.3f;
        renderer.velocityScale = 0.18f;
        renderer.material = GetParticleMaterial();
        ps.Play(true);
    }

    private static ParticleSystem CreateParticleSystem(Transform parent, string name)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        ParticleSystem ps = obj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Clear(true);
        ParticleSystem.MainModule main = ps.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        return ps;
    }

    private static Mesh GetSphereMesh()
    {
        if (sphereMesh != null)
            return sphereMesh;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        Object.Destroy(temp);
        return sphereMesh;
    }

    private static Material GetCoreMaterial()
    {
        if (coreMaterial != null)
            return coreMaterial;

        coreMaterial = CreateMaterial("Runtime_LightningCore", new Color(0.92f, 1f, 1f, 1f), new Color(0.25f, 0.95f, 1f, 1f));
        return coreMaterial;
    }

    private static Material GetGlowMaterial()
    {
        if (glowMaterial != null)
            return glowMaterial;

        glowMaterial = CreateMaterial("Runtime_LightningGlow", new Color(0.02f, 0.8f, 1f, 0.34f), new Color(0.04f, 0.85f, 1f, 0.8f));
        return glowMaterial;
    }

    private static Material GetParticleMaterial()
    {
        if (particleMaterial != null)
            return particleMaterial;

        particleMaterial = CreateMaterial("Runtime_LightningParticle", new Color(0.2f, 0.95f, 1f, 0.9f), new Color(0.16f, 0.95f, 1f, 1f));
        return particleMaterial;
    }

    private static Material CreateMaterial(string name, Color baseColor, Color emissionColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.name = name;
        SetColorIfPresent(material, "_BaseColor", baseColor);
        SetColorIfPresent(material, "_Color", baseColor);
        SetColorIfPresent(material, "_EmissionColor", emissionColor);
        SetFloatIfPresent(material, "_Surface", 1f);
        SetFloatIfPresent(material, "_Blend", 1f);
        SetFloatIfPresent(material, "_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        SetFloatIfPresent(material, "_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        SetFloatIfPresent(material, "_ZWrite", 0f);
        material.renderQueue = 3000;
        material.EnableKeyword("_EMISSION");
        return material;
    }

    private static void SetColorIfPresent(Material material, string property, Color color)
    {
        if (material != null && material.HasProperty(property))
            material.SetColor(property, color);
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material != null && material.HasProperty(property))
            material.SetFloat(property, value);
    }
}
