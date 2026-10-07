using System;
using System.Collections.Generic;
using Overburst.Persistence;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Overburst.Appearance
{
    [DisallowMultipleComponent]
    public sealed class AppearanceCharacterPreview : MonoBehaviour, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IScrollHandler
    {
        public const int PreviewLayer=30;
        [SerializeField] private RawImage target;
        [SerializeField,Range(0,35)] private float fullBodyCameraTilt=18f;
        [SerializeField,Range(0,35)] private float upperBodyCameraTilt=14f;
        [SerializeField,Range(0,35)] private float faceCameraTilt=10f;
        private CharacterAppearanceCatalog catalog;
        private AppearancePreviewQuality quality;
        private AppearanceCustomizationSession session;
        private GameObject rig, model, expressionSampler;
        private readonly List<(SkinnedMeshRenderer target,SkinnedMeshRenderer sample)> expressionParts=new List<(SkinnedMeshRenderer,SkinnedMeshRenderer)>();
        private AnimationClip sampledExpression;
        private float[][] expressionWeights;
        private Camera viewCamera;
        private Animator animator;
        private ParentConstraint[] headAttachments;
        private AppearancePreviewPhysics physics;
        private RenderTexture texture;
        private P09AppearanceApplier applier;
        private readonly Dictionary<Material,Material> materials=new Dictionary<Material,Material>();
        private PlayableGraph graph;
        private AnimationClipPlayable clipPlayable;
        private AnimationClip activeClip;
        private float yaw, zoom=1f, speed=1f;
        private bool dragging, paused, looping=true, comparing;
        private float animationHeight,motionDistance;
        private Quaternion animationRotation=Quaternion.identity;
        private bool seekingMotion;
        private Transform[] framingBones=Array.Empty<Transform>();
        public float AnimationHeight=>animationHeight;
        public Quaternion AnimationRotation=>animationRotation;
        private bool HasMotionRoot=>activeClip&&activeClip.isHumanMotion&&activeClip!=catalog.idleClip;
        private Vector3 modelPosition;
        private Quaternion modelRotation;
        private double nextResize;
        public AnimationClip ActiveClip=>activeClip;
        public bool IsPaused=>paused;
        public float PlaybackTime=>clipPlayable.IsValid()?(float)clipPlayable.GetTime():0;
        public float PlaybackSpeed=>speed;
        public bool Looping=>looping;
        public RenderTexture Texture=>texture;
        public GameObject Model=>model;

        public void Open(CharacterAppearanceCatalog source, AppearanceCustomizationSession draft,RawImage image)
        {
            Close();
            catalog=source?source:throw new ArgumentNullException(nameof(source));
            session=draft??throw new ArgumentNullException(nameof(draft));target=image?image:target;
            quality=Resources.Load<AppearancePreviewQuality>(AppearancePreviewQuality.ResourcePath);
            if(!target || !catalog.visualPrefab) throw new InvalidOperationException("캐릭터 프리뷰 자산이 없습니다.");
            try
            {
                rig=new GameObject("Appearance Preview Rig"){hideFlags=HideFlags.DontSave};
                rig.transform.position=new Vector3(512,512,512);
                // Source prefab is animation-only; never clone the gameplay actor.
                model=Instantiate(catalog.visualPrefab,rig.transform,false);
                model.name="Appearance Preview Model";
                model.transform.localPosition=Vector3.zero;
                model.transform.localRotation=Quaternion.identity;
                model.transform.localScale=Vector3.one;
                foreach(var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=PreviewLayer;
                animator=model.GetComponentInChildren<Animator>(true);
                if(!animator || !animator.avatar || !animator.avatar.isValid)
                    throw new InvalidOperationException("프리뷰 Avatar가 올바르지 않습니다.");
                animator.runtimeAnimatorController=null;animator.applyRootMotion=false;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    skin.quality=SkinQuality.Bone4;skin.updateWhenOffscreen=true;
                    skin.forceMatrixRecalculationPerRender=true;
                    skin.shadowCastingMode=ShadowCastingMode.On;skin.receiveShadows=true;
                }
                headAttachments=Array.FindAll(model.GetComponentsInChildren<ParentConstraint>(true),c=>c.transform.name=="Constraint_Head"&&c.sourceCount==1);
                applier=new P09AppearanceApplier(model.transform,catalog,OwnMaterial);
                applier.ApplyPreviewBody(session.Draft,session.PreviewBody,session.EquipmentExampleId,session.HeadgearVisible);
                model.AddComponent<AppearancePreviewMotionRoot>().Initialize(this,animator);
                CacheFramingBones();
                physics=rig.AddComponent<AppearancePreviewPhysics>();physics.Initialize(this,model);
                CreateCameraAndLights();
                if(quality&&quality.contactShadowMaterial)
                {
                    var shadow=GameObject.CreatePrimitive(PrimitiveType.Quad);shadow.name="Feet Contact Shadow";
                    shadow.transform.SetParent(rig.transform,false);shadow.transform.localPosition=new Vector3(0,.004f,0);
                    shadow.transform.localRotation=Quaternion.Euler(90,0,0);shadow.transform.localScale=new Vector3(.72f,.42f,1);
                    shadow.layer=PreviewLayer;ReleaseObject(shadow.GetComponent<Collider>());
                    var renderer=shadow.GetComponent<MeshRenderer>();renderer.sharedMaterial=OwnMaterial(quality.contactShadowMaterial);
                    renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                }
                yaw=180f;zoom=1f;speed=1f;paused=false;looping=true;comparing=false;
                modelPosition=model.transform.localPosition;
                modelRotation=model.transform.localRotation;
                Play(catalog.idleClip,true);
                session.Changed+=ApplyDraft;
                target.raycastTarget=true;
                ResizeTexture();FrameCamera();RenderNow();
            }
            catch {Close();throw;}
        }

        private Material OwnMaterial(Material original)
        {
            if(!original)return null;
            if(materials.TryGetValue(original,out var own))return own;
            own=new Material(original){name=original.name+"_AppearancePreview",hideFlags=HideFlags.DontSave};
            // Neutral, dedicated preview shading. Shared gameplay/vendor materials stay immutable.
            SetFloat(own,"_MonochromeLighting",0f);
            SetFloat(own,"_VertexLightStrength",.55f);
            SetFloat(own,"_LightMinLimit",.12f);
            SetFloat(own,"_LightMaxLimit",1f);
            SetFloat(own,"_AsUnlit",0f);
            SetFloat(own,"_OutlineWidth",0f);SetFloat(own,"_UseRim",0f);SetFloat(own,"_UseBacklight",0f);
            SetFloat(own,"_ShadowBlur",.75f);SetFloat(own,"_ShadowStrength",.60f);
            if(original.name.Contains("Skin")){SetFloat(own,"_LightMinLimit",.30f);SetFloat(own,"_ShadowStrength",.35f);}
            if(own.HasProperty("_ShadowColor"))own.SetColor("_ShadowColor",new Color(.70f,.57f,.52f,1));
            if(quality)foreach(var property in own.GetTexturePropertyNames())own.SetTexture(property,quality.Resolve(own.GetTexture(property)));
            materials.Add(original,own);return own;
        }
        private static void SetFloat(Material m,string property,float value)
        {if(m.HasProperty(property))m.SetFloat(property,value);}

        private void CreateCameraAndLights()
        {
            var cameraObject=new GameObject("Appearance Preview Camera");cameraObject.transform.SetParent(rig.transform,false);
            viewCamera=cameraObject.AddComponent<Camera>();viewCamera.enabled=false;
            viewCamera.scene=rig.scene;
            viewCamera.cullingMask=1<<PreviewLayer;
            viewCamera.clearFlags=CameraClearFlags.SolidColor;viewCamera.backgroundColor=Color.clear;
            viewCamera.nearClipPlane=.1f;viewCamera.farClipPlane=20f;viewCamera.fieldOfView=25f;
            viewCamera.allowHDR=true;viewCamera.allowMSAA=true;
            var data=viewCamera.GetUniversalAdditionalCameraData();
            data.renderShadows=true;data.renderPostProcessing=false;
            data.volumeLayerMask=0;
            // This transparent camera uses 2x supersampling and MSAA. URP post-AA
            // would require global alpha-output changes; keep the preview composite intact.
            data.antialiasing=AntialiasingMode.None;
            AddLight("Beauty Key",new Vector3(1.8f,2.35f,-3.1f),new Color(1f,.96f,.91f),1.1f);
            AddLight("Beauty Fill",new Vector3(-.8f,1.55f,-1.8f),new Color(1f,.97f,.94f),3.2f);
            AddLight("Warm Rim",new Vector3(-.6f,2.5f,2f),new Color(1f,.90f,.82f),7f);
        }
        private void AddLight(string name,Vector3 position,Color color,float intensity)
        {
            var go=new GameObject(name);go.transform.SetParent(rig.transform,false);
            go.transform.localPosition=position;
            var light=go.AddComponent<Light>();light.type=name=="Beauty Key"?LightType.Directional:LightType.Point;
            light.color=color;light.intensity=intensity;light.range=6f;
            light.cullingMask=1<<PreviewLayer;light.shadows=name=="Beauty Key"?LightShadows.Soft:LightShadows.None;
            if(light.type==LightType.Directional)go.transform.LookAt(rig.transform.position+Vector3.up*1.25f);
            if(GraphicsSettings.currentRenderPipeline==null)light.shadowResolution=LightShadowResolution.VeryHigh;
            else light.GetUniversalAdditionalLightData().softShadowQuality=SoftShadowQuality.High;
        }

        private void ResizeTexture()
        {
            if(!viewCamera || !target)return;
            int displayWidth=Screen.width>0?Screen.width:1920,displayHeight=Screen.height>0?Screen.height:1080;
            float scale=Mathf.Min(2f,3840f/displayWidth,2160f/displayHeight);
            int width=Mathf.Max(1,Mathf.RoundToInt(displayWidth*scale));
            int height=Mathf.Max(1,Mathf.RoundToInt(displayHeight*scale));
            if(texture && texture.width==width && texture.height==height)return;
            if(texture){target.texture=null;viewCamera.targetTexture=null;texture.Release();ReleaseObject(texture);}
            texture=new RenderTexture(width,height,24,SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)?RenderTextureFormat.ARGBHalf:RenderTextureFormat.ARGB32)
            {
                name="Appearance Preview Fullscreen",hideFlags=HideFlags.DontSave,
                antiAliasing=SystemInfo.supportsMultisampledTextures!=0?4:1,
                filterMode=FilterMode.Bilinear,useMipMap=false
            };
            texture.antiAliasing=SystemInfo.GetRenderTextureSupportedMSAASampleCount(texture.descriptor);
            texture.Create();viewCamera.targetTexture=texture;target.texture=texture;
        }
        private void ApplyDraft()
        {
            if(applier==null||session==null)return;
            applier.ApplyPreviewBody(comparing?session.OriginalForComparison:session.Draft,
                session.PreviewBody,session.EquipmentExampleId,session.HeadgearVisible);
            CacheFramingBones();
            if(physics){physics.RefreshSelection();physics.ResetSimulation();}
            FrameCamera();RenderNow();
        }
        public void SetComparison(bool value){comparing=value;ApplyDraft();}
        public void SetFraming(AppearanceFraming framing){if(session==null)return;session.Framing=framing;zoom=1;FrameCamera();}
        public void ResetView(){yaw=180f;zoom=1f;FrameCamera();if(physics)physics.ResetSimulation();}
        private void FrameCamera()
        {
            if(!viewCamera||!model||session==null)return;
            ApplyMotionRoot();
            float focus,span;
            switch(session.Framing)
            {
                case AppearanceFraming.Face:focus=1.425f;span=.32f;break;
                case AppearanceFraming.UpperBody:focus=1.12f;span=1.08f;break;
                default:focus=.75f;span=1.80f;break;
            }
            float distance=span/(2f*Mathf.Tan(viewCamera.fieldOfView*Mathf.Deg2Rad*.5f))/zoom;
            float angle=session.Framing==AppearanceFraming.Face?faceCameraTilt:session.Framing==AppearanceFraming.UpperBody?upperBodyCameraTilt:fullBodyCameraTilt;
            if(HasMotionRoot&&session.Framing==AppearanceFraming.FullBody&&framingBones.Length>0)
            {
                var inverse=Quaternion.Inverse(Quaternion.Euler(angle,0,0));float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
                foreach(var bone in framingBones)
                {
                    if(!bone)continue;var projected=inverse*(bone.position-rig.transform.position);
                    minY=Mathf.Min(minY,projected.y);maxY=Mathf.Max(maxY,projected.y);
                }
                float centerY=(minY+maxY)*.5f;
                focus=centerY/Mathf.Cos(angle*Mathf.Deg2Rad);
                float centerZ=-focus*Mathf.Sin(angle*Mathf.Deg2Rad);
                float halfFov=Mathf.Tan(viewCamera.fieldOfView*Mathf.Deg2Rad*.5f);
                // Perspective depth matters when an elevated hand moves toward the camera.
                // Include mesh beyond the weighted bones and retain visible top/bottom margins.
                foreach(var bone in framingBones)
                {
                    if(!bone)continue;var projected=inverse*(bone.position-rig.transform.position);
                    float relativeDepth=projected.z-centerZ;
                    float vertical=(Mathf.Abs(projected.y-centerY)+.12f)/(halfFov*.88f)-relativeDepth;
                    float horizontal=(Mathf.Abs(projected.x)+.12f)/(halfFov*viewCamera.aspect*.42f)-relativeDepth;
                    motionDistance=Mathf.Max(motionDistance,vertical,horizontal);
                }
                distance=Mathf.Max(distance,motionDistance/zoom);
            }
            var center=rig.transform.position+Vector3.up*focus;
            var offset=Quaternion.Euler(angle,0,0)*new Vector3(0,0,-distance);
            viewCamera.transform.position=center+offset;
            viewCamera.transform.LookAt(center);
        }
        public void Play(AnimationClip clip,bool repeat=true)
        {
            if(!animator||!clip)return;
            if(graph.IsValid())graph.Destroy();
            applier?.ClearExpressions();
            activeClip=clip;looping=repeat;paused=false;ResetMotionRoot();animator.applyRootMotion=HasMotionRoot;
            graph=PlayableGraph.Create("Appearance Animation Preview");
            graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
            clipPlayable=AnimationClipPlayable.Create(graph,clip);
            clipPlayable.SetApplyFootIK(false);
            clipPlayable.SetApplyPlayableIK(false);
            clipPlayable.SetSpeed(speed);
            var output=AnimationPlayableOutput.Create(graph,"Appearance Animation",animator);
            if(!clip.isHumanMotion&&catalog.idleClip)
            {
                var idle=AnimationClipPlayable.Create(graph,catalog.idleClip);idle.SetApplyFootIK(false);idle.SetApplyPlayableIK(false);
                var layers=AnimationLayerMixerPlayable.Create(graph,2);
                graph.Connect(idle,0,layers,0);graph.Connect(clipPlayable,0,layers,1);
                layers.SetInputWeight(0,1);layers.SetInputWeight(1,1);layers.SetLayerAdditive(1,true);
                if(clip.length<=.001f)output.SetSourcePlayable(idle);else output.SetSourcePlayable(layers);
            }
            else output.SetSourcePlayable(clipPlayable);
            graph.Play();
            // Animation events carry gameplay meaning. Preview source has no event receivers.
            animator.fireEvents=false;
            graph.Evaluate(0);ResetMotionRoot();ApplyStaticExpression();if(physics)physics.ResetSimulation();FrameCamera();
        }
        private void ApplyStaticExpression()
        {
            if(!activeClip||activeClip.isHumanMotion||activeClip.length>.001f)return;
            if(!expressionSampler)CreateExpressionSampler();
            if(sampledExpression!=activeClip)
            {
                foreach(var part in expressionParts)
                    for(int i=0;i<part.sample.sharedMesh.blendShapeCount;i++)part.sample.SetBlendShapeWeight(i,0);
                // SampleAnimation on the live humanoid resets its skeletal pose, even for face-only curves.
                // Read the immutable pose on an inactive, renderer-only hierarchy instead.
                activeClip.SampleAnimation(expressionSampler,0);
                expressionWeights=new float[expressionParts.Count][];
                for(int p=0;p<expressionParts.Count;p++)
                {
                    var part=expressionParts[p];expressionWeights[p]=new float[part.sample.sharedMesh.blendShapeCount];
                    for(int i=0;i<expressionWeights[p].Length;i++)expressionWeights[p][i]=part.sample.GetBlendShapeWeight(i);
                }
                sampledExpression=activeClip;
            }
            for(int p=0;p<expressionParts.Count;p++)
                for(int i=0;i<expressionWeights[p].Length;i++)
                    if(!Mathf.Approximately(expressionParts[p].target.GetBlendShapeWeight(i),expressionWeights[p][i]))
                        expressionParts[p].target.SetBlendShapeWeight(i,expressionWeights[p][i]);
        }
        private void CreateExpressionSampler()
        {
            expressionSampler=new GameObject("Appearance Expression Sampler"){hideFlags=HideFlags.DontSave};
            expressionSampler.transform.SetParent(rig.transform,false);expressionSampler.SetActive(false);
            foreach(var target in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if(!target.sharedMesh||target.sharedMesh.blendShapeCount==0)continue;
                var names=new Stack<string>();var current=target.transform;
                while(current!=model.transform){names.Push(current.name);current=current.parent;}
                current=expressionSampler.transform;
                foreach(var name in names)
                {
                    var next=current.Find(name);if(!next){next=new GameObject(name).transform;next.SetParent(current,false);}current=next;
                }
                var sample=current.gameObject.AddComponent<SkinnedMeshRenderer>();sample.enabled=false;sample.sharedMesh=target.sharedMesh;
                expressionParts.Add((target,sample));
            }
        }
        public void TogglePause(){paused=!paused;if(clipPlayable.IsValid())clipPlayable.SetSpeed(paused?0:speed);}
        public void StopAnimation(){Play(catalog.idleClip,true);}
        public void SetSpeed(float value){speed=Mathf.Clamp(value,.25f,2f);if(clipPlayable.IsValid())clipPlayable.SetSpeed(paused?0:speed);}
        public void SetLooping(bool value){looping=value;}
        public void Seek(float time)
        {
            if(!clipPlayable.IsValid()||!activeClip)return;time=Mathf.Clamp(time,0,activeClip.length);
            if(HasMotionRoot)
            {
                seekingMotion=true;double previousSpeed=clipPlayable.GetSpeed();
                try
                {
                    clipPlayable.SetSpeed(1);clipPlayable.SetTime(0);graph.Evaluate(0);ResetMotionRoot();
                    graph.Evaluate(time);animationHeight=animator.deltaPosition.y;animationRotation=animator.deltaRotation;ApplyMotionRoot();
                }
                finally{clipPlayable.SetSpeed(previousSpeed);seekingMotion=false;}
            }
            else{clipPlayable.SetTime(time);graph.Evaluate(0);}
            ApplyStaticExpression();SynchronizeHeadAttachments();if(physics)physics.ResetSimulation();FrameCamera();
        }
        private void CacheFramingBones()
        {
            var bones=new HashSet<Transform>();foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())foreach(var bone in skin.bones)if(bone)bones.Add(bone);framingBones=new List<Transform>(bones).ToArray();
        }
        private void ResetMotionRoot(){animationHeight=0;animationRotation=Quaternion.identity;motionDistance=0;ApplyMotionRoot();}
        private void ApplyMotionRoot(){if(!model)return;model.transform.localPosition=modelPosition+Vector3.up*animationHeight;model.transform.localRotation=Quaternion.Euler(0,yaw,0)*animationRotation;}
        internal void ReceiveRootMotion(Vector3 delta,Quaternion rotation)
        {
            if(!HasMotionRoot||seekingMotion)return;animationHeight+=delta.y;animationRotation*=rotation;ApplyMotionRoot();
        }

        private void LateUpdate()
        {
            if(!rig||session==null)return;
            if(Time.realtimeSinceStartupAsDouble>=nextResize)
            {nextResize=Time.realtimeSinceStartupAsDouble+.5;ResizeTexture();}
            if(clipPlayable.IsValid()&&activeClip&&PlaybackTime>=activeClip.length)
            {
                if(looping && activeClip.length>0){float framedDistance=motionDistance;Seek(PlaybackTime%activeClip.length);motionDistance=Mathf.Max(framedDistance,motionDistance);}
                else {clipPlayable.SetTime(activeClip.length);paused=true;clipPlayable.SetSpeed(0);}
            }
            ApplyStaticExpression();
            ApplyMotionRoot();
            applier.ApplyBodyStyle((comparing?session.OriginalForComparison:session.Draft).bodyShapeId);
            RenderNow();
        }
        internal void PreparePhysicsPose()=>SynchronizeHeadAttachments();
        internal void RenderAfterPhysics()=>RenderNow();
        public AppearancePreviewPhysics Physics=>physics;
        private void RenderNow()
        {
            if(!viewCamera||!texture)return;
#if UNITY_EDITOR
            if(UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode&&!Application.isPlaying)return;
#endif
            if(Application.isPlaying&&RenderPipelineManager.currentPipeline==null)return;
            SynchronizeHeadAttachments();FrameCamera();viewCamera.Render();
        }
        private void SynchronizeHeadAttachments()
        {
            // Manual graph evaluation does not run Unity's constraint update before a still render.
            // P09 hair has its own skeleton; match its attachment after pose and root rotation.
            if(headAttachments==null)return;
            foreach(var attachment in headAttachments)
            {
                if(!attachment||!attachment.constraintActive||attachment.weight<.999f)continue;
                var source=attachment.GetSource(0);if(!source.sourceTransform||source.weight<.999f)continue;
                attachment.transform.SetPositionAndRotation(source.sourceTransform.TransformPoint(attachment.GetTranslationOffset(0)),source.sourceTransform.rotation*Quaternion.Euler(attachment.GetRotationOffset(0)));
            }
        }
        public Texture2D CaptureStill(int width,int height,float time=.7f)
        {
            if(!viewCamera)throw new InvalidOperationException("프리뷰가 열려 있지 않습니다.");
            var previous=viewCamera.targetTexture;var active=RenderTexture.active;
            float factor=Mathf.Min(2f,3840f/width,2160f/height);
            int renderedWidth=Mathf.RoundToInt(width*factor),renderedHeight=Mathf.RoundToInt(height*factor);
            var shot=RenderTexture.GetTemporary(renderedWidth,renderedHeight,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default,SystemInfo.supportsMultisampledTextures!=0?4:1);
            RenderTexture resolved=null;
            try
            {
                viewCamera.scene=rig.scene;viewCamera.targetTexture=shot;Seek(time);
                applier.ApplyBodyStyle((comparing?session.OriginalForComparison:session.Draft).bodyShapeId);
                FrameCamera();SynchronizeHeadAttachments();viewCamera.Render();
                resolved=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32);
                Graphics.Blit(shot,resolved);RenderTexture.active=resolved;
                var result=new Texture2D(width,height,TextureFormat.RGBA32,false);
                result.ReadPixels(new Rect(0,0,width,height),0,0);result.Apply();return result;
            }
            finally{viewCamera.targetTexture=previous;RenderTexture.active=active;RenderTexture.ReleaseTemporary(shot);if(resolved)RenderTexture.ReleaseTemporary(resolved);}
        }
        public void OnBeginDrag(PointerEventData e){dragging=true;}
        public void OnDrag(PointerEventData e){if(!dragging||session==null)return;yaw-=e.delta.x*.3f;FrameCamera();}
        public void OnEndDrag(PointerEventData e){dragging=false;}
        public void OnScroll(PointerEventData e){if(session==null)return;zoom=Mathf.Clamp(zoom+e.scrollDelta.y*.06f,.65f,1.6f);FrameCamera();}
        public void Close()
        {
            if(physics){physics.Dispose();physics=null;}
            if(session!=null)session.Changed-=ApplyDraft;
            if(graph.IsValid())graph.Destroy();
            if(target)target.texture=null;
            if(viewCamera)viewCamera.targetTexture=null;
            if(texture){texture.Release();ReleaseObject(texture);}
            if(rig)ReleaseObject(rig);
            foreach(var material in materials.Values)ReleaseObject(material);
            materials.Clear();rig=model=null;viewCamera=null;animator=null;texture=null;
            activeClip=null;applier=null;session=null;quality=null;headAttachments=null;dragging=false;
            expressionSampler=null;expressionParts.Clear();sampledExpression=null;expressionWeights=null;framingBones=Array.Empty<Transform>();animationHeight=motionDistance=0;animationRotation=Quaternion.identity;seekingMotion=false;
        }
        private static void ReleaseObject(UnityEngine.Object value)
        {if(!value)return;if(Application.isPlaying)Destroy(value);else DestroyImmediate(value);}
        private void OnDisable()=>Close();
        private void OnDestroy()=>Close();
    }
}

