// Arcade tire track: continuous multiply-darkened ribbon from the TireTracks stamp mesh.
// The stamp quads overlap (0.58 m long, stamped every ~0.32 m); a stencil bit lets only the first quad touch each pixel,
// so overlaps never stack into a dotted "ladder". Vertex alpha (0.30 fresh -> 0 after 14 s in TireTracks) is remapped so the
// track fades out over the last-to-first window [_FadeFloor, _FadeCeil]. Multiply blend keeps the darkening relative to any region tint.
// Without a stencil buffer the stencil test simply passes (graceful fallback). SRP Batcher compatible, WebGL2-safe.
Shader "RoadGunner/Arcade Particle Track"
{
    Properties
    {
        [MainTexture] _BaseMap("Track profile (alpha across the width)", 2D) = "white" {}
        [MainColor] _BaseColor("Multiply tint (a = strength)", Color) = (0.8, 0.72, 0.68, 0.45)
        _FadeFloor("Vertex alpha where the track is gone", Range(0, 0.3)) = 0.225
        _FadeCeil("Vertex alpha of a fresh stamp", Range(0.01, 1)) = 0.3
        [IntRange] _StencilBit("Stencil bit value", Range(1, 255)) = 128
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-40" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "ArcadeParticleTrack"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off
            Stencil
            {
                Ref [_StencilBit]
                ReadMask [_StencilBit]
                WriteMask [_StencilBit]
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _FadeFloor;
                half _FadeCeil;
                half _StencilBit;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half age = saturate((i.color.a - _FadeFloor) / max(_FadeCeil - _FadeFloor, 0.001h));
                age = age * age * (3.0h - 2.0h * age);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half a = tex.a * age * _BaseColor.a;
                // Faded pixels must not claim the stencil, or old invisible stamps would block fresh tracks driven over them.
                clip(a - 0.004h);
                half3 tint = lerp(half3(1, 1, 1), _BaseColor.rgb * tex.rgb, a);
                return half4(tint, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
