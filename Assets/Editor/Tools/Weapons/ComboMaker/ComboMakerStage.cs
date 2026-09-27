using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Overburst.EditorTools.ComboMaker
{
    // Explicit scene ownership: no global RenderSettings or preview-lighting override stack.
    internal sealed class ComboMakerStage : IDisposable
    {
        private Scene scene;
        private VolumeProfile profile;
        public Camera camera { get; private set; }
        public Light[] lights { get; private set; }
        public ComboMakerStage()
        {
            scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var cameraRoot=Root("Combo Maker Camera");
                camera=cameraRoot.AddComponent<Camera>();camera.enabled=false;camera.cameraType=CameraType.Game;
                camera.scene=scene;camera.useOcclusionCulling=false;
                camera.allowHDR=true;
                var volumeRoot=Root("Combo Maker Bloom");volumeRoot.layer=31;
                var volume=volumeRoot.AddComponent<Volume>();volume.isGlobal=true;
                profile=ScriptableObject.CreateInstance<VolumeProfile>();volume.sharedProfile=profile;
                var bloom=profile.Add<Bloom>(true);
                bloom.threshold.Override(.7f);bloom.intensity.Override(1.2f);bloom.scatter.Override(.65f);
                lights=new Light[2];
                for(int i=0;i<lights.Length;i++)
                {
                    lights[i]=Root("Combo Maker Light "+i).AddComponent<Light>();
                    lights[i].type=LightType.Directional;lights[i].shadows=LightShadows.None;
                }
            }
            catch {Dispose();throw;}
        }
        private GameObject Root(string name)
        {
            var root=new GameObject(name){hideFlags=HideFlags.HideAndDontSave};
            SceneManager.MoveGameObjectToScene(root,scene);return root;
        }
        public void AddSingleGO(GameObject root)=>SceneManager.MoveGameObjectToScene(root,scene);
        public void Cleanup()=>Dispose();
        public void Dispose()
        {
            if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);
            if(profile!=null)UnityEngine.Object.DestroyImmediate(profile);profile=null;
            scene=default;camera=null;lights=Array.Empty<Light>();
        }
    }
}
