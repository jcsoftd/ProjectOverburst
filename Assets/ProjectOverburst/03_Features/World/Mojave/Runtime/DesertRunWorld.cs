using System;
using System.Collections;
using System.Collections.Generic;
using Overburst.Persistence;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Overburst.Mojave
{
    [DisallowMultipleComponent, RequireComponent(typeof(MojaveWorld), typeof(RunWorldGate))]
    public sealed partial class DesertRunWorld : MonoBehaviour
    {
        public IReadOnlyList<Vector3> AreaCenters => centers;
        public string PreparationStage { get; private set; }
        public float PreparationSeconds { get; private set; }
        public int ConservativeObstacleCount { get; private set; }
        public DesertMinimapData MinimapData { get; private set; }
        public Transform TerrainRoot => world ? world.generatedRoot : null;
        readonly List<Vector3> centers = new List<Vector3>();
        RunWorldGate gate;
        MojaveWorld world;
        NavMeshSurface surface;
        NavMeshData navigation;
        IEnumerator preparation;
        bool started;
        Light gameplaySun;
        Quaternion originalSunRotation;
        float originalShadowStrength, lightingYaw = float.NaN;
        void LateUpdate()
        {
            var camera = QuarterViewCamera.ActiveInstance;
            if (!camera || WorldSessionState.ContentScene != gameObject.scene || WorldSessionState.Phase != WorldPhase.Run) return;
            if (!gameplaySun)
            {
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    foreach (var light in root.GetComponentsInChildren<Light>())
                        if (light.enabled && light.type == LightType.Directional && (!gameplaySun || light.intensity > gameplaySun.intensity)) gameplaySun = light;
                if (!gameplaySun) return;
                originalSunRotation = gameplaySun.transform.rotation; originalShadowStrength = gameplaySun.shadowStrength;
                gameplaySun.shadowStrength = Mathf.Min(originalShadowStrength, .68f);
            }
            if (Mathf.Approximately(lightingYaw, camera.CurrentYaw)) return;
            lightingYaw = camera.CurrentYaw;
            // Light comes from the camera-side lower left, above the floor, to illuminate visible actor faces.
            var source = Quaternion.Euler(0, lightingYaw, 0) * new Vector3(-.65f, 1.15f, -1f);
            gameplaySun.transform.rotation = Quaternion.LookRotation(-source);
        }
        void OnDisable()
        {
            ReleaseBoundary();
            if (gameplaySun) { gameplaySun.transform.rotation = originalSunRotation; gameplaySun.shadowStrength = originalShadowStrength; }
            gameplaySun = null; lightingYaw = float.NaN;
        }

        public static Vector3 EntryFor(MojaveWorld world)
        {
            world.EnsureLayout();
            foreach(var place in world.layout.places)
                if(place.role==MojaveTileRole.Entrance)return world.Ground(place.World(new Vector2(-2,-2)));
            var entrance = world.layout.places[0];
            // Keep the sole large/medium tile available for the boss instead of spawning the player there.
            foreach (var place in world.layout.places)
                if (world.catalog.patches[place.patch].combatSize > world.catalog.patches[entrance.patch].combatSize) entrance = place;
            return world.Ground(entrance.World(new Vector2(-2, -2)));
        }

        // Called by sceneLoaded before the authoring explorer's Start/input/camera callbacks.
        public static DesertRunWorld Install(Scene scene, MapInstanceState map)
        {
            if (!scene.IsValid() || !scene.isLoaded || scene.path != MapTerrainCatalog.SceneFor(map)
                || map.terrain != MapTerrainKind.Desert) throw new InvalidOperationException("사막 지도 목적지가 일치하지 않습니다.");
            MojaveWorld selected = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<MojaveWorld>(true))
                {
                    if (selected) throw new InvalidOperationException("사막 월드가 중복되었습니다.");
                    selected = candidate;
                }
                foreach (var explorer in root.GetComponentsInChildren<MojaveExplorer>(true)) explorer.gameObject.SetActive(false);
                foreach (var camera in root.GetComponentsInChildren<MojaveCamera>(true)) camera.gameObject.SetActive(false);
                foreach (var portal in root.GetComponentsInChildren<MojavePortal>(true)) portal.gameObject.SetActive(false);
            }
            if (!selected) throw new InvalidOperationException("저장된 사막 지형이 없습니다.");
            return selected.GetComponent<DesertRunWorld>() ?? selected.gameObject.AddComponent<DesertRunWorld>();
        }
        void Awake() { world = GetComponent<MojaveWorld>(); gate = GetComponent<RunWorldGate>(); }
        void Update()
        {
            if (started || gate.Context == null || gate.Error != null) return;
            started = true; preparation = Prepare(); StartCoroutine(PrepareSafely());
        }
        IEnumerator PrepareSafely()
        {
            float began = Time.realtimeSinceStartup;
            try
            {
                while (true)
                {
                    bool next;
                    try { next = preparation.MoveNext(); }
                    catch (Exception error) { gate.RejectPreparation(gate.Context.RunId, error.Message); Debug.LogException(error, this); yield break; }
                    if (!next) break;
                    yield return preparation.Current;
                }
            }
            finally { PreparationSeconds = Time.realtimeSinceStartup - began; (preparation as IDisposable)?.Dispose(); preparation = null; }
        }
        IEnumerator Prepare()
        {
            MapGenerationPolicy.Validate(gate.Map, false);
            var candidate = MapTerrainCatalog.Resolve(gate.Map.desertCandidateId);
            if (!world.generatedRoot || !world.surface || !world.surface.terrainData || world.bakedSeed != candidate.seed
                || world.seed != candidate.seed || world.bakedLayoutVersion != MojaveLayout.Version)
                throw new InvalidOperationException("사막 베이크 정보가 지도 후보와 다릅니다. 지도를 보존하고 입장을 취소합니다.");
            if (MapThemeCatalog.Resolve(gate.Map.monsterThemeId) == null)
                throw new InvalidOperationException("지도 몬스터 테마를 준비하지 못했습니다.");
            PreparationStage = "사막 전장 연결 확인";
            yield return null;
            world.EnsureLayout();
            MinimapData = DesertMinimapData.Load(candidate, world);
            boundary = new WalkBoundary(MinimapData);
            if (world.layout.places.Count != gate.Map.areaCount)
                throw new InvalidOperationException("사막 전장 수가 저장된 지도와 다릅니다.");
            int ground = LayerMask.NameToLayer("Ground");
            if (ground >= 0) world.surface.gameObject.layer = ground;
            Physics.SyncTransforms();
            PreparationStage = "사막 이동 영역 준비";
            surface = gameObject.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true; surface.voxelSize = .2f;
            surface.overrideTileSize = true; surface.tileSize = 128;
            navigation = new NavMeshData(surface.agentTypeID);
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(world.generatedRoot, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
            if (sources.Count == 0) throw new InvalidOperationException("사막 이동 지면이 없습니다.");
            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source.shape != NavMeshBuildSourceShape.Mesh || !(source.sourceObject is Mesh mesh) || mesh.isReadable) continue;
                // Imported rock meshes are CPU-inaccessible in Player. Keep their solid footprint
                // as a conservative obstacle; retain the original visual and physics collider.
                source.shape = NavMeshBuildSourceShape.Box;
                source.transform *= Matrix4x4.Translate(mesh.bounds.center);
                source.size = mesh.bounds.size; source.sourceObject = null; source.area = 1;
                sources[i] = source; ConservativeObstacleCount++;
            }
            var bounds = new Bounds(world.surface.transform.position + world.surface.terrainData.size * .5f, world.surface.terrainData.size);
            bounds.Expand(4);
            boundary.AppendNavigationSources(sources, bounds, surface.voxelSize);
            var settings = surface.GetBuildSettings(); settings.agentRadius = .4f; settings.agentHeight = 1.8f; settings.agentClimb = .6f;
            yield return NavMeshBuilder.UpdateNavMeshDataAsync(navigation, settings, sources, bounds);
            surface.navMeshData = navigation; surface.AddData();
            PreparationStage = "사막 입구와 전장 도달성 확인";
            if (!TryFloor(EntryFor(world), out Vector3 entry)) throw new InvalidOperationException("사막 입구에서 안전한 바닥을 찾지 못했습니다.");
            var path = new NavMeshPath();
            foreach (var place in world.layout.places)
            {
                if (!TryFloor(world.Ground(place.center), out var center)
                    || !NavMesh.CalculatePath(entry, center, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("사막 전장으로 이어지는 이동 경로가 없습니다: " + place.name);
                centers.Add(center); yield return null;
            }
            var point = new GameObject("Desert Run Entry").transform; point.SetParent(transform, false);
            point.position = entry + Vector3.up * .12f;
            PreparationStage = "전투 면적과 고정 몬스터 명단 준비";
            var roster = MapCombatGeometry.Prepare(gate, null, world, point.position);
            try { while (roster.MoveNext()) yield return roster.Current; }
            finally { (roster as IDisposable)?.Dispose(); }
            PreparationStage = "지형 준비 완료";
            if (!gate.CompletePreparation(gate.Context.RunId, point)) throw new InvalidOperationException("사막 준비 완료를 확정하지 못했습니다.");
            boundaryEntry = point.position; activeBoundary = this;
        }
        bool TryFloor(Vector3 desired, out Vector3 floor)
        {
            // Ground clearance and the authored walk mask reject roofs/background islands.
            for (int ring = 0; ring <= 8; ring++)
                for (int i = 0; i < (ring == 0 ? 1 : 16); i++)
                {
                    float angle = i * Mathf.PI / 8;
                    var p = new Vector2(desired.x + Mathf.Cos(angle) * ring, desired.z + Mathf.Sin(angle) * ring);
                    if (!world.IsWalkable(p, .65f)) continue;
                    var ground = world.Ground(p);
                    if (!NavMesh.SamplePosition(ground, out var hit, .6f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - ground.y) > .35f) continue;
                    if (!ContainsWalkableFootprint(hit.position, .4f)) continue;
                    floor = hit.position; return true;
                }
            floor = default; return false;
        }
        void OnDestroy()
        {
            ReleaseBoundary();
            StopAllCoroutines(); (preparation as IDisposable)?.Dispose(); preparation = null;
            if (surface) surface.RemoveData();
            if (navigation) { NavMeshBuilder.Cancel(navigation); Destroy(navigation); }
        }
    }
}
