// Full-screen silhouette outline for the URP FullScreenPassRendererFeature (injection: before transparents,
// requirements: Depth, no colour fetch). It blends a violet-dark ink over the camera target, so VFX, UI and
// world canvases drawn afterwards stay clean.
//
// Edge test: in an orthographic view the eye depth of any plane is affine in screen space, so the second
// difference a + b - 2c of opposite neighbours is ~0 on flat ground and slopes and only spikes where c is a
// rim pixel in front of the surface behind it. Features thinner than the kernel (tracers, cables, barrels)
// have both neighbours far behind and are skipped to avoid noisy lines on tiny details.
Shader "RoadGunner/Arcade Outline"
{
    Properties
    {
        _OutlineColor("Ink colour (A = opacity)", Color) = (0.17, 0.13, 0.22, 0.9)
        _Thickness("Thickness (px at a 1080 px short side)", Range(0.5, 6)) = 3.3
        _DepthThreshold("Depth jump threshold (m)", Range(0.02, 3)) = 0.28
        _DepthSoftness("Depth jump softness (m)", Range(0.01, 3)) = 0.3
        _FadeDistance("Fade start, end (view m), far opacity", Vector) = (40, 110, 0.5, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask RGB

        Pass
        {
            Name "ArcadeOutline"

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment OutlineFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _Thickness;
                float _DepthThreshold;
                float _DepthSoftness;
                float4 _FadeDistance;
            CBUFFER_END

            float ArcadeEyeDepth(float2 uv)
            {
                float raw = SampleSceneDepth(uv);
                if (unity_OrthoParams.w > 0.5)
                {
                #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            float EdgePair(float a, float b, float c, float threshold)
            {
                float bend = a + b - 2.0 * c;  // > 0: c is nearer than the line through its neighbours
                float thin = min(a, b) - c;    // both neighbours far behind: sub-kernel feature
                return smoothstep(threshold, threshold + _DepthSoftness, bend)
                     * (1.0 - smoothstep(threshold, threshold + _DepthSoftness, thin));
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 screen = max(_ScreenParams.xy, float2(1, 1));
                // Scales with the short screen side: ~2.2 px at 720p portrait, 3.3 px at 1080p, same in landscape.
                float pixels = max(0.75, _Thickness * min(screen.x, screen.y) / 1080.0);
                float2 o = pixels / screen;

                float c = ArcadeEyeDepth(uv);
                if (c > _ProjectionParams.z * 0.98) return half4(0, 0, 0, 0);

                float l = ArcadeEyeDepth(uv + float2(-o.x, 0));
                float r = ArcadeEyeDepth(uv + float2(o.x, 0));
                float b = ArcadeEyeDepth(uv + float2(0, -o.y));
                float t = ArcadeEyeDepth(uv + float2(0, o.y));
                float bl = ArcadeEyeDepth(uv - o);
                float tr = ArcadeEyeDepth(uv + o);
                float tl = ArcadeEyeDepth(uv + float2(-o.x, o.y));
                float br = ArcadeEyeDepth(uv + float2(o.x, -o.y));

                // Constant in metres for ortho; grows with distance if a perspective camera ever uses this renderer.
                float threshold = _DepthThreshold * max(1.0, c * 0.02);
                float edge = max(max(EdgePair(l, r, c, threshold), EdgePair(b, t, c, threshold)),
                                 max(EdgePair(bl, tr, c, threshold), EdgePair(tl, br, c, threshold)));

                float fade = lerp(1.0, _FadeDistance.z, saturate((c - _FadeDistance.x) / max(_FadeDistance.y - _FadeDistance.x, 0.001)));
                return half4(_OutlineColor.rgb, _OutlineColor.a * edge * fade);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
