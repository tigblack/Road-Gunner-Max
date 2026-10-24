Shader "RoadGunner/Concept Desert"
{
 Properties { _BaseMap("World desert albedo",2D)="white"{} _BaseColor("Tint",Color)=(1,1,1,1) _WorldScale("World size",Float)=512 _Cutoff("Cutoff",Float)=.5 }
 SubShader {
 Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
 Pass { Name "ForwardLit" Tags {"LightMode"="UniversalForward"}
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.0
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
 #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
 #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
 CBUFFER_START(UnityPerMaterial)
 float4 _BaseMap_ST;half4 _BaseColor;float _WorldScale;float _Cutoff;
 CBUFFER_END
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;half fog:TEXCOORD2;UNITY_VERTEX_OUTPUT_STEREO};
 V Vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=half3(0,1,0);o.fog=ComputeFogFactor(p.positionCS.z);return o;}
 half4 Frag(V i):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);SurfaceData s=(SurfaceData)0;s.albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.positionWS.xz/_WorldScale+.5).rgb*_BaseColor.rgb;s.alpha=1;s.normalTS=half3(0,0,1);s.smoothness=.08;s.occlusion=1;s.metallic=0;s.specular=half3(.04,.04,.04);InputData d=(InputData)0;d.positionWS=i.positionWS;d.normalWS=NormalizeNormalPerPixel(i.normalWS);d.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);d.shadowCoord=TransformWorldToShadowCoord(i.positionWS);d.bakedGI=SampleSH(d.normalWS);d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);d.shadowMask=half4(1,1,1,1);half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,i.fog);return c;}
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
}
