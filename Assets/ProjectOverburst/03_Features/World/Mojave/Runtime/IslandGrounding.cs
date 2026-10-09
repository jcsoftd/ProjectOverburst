using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;

namespace Overburst.Mojave
{
    public static class IslandGrounding
    {
        public static void Apply(MojaveWorld world,Func<Vector3,float> floor)
        {
            var cache=new Dictionary<Mesh,Vector3[]>();world.groundedPropCount=world.conformedMeshCount=0;
            foreach(var prop in MojaveTerrainFinish.PropRoots(world)) {
                if(prop.transform.parent.name.StartsWith("Background islet",StringComparison.Ordinal)&&(prop.name.Contains("RockJungle")||prop.name.Contains("IslandClif")))continue;
                var lod=prop.GetComponent<LODGroup>();
                var renderers=lod!=null?lod.GetLODs()[0].renderers:prop.GetComponentsInChildren<Renderer>(true);
                var points=new List<Vector3>();
                foreach(var renderer in renderers) {
                    if(renderer==null)continue;var filter=renderer.GetComponent<MeshFilter>();if(filter==null)continue;
                    var source=filter.sharedMesh;if(source==null)continue;
                    if(!cache.TryGetValue(source,out var vertices)){
                        if(source.isReadable)vertices=source.vertices;
                        else {using(var data=Mesh.AcquireReadOnlyMeshData(source)){var native=new NativeArray<Vector3>(data[0].vertexCount,Allocator.Temp);try{data[0].GetVertices(native);vertices=native.ToArray();}finally{native.Dispose();}}}
                        cache[source]=vertices;
                    }
                    foreach(var vertex in vertices)points.Add(filter.transform.TransformPoint(vertex));
                }
                if(points.Count==0)continue;
                // Imported FBX models can have a rotated local up axis. Measure the root
                // in world space and retain the authored mesh, silhouette and all LODs.
                float bottom=points.Min(p=>p.y),height=points.Max(p=>p.y)-bottom;
                bool rock=prop.name.Contains("Boulder")||prop.name.Contains("Cliff")||prop.name.Contains("Rock");
                float band=Mathf.Clamp(height*(rock?.10f:.025f),.025f,rock?.35f:.15f);
                var supports=points.Where(p=>p.y<=bottom+band).Select(p=>floor(p)-p.y).OrderBy(v=>v).ToArray();
                float embed=Mathf.Clamp(height*.025f,.015f,.12f);
                prop.transform.position+=Vector3.up*(supports[supports.Length/2]-embed);
                world.groundedPropCount++;
            }
        }
    }
}
