using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Local authoring tool. The generated meshes, materials and prefabs are the project assets.
public static class FlaskModelBuilder
{
    private const string Root = "Assets/ProjectOverburst/03_Features/Items";
    private const string ModelRoot = Root + "/Art/Models/Flasks";
    private const string MeshFolder = ModelRoot + "/Meshes";
    private const string MaterialFolder = ModelRoot + "/Materials";
    private const string PrefabFolder = Root + "/Prefabs/Flasks";

    private readonly struct Ring
    {
        public readonly float y, x, z, centerX;
        public Ring(float y, float x, float z, float centerX = 0f)
        { this.y = y; this.x = x; this.z = z; this.centerX = centerX; }
    }

    [MenuItem("JC Tool/Items/Build 3D Flask Models")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build 3D Flask Models requires Edit Mode.");
        // 2026-10-01 폐기: 물약 픽업은 조각 FBX 모델(Assemble Sculpted Flasks)을 쓴다. 이 도구는 ModelRoot를 지우고 옛 병·충돌체·재질을 다시 만든다.
        EditorSceneSafety.RefuseRetired("Build 3D Flask Models", "Flask pickups use the sculpted FBX models assembled by Assemble Sculpted Flasks (Missing Only); "
            + "this tool deletes ModelRoot and rebuilds the old lathe bottles, collider size and materials.");
        Directory.CreateDirectory(MeshFolder);
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");

        Material brightMetal = Material("BrightMetal", new Color(.73f, .81f, .86f), .78f, .78f, Color.black, shader);
        Material darkMetal = Material("DarkMetal", new Color(.075f, .105f, .16f), .78f, .48f, Color.black, shader);
        Material gold = Material("Gold", new Color(.82f, .58f, .20f), .75f, .72f, Color.black, shader);
        Material ivory = Material("Ivory", new Color(.95f, .87f, .63f), .18f, .52f, Color.black, shader);
        Mesh spike = MeshAsset("FacetSpike", CreateLathe("FacetSpike", 4, new[] {
            new Ring(0f,.095f,.095f), new Ring(.72f,.073f,.073f), new Ring(1f,.002f,.002f) }));

        foreach (FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
        {
            Color tint = ColorFor(kind);
            Color glassTint = Color.Lerp(tint, Color.white, .68f);
            glassTint.a = .38f;
            Material bodyMaterial = Material(kind + "_Glass", glassTint, .02f, .95f, Color.black, shader, true);
            Material glowMaterial = Material(kind + "_Core", Color.Lerp(tint, Color.black, .18f), .05f, .58f, tint * .62f, shader);
            Mesh body = MeshAsset(kind + "_Body", CreateLathe(kind + "_Body", Sides(kind), Profile(kind)));
            string path = PrefabFolder + "/PF_Flask_" + kind + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Existing pickup prefab is missing: " + path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform oldIcon = root.transform.Find("FlaskIcon");
                if (oldIcon != null) UnityEngine.Object.DestroyImmediate(oldIcon.gameObject);
                Transform oldModel = root.transform.Find("ModelRoot");
                if (oldModel != null) UnityEngine.Object.DestroyImmediate(oldModel.gameObject);

                GameObject modelObject = new GameObject("ModelRoot");
                modelObject.transform.SetParent(root.transform, false);
                Transform model = modelObject.transform;
                BuildShape(kind, model, body, spike, bodyMaterial, glowMaterial, brightMetal, darkMetal, gold, ivory);
                BoxCollider collider = root.GetComponent<BoxCollider>();
                if (collider != null)
                {
                    collider.size = new Vector3(.70f, .88f, .58f);
                    collider.center = new Vector3(0f, .42f, 0f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("FLASK_MODEL_PASS: 12 authored 3D bottle silhouettes and pickup prefabs");
    }

    private static void BuildShape(FlaskKind kind, Transform root, Mesh body, Mesh spike,
        Material enamel, Material core, Material silver, Material dark, Material gold, Material ivory)
    {
        if (kind == FlaskKind.Overcharge)
        {
            for (int i = -1; i <= 1; i += 2)
            {
                Vector3 shift = new Vector3(i * .16f, 0f, 0f);
                AddMesh(root, "TwinAmpoule", body, enamel, shift, Vector3.one);
                AddMesh(root, "TwinLiquid", body, core, shift + new Vector3(0f,.05f,0f), new Vector3(.72f,.80f,.72f));
                AddPrimitive(root, "Neck", PrimitiveType.Cylinder, dark, shift + new Vector3(0f,.69f,0f), new Vector3(.18f,.055f,.18f));
                AddPrimitive(root, "Cap", PrimitiveType.Cylinder, i < 0 ? silver : gold, shift + new Vector3(0f,.76f,0f), new Vector3(.24f,.07f,.24f));
                AddPrimitive(root, "ChargeCore", PrimitiveType.Sphere, core, shift + new Vector3(0f,.37f,-.105f), new Vector3(.12f,.32f,.055f));
            }
            AddPrimitive(root, "ConductiveBridge", PrimitiveType.Cube, silver, new Vector3(0f,.48f,0f), new Vector3(.30f,.055f,.15f));
        }
        else
        {
            AddMesh(root, "SculptedVessel", body, enamel, Vector3.zero, Vector3.one);
            AddMesh(root, "LiquidCore", body, core, new Vector3(0f,.055f,0f), new Vector3(.72f,.78f,.72f));
            AddPrimitive(root, "LowerCollar", PrimitiveType.Cylinder, dark, new Vector3(0f,.11f,0f), new Vector3(.34f,.038f,.30f));
            AddPrimitive(root, "Neck", PrimitiveType.Cylinder, dark, new Vector3(0f,.69f,0f), new Vector3(.20f,.065f,.20f));
            AddPrimitive(root, "Rim", PrimitiveType.Cylinder, silver, new Vector3(0f,.75f,0f), new Vector3(.27f,.035f,.27f));
            AddPrimitive(root, "Stopper", PrimitiveType.Cylinder, gold, new Vector3(0f,.81f,0f), new Vector3(.20f,.06f,.20f));
            AddPrimitive(root, "CoreWindow", PrimitiveType.Sphere, core, new Vector3(0f,.39f,-.16f), new Vector3(.16f,.28f,.055f));
        }
        AddAccents(kind, root, spike, core, silver, dark, gold, ivory);
    }

    private static void AddAccents(FlaskKind kind, Transform root, Mesh spike, Material core,
        Material silver, Material dark, Material gold, Material ivory)
    {
        switch (kind)
        {
            case FlaskKind.Life:
                for (int i = -1; i <= 1; i += 2)
                    AddPrimitive(root, "HeartShoulder", PrimitiveType.Sphere, core, new Vector3(i*.14f,.55f,-.015f), new Vector3(.25f,.18f,.25f));
                AddPrimitive(root, "PulseVertical", PrimitiveType.Cube, ivory, new Vector3(0f,.40f,-.225f), new Vector3(.035f,.20f,.018f));
                AddPrimitive(root, "PulseCross", PrimitiveType.Cube, ivory, new Vector3(0f,.40f,-.236f), new Vector3(.15f,.035f,.018f));
                break;
            case FlaskKind.Regeneration:
                for (int i = -1; i <= 1; i += 2)
                    AddPrimitive(root, "Leaf", PrimitiveType.Sphere, core, new Vector3(i*.10f,.43f,-.19f), new Vector3(.11f,.26f,.025f), Quaternion.Euler(0f,0f,i*32f));
                AddPrimitive(root, "LeafStem", PrimitiveType.Cube, gold, new Vector3(0f,.35f,-.205f), new Vector3(.018f,.25f,.02f));
                break;
            case FlaskKind.Berserker:
                for (int i = -1; i <= 1; i += 2)
                    AddMesh(root, "IvoryTusk", spike, ivory, new Vector3(i*.30f,.29f,0f), new Vector3(.85f,.35f,.85f), Quaternion.Euler(0f,0f,-i*23f));
                AddPrimitive(root, "WarBand", PrimitiveType.Cube, dark, new Vector3(0f,.48f,-.19f), new Vector3(.45f,.08f,.05f));
                break;
            case FlaskKind.Giant:
                for (int i = -1; i <= 1; i += 2)
                    AddPrimitive(root, "WeightGuard", PrimitiveType.Cube, dark, new Vector3(i*.31f,.37f,0f), new Vector3(.11f,.43f,.26f));
                AddPrimitive(root, "HammerMark", PrimitiveType.Cube, gold, new Vector3(0f,.42f,-.205f), new Vector3(.23f,.09f,.045f));
                AddPrimitive(root, "HammerGrip", PrimitiveType.Cube, gold, new Vector3(0f,.30f,-.215f), new Vector3(.055f,.24f,.04f));
                break;
            case FlaskKind.Executioner:
                AddMesh(root, "BladeTip", spike, silver, new Vector3(0f,.46f,-.19f), new Vector3(.85f,.30f,.36f));
                AddPrimitive(root, "BladeGrip", PrimitiveType.Cube, dark, new Vector3(0f,.33f,-.23f), new Vector3(.045f,.20f,.04f));
                AddPrimitive(root, "BladeGuard", PrimitiveType.Cube, gold, new Vector3(0f,.38f,-.24f), new Vector3(.21f,.035f,.05f));
                break;
            case FlaskKind.Overcharge:
                AddPrimitive(root, "Conduit", PrimitiveType.Cube, core, new Vector3(0f,.49f,-.15f), new Vector3(.27f,.035f,.035f));
                break;
            case FlaskKind.Ironclad:
                for (int i = -1; i <= 1; i += 2)
                    AddPrimitive(root, "ArmorRail", PrimitiveType.Cube, silver, new Vector3(i*.23f,.41f,-.08f), new Vector3(.075f,.45f,.23f));
                AddPrimitive(root, "ShieldBoss", PrimitiveType.Sphere, silver, new Vector3(0f,.40f,-.23f), new Vector3(.22f,.22f,.07f));
                break;
            case FlaskKind.Ghost:
                for (int i = -1; i <= 1; i += 2)
                    AddMesh(root, "WispHorn", spike, core, new Vector3(i*.18f,.48f,0f), new Vector3(.6f,.24f,.6f), Quaternion.Euler(0f,0f,-i*24f));
                break;
            case FlaskKind.Fire:
                for (int i = -1; i <= 1; i += 2)
                    AddMesh(root, "FlameTongue", spike, core, new Vector3(i*.20f,.48f,0f), new Vector3(.60f,.30f,.60f), Quaternion.Euler(0f,0f,-i*24f));
                AddMesh(root, "CrownFlame", spike, gold, new Vector3(0f,.73f,0f), new Vector3(.55f,.21f,.55f));
                break;
            case FlaskKind.Ice:
                for (int i = -1; i <= 1; i += 2)
                    AddMesh(root, "IceShard", spike, silver, new Vector3(i*.22f,.39f,0f), new Vector3(.55f,.32f,.55f), Quaternion.Euler(0f,0f,-i*16f));
                AddMesh(root, "IceCrown", spike, core, new Vector3(0f,.69f,0f), new Vector3(.80f,.30f,.80f));
                break;
            case FlaskKind.Lightning:
                AddPrimitive(root, "BoltUpper", PrimitiveType.Cube, gold, new Vector3(.05f,.53f,-.20f), new Vector3(.07f,.24f,.04f), Quaternion.Euler(0f,0f,-35f));
                AddPrimitive(root, "BoltMiddle", PrimitiveType.Cube, gold, new Vector3(0f,.41f,-.21f), new Vector3(.18f,.055f,.04f));
                AddPrimitive(root, "BoltLower", PrimitiveType.Cube, gold, new Vector3(-.05f,.28f,-.20f), new Vector3(.07f,.24f,.04f), Quaternion.Euler(0f,0f,-35f));
                break;
            case FlaskKind.Dark:
                AddPrimitive(root, "GravityCore", PrimitiveType.Sphere, core, new Vector3(0f,.43f,-.20f), new Vector3(.17f,.17f,.08f));
                break;
            case FlaskKind.Light:
                AddMesh(root, "RadiantCrown", spike, gold, new Vector3(0f,.70f,0f), new Vector3(.70f,.25f,.70f));
                break;
        }
    }

    private static Ring[] Profile(FlaskKind kind)
    {
        switch (kind)
        {
            case FlaskKind.Life: return new[] {new Ring(.07f,.035f,.08f),new Ring(.17f,.14f,.12f),new Ring(.36f,.25f,.17f),new Ring(.54f,.30f,.18f),new Ring(.62f,.17f,.14f),new Ring(.67f,.10f,.10f)};
            case FlaskKind.Regeneration: return new[] {new Ring(.09f,.14f,.13f),new Ring(.15f,.20f,.17f),new Ring(.56f,.20f,.17f),new Ring(.64f,.12f,.11f),new Ring(.68f,.10f,.09f)};
            case FlaskKind.Berserker: return new[] {new Ring(.09f,.18f,.15f),new Ring(.18f,.32f,.20f),new Ring(.50f,.32f,.20f),new Ring(.60f,.21f,.16f),new Ring(.67f,.10f,.10f)};
            case FlaskKind.Giant: return new[] {new Ring(.08f,.25f,.18f),new Ring(.15f,.34f,.21f),new Ring(.52f,.34f,.21f),new Ring(.60f,.23f,.17f),new Ring(.67f,.10f,.10f)};
            case FlaskKind.Executioner: return new[] {new Ring(.09f,.05f,.08f),new Ring(.17f,.16f,.13f),new Ring(.39f,.28f,.17f),new Ring(.58f,.15f,.13f),new Ring(.67f,.09f,.09f)};
            case FlaskKind.Overcharge: return new[] {new Ring(.09f,.09f,.08f),new Ring(.16f,.14f,.11f),new Ring(.57f,.14f,.11f),new Ring(.66f,.08f,.08f)};
            case FlaskKind.Ironclad: return new[] {new Ring(.08f,.08f,.09f),new Ring(.18f,.21f,.15f),new Ring(.52f,.27f,.18f),new Ring(.61f,.22f,.15f),new Ring(.67f,.10f,.10f)};
            case FlaskKind.Ghost: return new[] {new Ring(.08f,.08f,.09f,-.12f),new Ring(.18f,.17f,.13f,-.08f),new Ring(.42f,.22f,.15f,.02f),new Ring(.59f,.15f,.12f,.11f),new Ring(.67f,.08f,.08f,.16f)};
            case FlaskKind.Fire: return new[] {new Ring(.08f,.13f,.12f),new Ring(.16f,.23f,.16f),new Ring(.46f,.23f,.16f),new Ring(.60f,.12f,.10f),new Ring(.67f,.08f,.08f)};
            case FlaskKind.Ice: return new[] {new Ring(.08f,.07f,.08f),new Ring(.21f,.19f,.14f),new Ring(.43f,.25f,.17f),new Ring(.59f,.15f,.12f),new Ring(.67f,.08f,.08f)};
            case FlaskKind.Lightning: return new[] {new Ring(.08f,.13f,.11f),new Ring(.16f,.23f,.16f),new Ring(.54f,.23f,.16f),new Ring(.63f,.13f,.11f),new Ring(.68f,.09f,.08f)};
            default: return new[] {new Ring(.08f,.20f,.14f),new Ring(.15f,.26f,.17f),new Ring(.56f,.26f,.17f),new Ring(.65f,.13f,.10f),new Ring(.69f,.10f,.08f)};
        }
    }

    private static int Sides(FlaskKind kind)
    {
        return kind == FlaskKind.Executioner || kind == FlaskKind.Ice || kind == FlaskKind.Dark ? 4
            : kind == FlaskKind.Regeneration || kind == FlaskKind.Giant || kind == FlaskKind.Fire ? 6
            : kind == FlaskKind.Ghost ? 7 : 8;
    }

    private static Color ColorFor(FlaskKind kind)
    {
        switch (kind)
        {
            case FlaskKind.Life: return new Color(.94f,.10f,.18f);
            case FlaskKind.Regeneration: return new Color(.22f,.84f,.36f);
            case FlaskKind.Berserker: return new Color(1f,.24f,.08f);
            case FlaskKind.Giant: return new Color(.91f,.57f,.13f);
            case FlaskKind.Executioner: return new Color(.62f,.12f,.66f);
            case FlaskKind.Overcharge: return new Color(.12f,.90f,.77f);
            case FlaskKind.Ironclad: return new Color(.23f,.50f,.94f);
            case FlaskKind.Ghost: return new Color(.27f,.97f,.79f);
            case FlaskKind.Fire: return new Color(1f,.30f,.04f);
            case FlaskKind.Ice: return new Color(.28f,.83f,1f);
            case FlaskKind.Lightning: return new Color(1f,.85f,.12f);
            case FlaskKind.Dark: return new Color(.23f,.06f,.35f);
            case FlaskKind.Light: return new Color(1f,.86f,.48f);
            default: return Color.white;
        }
    }

    private static Mesh CreateLathe(string name, int sides, Ring[] rings)
    {
        int n = rings.Length;
        Vector3[] vertices = new Vector3[n * sides + 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[(n - 1) * sides * 6 + sides * 6];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < sides; i++)
            {
                float angle = 2f * Mathf.PI * i / sides;
                int index = j * sides + i;
                vertices[index] = new Vector3(rings[j].centerX + Mathf.Cos(angle) * rings[j].x,
                    rings[j].y, Mathf.Sin(angle) * rings[j].z);
                uv[index] = new Vector2(i / (float)sides, j / (float)(n - 1));
            }
        vertices[n * sides] = new Vector3(rings[0].centerX, rings[0].y, 0f);
        vertices[n * sides + 1] = new Vector3(rings[n - 1].centerX, rings[n - 1].y, 0f);
        int t = 0;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < sides; i++)
            {
                int a = j * sides + i, b = j * sides + (i + 1) % sides;
                int c = (j + 1) * sides + i, d = (j + 1) * sides + (i + 1) % sides;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
        for (int i = 0; i < sides; i++)
        {
            triangles[t++] = n * sides; triangles[t++] = i; triangles[t++] = (i + 1) % sides;
            triangles[t++] = n * sides + 1; triangles[t++] = (n - 1) * sides + (i + 1) % sides;
            triangles[t++] = (n - 1) * sides + i;
        }
        Mesh mesh = new Mesh { name = name, vertices = vertices, uv = uv, triangles = triangles };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh MeshAsset(string name, Mesh generated)
    {
        string path = MeshFolder + "/Mesh_Flask_" + name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(generated, path); return generated; }
        EditorUtility.CopySerialized(generated, existing); EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(generated);
        return existing;
    }

    private static Material Material(string name, Color color, float metallic, float smoothness, Color emission, Shader shader, bool transparent = false)
    {
        string path = MaterialFolder + "/MAT_Flask_" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null) { material = new Material(shader) { name = "MAT_Flask_" + name }; AssetDatabase.CreateAsset(material, path); }
        material.shader = shader;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetColor("_EmissionColor", emission);
        if (emission.maxColorComponent > .01f) material.EnableKeyword("_EMISSION");
        else material.DisableKeyword("_EMISSION");
        material.SetFloat("_Surface", transparent ? 1f : 0f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_ZWrite", transparent ? 0f : 1f);
        material.SetFloat("_SrcBlend", transparent ? 5f : 1f);
        material.SetFloat("_DstBlend", transparent ? 10f : 0f);
        material.renderQueue = transparent ? 3000 : -1;
        material.SetOverrideTag("RenderType", transparent ? "Transparent" : "Opaque");
        if (transparent) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject AddMesh(Transform parent, string name, Mesh mesh, Material material,
        Vector3 position, Vector3 scale, Quaternion? rotation = null)
    {
        GameObject part = new GameObject(name);
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation ?? Quaternion.identity;
        part.transform.localScale = scale;
        part.AddComponent<MeshFilter>().sharedMesh = mesh;
        part.AddComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }

    private static GameObject AddPrimitive(Transform parent, string name, PrimitiveType shape, Material material,
        Vector3 position, Vector3 scale, Quaternion? rotation = null)
    {
        GameObject part = GameObject.CreatePrimitive(shape);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localRotation = rotation ?? Quaternion.identity;
        part.transform.localScale = scale;
        Collider collider = part.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        part.GetComponent<MeshRenderer>().sharedMaterial = material;
        return part;
    }
}
