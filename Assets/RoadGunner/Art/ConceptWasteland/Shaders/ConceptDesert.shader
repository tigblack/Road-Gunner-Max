// World-mapped arcade desert ground. _BaseMap is the baked 512 m world texture: RGB region colours with noise-feathered
// borders and the trampled combat-pocket floors (no roads), A a smooth macro tone field that the shader steps into
// crisp deep/mid/main/light toon patches; _AccentMap (same mapping) RGB the regional accent colour (sage, rust stains,
// deep lilac), A its smooth field, thresholded at 0.5 with fwidth so patch edges stay 1-2 px crisp at any zoom. _RoadMap (same mapping) holds
// signed distances: R to the packed-road union (roads + pockets + laybys), G to the travelled lanes (ArcadeVisualGround).
// The shader paints the packed roads as filled bands (tinted toward the local region colour) with soft noise-warped
// shoulders, faint double ruts, tiling world-space sand detail (grain, wind ripples, chips, macro tint) and stepped
// toon received shadows with the shared saturation-capped shade formula. Property names (_BaseMap, _BaseColor,
// _WorldScale) are relied on by the minimap baker and installers.
Shader "RoadGunner/Concept Desert"
{
    Properties
    {
        _BaseMap("World desert albedo", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _WorldScale("World size", Float) = 512
        _Cutoff("Cutoff", Float) = .5
        [NoScaleOffset] _AccentMap("Accent patches (RGB colour, A field)", 2D) = "black" {}
        _ToneLevels("Tone steps (deep, mid, light, strength)", Vector) = (0.315, 0.47, 0.665, 1)

        [Header(Arcade sand detail)]
        [NoScaleOffset] _DetailMap("Detail (R grain, G ripples, B pebbles, A macro)", 2D) = "gray" {}
        _DetailScale("Detail tile (m)", Float) = 6
        _GrainStrength("Grain", Range(0, 0.3)) = 0.05
        _RippleStrength("Wind ripples", Range(0, 0.3)) = 0.06
        _PebbleStrength("Pebbles", Range(0, 1)) = 0.35
        _MacroStrength("Macro tint", Range(0, 0.3)) = 0.04
        _MottleStrength("Soft dune mottling", Range(0, 0.3)) = 0.05
        _PebbleColor("Pebble tint", Color) = (0.68, 0.5, 0.42, 1)

        [Header(Packed road)]
        [NoScaleOffset] _RoadMap("Road distance (R union, G lanes, B pockets)", 2D) = "white" {}
        _RoadColor("Packed road fill", Color) = (0.99, 0.92, 0.75, 1)
        _RoadTint("Road tint toward region", Range(0, 1)) = 0.3
        _PocketRoad("Road fill inside pockets", Range(0, 1)) = 0.5
        _RoadSoftness("Road edge softness (m)", Range(0.02, 1)) = 0.22
        _RoadWarp("Road edge noise warp (m)", Range(0, 1)) = 0.3
        _RoadEdgeColor("Shoulder multiply tint (A = strength)", Color) = (0.86, 0.78, 0.72, 1)
        _RoadEdgeWidth("Shoulder width (m)", Range(0, 2)) = 0.75
        _RoadDetail("Detail kept on packed road", Range(0, 1)) = 0.35
        _RutStrength("Tire ruts", Range(0, 0.3)) = 0.06

        [Header(Toon light)]
        _ShadeColor("Shade tint", Color) = (0.75, 0.63, 0.81, 1)
        _ShadowThreshold("Received shadow step", Range(0, 1)) = 0.5
        _ShadowSmoothness("Received shadow softness", Range(0.001, 0.5)) = 0.03
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _WorldScale;
            float _Cutoff;
            half4 _ToneLevels;
            float _DetailScale;
            half _GrainStrength;
            half _RippleStrength;
            half _PebbleStrength;
            half _MacroStrength;
            half _MottleStrength;
            half4 _PebbleColor;
            half4 _RoadColor;
            half _RoadTint;
            half _PocketRoad;
            half _RoadSoftness;
            half _RoadWarp;
            half4 _RoadEdgeColor;
            half _RoadEdgeWidth;
            half _RoadDetail;
            half _RutStrength;
            half4 _ShadeColor;
            half _ShadowThreshold;
            half _ShadowSmoothness;
        CBUFFER_END

        TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
        TEXTURE2D(_AccentMap);
        TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
        TEXTURE2D(_RoadMap);   SAMPLER(sampler_RoadMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing

            #include "Assets/RoadGunner/Art/Arcade/Shaders/ArcadeLighting.hlsl"

            struct A
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half fog          : TEXCOORD1;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3 vertexLight : TEXCOORD2;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD3;
            #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            V Vert(A a)
            {
                V o = (V)0;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_TRANSFER_INSTANCE_ID(a, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(a.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.fog = ArcadeFogFactor(p.positionWS);
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                o.vertexLight = VertexLighting(p.positionWS, half3(0, 1, 0));
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                o.shadowCoord = GetShadowCoord(p);
            #endif
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 p = i.positionWS.xz;
                float2 worldUV = p / _WorldScale + 0.5;
                half4 world = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, worldUV);
                half3 offroad = world.rgb;
                // Crisp toon-stepped macro tones from the smooth field in A (hue-preserving deep / lifted light).
                half tone = world.a;
                half tw = max(fwidth(tone), 0.002h);
                half luma = dot(offroad, half3(0.2126h, 0.7152h, 0.0722h));
                half3 deepTone = lerp(luma.xxx, offroad, 1.25h) * 0.86h;
                half3 lightTone = lerp(luma.xxx, offroad, 0.95h) * 1.07h + 0.015h;
                half3 toned = lerp(offroad * 0.965h, offroad, smoothstep(_ToneLevels.y - tw, _ToneLevels.y + tw, tone));
                toned = lerp(toned, lightTone, smoothstep(_ToneLevels.z - tw, _ToneLevels.z + tw, tone));
                toned = lerp(toned, deepTone, 1.0h - smoothstep(_ToneLevels.x - tw, _ToneLevels.x + tw, tone));
                offroad = lerp(offroad, toned, _ToneLevels.w);
                half4 accentSample = SAMPLE_TEXTURE2D(_AccentMap, sampler_BaseMap, worldUV);
                half aw = max(fwidth(accentSample.a), 0.002h);
                offroad = lerp(offroad, accentSample.rgb, smoothstep(0.5h - aw, 0.5h + aw, accentSample.a));
                offroad = lerp(offroad, accentSample.rgb * 0.88h, smoothstep(0.62h - aw, 0.62h + aw, accentSample.a));
                offroad = saturate(offroad) * _BaseColor.rgb;

                // Three detail taps at unrelated scales/rotations hide the tile repeat.
                half4 fine = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p / _DetailScale);
                float2 q = float2(p.x * 0.906 - p.y * 0.423, p.x * 0.423 + p.y * 0.906); // ripple crests run ~screen-horizontal
                half4 wind = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, q / (_DetailScale * 2.35));
                half4 broad = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, p / (_DetailScale * 15.7) + 0.37);

                // 0.5..1 encodes signed distances (m), -6 inside .. +8 outside (white = no road):
                // R to the packed-road union (roads + pockets + laybys), G to the travelled lanes only.
                half3 roads = SAMPLE_TEXTURE2D(_RoadMap, sampler_RoadMap, worldUV).rgb * 28.0h - 20.0h; // B: pockets + laybys only
                half warp = (wind.a - 0.5h) * 2.0h * _RoadWarp + (fine.g - 0.5h) * 0.1h;
                half dLane = roads.y + warp;
                half dUnion = roads.x + warp;
                half pocket = 1.0h - smoothstep(-0.8h, 0.4h, roads.z + warp);
                half aa = max(fwidth(roads.y), 0.02h);
                half soft = max(_RoadSoftness, aa);
                half onLane = (1.0h - smoothstep(-soft, soft, dLane)) * lerp(1.0h, _PocketRoad, pocket);
                half packed = max(onLane, pocket);

                // Packed road = the local sand lifted ~10-15 L* toward the packed cream (region-tinted, never a flat sheet).
                half3 roadColor = saturate(lerp(_RoadColor.rgb, offroad * 1.15h + 0.03h, _RoadTint));
                half3 albedo = lerp(offroad, roadColor, onLane);

                // Soft shoulder just outside the packed union edge: multiplied (keeps the region hue), no ink stroke.
                half shoulder = smoothstep(-0.12h, 0.08h, dUnion) * (1.0h - smoothstep(0.0h, max(_RoadEdgeWidth, 0.05h), dUnion));
                albedo *= lerp(half3(1, 1, 1), _RoadEdgeColor.rgb, shoulder * _RoadEdgeColor.a);

                half keep = lerp(1.0h, _RoadDetail, onLane);
                half macro = (broad.a - 0.5h) * 2.0h * _MacroStrength;
                half grain = (fine.r - 0.5h) * 2.0h * _GrainStrength * keep;
                half ripple = (wind.g - 0.5h) * 2.0h * _RippleStrength * (1.0h - onLane) * (1.0h - 0.5h * pocket);
                // Mid-scale (2-4 m) soft dune patches: lighter patches lean yellow, darker ones lean orange.
                half mottle = (wind.a - 0.5h) * 2.0h * _MottleStrength * keep;
                albedo *= (1.0h + macro + grain + ripple) * (1.0h + mottle * half3(0.8h, 1.0h, 1.35h));

                half chips = _PebbleStrength * keep * (1.0h - 0.6h * packed);
                half darkPebble = saturate((fine.b - 0.5h) * 2.0h) * chips;
                half lightChip = saturate((0.5h - fine.b) * 2.0h) * chips * 0.5h;
                albedo = lerp(albedo, albedo * _PebbleColor.rgb, darkPebble);
                albedo = lerp(albedo, albedo * 1.08h + 0.02h, lightChip);

                // Faint double wheel tracks per lane side (1.7 m and 3.3 m in from each edge), softer through pockets.
                half laneInside = -(roads.y + warp * 0.5h);
                half rutAA = max(fwidth(laneInside), 0.02h);
                half tracks = 0.0h;
                tracks += 1.0h - smoothstep(0.1h, 0.1h + rutAA * 1.5h, abs(laneInside - 1.7h));
                tracks += 1.0h - smoothstep(0.1h, 0.1h + rutAA * 1.5h, abs(laneInside - 3.3h));
                albedo *= 1.0h - _RutStrength * saturate(tracks) * (1.0h - 0.5h * pocket) * step(0.0h, laneInside) * (0.75h + 0.5h * fine.r);

                // Soft shoulder above 0.86 linear (sRGB ~F0): lit ground never clips a channel (terracotta road R, light patches).
                albedo -= max(albedo - 0.86h, 0.0h) * 0.65h;

                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalWS = half3(0, 1, 0);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = ArcadeFragmentShadowCoord(i.positionWS, i.shadowCoord);
            #else
                inputData.shadowCoord = ArcadeFragmentShadowCoord(i.positionWS, float4(0, 0, 0, 0));
            #endif
                inputData.shadowMask = half4(1, 1, 1, 1);

                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                half lit = ArcadeReceivedShadow(mainLight, inputData.shadowCoord, inputData.positionWS, _ShadowThreshold, _ShadowSmoothness);
                half3 extra = ArcadeAdditionalLights(inputData, half3(0, 1, 0));
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                extra += i.vertexLight;
            #endif
                half3 color = ArcadeToonCombine(albedo, ArcadeMainLightColor(mainLight), _ShadeColor.rgb, ArcadeAmbient(half3(0, 1, 0)), extra, lit);
                color = MixFog(color, i.fog);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DA { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct DV { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            DV DepthVert(DA a)
            {
                DV o = (DV)0;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_TRANSFER_INSTANCE_ID(a, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                return o;
            }

            half DepthFrag(DV i) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            struct NA { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct NV { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };

            NV DepthNormalsVert(NA a)
            {
                NV o = (NV)0;
                UNITY_SETUP_INSTANCE_ID(a);
                UNITY_TRANSFER_INSTANCE_ID(a, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(a.positionOS.xyz);
                o.normalWS = half3(TransformObjectToWorldNormal(a.normalOS));
                return o;
            }

            void DepthNormalsFrag(NV i, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalize(float3(i.normalWS)));
                outNormalWS = half4(PackFloat2To888(saturate(octNormalWS * 0.5 + 0.5)), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            #endif
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
