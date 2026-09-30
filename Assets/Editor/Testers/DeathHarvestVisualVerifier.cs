using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

// Local visual QA; every temporary object stays in an unsaved additive scene.
public static class DeathHarvestVisualVerifier
{
    private const string Output = @"D:\JC Program\유니티\개인프로젝트\프로젝트 오버버스트\개인파일\코덱스산출\MonsterThemes\20260924_UndeadHorde\captures";

    public static string AuditRake()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(
            MonsterThemeCombatBuilder.Root+"/Actors/PF_DeathHarvest_RakeSkulker.prefab");
        var profile=AssetDatabase.LoadAssetAtPath<EnemyAnimationProfile>(
            MonsterThemeCombatBuilder.Root+"/Animations/DeathHarvest_RakeSkulker.asset");
        var previous=SceneManager.GetActiveScene();
        var preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(preview);
        try
        {
            var root=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);
            var animator=root.GetComponentInChildren<Animator>(true);
            animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            var renderer=root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var arm=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            var head=animator.GetBoneTransform(HumanBodyBones.Head);
            var clips=new[]{profile.Idle,profile.GetAttackClip(0)};
            var lines=new System.Collections.Generic.List<string>{
                "root="+root.activeInHierarchy+" animator="+animator.gameObject.activeInHierarchy+
                " controller="+(animator.runtimeAnimatorController!=null)};
            foreach(var clip in clips)
            {
                var graph=PlayableGraph.Create("Rake pose audit");
                try
                {
                    var play=AnimationClipPlayable.Create(graph,clip);
                    var output=AnimationPlayableOutput.Create(graph,"Animator",animator);
                    output.SetSourcePlayable(play);
                    graph.Play();
                    graph.Evaluate(clip.length*.55f);
                    var mesh=new Mesh();
                    renderer.BakeMesh(mesh);
                    var vertices=mesh.vertices;
                    var sample=vertices.Length>1000?vertices[1000]:vertices[vertices.Length/2];
                    lines.Add(clip.name+" arm="+arm.localRotation.eulerAngles+
                        " head="+head.localRotation.eulerAngles+" pos="+head.position+" sample="+sample);
                    Object.DestroyImmediate(mesh);
                }
                finally{graph.Destroy();}
            }
            return string.Join("\n",lines);
        }
        finally{SceneManager.SetActiveScene(previous);EditorSceneManager.CloseScene(preview,true);}
    }

    public static string Capture()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Edit Mode required.");
        var table=AssetDatabase.LoadAssetAtPath<EnemyThemeTable>(
            MonsterThemeCombatBuilder.Root+"/Tables/DeathHarvest.asset");
        if(table==null)throw new InvalidOperationException("DeathHarvest table missing.");
        Directory.CreateDirectory(Output);
        Scene previous=SceneManager.GetActiveScene();
        Scene preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        SceneManager.SetActiveScene(preview);
        var lines=new System.Collections.Generic.List<string>();
        try
        {
            var cameraRoot=new GameObject("DeathHarvest capture camera");
            var camera=cameraRoot.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.12f,.15f,.18f,1f);
            camera.cullingMask=1 << 31;
            camera.fieldOfView=35f;
            camera.nearClipPlane=.03f;camera.farClipPlane=50f;
            var lightRoot=new GameObject("Key light");
            var light=lightRoot.AddComponent<Light>();light.type=LightType.Directional;
            light.intensity=1.8f;light.color=new Color(.82f,.90f,1f);
            lightRoot.transform.rotation=Quaternion.Euler(48f,-35f,0);
            var fillRoot=new GameObject("Fill light");
            var fill=fillRoot.AddComponent<Light>();fill.type=LightType.Directional;
            fill.intensity=.85f;fill.color=new Color(.50f,.78f,.81f);
            fillRoot.transform.rotation=Quaternion.Euler(35f,140f,0);
            foreach(var entry in table.Entries)
            {
                var prefab=entry.definition.ActorPrefab.gameObject;
                var actor=(GameObject)PrefabUtility.InstantiatePrefab(prefab,preview);
                foreach(var child in actor.GetComponentsInChildren<Transform>(true))child.gameObject.layer=31;
                var animator=actor.GetComponentInChildren<Animator>(true);
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    skin.updateWhenOffscreen=true;
                float centerY=entry.tier==EnemyThemeTier.Small?.9f:
                    entry.tier==EnemyThemeTier.Medium?1.4f:2.2f;
                float distance=entry.tier==EnemyThemeTier.Small?5.8f:
                    entry.tier==EnemyThemeTier.Medium?8.2f:12f;
                cameraRoot.transform.position=new Vector3(distance*.42f,centerY+distance*.3f,distance);
                cameraRoot.transform.LookAt(new Vector3(0,centerY,0));
                var clips=new[]{entry.definition.AnimationProfile.Idle,
                    entry.definition.AnimationProfile.GetAttackClip(0)};
                for(int i=0;i<clips.Length;i++)
                {
                    var clip=clips[i];
                    var graph=PlayableGraph.Create("DeathHarvest visual QA");
                    try
                    {
                        var playable=AnimationClipPlayable.Create(graph,clip);
                        var output=AnimationPlayableOutput.Create(graph,"Model",animator);
                        output.SetSourcePlayable(playable);
                        graph.Play();
                        graph.Evaluate(clip.length*(i==0?.25f:.55f));
                        var baked=new System.Collections.Generic.List<GameObject>();
                        var disabled=new System.Collections.Generic.List<SkinnedMeshRenderer>();
                        foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            if(!skin.enabled)continue;
                            var mesh=new Mesh();
                            skin.BakeMesh(mesh);
                            var pose=new GameObject("Baked pose "+skin.name,typeof(MeshFilter),typeof(MeshRenderer));
                            pose.layer=31;pose.transform.SetParent(skin.transform,false);
                            pose.GetComponent<MeshFilter>().sharedMesh=mesh;
                            pose.GetComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
                            baked.Add(pose);disabled.Add(skin);skin.enabled=false;
                        }
                        var target=new RenderTexture(900,900,24,RenderTextureFormat.ARGB32);
                        try
                        {
                            camera.targetTexture=target;
                            camera.Render();
                            camera.Render();
                            RenderTexture previousTarget=RenderTexture.active;
                            RenderTexture.active=target;
                            var image=new Texture2D(900,900,TextureFormat.RGBA32,false);
                            try
                            {
                                image.ReadPixels(new Rect(0,0,900,900),0,0);
                                image.Apply();
                                string path=Path.Combine(Output,entry.definition.EnemyId+
                                    (i==0?"_idle":"_attack")+".png");
                                File.WriteAllBytes(path,image.EncodeToPNG());
                                lines.Add(path+" bytes="+new FileInfo(path).Length);
                            }
                            finally
                            {
                                RenderTexture.active=previousTarget;
                                camera.targetTexture=null;
                                Object.DestroyImmediate(image);
                            }
                        }
                        finally
                        {
                            foreach(var skin in disabled)skin.enabled=true;
                            foreach(var pose in baked)
                            {
                                var mesh=pose.GetComponent<MeshFilter>().sharedMesh;
                                Object.DestroyImmediate(pose);
                                Object.DestroyImmediate(mesh);
                            }
                            Object.DestroyImmediate(target);
                        }
                    }
                    finally {graph.Destroy();}
                }
                Object.DestroyImmediate(actor);
            }
        }
        finally
        {
            SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(preview,true);
        }
        return string.Join("\n",lines);
    }
}
