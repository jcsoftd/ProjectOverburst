using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Mojave;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MojaveCombatLayoutVerifier
{
    public static string Run()
    {
        MojaveWorldBuilder.Guard();
        var library=AssetDatabase.LoadAssetAtPath<MojaveCombatSizeLibrary>(MojaveWorldBuilder.SizedLibraryPath);
        if(library==null)throw new InvalidOperationException("Combat size library is missing.");
        var scene=EditorSceneManager.NewPreviewScene();GameObject owner=null;
        var fullPreviews=new List<object>();int cases=0;
        try {
            owner=new GameObject("Combat layout verification");SceneManager.MoveGameObjectToScene(owner,scene);
            var tiles=owner.AddComponent<MojaveCombatTileSet>();tiles.rules=library.rules;tiles.mixedSizes=true;
            MojaveLayout Raw(int seed,int count,int size) {
                var layout=new MojaveLayout(seed,library.catalog.patches.Count(p=>p.combatSize==MojaveCombatSize.Large),true,true,count,size);
                tiles.Apply(layout,library.catalog);return layout;
            }
            foreach(var sample in new[]{(6,256),(9,320),(12,384),(15,512)}) {
                for(int seed=73100;seed<73200;seed++) {
                    var a=Raw(seed,sample.Item1,sample.Item2);
                    var before=a.places.Select(p=>new{p.patch,p.radius,p.rotation,p.height,p.kind}).ToArray();
                    a.ScatterCombatPlaces(library.catalog);Check(a,library.catalog,sample.Item1);
                    if(!before.SequenceEqual(a.places.Select(p=>new{p.patch,p.radius,p.rotation,p.height,p.kind})))throw new Exception("Scattering changed tile contents or heights.");
                    var b=Raw(seed,sample.Item1,sample.Item2);b.ScatterCombatPlaces(library.catalog);
                    if(Plan(a)!=Plan(b))throw new Exception("Seed is not repeatable.");cases++;
                }
                var clock=System.Diagnostics.Stopwatch.StartNew();
                var plan=MojaveMapGeneratorWindow.BuildLayoutPreview(73135,sample.Item2,sample.Item1,true,true,true,true,out var catalog,true);
                Check(plan,catalog,sample.Item1);fullPreviews.Add(new{areas=sample.Item1,metres=sample.Item2,seconds=clock.Elapsed.TotalSeconds,roads=plan.trails.Count});
            }
            tiles.mixedSizes=false;var crowded=Raw(73135,15,256);string previous=Plan(crowded);bool rejected=false;
            try{crowded.ScatterCombatPlaces(library.catalog);}catch(InvalidOperationException){rejected=true;}
            if(!rejected||previous!=Plan(crowded))throw new Exception("Infeasible layout was not rejected without modifying its previous plan.");
            var large=Raw(73135,12,512);large.ScatterCombatPlaces(library.catalog);Check(large,library.catalog,12);
            tiles.mixedSizes=true;
            var first=Raw(73135,12,384);first.ScatterCombatPlaces(library.catalog);var second=Raw(73136,12,384);second.ScatterCombatPlaces(library.catalog);
            if(Plan(first)==Plan(second))throw new Exception("Different seeds produced the same placement.");
            return JsonConvert.SerializeObject(new{status="PASS_COMBAT_LAYOUT",cases,repeatable=true,contentsPreserved=true,noOverlap=true,inBounds=true,connected=true,infeasiblePlanUnchanged=true,fullPreviews},Formatting.Indented);
        } finally {if(owner!=null)UnityEngine.Object.DestroyImmediate(owner);EditorSceneManager.ClosePreviewScene(scene);}
    }
    static void Check(MojaveLayout plan,MojaveCatalog catalog,int count)
    {
        if(plan.places.Count!=count)throw new Exception("Combat count changed.");
        var reached=new HashSet<int>{0};
        for(int pass=0;pass<count;pass++)foreach(var t in plan.trails) {if(reached.Contains(t.from))reached.Add(t.to);if(reached.Contains(t.to))reached.Add(t.from);}
        if(reached.Count!=count)throw new Exception("Disconnected combat tile.");
        float Radius(MojavePlace p)=>Mathf.Max(catalog.patches[p.patch].size*.707107f,Mathf.Max(p.radius.x,p.radius.y)*1.28f);
        for(int i=0;i<count;i++) {
            var a=plan.places[i];float radius=Radius(a);
            if(Mathf.Max(Mathf.Abs(a.center.x),Mathf.Abs(a.center.y))+radius>plan.extent*.5f-8+.001f)throw new Exception("Tile exceeds map boundary.");
            for(int j=i+1;j<count;j++)if(Vector2.Distance(a.center,plan.places[j].center)<radius+Radius(plan.places[j])+6-.001f)throw new Exception("Overlapping tile footprints.");
        }
        foreach(var t in plan.trails)foreach(var p in t.points)if(float.IsNaN(p.x)||float.IsNaN(p.y)||float.IsInfinity(p.x)||float.IsInfinity(p.y))throw new Exception("Invalid road position.");
    }
    static string Plan(MojaveLayout layout)=>JsonConvert.SerializeObject(new{places=layout.places.Select(p=>new{center=new[]{p.center.x,p.center.y},p.patch,radius=new[]{p.radius.x,p.radius.y},p.rotation,p.height,p.kind}),trails=layout.trails.Select(t=>new{t.from,t.to,points=t.points.Select(p=>new[]{p.x,p.y}),t.widths,t.heights})});
}
