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
        void Awake() => gate = GetComponent<RunWorldGate>();
        void Update()
        {
            if (!prepared && gate.Context != null)
            {
                prepared = true;
                var world = GetComponent<CaveWorld>();
                Physics.SyncTransforms();
                if (!world.authoredLayout || world.courts.Count < 2 || !entryPoint || !world.Ground(entryPoint.position, out _))
                    gate.RejectPreparation(gate.Context.RunId, "동굴 입구의 발판을 확인하지 못했습니다.");
                else gate.CompletePreparation(gate.Context.RunId, entryPoint);
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
        void OnDisable() { if (cameraConfigured && cameraData) cameraData.renderPostProcessing = priorPost; }
    }
}
