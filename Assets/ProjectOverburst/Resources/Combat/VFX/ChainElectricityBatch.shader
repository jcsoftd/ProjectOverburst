Shader "OVERBURST/VFX/Chain Electricity Batch"
{
 Properties { [HDR] _TintColor("Tint",Color)=(1,1,1,1) _Intensity("Intensity",Float)=3 _EdgePower("Edge Softness",Float)=2.2 }
 SubShader
 {
  Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Blend SrcAlpha One Cull Off ZWrite Off ZTest LEqual
   HLSLPROGRAM
   #pragma target 4.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Link { float4 centerTime,right,up,forward,phases,branch0,branch1,branchPhases; };
   StructuredBuffer<Link> _Links;
   StructuredBuffer<float4> _Profiles;
   StructuredBuffer<float4> _Segments;
   float _Now,_Lifetime,_MainAmplitude,_BranchAmplitude;
   CBUFFER_START(UnityPerMaterial)
    half4 _TintColor; half _Intensity; half _EdgePower;
   CBUFFER_END
   struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
   float3 MainPoint(float t,Link link)
   {
    float taper=sin(t*PI);
    return float3((sin(t*13.5+link.phases.x)*.72+sin(t*31+link.phases.y)*.28)*_MainAmplitude*taper,
      (sin(t*16.5+link.phases.y)*.68+sin(t*37+link.phases.x)*.32)*_MainAmplitude*taper,t-.5);
   }
   float3 Point(float t,uint stripId,Link link)
   {
    if(stripId<2)return MainPoint(t,link);
    float4 branch=stripId<4?link.branch0:link.branch1;
    float phase=stripId<4?link.branchPhases.x:link.branchPhases.y;
    float3 start=MainPoint(branch.x,link);
    float bend=sin(t*PI)*_BranchAmplitude*.18;
    float jag=sin(t*24+phase)*_BranchAmplitude*.12*sin(t*PI);
    return float3(start.x+branch.z*t+bend+jag,start.y+branch.w*t-bend+jag*.7,lerp(start.z,branch.y,t));
   }
   float3 World(float3 p,Link link){return link.centerTime.xyz+link.right.xyz*p.x+link.up.xyz*p.y+link.forward.xyz*p.z;}
   Varyings Vert(uint vertex:SV_VertexID,uint instance:SV_InstanceID)
   {
    Link link=_Links[instance];float4 segment=_Segments[vertex/6];uint corner=vertex%6;
    uint endpoint=(corner==2||corner==4||corner==5)?1:0;
    uint side=(corner==1||corner==2||corner==4)?1:0;
    uint profileIndex=(uint)segment.z+endpoint;
    float4 profile=_Profiles[profileIndex*2+1];float t=profile.y;
    uint stripId=(uint)segment.x;float step=1/(segment.w-1);
    float3 p=World(Point(t,stripId,link),link);
    float3 tangent=World(Point(min(1,t+step),stripId,link),link)-World(Point(max(0,t-step),stripId,link),link);
    float3 view=unity_OrthoParams.w>.5?-UNITY_MATRIX_V[2].xyz:(_WorldSpaceCameraPos-p);
    float3 crossDir=cross(tangent,view);float3 across=crossDir*rsqrt(max(dot(crossDir,crossDir),1e-12));
    float elapsed=max(0,_Now-link.centerTime.w);
    float fade=1-smoothstep(.3,1,saturate(elapsed/_Lifetime));
    float width=elapsed<=.000001?1:max(.015,fade*(.92+sin(elapsed*96+link.phases.z)*.08));
    p+=across*((float)side-.5)*profile.x*width;
    Varyings o;o.positionCS=TransformWorldToHClip(p);o.color=_Profiles[profileIndex*2];o.uv=float2(t,side);return o;
   }
   half4 Frag(Varyings input):SV_Target
   {
    half center=saturate(1-abs(input.uv.y*2-1));half softEdge=pow(center,_EdgePower);
    half pulse=.9+.1*sin(input.uv.x*31+_Time.y*38);
    return half4(input.color.rgb*_TintColor.rgb*_Intensity*pulse,input.color.a*_TintColor.a*softEdge);
   }
   ENDHLSL
  }
 }
}
