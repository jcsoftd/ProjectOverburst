using System;
using UnityEditor;
using UnityEngine;

// Local authoring tool. Source Blender script is archived outside the public repository.
public static class FlaskFbxAssembler
{
    private const string Root = "Assets/ProjectOverburst/03_Features/Items";
    private const string Models = Root + "/Art/Models/Flasks";
    private const string Prefabs = Root + "/Prefabs/Flasks";

    [MenuItem("JC Tool/Items/Assemble Sculpted Flasks")]
    public static void Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Edit mode required");
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidOperationException("URP Lit missing");
        Material silver = GetMaterial("Metal_Silver", new Color(.46f,.54f,.60f), .8f,.76f,Color.black,shader);
        Material dark = GetMaterial("Metal_Gunmetal", new Color(.055f,.068f,.09f), .68f,.58f,Color.black,shader);
        Material gold = GetMaterial("Metal_AgedBrass", new Color(.59f,.34f,.11f), .78f,.68f,Color.black,shader);
        Material ivory = GetMaterial("Bone_Ivory", new Color(.80f,.74f,.58f), .08f,.44f,Color.black,shader);

        int count = 0;
        foreach (FlaskKind kind in Enum.GetValues(typeof(FlaskKind)))
        {
            string fbxPath = Models + "/FBX/SM_Flask_" + kind + ".fbx";
            string prefabPath = Prefabs + "/PF_Flask_" + kind + ".prefab";
            GameObject sculpt = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (sculpt == null) throw new InvalidOperationException("Missing sculpted mesh: " + fbxPath);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                throw new InvalidOperationException("Missing pickup prefab: " + prefabPath);

            Color tint = Tint(kind);
            Color shell = Color.Lerp(tint, Color.white, .45f);
            shell.a = .58f;
            Material glass = GetMaterial(kind + "_CutGlass",shell,.05f,.88f,Color.black,shader,true);
            Material essence = GetMaterial(kind + "_Essence",Color.Lerp(tint,Color.black,.12f),.03f,.64f,
                tint * .27f,shader);
            GameObject prefab = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform oldIcon = prefab.transform.Find("FlaskIcon");
                if (oldIcon != null) UnityEngine.Object.DestroyImmediate(oldIcon.gameObject);
                Transform oldModel = prefab.transform.Find("ModelRoot");
                if (oldModel != null) UnityEngine.Object.DestroyImmediate(oldModel.gameObject);

                GameObject holder = new GameObject("ModelRoot");
                holder.transform.SetParent(prefab.transform,false);
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(sculpt);
                instance.name = "Sculpted_" + kind;
                instance.transform.SetParent(holder.transform,false);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
                MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
                if (renderers.Length < 15) throw new InvalidOperationException("Incomplete mesh: " + kind + " (" + renderers.Length + ")");
                foreach (MeshRenderer renderer in renderers)
                {
                    string name = renderer.gameObject.name.ToLowerInvariant();
                    Material selected = name.StartsWith("glass_") ? glass :
                        name.StartsWith("liquid_") ? essence :
                        name.StartsWith("dark_") ? dark :
                        name.StartsWith("gold_") ? gold :
                        name.StartsWith("ivory_") ? ivory : silver;
                    renderer.sharedMaterial = selected;
                    renderer.shadowCastingMode = name.StartsWith("glass_") ?
                        UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                }
                BoxCollider collider = prefab.GetComponent<BoxCollider>();
                if (collider != null)
                {
                    collider.size = new Vector3(.55f,.55f,.47f);
                    collider.center = new Vector3(0f,.25f,0f);
                }
                WorldPickupPresentation presentation = prefab.GetComponent<WorldPickupPresentation>();
                if (presentation != null)
                {
                    SerializedObject so = new SerializedObject(presentation);
                    so.FindProperty("groundClearance").floatValue = .025f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
                count++;
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("SCULPTED_FLASK_PASS: " + count + " small, detailed, material-assigned 3D flasks");
    }

    private static Material GetMaterial(string name, Color baseColor, float metallic, float smoothness,
        Color emission, Shader shader, bool transparent=false)
    {
        string path = Models + "/Materials/MAT_Flask_" + name + ".mat";
        Material result = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (result == null)
        {
            result = new Material(shader) {name = "MAT_Flask_" + name};
            AssetDatabase.CreateAsset(result,path);
        }
        result.shader=shader;
        result.SetColor("_BaseColor",baseColor);
        result.SetFloat("_Metallic",metallic);
        result.SetFloat("_Smoothness",smoothness);
        result.SetColor("_EmissionColor",emission);
        if (emission.maxColorComponent>.01f) result.EnableKeyword("_EMISSION");
        else result.DisableKeyword("_EMISSION");
        result.SetFloat("_Surface",transparent ? 1f:0f);
        result.SetFloat("_Blend",0f);
        result.SetFloat("_ZWrite",transparent ? 0f:1f);
        result.SetFloat("_SrcBlend",transparent ? 5f:1f);
        result.SetFloat("_DstBlend",transparent ? 10f:0f);
        result.renderQueue=transparent?3000:-1;
        result.SetOverrideTag("RenderType",transparent?"Transparent":"Opaque");
        if (transparent) result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        else result.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        EditorUtility.SetDirty(result);
        return result;
    }

    private static Color Tint(FlaskKind kind)
    {
        switch (kind)
        {
            case FlaskKind.Life: return new Color(.76f,.04f,.10f);
            case FlaskKind.Regeneration: return new Color(.08f,.59f,.24f);
            case FlaskKind.Berserker: return new Color(.71f,.045f,.035f);
            case FlaskKind.Giant: return new Color(.77f,.37f,.045f);
            case FlaskKind.Executioner: return new Color(.39f,.12f,.56f);
            case FlaskKind.Overcharge: return new Color(.09f,.70f,.78f);
            case FlaskKind.Ironclad: return new Color(.25f,.47f,.65f);
            case FlaskKind.Ghost: return new Color(.42f,.72f,.72f);
            case FlaskKind.Fire: return new Color(.94f,.23f,.025f);
            case FlaskKind.Ice: return new Color(.16f,.71f,.91f);
            case FlaskKind.Lightning: return new Color(.97f,.76f,.06f);
            default: return new Color(.07f,.36f,.82f);
        }
    }
}
