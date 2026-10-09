using System;
using UnityEngine;

namespace Overburst.Mojave
{
    public static class VolcanoEffectsBuilder
    {
        public static void Build(MojaveWorld world)
        {
            var root=new GameObject("Volcanic air · native ash and lava embers").transform;root.SetParent(world.generatedRoot,false);
            if(world.catalog.ashPrefab!=null) {
                var ash=UnityEngine.Object.Instantiate(world.catalog.ashPrefab,root);ash.name="Native drifting ash";
                ash.transform.position=new Vector3(0,24,0);ash.transform.rotation=Quaternion.identity;ash.transform.localScale=Vector3.one;
                foreach(var particles in ash.GetComponentsInChildren<ParticleSystem>(true)) {
                    particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=particles.main;
                    main.simulationSpace=ParticleSystemSimulationSpace.World;main.maxParticles=1800;main.startLifetime=new ParticleSystem.MinMaxCurve(10,18);main.startSpeed=new ParticleSystem.MinMaxCurve(.18f,.55f);
                    var shape=particles.shape;shape.enabled=true;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(240,8,240);
                    var emission=particles.emission;emission.rateOverTime=50;
                    var velocity=particles.velocityOverLifetime;velocity.enabled=true;velocity.space=ParticleSystemSimulationSpace.World;
                    velocity.x=new ParticleSystem.MinMaxCurve(.20f,.48f);velocity.y=new ParticleSystem.MinMaxCurve(-.75f,-.42f);velocity.z=new ParticleSystem.MinMaxCurve(.08f,.25f);
                    particles.useAutoRandomSeed=false;particles.randomSeed=unchecked((uint)world.seed)+17;
                    particles.Simulate(8,false,true,true);particles.Play();
                }
                foreach(var component in ash.GetComponentsInChildren<MonoBehaviour>(true))if(component!=null&&component.GetType().Name=="TerrainParticleFollow") {
                    var field=component.GetType().GetField("terrain");field?.SetValue(component,world.surface);
                }
            }
            int index=0;
            foreach(var filter in world.generatedRoot.GetComponentsInChildren<MeshFilter>(true)) {
                if(filter.sharedMesh==null||!filter.name.StartsWith("Volcano authored lava",StringComparison.Ordinal))continue;
                var bounds=filter.sharedMesh.bounds;var position=bounds.center+Vector3.up*.6f;
                if(world.catalog.lavaSparkPrefab!=null) {
                    var fx=UnityEngine.Object.Instantiate(world.catalog.lavaSparkPrefab,root);fx.name="Native lava embers · "+(++index);
                    fx.transform.position=position;fx.transform.localScale=Vector3.one*.65f;
                    foreach(var particles in fx.GetComponentsInChildren<ParticleSystem>(true)) {
                        particles.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);var main=particles.main;main.maxParticles=120;
                        var shape=particles.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(Mathf.Min(20,bounds.size.x),.1f,Mathf.Min(20,bounds.size.z));
                        var emission=particles.emission;emission.rateOverTime=9;
                        particles.useAutoRandomSeed=false;particles.randomSeed=unchecked((uint)world.seed)+(uint)(31+index);
                        particles.Simulate(2,false,true,true);particles.Play();
                    }
                }
                var light=new GameObject("Lava bounce light").AddComponent<Light>();light.transform.SetParent(root,false);light.transform.position=position;
                light.type=LightType.Point;light.color=new Color(1,.23f,.035f);light.intensity=3.2f;light.range=Mathf.Clamp(Mathf.Max(bounds.size.x,bounds.size.z)*.55f,9,21);light.shadows=LightShadows.None;
            }
        }
    }
}
