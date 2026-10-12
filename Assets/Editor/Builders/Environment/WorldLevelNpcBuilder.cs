using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class WorldLevelNpcBuilder
{
    public const string NpcPath = "Assets/ProjectOverburst/03_Features/World/Prefabs/PF_WorldLevelNpc.prefab";
    public const string FontPath = "Assets/ProjectOverburst/Resources/UI/WorldLevel/WorldLevelNpcNames SDF.asset";
    const string Source = "Assets/ThirdParty/02_인간캐릭터/Medieval_NPC_Pack_2/OVERBURST_Merchants/PF_HideoutWeaponMerchantVisual.prefab";
    public static void BuildNpc()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject root = null;
        try
        {
            Folder(Path.GetDirectoryName(NpcPath).Replace('\\','/'));
            root = new GameObject("PF_WorldLevelNpc"); SceneManager.MoveGameObjectToScene(root, scene);
            var interaction = root.AddComponent<WorldLevelNpcInteractable>();
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source), scene);
            visual.name = "NPC Visual"; visual.transform.SetParent(root.transform, false);
            var allowed = new System.Collections.Generic.HashSet<string>(new[]{"SK_Blouse_Cut","SK_Boots","SK_Cape","M_Hands","M_Head","M_Hair2","SK_Jacket","SK_Jacket_Belt2","SK_Knife","SK_Sheath","SK_Belt","SK_Pants","SK_Bag3"});
            var renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.gameObject.SetActive(allowed.Contains(renderer.name));
                if (!renderer.gameObject.activeSelf) continue;
                for (var t=renderer.transform;t&&t!=visual.transform;t=t.parent)t.gameObject.SetActive(true);
                renderer.enabled=true;
            }
            var head = renderers.Single(r=>r.name=="M_Head");
            var headSource = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/CharacterVisual/Prefabs/Medieval_NPC_Pack_2/Merchant/Prefab/Body/SK_Head_4.prefab").GetComponentInChildren<SkinnedMeshRenderer>(true);
            if(head.bones.Length!=headSource.bones.Length)throw new InvalidOperationException("NPC bone contract mismatch");
            head.sharedMesh=headSource.sharedMesh;head.sharedMaterials=headSource.sharedMaterials;head.localBounds=headSource.localBounds;
            var animator=visual.GetComponent<Animator>();
            if(!animator||!animator.avatar||!animator.runtimeAnimatorController)throw new InvalidOperationException("NPC rig/idle missing");
            animator.applyRootMotion=false;animator.Rebind();animator.Update(0);
            var visible=renderers.Where(r=>r.enabled&&r.gameObject.activeInHierarchy).ToArray();
            var bounds=visible[0].bounds;foreach(var renderer in visible.Skip(1))bounds.Encapsulate(renderer.bounds);
            float scale=1.82f/bounds.size.y;visual.transform.localScale*=scale;visual.transform.localPosition=Vector3.up*(-bounds.min.y*scale);
            var collider=root.AddComponent<CapsuleCollider>();collider.center=new Vector3(0,.9f,0);collider.height=1.8f;collider.radius=.28f;
            var name=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NpcPresentationBuilder.PrefabPath),scene);
            name.name="NPC Name";name.transform.SetParent(root.transform,false);name.transform.localPosition=Vector3.up*2.35f;
            var nameText=name.GetComponent<TextMeshPro>();nameText.font=EnsureFont();nameText.fontSharedMaterial=nameText.font.material;nameText.text="순찰대장 로웬";
            var prompt=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/02_Shared/Interaction/Prefabs/PF_WorldInteractionKeyPrompt.prefab"),scene);
            prompt.name="PromptRoot";prompt.transform.SetParent(root.transform,false);prompt.transform.localPosition=Vector3.up*2.75f;prompt.SetActive(false);
            var promptText=prompt.GetComponent<TextMeshPro>();
            if(promptText){promptText.font=nameText.font;promptText.fontSharedMaterial=nameText.font.material;promptText.text="F : 월드레벨";}
            var so=new SerializedObject(interaction);so.FindProperty("promptRoot").objectReferenceValue=prompt;so.FindProperty("promptText").objectReferenceValue=promptText;so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root,NpcPath);
        }
        finally {if(root)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static TMP_FontAsset EnsureFont()
    {
        var existing=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);if(existing)return existing;
        var source=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NpcPresentationBuilder.FontPath);
        var sourceFile=AssetDatabase.LoadAssetAtPath<Font>("Assets/ProjectOverburst/Resources/UI/Fonts/Quest/NotoSerifKR_Semibold.ttf");
        var font=TMP_FontAsset.CreateFontAsset(sourceFile,90,12,UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,512,512,AtlasPopulationMode.Dynamic);
        font.name="WorldLevelNpcNames SDF";
        if(!font.TryAddCharacters("순찰대장 로웬F : 월드레벨",out var missing))throw new InvalidOperationException(missing);
        font.atlasPopulationMode=AtlasPopulationMode.Static;font.material.shader=source.material.shader;
        font.material.SetFloat("_OutlineWidth",.16f);font.material.SetColor("_OutlineColor",new Color(0,0,0,.92f));font.material.EnableKeyword("OUTLINE_ON");
        AssetDatabase.CreateAsset(font,FontPath);AssetDatabase.AddObjectToAsset(font.material,font);foreach(var atlas in font.atlasTextures)AssetDatabase.AddObjectToAsset(atlas,font);AssetDatabase.SaveAssetIfDirty(font);return font;
    }
    public static void InstallScene()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Editor must be idle");
        if(SceneManager.GetSceneByPath(MainTownBuilder.ScenePath).IsValid())throw new InvalidOperationException("Preserve open MainScene");
        var active=SceneManager.GetActiveScene();var scene=EditorSceneManager.OpenScene(MainTownBuilder.ScenePath,OpenSceneMode.Additive);
        try
        {
            if(scene.GetRootGameObjects().Any(r=>r.name=="WorldLevelServices"))throw new InvalidOperationException("Already installed; inspect instead of duplicating");
            var root=new GameObject("WorldLevelServices");SceneManager.MoveGameObjectToScene(root,scene);
            var npc=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(NpcPath),scene);npc.name="순찰대장 로웬";npc.transform.SetParent(root.transform,false);
            var terrain=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Terrain>(true)).First();
            var position=new Vector3(151.8f,0,96.5f);position.y=terrain.SampleHeight(position)+terrain.transform.position.y;
            npc.transform.SetPositionAndRotation(position,Quaternion.identity);
            var poolRoot=new GameObject("InactiveEnemyPool");poolRoot.transform.SetParent(root.transform,false);poolRoot.SetActive(false);
            var enemies=new GameObject("OutdoorEnemies");enemies.transform.SetParent(root.transform,false);
            var town=new GameObject("TownBoundary");town.transform.SetParent(root.transform,false);town.transform.position=new Vector3(155,0,110);
            var boundary=town.AddComponent<BoxCollider>();boundary.isTrigger=true;boundary.size=new Vector3(52,100,56);
            var table=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>("Assets/ProjectOverburst/Resources/Enemies/Themes/Tables/GraveHunt.asset");
            if(!table||!table.Validate(out var reason))throw new InvalidOperationException("GraveHunt unavailable");
            var pool=root.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
            var spawn=root.AddComponent<EnemySpawnService>();spawn.Configure(table.Catalog,pool);
            var outdoor=root.AddComponent<OutdoorWorldSpawner>();var so=new SerializedObject(outdoor);
            so.FindProperty("terrain").objectReferenceValue=terrain;so.FindProperty("theme").objectReferenceValue=table;so.FindProperty("spawn").objectReferenceValue=spawn;
            so.FindProperty("town").objectReferenceValue=boundary;so.FindProperty("enemiesRoot").objectReferenceValue=enemies.transform;so.ApplyModifiedPropertiesWithoutUndo();
            if(!EditorSceneManager.SaveScene(scene))throw new IOException("MainScene save failed");
        }
        finally {if(active.IsValid())SceneManager.SetActiveScene(active);EditorSceneManager.CloseScene(scene,true);}
    }
    static void Folder(string path)
    {string current="Assets";foreach(var part in path.Split('/').Skip(1)){var next=current+"/"+part;if(!AssetDatabase.IsValidFolder(next))AssetDatabase.CreateFolder(current,part);current=next;}}
}
