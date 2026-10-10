using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Overburst.Caves
{
    [RequireComponent(typeof(RunWorldGate), typeof(CaveWorld))]
    public sealed class CaveRunWorld : MonoBehaviour
    {
        public Transform entryPoint;
        RunWorldGate gate;
        bool prepared, cameraConfigured, priorPost;
        UniversalAdditionalCameraData cameraData;
        IEnumerator generation;
        void Awake() => gate = GetComponent<RunWorldGate>();
        void Update()
        {
            if (!prepared && gate.Context != null)
            {
                prepared = true;
                var world = GetComponent<CaveWorld>();
                var generator = GetComponent<CaveRuntimeGenerator>();
                if (generator)
                {
                    generation = generator.Generate(world.combatCount, CaveRuntimeGenerator.SeedFor(gate.Context.RunId));
                    StartCoroutine(PrepareLive(world));
                }
                else
                {
                Physics.SyncTransforms();
                if (!world.authoredLayout || world.courts.Count < 2 || !entryPoint || !world.Ground(entryPoint.position, out _))
                    gate.RejectPreparation(gate.Context.RunId, "동굴 입구의 발판을 확인하지 못했습니다.");
                else gate.CompletePreparation(gate.Context.RunId, entryPoint);
                }
            }
            if (!cameraConfigured && WorldSessionState.Phase == WorldPhase.Run && WorldSessionState.ContentScene == gameObject.scene)
            {
                cameraConfigured = true;
                var camera = QuarterViewCamera.ActiveInstance?.GetComponent<Camera>();
                if (camera)
                {
                    cameraData = camera.GetUniversalAdditionalCameraData();
                    priorPost = cameraData.renderPostProcessing; cameraData.renderPostProcessing = true;
                }
                WorldMinimapController.Instance?.ShowForScene(PlayerContext.Instance.CurrentActor.transform, gameObject.scene.handle);
            }
        }
        IEnumerator PrepareLive(CaveWorld world)
        {
            try
            {
                while (true)
                {
                    bool next;
                    try { next = generation.MoveNext(); }
                    catch (Exception error) { gate.RejectPreparation(gate.Context.RunId, error.Message); Debug.LogException(error); yield break; }
                    if (!next) break;
                    yield return generation.Current;
                }
                entryPoint = new GameObject("Cave Run Entry").transform; entryPoint.SetParent(transform, false);
                entryPoint.position = world.courts[0].center + Vector3.up * .12f;
                var portal = new GameObject("Cave Return Portal").AddComponent<CaveDungeonPortal>();
                portal.transform.SetParent(transform, false); portal.returnToTown = true;
                var near = entryPoint.position + Vector3.right * 3;
                portal.transform.position = world.Ground(near, out var floor) ? floor.point + Vector3.up * .05f : entryPoint.position;
                if (!world.Ground(entryPoint.position, out _)) gate.RejectPreparation(gate.Context.RunId, "동굴 입구의 발판을 확인하지 못했습니다.");
                else gate.CompletePreparation(gate.Context.RunId, entryPoint);
            }
            finally { (generation as IDisposable)?.Dispose(); generation = null; }
        }
        void OnDisable()
        {
            StopAllCoroutines(); (generation as IDisposable)?.Dispose(); generation = null;
            if (cameraConfigured && cameraData) cameraData.renderPostProcessing = priorPost;
        }
    }
}
