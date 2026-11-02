// Arcade additive particle: texture * HDR _BaseColor * vertex colour, premultiplied by alpha and added.
// Values above 1 survive in the HDR target and bloom. SRP Batcher compatible, no geometry/compute, WebGL2-safe.
Shader "RoadGunner/Arcade Particle Additive"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor][HDR] _BaseColor("Color (HDR)", Color) = (1,1,1,1)
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticles("Soft edge (needs camera depth texture)", Float) = 0
        _SoftFade("Soft edge distance", Range(0.05, 4)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "ArcadeParticleAdditive"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma shader_feature_local _SOFTPARTICLES_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #if defined(_SOFTPARTICLES_ON)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #endif

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _SoftParticles;
                half _SoftFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                #if defined(_SOFTPARTICLES_ON)
                float4 screen : TEXCOORD2;
                #endif
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fogFactor = ComputeFogFactorZ0ToFar(max(-p.positionVS.z - _ProjectionParams.y, 0.0)); // view depth: clip z carries no distance under the ortho camera
                #if defined(_SOFTPARTICLES_ON)
                o.screen = float4(p.positionNDC.xy, p.positionNDC.w, -p.positionVS.z);
                #endif
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
                #if defined(_SOFTPARTICLES_ON)
                float raw = SampleSceneDepth(i.screen.xy / i.screen.z);
                float scene = unity_OrthoParams.w == 0 ? LinearEyeDepth(raw, _ZBufferParams) : LinearDepthToEyeDepth(raw);
                c.a *= saturate((scene - i.screen.w) / _SoftFade);
                #endif
                c.rgb *= c.a;
                c.rgb = MixFogColor(c.rgb, half3(0, 0, 0), i.fogFactor);
                return half4(c.rgb, c.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
