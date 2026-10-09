using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MainTownDestructionAssetsVerifier
{
    public static object Run(string directory)
    {
        var scene = MainTownDestructionBuilder.RequireScene();
        var controller = scene.GetRootGameObjects().Single(g => g.name == MainTownDestructionBuilder.Group).GetComponent<MainTownDestruction>();
        var rows = new List<object>(); Directory.CreateDirectory(directory);
        foreach (var definition in controller.catalog.definitions)
        {
            var debris = definition.debris.GetComponent<MainTownDebris>();
            if (debris == null || debris.bodies.Length == 0 || definition.source == null) throw new InvalidOperationException("Missing source or debris");
            if (definition.debris.GetComponentsInChildren<Transform>(true).Any(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) != 0)) throw new InvalidOperationException("Missing script");
            int vertices = 0, triangles = 0; double exteriorArea=0,sourceArea=0;
            foreach (var body in debris.bodies)
            {
                var mesh = body.GetComponent<MeshFilter>().sharedMesh; var materials = body.GetComponent<MeshRenderer>().sharedMaterials;
                if (mesh == null || mesh.vertexCount < 3 || mesh.uv.Length != mesh.vertexCount || mesh.normals.Length != mesh.vertexCount || materials.Length != mesh.subMeshCount || materials.Any(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader")) throw new InvalidOperationException("Invalid fragment: " + definition.source.name);
                if (!body.isKinematic || body.useGravity || body.GetComponent<Collider>().enabled) throw new InvalidOperationException("Fragment is not dormant");
                vertices += mesh.vertexCount; triangles += mesh.triangles.Length / 3;
                exteriorArea+=MainTownMeshSlicer.ExteriorArea(mesh,mesh.subMeshCount-1);
            }
            var preview = new PreviewRenderUtility();
            try
            {
                preview.camera.fieldOfView = 30; preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.13f,.16f,.19f);
                preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(35,30,0); preview.lights[1].intensity = .9f;
                var original = Object.Instantiate(definition.source); var fragments = Object.Instantiate(definition.debris); preview.AddSingleGO(original); preview.AddSingleGO(fragments);
                var sourceLods=original.GetComponentsInChildren<LODGroup>(true);
                var sourceRenderers=sourceLods.Length==0?original.GetComponentsInChildren<MeshRenderer>(true):sourceLods.SelectMany(l=>l.GetLODs()[0].renderers).OfType<MeshRenderer>().Distinct().ToArray();
                foreach(var r in sourceRenderers){var f=r.GetComponent<MeshFilter>();if(f!=null&&f.sharedMesh!=null)sourceArea+=MainTownMeshSlicer.Area(MainTownMeshSlicer.Read(f.sharedMesh,original.transform.worldToLocalMatrix*f.transform.localToWorldMatrix));}
                foreach(var lod in original.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
                var b = definition.bounds; float distance = Mathf.Max(.3f,b.size.magnitude)*2.1f; preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = distance*5+100;
                preview.camera.transform.position = b.center + new Vector3(1,.6f,1).normalized*distance; preview.camera.transform.LookAt(b.center);
                for(int variant=0;variant<2;variant++)
                {
                    original.SetActive(variant==0);fragments.SetActive(variant==1);
                    preview.BeginPreview(new Rect(0,0,320,320),GUIStyle.none);preview.Render(true);var rendered=preview.EndPreview();var prior=RenderTexture.active;Texture2D read=null;
                    try { RenderTexture.active=(RenderTexture)rendered;read=new Texture2D(320,320,TextureFormat.RGB24,false);read.ReadPixels(new Rect(0,0,320,320),0,0);read.Apply();File.WriteAllBytes(Path.Combine(directory,definition.source.name+(variant==0?"-source.png":"-debris.png")),read.EncodeToPNG()); }
                    finally {RenderTexture.active=prior;if(read!=null)Object.DestroyImmediate(read);}
                }
            }
            finally { preview.Cleanup(); }
            double exteriorRatio=exteriorArea/sourceArea;if(!double.IsFinite(exteriorRatio)||Math.Abs(exteriorRatio-1)>.005)throw new InvalidOperationException("Exterior surface mismatch: "+definition.source.name+" ratio="+exteriorRatio);
            rows.Add(new { name=definition.source.name, definition.tree,pieces=debris.bodies.Length,vertices,triangles,exteriorRatio,source=AssetDatabase.GetAssetPath(definition.source),debris=AssetDatabase.GetAssetPath(definition.debris) });
        }
        var result = new { status="PASS", count=rows.Count, placements=controller.placements.Length, terrainTrees=controller.terrain.terrainData.treeInstanceCount, rows };
        File.WriteAllText(Path.Combine(directory,"native-assets.json"),JsonConvert.SerializeObject(result,Formatting.Indented)); return new { status="PASS", count=rows.Count };
    }
}
