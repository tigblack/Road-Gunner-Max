// Arcade alpha particle: texture * HDR _BaseColor * vertex colour, alpha blended.
// _Erode > 0 turns vertex alpha into a crisp toon erosion threshold over the texture alpha (store a distance-like gradient
// in the texture alpha): puffs keep a clean cartoon edge while they shrink away, with an optional darker rim band.
// SRP Batcher compatible, no geometry/compute, WebGL2-safe.
Shader "RoadGunner/Arcade Particle Alpha"
{
    Properties
    {
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        [MainColor][HDR] _BaseColor("Color (HDR)", Color) = (1,1,1,1)
        _Erode("Toon erosion (vertex alpha = threshold)", Range(0, 1)) = 0
        _EdgeSoftness("Edge softness", Range(0.005, 0.3)) = 0.04
        _RimColor("Rim tint (multiplied)", Color) = (1,1,1,1)
        _RimWidth("Rim width", Range(0, 0.4)) = 0
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticles("Soft edge (needs camera depth texture)", Float) = 0
        _SoftFade("Soft edge distance", Range(0.05, 4)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "ArcadeParticleAlpha"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
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
                half4 _RimColor;
                half _Erode;
                half _EdgeSoftness;
                half _RimWidth;
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
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half s = _EdgeSoftness;
                // Threshold rises from just above zero (vertex alpha 1, whole shape) to above one (vertex alpha 0, gone).
                half threshold = lerp(s + 0.01h, 1.02h + s, (1.0h - i.color.a) * _Erode);
                half eroded = smoothstep(threshold - s, threshold + s, tex.a);
                half alpha = lerp(tex.a * i.color.a, eroded, _Erode) * _BaseColor.a;
                half rim = (1.0h - smoothstep(threshold + s, threshold + s + _RimWidth, tex.a)) * step(0.001h, _RimWidth);
                half3 rgb = tex.rgb * _BaseColor.rgb * i.color.rgb * lerp(half3(1, 1, 1), _RimColor.rgb, rim);
                #if defined(_SOFTPARTICLES_ON)
                float raw = SampleSceneDepth(i.screen.xy / i.screen.z);
                float scene = unity_OrthoParams.w == 0 ? LinearEyeDepth(raw, _ZBufferParams) : LinearDepthToEyeDepth(raw);
                alpha *= saturate((scene - i.screen.w) / _SoftFade);
                #endif
                rgb = MixFog(rgb, i.fogFactor);
                return half4(rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
