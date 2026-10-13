using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DesertBakedSpawnIntegrationVerifier
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    public static object VerifyData(string directory, string id)
    {
        MojaveWorldBuilder.Guard();var raw=JObject.Parse(File.ReadAllText(Path.Combine(directory,"Bakes",id,"candidates.json")));
        string scenePath=(string)raw["scenePath"];int checks=0;
        void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
        var scene=EditorSceneManager.OpenPreviewScene(scenePath);
        try
        {
            var world=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MojaveWorld>(true)).Single();world.EnsureLayout();
            var bank=DesertSpawnData.Load(world);Check(bank,"Current saved map binds to its authored spawn data.");
            Check(bank.bodies.Length==48&&bank.bodies.Sum(b=>b.definitions.Length)==62,"Current complete body profile bank retained.");
            Check(bank.areas.Length==world.layout.places.Count,"Every authored platform has one area bank.");
            var sourceType=typeof(MapCombatGeometry).GetNestedType("Source",BindingFlags.NonPublic);var source=Activator.CreateInstance(sourceType,true);
            sourceType.GetField("desertSpawnData",Instance).SetValue(source,bank);sourceType.GetField("desertSpawnArea",Instance).SetValue(source,bank.areas[0]);
            var binding=sourceType.GetMethod("BakedBodyIndex",Instance);
            for(int b=0;b<bank.bodies.Length;b++)foreach(var def in bank.bodies[b].definitions)
            {
                var body=MapCombatGeometry.BodyFor(def);
                Check((int)binding.Invoke(source,new object[]{def,body})==b,"Runtime body mask resolves: "+def.EnemyId);
                Check((int)binding.Invoke(source,new object[]{def,new MapCombatGeometry.Body(body.Radius+.01f,body.Height)})==-1,"Changed body uses the live-grid fallback: "+def.EnemyId);
            }
            long validMask=(1L<<bank.bodies.Length)-1;int points=0,cells=0;
            for(int i=0;i<bank.areas.Length;i++)
            {
                var area=bank.areas[i];var original=raw["areas"][i];
                Check(area.index==i&&area.place==JsonUtility.ToJson(world.layout.places[i]),"Platform identity and pose retained: "+i);
                Check(area.cells.Length==original["cells"].Count()&&area.points.Length==original["points"].Count(),"Saved candidate and floor counts retained: "+i);
                for(int p=0;p<area.points.Length;p++)
                {
                    var expected=original["points"][p];var actual=area.points[p];
                    Check(actual.bodyMask>0&&(actual.bodyMask&~validMask)==0&&actual.bodyMask==(long)expected["bodyMask"],"64-bit body mask preserved: "+i+"/"+p);
                    Check(actual.position==expected["position"].ToObject<Vector3>(),"Candidate coordinate preserved: "+i+"/"+p);
                }
                sourceType.GetField("desertSpawnArea",Instance).SetValue(source,area);
                var candidates=(IEnumerable<(Vector3 position,long bodyMask)>)sourceType.GetMethod("BakedCandidates",Instance).Invoke(source,null);
                Check(candidates.Count()==area.points.Length,"Runtime enumerates saved positions once: "+i);
                points+=area.points.Length;cells+=area.cells.Length;
            }
            var current=world.seed;world.seed++;
            Check(!DesertSpawnData.Load(world),"Changed seed rejects the stale bank.");world.seed=current;
            var scale=world.transform.localScale;world.transform.localScale=scale*2;
            Check(!DesertSpawnData.Load(world),"Scaled terrain rejects the bank.");world.transform.localScale=scale;
            var result=new {status="PASS_NATIVE_BINDING",id,checks,points,cells,profiles=bank.bodies.Length,definitions=bank.bodies.Sum(b=>b.definitions.Length),scope="Native asset round trip and runtime binding/fallback. Entry Play and population parity NOT_RUN."};
            File.WriteAllText(Path.Combine(directory,"Bakes",id,"native-binding.json"),JsonConvert.SerializeObject(result,Formatting.Indented));return result;
        }
        finally{EditorSceneManager.ClosePreviewScene(scene);}
    }
}
