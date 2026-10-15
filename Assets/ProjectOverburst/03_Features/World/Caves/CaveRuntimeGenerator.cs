using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.AI.Navigation;
using Object = UnityEngine.Object;

namespace Overburst.Caves
{
    [RequireComponent(typeof(CaveWorld))]
    public sealed class CaveRuntimeGenerator : MonoBehaviour
    {
        public CaveGenerationAssets assets;
        public bool IsReady { get; private set; }
        public string Stage { get; private set; }
        readonly List<Object> owned = new List<Object>();
        public void Own(Object asset) { if (asset) owned.Add(asset); }
        void OnDestroy()
        {
            foreach(var asset in owned) if(asset)
            { if(asset is NavMeshData data) NavMeshBuilder.Cancel(data); Destroy(asset); }
            owned.Clear();
        }

        public static int SeedFor(string runId)
        {
            unchecked { uint hash = 2166136261; foreach(char c in runId) hash = (hash ^ c) * 16777619; return (int)hash; }
        }

        public IEnumerator Generate(int count, int seed)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Live cave generation requires Play Mode.");
            if (!assets || !assets.library || !assets.terrain || !assets.lighting) throw new InvalidOperationException("Cave generation assets are missing.");
            var world = GetComponent<CaveWorld>();
            if (world.generatedRoot) throw new InvalidOperationException("Generate into an empty cave world.");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            world.authoredLayout = true; world.seed = seed; world.combatCount = count; world.catalog = assets.catalog;
            CaveFallProtection.Unregister(world); IsReady = false;
            var library = assets.library;
            Stage = "플랫폼 연결 계산";
            CavePlatformLayout.Layout plan = null;
            var probes = SceneManager.CreateScene("Cave layout probes " + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var colliders = new Dictionary<CavePlatformLibrary.Platform, Collider[]>();
            try
            {
                var probe = new GameObject("Connection clearance probe"); SceneManager.MoveGameObjectToScene(probe, probes);
                var capsule = probe.AddComponent<CapsuleCollider>(); capsule.height=1.8f; capsule.radius=.32f; capsule.center=Vector3.up*.95f;
                foreach(var platform in library.platforms)
                {
                    if (TryCreateObstacleProbe(platform.prefab, probes, out var obstacles))
                    {
                        colliders[platform] = obstacles;
                        yield return null;
                        continue;
                    }
                    var clone=Instantiate(platform.prefab); SceneManager.MoveGameObjectToScene(clone,probes);
                    foreach(var renderer in clone.GetComponentsInChildren<Renderer>()) renderer.enabled=false;
                    foreach(var light in clone.GetComponentsInChildren<Light>()) light.enabled=false;
                    var floor=clone.transform.Find("Platform");
                    colliders[platform]=clone.GetComponentsInChildren<Collider>(true).Where(c=>c.enabled&&!c.isTrigger&&!c.transform.IsChildOf(floor)).ToArray();
                    yield return null;
                }
                Physics.SyncTransforms();
                var obstacleProbes = colliders.ToDictionary(pair => pair.Key,
                    pair => pair.Value.Select(c => (collider: c, bounds: c.bounds, position: c.transform.position, rotation: c.transform.rotation)).ToArray());
                bool Blocked(CavePlatformLibrary.Platform platform, Vector3 floor)
                {
                    foreach(var obstacle in obstacleProbes[platform])
                    {
                        var b=obstacle.bounds;
                        if(b.max.y<=floor.y+.5f||b.min.y>=floor.y+1.85f||floor.x<b.min.x-.4f||floor.x>b.max.x+.4f||floor.z<b.min.z-.4f||floor.z>b.max.z+.4f) continue;
                        if(Physics.ComputePenetration(capsule,floor,Quaternion.identity,obstacle.collider,obstacle.position,obstacle.rotation,out _,out float depth)&&depth>.015f) return true;
                    }
                    return false;
                }
                var search=CavePlatformLayout.Solve(library,count,seed,1,Blocked,value=>plan=value,
                    attempt=>Stage=attempt==1 ? "플랫폼 연결 계산" : "플랫폼 연결 재생성 " + attempt + "/" + CavePlatformLayout.MaxLayoutAttempts);
                try { while(search.MoveNext()) yield return search.Current; }
                finally { (search as IDisposable)?.Dispose(); }
            }
            finally
            {
                if(probes.IsValid()&&probes.isLoaded)
                {
                    foreach(var probe in probes.GetRootGameObjects()) { probe.SetActive(false); Destroy(probe); }
                    SceneManager.UnloadSceneAsync(probes);
                }
            }
            Stage = "플랫폼과 통로 배치";
            world.generatedRoot=new GameObject("Live platforms and connections").transform; world.generatedRoot.SetParent(transform,false);
            for(int i=0;i<plan.rooms.Count;i++)
            {
                var room=plan.rooms[i]; var definition=library.platforms[room.platform];
                var go=Instantiate(definition.prefab,world.generatedRoot); go.name="Area "+(i+1)+" · "+definition.source.name;
                var rotation=Quaternion.Euler(0,room.yaw,0); go.transform.SetPositionAndRotation(room.position,rotation);
                world.courts.Add(new CaveWorld.Court {name=go.name,center=room.position+rotation*definition.spawn,radius=go.GetComponent<CaveTile>().combatRadius,tile=go.GetComponent<CaveTile>()});
                yield return null;
            }
            foreach(var link in plan.links) { Connect(world,library,link); yield return null; }
            int ground=LayerMask.NameToLayer("Ground");
            foreach(var collider in world.generatedRoot.GetComponentsInChildren<Collider>(true))
                if(collider.GetComponentInParent<CaveWalkSurface>()) collider.gameObject.layer=ground;
            var renderers=world.generatedRoot.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
            var bounds=renderers[0].bounds; foreach(var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            world.mapSize=Mathf.Max(bounds.size.x,bounds.size.z)+40;
            Physics.SyncTransforms();
            Stage = "이동 영역 계산";
            var surface=gameObject.AddComponent<NavMeshSurface>(); surface.collectObjects=CollectObjects.Children;
            surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders; surface.overrideVoxelSize=true; surface.voxelSize=.12f; surface.overrideTileSize=true; surface.tileSize=128;
            var navigation=new NavMeshData(surface.agentTypeID); Own(navigation);
            var sources=new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(world.generatedRoot,~0,NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
            var settings=surface.GetBuildSettings();
            settings.agentRadius=.32f; settings.agentClimb=1.2f;
            bounds.Expand(4);
            yield return NavMeshBuilder.UpdateNavMeshDataAsync(navigation,settings,sources,bounds);
            surface.navMeshData=navigation; surface.AddData();
            foreach(var court in world.courts)
            {
                if (!MapCombatGeometry.TryNavigationCenter(world, court, out var center))
                    throw new InvalidOperationException("플랫폼의 연결된 전투 바닥을 찾지 못했습니다: " + court.name);
                court.center = center;
            }
            // Native stair treads can leave tiny navigation seams even when actors can step across.
            // Join only installed connectors, using contacts on the two authored platform floors.
            foreach(var connection in world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>())
            {
                var direction=connection.end-connection.start; direction.y=0; direction.Normalize();
                var a=connection.start-direction*1.2f; var b=connection.end+direction*1.2f;
                var courtA=world.courts.First(c=>c.tile.GetComponent<CavePlatformBoundary>()==connection.boundaryA);
                var courtB=world.courts.First(c=>c.tile.GetComponent<CavePlatformBoundary>()==connection.boundaryB);
                if(!NavigationContact(courtA,connection.boundaryA,a,out var start)||!NavigationContact(courtB,connection.boundaryB,b,out var end))
                    throw new InvalidOperationException("다리 이동 경로의 접점을 찾지 못했습니다: "+connection.name);
                var link=connection.gameObject.AddComponent<NavMeshLink>(); link.agentTypeID=surface.agentTypeID;
                link.startPoint=connection.transform.InverseTransformPoint(start);
                link.endPoint=connection.transform.InverseTransformPoint(end);
                link.width=0; link.bidirectional=true; link.UpdateLink();
            }
            yield return null;
            var route=new NavMeshPath();
            foreach(var court in world.courts)
                if(!NavMesh.CalculatePath(world.courts[0].center,court.center,NavMesh.AllAreas,route)||route.status!=NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("플랫폼 이동 경로가 끊겼습니다: "+court.name+" / seed "+seed);
            Stage = "하부 지형과 장식 배치";
            var dressing=CaveRuntimeDressing.Build(world,assets,this);
            try { while(dressing.MoveNext()) yield return dressing.Current; }
            finally { (dressing as IDisposable)?.Dispose(); }
            Physics.SyncTransforms();
            CaveFallProtection.Register(world);
            world.generationMilliseconds=timer.ElapsedMilliseconds;
            IsReady=true; Stage="준비 완료";
        }

        static bool TryCreateObstacleProbe(GameObject prefab, Scene scene, out Collider[] obstacles)
        {
            obstacles = null;
            if (!prefab || prefab.transform.parent || prefab.scene.IsValid()) return false;
            var floor = prefab.transform.Find("Platform");
            if (!floor) return false;
            // These components cannot change the static shapes during the layout probe's lifetime.
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (!component) return false;
                if (component is ParticleSystem particles
                    && (particles.main.stopAction != ParticleSystemStopAction.None || particles.subEmitters.enabled)) return false;
                var type = component.GetType();
                if (type != typeof(Transform) && type != typeof(MeshFilter) && type != typeof(MeshRenderer)
                    && type != typeof(ParticleSystem) && type != typeof(ParticleSystemRenderer) && type != typeof(Light)
                    && type != typeof(UnityEngine.Rendering.Universal.UniversalAdditionalLightData)
                    && type != typeof(CaveTile) && type != typeof(CaveWalkSurface)
                    && type != typeof(CavePlatformBoundary) && type != typeof(CaveConnectionGuide)
                    && type != typeof(BoxCollider) && type != typeof(SphereCollider)
                    && type != typeof(CapsuleCollider) && type != typeof(MeshCollider)) return false;
            }
            var source = prefab.GetComponentsInChildren<Collider>(true)
                .Where(c => c.enabled && !c.isTrigger && !c.transform.IsChildOf(floor)).ToArray();
            var required = new HashSet<Transform> { prefab.transform };
            foreach (var collider in source)
            {
                // ponytail: unreadable meshes keep the original clone path; no new cooking assumptions.
                if (collider is MeshCollider mesh && (!mesh.sharedMesh || !mesh.sharedMesh.isReadable)) return false;
                for (var node = collider.transform; node; node = node.parent)
                {
                    var scale = node.localScale;
                    if (!float.IsFinite(scale.x) || !float.IsFinite(scale.y) || !float.IsFinite(scale.z)
                        || scale.x == 0 || scale.y == 0 || scale.z == 0) return false;
                    required.Add(node);
                }
            }
            GameObject root = null;
            bool complete = false;
            try
            {
                root = new GameObject(prefab.name + " · collision probe"); root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                var copies = new Dictionary<Transform, Transform>();
                void CopyTree(Transform original, Transform copy)
                {
                    copies.Add(original, copy);
                    copy.localPosition = original.localPosition; copy.localRotation = original.localRotation;
                    copy.localScale = original.localScale; copy.gameObject.layer = original.gameObject.layer;
                    foreach (Transform child in original)
                    {
                        if (!required.Contains(child)) continue;
                        var next = new GameObject(child.name).transform; next.SetParent(copy, false);
                        next.gameObject.SetActive(child.gameObject.activeSelf); CopyTree(child, next);
                    }
                }
                CopyTree(prefab.transform, root.transform);
                var result = new Collider[source.Length];
                for (int i = 0; i < source.Length; i++)
                {
                    var original = source[i]; var target = copies[original.transform].gameObject;
                    Collider copy;
                    if (original is BoxCollider box)
                    { var shape = target.AddComponent<BoxCollider>(); shape.center = box.center; shape.size = box.size; copy = shape; }
                    else if (original is SphereCollider sphere)
                    { var shape = target.AddComponent<SphereCollider>(); shape.center = sphere.center; shape.radius = sphere.radius; copy = shape; }
                    else if (original is CapsuleCollider capsule)
                    {
                        var shape = target.AddComponent<CapsuleCollider>(); shape.direction = capsule.direction;
                        shape.center = capsule.center; shape.height = capsule.height; shape.radius = capsule.radius; copy = shape;
                    }
                    else
                    {
                        var mesh = (MeshCollider)original; var shape = target.AddComponent<MeshCollider>();
                        shape.cookingOptions = mesh.cookingOptions; shape.convex = mesh.convex;
                        shape.sharedMesh = mesh.sharedMesh; copy = shape;
                    }
                    copy.sharedMaterial = original.sharedMaterial; copy.contactOffset = original.contactOffset;
                    copy.isTrigger = original.isTrigger; copy.enabled = original.enabled;
                    result[i] = copy;
                }
                root.SetActive(prefab.activeSelf);
                obstacles = result; complete = true; return true;
            }
            catch (UnityException) { return false; }
            catch (ArgumentException) { return false; }
            finally
            {
                if (!complete && root) { root.SetActive(false); Destroy(root); }
            }
        }

        static bool FloorHit(MeshCollider[] floors, Vector3 near, out Vector3 point)
        {
            point=default; float highest=float.MinValue;
            foreach(var floor in floors)
                if(floor.Raycast(new Ray(new Vector3(near.x,near.y+10,near.z),Vector3.down),out var hit,30)&&hit.normal.y>.6f&&hit.point.y>highest)
                { highest=hit.point.y; point=hit.point; }
            return highest>float.MinValue;
        }
        static bool NavigationContact(CaveWorld.Court court, CavePlatformBoundary boundary, Vector3 near, out Vector3 point)
        {
            point=default; float best=float.MaxValue; var path=new NavMeshPath();
            // The nearest NavMesh point may be on an isolated plank/rock top. Use the court's connected floor.
            for(int z=-6;z<=6;z++) for(int x=-6;x<=6;x++)
            {
                var candidate=near+new Vector3(x*.5f,0,z*.5f);
                if(!NavMesh.SamplePosition(candidate,out var hit,.6f,NavMesh.AllAreas)||!boundary.Sample(hit.position,.5f,.5f,out _)) continue;
                float distance=(hit.position-near).sqrMagnitude;
                if(distance>=best||!NavMesh.CalculatePath(court.center,hit.position,NavMesh.AllAreas,path)||path.status!=NavMeshPathStatus.PathComplete) continue;
                best=distance; point=hit.position;
            }
            return best<float.MaxValue;
        }
        static CavePlatformLibrary.Connector[] ConnectorPieces(CavePlatformLibrary library,int count)=>CavePlatformLayout.ConnectorPieces(library,count);
    public static void Connect(CaveWorld world, CavePlatformLibrary library, CavePlatformLayout.Link link)
    {
        var root = new GameObject("Connection " + (link.a + 1) + "–" + (link.b + 1) + (link.stone ? " · Stairs " + link.stairCount : " · Timber fixed length"));
        root.transform.SetParent(world.generatedRoot, false); root.AddComponent<CaveWalkSurface>();
        var placed = root.AddComponent<CaveRigidConnection>(); placed.start = link.start; placed.end = link.end; placed.stairCount = link.stairCount;
        placed.boundaryA = world.courts[link.a].tile.GetComponent<CavePlatformBoundary>();
        placed.boundaryB = world.courts[link.b].tile.GetComponent<CavePlatformBoundary>();
        placed.portA = link.portA; placed.portB = link.portB;
        placed.walkWidth = ConnectorPieces(library, link.stairCount).Min(p => p.width);
        var pieces = new List<CaveRigidConnection.Piece>();
        var direction = link.end - link.start; direction.y = 0; direction.Normalize();
        var cursor = link.start;
        foreach (var piece in ConnectorPieces(library, link.stairCount))
        {
            bool reverse = link.stone && Mathf.Sign(piece.Span.y) != link.stairDirection;
            var entry = reverse ? piece.exit : piece.entry; var exit = reverse ? piece.entry : piece.exit;
            var scale = piece.prefab.transform.localScale;
            var span = Vector3.Scale(exit - entry, scale);
            var targetSpan = link.stone
                ? direction * new Vector2(span.x, span.z).magnitude + Vector3.up * span.y
                : (direction * Mathf.Cos(link.pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(link.pitch * Mathf.Deg2Rad)) * span.magnitude;
            var rotation = link.stone
                ? Quaternion.LookRotation(direction, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(new Vector3(span.x, 0, span.z), Vector3.up))
                : Quaternion.LookRotation(targetSpan.normalized, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(span.normalized, Vector3.up));
            var instance = Object.Instantiate(piece.prefab, root.transform);
            instance.transform.SetParent(root.transform, false);
            instance.transform.SetPositionAndRotation(cursor - rotation * Vector3.Scale(entry, scale), rotation);
            // Visible source meshes and scale stay intact; stairs get one continuous walk collider below.
            if (!instance.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && !c.isTrigger))
                foreach (var f in instance.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh && f.GetComponent<Renderer>() && f.GetComponent<Renderer>().enabled))
                    f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            pieces.Add(new CaveRigidConnection.Piece { source = piece.prefab, instance = instance.transform, localStart = entry, localEnd = exit });
            cursor = instance.transform.TransformPoint(exit);
        }
        if (Vector3.Distance(cursor, link.end) > .01f) throw new InvalidOperationException("Rigid connector endpoint did not match its planned dimensions.");
        placed.pieces = pieces.ToArray(); Physics.SyncTransforms();
        var floorsA = world.courts[link.a].tile.transform.Find("Platform").GetComponentsInChildren<MeshCollider>();
        var floorsB = world.courts[link.b].tile.transform.Find("Platform").GetComponentsInChildren<MeshCollider>();
        placed.startContactGap = FloorHit(floorsA, link.start, out var ga) ? Mathf.Abs(ga.y - link.start.y) : 1;
        placed.endContactGap = FloorHit(floorsB, link.end, out var gb) ? Mathf.Abs(gb.y - link.end.y) : Mathf.Max(1, link.reviewShortfall);
        if (link.stone) BuildStairWalkSurface(placed, library);
        placed.needsReview = link.reviewShortfall > 0 || placed.startContactGap > .25f || placed.endContactGap > .25f;
        if (placed.needsReview) root.name += " · REVIEW contact";
        int samples = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(link.start, link.end) / .3f));
        var points = new Vector3[samples + 1];
        for (int i = 0; i <= samples; i++)
        {
            var p = Vector3.Lerp(link.start, link.end, i / (float)samples);
            if (world.Ground(p, out var hit, 1.2f)) p = hit.point;
            points[i] = p;
        }
        world.passages.Add(new CaveWorld.Passage { a = link.a, b = link.b, points = points, widths = Enumerable.Repeat(ConnectorPieces(library, link.stairCount).Min(p => p.width), points.Length).ToArray() });
    }

        static void BuildStairWalkSurface(CaveRigidConnection connection, CavePlatformLibrary library)
        {
            var definitionA = library.platforms.First(p => p.source.GetComponent<CavePlatformBoundary>() == connection.boundaryA.Data);
            var definitionB = library.platforms.First(p => p.source.GetComponent<CavePlatformBoundary>() == connection.boundaryB.Data);
            float? Floor(bool fromA, Vector3 near)
            {
                var definition = fromA ? definitionA : definitionB;
                var pose = (fromA ? connection.boundaryA : connection.boundaryB).transform;
                if (!definition.Surface(pose.InverseTransformPoint(near), out var point)) return null;
                return pose.TransformPoint(point).y;
            }
            var deck = CavePlatformLayout.StairDeckPoints(library, connection.stairCount, connection.start, connection.end);
            var points = CavePlatformLayout.StairWalkPoints(connection.start, connection.end, connection.walkWidth, deck, Floor);
            if (points == null) throw new InvalidOperationException("Stair connection exceeds the player walk slope: " + connection.name);
            connection.CreateStairWalkSurface(points);
        }
    }
}
