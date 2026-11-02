// Arcade floating text: font atlas alpha (dynamic Alpha8 font texture) * vertex colour * _BaseColor.
// Used by the single combined damage-number mesh (outline, shadow and face layers are separate vertex-coloured quads).
// Always drawn over the world (ZTest Always) so numbers never hide behind props; no fog. SRP Batcher compatible, WebGL2-safe.
Shader "RoadGunner/Arcade Particle Text"
{
    Properties
    {
        [MainTexture] _BaseMap("Font atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "ArcadeParticleText"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a;
                return half4(i.color.rgb * _BaseColor.rgb, saturate(a * i.color.a * _BaseColor.a));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
