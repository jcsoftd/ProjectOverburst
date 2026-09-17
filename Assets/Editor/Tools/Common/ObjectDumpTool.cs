using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class ObjectDumpTool
{
    private const string DumpFolder = "Assets/_FullDumps";

    [MenuItem("OVERBURST/User Tools/Debug/Dump Selected Object Full Info")]
    private static void DumpSelectedObjectFullInfo()
    {
        UnityEngine.Object selectedObject = Selection.activeObject; // 선택 대상

        if (selectedObject == null)
        {
            Debug.LogWarning("선택된 오브젝트가 없습니다.");
            return;
        }

        StringBuilder builder = new StringBuilder();

        builder.AppendLine("===== FULL OBJECT DUMP START =====");
        builder.AppendLine("Dump Time        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine("Selected Name    : " + selectedObject.name);
        builder.AppendLine("Selected Type    : " + selectedObject.GetType().FullName);
        builder.AppendLine("Asset Path       : " + AssetDatabase.GetAssetPath(selectedObject));
        builder.AppendLine();

        GameObject selectedGameObject = GetGameObjectFromSelection(selectedObject); // GameObject 변환

        DumpAssetInfo(selectedObject, builder); // 에셋 정보
        DumpImporterInfo(selectedObject, builder); // Importer 정보

        if (selectedGameObject != null)
        {
            DumpGameObjectInfo(selectedGameObject, builder); // 오브젝트 기본 정보
            DumpHierarchy(selectedGameObject.transform, builder, 0); // 계층 구조
            DumpAllComponents(selectedGameObject, builder); // 모든 컴포넌트
            DumpAnimatorInfo(selectedGameObject, builder); // Animator 정보
            DumpHumanoidBones(selectedGameObject, builder); // Humanoid 본
            DumpSkinnedMeshRenderers(selectedGameObject, builder); // 스킨 메시
            DumpMeshFilters(selectedGameObject, builder); // 일반 메시
            DumpRenderers(selectedGameObject, builder); // 렌더러/머티리얼
            DumpPhysicsComponents(selectedGameObject, builder); // 물리 컴포넌트
            DumpAnimationClips(selectedGameObject, builder); // 애니메이션 클립
            DumpAnimatorControllerInfo(selectedGameObject, builder); // 컨트롤러 구조
        }

        builder.AppendLine("===== FULL OBJECT DUMP END =====");

        string dumpText = builder.ToString();

        EditorGUIUtility.systemCopyBuffer = dumpText; // 클립보드 복사
        string savedPath = SaveDumpFile(selectedObject.name, dumpText); // 파일 저장

        Debug.Log("Full Dump 완료. 클립보드 복사 + 파일 저장: " + savedPath);
    }

    private static GameObject GetGameObjectFromSelection(UnityEngine.Object selectedObject)
    {
        GameObject selectedGameObject = selectedObject as GameObject;

        if (selectedGameObject != null)
            return selectedGameObject; // GameObject 선택

        Component selectedComponent = selectedObject as Component;

        if (selectedComponent != null)
            return selectedComponent.gameObject; // Component 선택

        string assetPath = AssetDatabase.GetAssetPath(selectedObject);

        if (string.IsNullOrEmpty(assetPath))
            return null; // 에셋 경로 없음

        GameObject assetGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

        return assetGameObject; // FBX/Prefab 에셋 선택
    }

    private static void DumpAssetInfo(UnityEngine.Object selectedObject, StringBuilder builder)
    {
        builder.AppendLine("===== ASSET INFO =====");

        string assetPath = AssetDatabase.GetAssetPath(selectedObject);
        builder.AppendLine("Asset Path       : " + assetPath);
        builder.AppendLine("GUID             : " + GetGuid(assetPath));
        builder.AppendLine("Is Main Asset    : " + AssetDatabase.IsMainAsset(selectedObject));
        builder.AppendLine("Is Sub Asset     : " + AssetDatabase.IsSubAsset(selectedObject));
        builder.AppendLine("Is Native Asset  : " + AssetDatabase.IsNativeAsset(selectedObject));
        builder.AppendLine("Is Foreign Asset : " + AssetDatabase.IsForeignAsset(selectedObject));
        builder.AppendLine();
    }

    private static void DumpImporterInfo(UnityEngine.Object selectedObject, StringBuilder builder)
    {
        builder.AppendLine("===== IMPORTER INFO =====");

        string assetPath = AssetDatabase.GetAssetPath(selectedObject);

        if (string.IsNullOrEmpty(assetPath))
        {
            builder.AppendLine("Importer: NONE");
            builder.AppendLine();
            return;
        }

        AssetImporter importer = AssetImporter.GetAtPath(assetPath);

        if (importer == null)
        {
            builder.AppendLine("Importer: NONE");
            builder.AppendLine();
            return;
        }

        builder.AppendLine("Importer Type : " + importer.GetType().FullName);
        builder.AppendLine("Asset Bundle  : " + importer.assetBundleName);
        builder.AppendLine("User Data     : " + importer.userData);

        ModelImporter modelImporter = importer as ModelImporter;

        if (modelImporter != null)
        {
            builder.AppendLine("Model Importer");
            builder.AppendLine("  Global Scale          : " + modelImporter.globalScale);
            builder.AppendLine("  Animation Type        : " + modelImporter.animationType);
            builder.AppendLine("  Avatar Setup          : " + modelImporter.avatarSetup);
            builder.AppendLine("  Optimize Game Objects : " + modelImporter.optimizeGameObjects);
            builder.AppendLine("  Import Animation      : " + modelImporter.importAnimation);
            builder.AppendLine("  Import BlendShapes    : " + modelImporter.importBlendShapes);
            builder.AppendLine("  Import Cameras        : " + modelImporter.importCameras);
            builder.AppendLine("  Import Lights         : " + modelImporter.importLights);
            builder.AppendLine("  Mesh Compression      : " + modelImporter.meshCompression);
            builder.AppendLine("  Read Write Enabled    : " + modelImporter.isReadable);
            builder.AppendLine("  Material Import Mode  : " + modelImporter.materialImportMode);
        }

        builder.AppendLine();
    }

    private static void DumpGameObjectInfo(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== GAMEOBJECT INFO =====");
        builder.AppendLine("Name       : " + root.name);
        builder.AppendLine("Path       : " + GetPath(root.transform));
        builder.AppendLine("Tag        : " + root.tag);
        builder.AppendLine("Layer      : " + LayerMask.LayerToName(root.layer) + " (" + root.layer + ")");
        builder.AppendLine("ActiveSelf : " + root.activeSelf);
        builder.AppendLine("ActiveTree : " + root.activeInHierarchy);
        builder.AppendLine();
    }

    private static void DumpHierarchy(Transform target, StringBuilder builder, int depth)
    {
        if (depth == 0)
            builder.AppendLine("===== HIERARCHY / TRANSFORMS =====");

        string indent = new string(' ', depth * 2);

        builder.AppendLine(indent + "- " + target.name);
        builder.AppendLine(indent + "  Path           : " + GetPath(target));
        builder.AppendLine(indent + "  Local Position : " + FormatVector3(target.localPosition));
        builder.AppendLine(indent + "  Local Rotation : " + FormatVector3(target.localEulerAngles));
        builder.AppendLine(indent + "  Local Scale    : " + FormatVector3(target.localScale));
        builder.AppendLine(indent + "  World Position : " + FormatVector3(target.position));
        builder.AppendLine(indent + "  World Rotation : " + FormatVector3(target.eulerAngles));
        builder.AppendLine(indent + "  World Scale    : " + FormatVector3(target.lossyScale));
        builder.AppendLine();

        for (int i = 0; i < target.childCount; i++)
        {
            DumpHierarchy(target.GetChild(i), builder, depth + 1);
        }

        if (depth == 0)
            builder.AppendLine();
    }

    private static void DumpAllComponents(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== ALL COMPONENTS / SERIALIZED VALUES =====");

        Component[] components = root.GetComponentsInChildren<Component>(true);

        builder.AppendLine("Component Count: " + components.Length);
        builder.AppendLine();

        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];

            if (component == null)
            {
                builder.AppendLine("[" + i + "] Missing Component");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine("[" + i + "] " + component.GetType().FullName);
            builder.AppendLine("  Path: " + GetPath(component.transform));

            Behaviour behaviour = component as Behaviour;
            if (behaviour != null)
                builder.AppendLine("  Enabled: " + behaviour.enabled);

            Renderer renderer = component as Renderer;
            if (renderer != null)
                builder.AppendLine("  Enabled: " + renderer.enabled);

            DumpSerializedProperties(component, builder, "  ");

            builder.AppendLine();
        }
    }

    private static void DumpSerializedProperties(UnityEngine.Object target, StringBuilder builder, string indent)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.GetIterator();

        bool enterChildren = true;
        int propertyCount = 0;
        const int maxPropertyCount = 300;

        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            propertyCount++;

            if (propertyCount > maxPropertyCount)
            {
                builder.AppendLine(indent + "... property dump truncated");
                break;
            }

            builder.AppendLine(indent + property.propertyPath + " = " + GetSerializedPropertyValue(property));
        }
    }

    private static string GetSerializedPropertyValue(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
                return property.intValue.ToString();

            case SerializedPropertyType.Boolean:
                return property.boolValue.ToString();

            case SerializedPropertyType.Float:
                return property.floatValue.ToString("F4");

            case SerializedPropertyType.String:
                return property.stringValue;

            case SerializedPropertyType.Color:
                return property.colorValue.ToString();

            case SerializedPropertyType.ObjectReference:
                return GetObjectReferenceValue(property.objectReferenceValue);

            case SerializedPropertyType.LayerMask:
                return property.intValue.ToString();

            case SerializedPropertyType.Enum:
                return property.enumDisplayNames[property.enumValueIndex];

            case SerializedPropertyType.Vector2:
                return property.vector2Value.ToString("F4");

            case SerializedPropertyType.Vector3:
                return property.vector3Value.ToString("F4");

            case SerializedPropertyType.Vector4:
                return property.vector4Value.ToString("F4");

            case SerializedPropertyType.Rect:
                return property.rectValue.ToString();

            case SerializedPropertyType.ArraySize:
                return property.intValue.ToString();

            case SerializedPropertyType.Character:
                return property.intValue.ToString();

            case SerializedPropertyType.AnimationCurve:
                return "AnimationCurve Keys: " + property.animationCurveValue.length;

            case SerializedPropertyType.Bounds:
                return property.boundsValue.ToString();

            case SerializedPropertyType.Quaternion:
                return property.quaternionValue.eulerAngles.ToString("F4");

            default:
                return property.propertyType.ToString();
        }
    }

    private static string GetObjectReferenceValue(UnityEngine.Object target)
    {
        if (target == null)
            return "NULL";

        string assetPath = AssetDatabase.GetAssetPath(target);

        if (!string.IsNullOrEmpty(assetPath))
            return target.name + " [" + target.GetType().Name + "] AssetPath: " + assetPath;

        GameObject gameObject = target as GameObject;

        if (gameObject != null)
            return GetPath(gameObject.transform) + " [GameObject]";

        Component component = target as Component;

        if (component != null)
            return GetPath(component.transform) + " [" + component.GetType().Name + "]";

        return target.name + " [" + target.GetType().Name + "]";
    }

    private static void DumpAnimatorInfo(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== ANIMATOR INFO =====");

        Animator[] animators = root.GetComponentsInChildren<Animator>(true);

        builder.AppendLine("Animator Count: " + animators.Length);
        builder.AppendLine();

        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];

            builder.AppendLine("Animator " + i);
            builder.AppendLine("  Path              : " + GetPath(animator.transform));
            builder.AppendLine("  Controller        : " + GetObjectName(animator.runtimeAnimatorController));
            builder.AppendLine("  Avatar            : " + GetObjectName(animator.avatar));
            builder.AppendLine("  Avatar Valid      : " + (animator.avatar != null && animator.avatar.isValid));
            builder.AppendLine("  Avatar Human      : " + (animator.avatar != null && animator.avatar.isHuman));
            builder.AppendLine("  Apply Root Motion : " + animator.applyRootMotion);
            builder.AppendLine("  Update Mode       : " + animator.updateMode);
            builder.AppendLine("  Culling Mode      : " + animator.cullingMode);
            builder.AppendLine();
        }
    }

    private static void DumpHumanoidBones(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== HUMANOID BONE MAP =====");

        Animator animator = root.GetComponentInChildren<Animator>(true);

        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            builder.AppendLine("Humanoid Bone Map: unavailable");
            builder.AppendLine();
            return;
        }

        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            HumanBodyBones bone = (HumanBodyBones)i;
            Transform boneTransform = animator.GetBoneTransform(bone);

            if (boneTransform == null)
                continue;

            builder.AppendLine(bone.ToString());
            builder.AppendLine("  Path           : " + GetPath(boneTransform));
            builder.AppendLine("  Local Position : " + FormatVector3(boneTransform.localPosition));
            builder.AppendLine("  Local Rotation : " + FormatVector3(boneTransform.localEulerAngles));
            builder.AppendLine("  Local Scale    : " + FormatVector3(boneTransform.localScale));
            builder.AppendLine("  World Position : " + FormatVector3(boneTransform.position));
            builder.AppendLine("  World Rotation : " + FormatVector3(boneTransform.eulerAngles));
            builder.AppendLine();
        }
    }

    private static void DumpSkinnedMeshRenderers(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== SKINNED MESH RENDERERS =====");

        SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        builder.AppendLine("Count: " + renderers.Length);
        builder.AppendLine();

        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];

            builder.AppendLine("Renderer " + i);
            builder.AppendLine("  Path              : " + GetPath(renderer.transform));
            builder.AppendLine("  Mesh              : " + GetObjectName(renderer.sharedMesh));
            builder.AppendLine("  Root Bone         : " + GetPathSafe(renderer.rootBone));
            builder.AppendLine("  Bones Count       : " + (renderer.bones != null ? renderer.bones.Length : 0));
            builder.AppendLine("  Bounds Center     : " + FormatVector3(renderer.localBounds.center));
            builder.AppendLine("  Bounds Size       : " + FormatVector3(renderer.localBounds.size));
            builder.AppendLine("  Update Offscreen  : " + renderer.updateWhenOffscreen);
            builder.AppendLine("  Quality           : " + renderer.quality);

            DumpMeshInfo(renderer.sharedMesh, builder, "  ");
            DumpMaterials(renderer.sharedMaterials, builder, "  ");

            builder.AppendLine("  Bones");

            if (renderer.bones != null)
            {
                for (int b = 0; b < renderer.bones.Length; b++)
                {
                    builder.AppendLine("    [" + b + "] " + GetPathSafe(renderer.bones[b]));
                }
            }

            builder.AppendLine();
        }
    }

    private static void DumpMeshFilters(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== MESH FILTERS =====");

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);

        builder.AppendLine("Count: " + filters.Length);
        builder.AppendLine();

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter filter = filters[i];

            builder.AppendLine("MeshFilter " + i);
            builder.AppendLine("  Path : " + GetPath(filter.transform));
            DumpMeshInfo(filter.sharedMesh, builder, "  ");
            builder.AppendLine();
        }
    }

    private static void DumpMeshInfo(Mesh mesh, StringBuilder builder, string indent)
    {
        if (mesh == null)
        {
            builder.AppendLine(indent + "Mesh: NULL");
            return;
        }

        builder.AppendLine(indent + "Mesh Name        : " + mesh.name);
        builder.AppendLine(indent + "Mesh Asset Path  : " + AssetDatabase.GetAssetPath(mesh));
        builder.AppendLine(indent + "Vertex Count     : " + mesh.vertexCount);
        builder.AppendLine(indent + "Sub Mesh Count   : " + mesh.subMeshCount);
        builder.AppendLine(indent + "BlendShape Count : " + mesh.blendShapeCount);
        builder.AppendLine(indent + "Bounds Center    : " + FormatVector3(mesh.bounds.center));
        builder.AppendLine(indent + "Bounds Size      : " + FormatVector3(mesh.bounds.size));

        for (int i = 0; i < mesh.blendShapeCount; i++)
        {
            builder.AppendLine(indent + "  BlendShape[" + i + "] " + mesh.GetBlendShapeName(i));
        }
    }

    private static void DumpRenderers(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== RENDERERS / MATERIALS =====");

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);

        builder.AppendLine("Renderer Count: " + renderers.Length);
        builder.AppendLine();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];

            builder.AppendLine("Renderer " + i);
            builder.AppendLine("  Path            : " + GetPath(renderer.transform));
            builder.AppendLine("  Type            : " + renderer.GetType().Name);
            builder.AppendLine("  Enabled         : " + renderer.enabled);
            builder.AppendLine("  Shadow Casting  : " + renderer.shadowCastingMode);
            builder.AppendLine("  Receive Shadows : " + renderer.receiveShadows);
            DumpMaterials(renderer.sharedMaterials, builder, "  ");
            builder.AppendLine();
        }
    }

    private static void DumpMaterials(Material[] materials, StringBuilder builder, string indent)
    {
        builder.AppendLine(indent + "Materials Count: " + (materials != null ? materials.Length : 0));

        if (materials == null)
            return;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];

            if (material == null)
            {
                builder.AppendLine(indent + "  [" + i + "] NULL");
                continue;
            }

            builder.AppendLine(indent + "  [" + i + "] " + material.name);
            builder.AppendLine(indent + "      Asset Path : " + AssetDatabase.GetAssetPath(material));
            builder.AppendLine(indent + "      Shader     : " + (material.shader != null ? material.shader.name : "NULL"));

            DumpMaterialTextures(material, builder, indent + "      ");
        }
    }

    private static void DumpMaterialTextures(Material material, StringBuilder builder, string indent)
    {
        if (material == null || material.shader == null)
            return;

        int propertyCount = ShaderUtil.GetPropertyCount(material.shader);

        for (int i = 0; i < propertyCount; i++)
        {
            if (ShaderUtil.GetPropertyType(material.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                continue;

            string propertyName = ShaderUtil.GetPropertyName(material.shader, i);
            Texture texture = material.GetTexture(propertyName);

            if (texture == null)
                continue;

            builder.AppendLine(indent + propertyName + " = " + texture.name + " / " + AssetDatabase.GetAssetPath(texture));
        }
    }

    private static void DumpPhysicsComponents(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== PHYSICS COMPONENTS =====");

        Rigidbody[] rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);

        builder.AppendLine("Rigidbody Count: " + rigidbodies.Length);

        for (int i = 0; i < rigidbodies.Length; i++)
        {
            Rigidbody rb = rigidbodies[i];

            builder.AppendLine("Rigidbody " + i);
            builder.AppendLine("  Path                 : " + GetPath(rb.transform));
            builder.AppendLine("  Mass                 : " + rb.mass);
            builder.AppendLine("  Use Gravity          : " + rb.useGravity);
            builder.AppendLine("  Is Kinematic         : " + rb.isKinematic);
            builder.AppendLine("  Interpolation        : " + rb.interpolation);
            builder.AppendLine("  Collision Detection  : " + rb.collisionDetectionMode);
            builder.AppendLine("  Constraints          : " + rb.constraints);
            builder.AppendLine();
        }

        builder.AppendLine("Collider Count: " + colliders.Length);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];

            builder.AppendLine("Collider " + i);
            builder.AppendLine("  Path       : " + GetPath(collider.transform));
            builder.AppendLine("  Type       : " + collider.GetType().Name);
            builder.AppendLine("  Enabled    : " + collider.enabled);
            builder.AppendLine("  Is Trigger : " + collider.isTrigger);
            builder.AppendLine("  Material   : " + GetObjectName(collider.sharedMaterial));
            builder.AppendLine();
        }

        builder.AppendLine();
    }

    private static void DumpAnimationClips(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== ANIMATION CLIPS =====");

        AnimationClip[] clips = AnimationUtility.GetAnimationClips(root);

        builder.AppendLine("Clips From Object: " + clips.Length);

        for (int i = 0; i < clips.Length; i++)
        {
            DumpClipInfo(clips[i], builder, "  ");
        }

        Animator animator = root.GetComponentInChildren<Animator>(true);

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimationClip[] controllerClips = animator.runtimeAnimatorController.animationClips;

            builder.AppendLine("Clips From Controller: " + controllerClips.Length);

            for (int i = 0; i < controllerClips.Length; i++)
            {
                DumpClipInfo(controllerClips[i], builder, "  ");
            }
        }

        builder.AppendLine();
    }

    private static void DumpClipInfo(AnimationClip clip, StringBuilder builder, string indent)
    {
        if (clip == null)
            return;

        builder.AppendLine(indent + "Clip: " + clip.name);
        builder.AppendLine(indent + "  Asset Path   : " + AssetDatabase.GetAssetPath(clip));
        builder.AppendLine(indent + "  Length       : " + clip.length.ToString("F4"));
        builder.AppendLine(indent + "  Frame Rate   : " + clip.frameRate.ToString("F2"));
        builder.AppendLine(indent + "  Loop Time    : " + GetClipLoopTime(clip));
        builder.AppendLine(indent + "  Human Motion : " + clip.humanMotion);
        builder.AppendLine(indent + "  Legacy       : " + clip.legacy);
        builder.AppendLine(indent + "  Empty        : " + clip.empty);
    }

    private static bool GetClipLoopTime(AnimationClip clip)
    {
        SerializedObject serializedObject = new SerializedObject(clip);
        SerializedProperty settings = serializedObject.FindProperty("m_AnimationClipSettings");
        SerializedProperty loopTime = settings != null ? settings.FindPropertyRelative("m_LoopTime") : null;

        return loopTime != null && loopTime.boolValue;
    }

    private static void DumpAnimatorControllerInfo(GameObject root, StringBuilder builder)
    {
        builder.AppendLine("===== ANIMATOR CONTROLLER INFO =====");

        Animator animator = root.GetComponentInChildren<Animator>(true);

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            builder.AppendLine("Animator Controller: NONE");
            builder.AppendLine();
            return;
        }

        AnimatorController controller = animator.runtimeAnimatorController as AnimatorController;

        if (controller == null)
        {
            builder.AppendLine("Controller Type: " + animator.runtimeAnimatorController.GetType().FullName);
            builder.AppendLine("Controller Name: " + animator.runtimeAnimatorController.name);
            builder.AppendLine();
            return;
        }

        builder.AppendLine("Controller Name : " + controller.name);
        builder.AppendLine("Asset Path      : " + AssetDatabase.GetAssetPath(controller));
        builder.AppendLine();

        builder.AppendLine("Parameters");

        for (int i = 0; i < controller.parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = controller.parameters[i];

            builder.AppendLine("  [" + i + "] " + parameter.name + " / " + parameter.type);
        }

        builder.AppendLine();

        builder.AppendLine("Layers");

        for (int i = 0; i < controller.layers.Length; i++)
        {
            AnimatorControllerLayer layer = controller.layers[i];

            builder.AppendLine("  Layer[" + i + "] " + layer.name);
            builder.AppendLine("    Weight       : " + layer.defaultWeight);
            builder.AppendLine("    Blending     : " + layer.blendingMode);
            builder.AppendLine("    Mask         : " + GetObjectName(layer.avatarMask));

            if (layer.stateMachine != null)
            {
                builder.AppendLine("    States");

                ChildAnimatorState[] states = layer.stateMachine.states;

                for (int s = 0; s < states.Length; s++)
                {
                    AnimatorState state = states[s].state;

                    builder.AppendLine("      State[" + s + "] " + state.name);
                    builder.AppendLine("        Motion      : " + GetObjectName(state.motion));
                    builder.AppendLine("        Speed       : " + state.speed);
                    builder.AppendLine("        WriteDefault: " + state.writeDefaultValues);
                }
            }

            builder.AppendLine();
        }

        builder.AppendLine();
    }

    private static string SaveDumpFile(string selectedName, string dumpText)
    {
        if (!AssetDatabase.IsValidFolder(DumpFolder))
        {
            AssetDatabase.CreateFolder("Assets", "_FullDumps"); // 덤프 폴더
        }

        string safeName = MakeSafeFileName(selectedName);
        string fileName = safeName + "_FullDump_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        string assetPath = DumpFolder + "/" + fileName;
        string fullPath = Path.Combine(Application.dataPath, "_FullDumps", fileName);

        File.WriteAllText(fullPath, dumpText, Encoding.UTF8); // 저장
        AssetDatabase.ImportAsset(assetPath);
        AssetDatabase.Refresh();

        return assetPath;
    }

    private static string GetGuid(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
            return "NONE";

        return AssetDatabase.AssetPathToGUID(assetPath);
    }

    private static string FormatVector3(Vector3 value)
    {
        return string.Format("({0:F4}, {1:F4}, {2:F4})", value.x, value.y, value.z);
    }

    private static string GetPath(Transform target)
    {
        if (target == null)
            return "NULL";

        string path = target.name;

        while (target.parent != null)
        {
            target = target.parent;
            path = target.name + "/" + path;
        }

        return path;
    }

    private static string GetPathSafe(Transform target)
    {
        if (target == null)
            return "NULL";

        return GetPath(target);
    }

    private static string GetObjectName(UnityEngine.Object target)
    {
        if (target == null)
            return "NULL";

        return target.name;
    }

    private static string MakeSafeFileName(string fileName)
    {
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }
}
