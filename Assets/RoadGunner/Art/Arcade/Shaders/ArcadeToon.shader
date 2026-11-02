// Road Gunner "Sunny Wasteland Arcade" cel shader: two-band terminator, stepped received shadows tinted
// toward a warm violet shade colour (saturation-capped, ArcadeShadeCap), hemispheric trilight ambient, hard toon spec
// bands driven by the palette response atlas (R = metallic, A = smoothness) and a subtle warm rim. Optional wreck
// response: a dark _BaseColor tint (burnt buggy husk) becomes charcoal-violet with glowing ember specks instead of
// flat black. One UnityPerMaterial CBUFFER for every pass (SRP Batcher).
Shader "RoadGunner/Arcade Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor][HDR] _BaseColor("Color (HDR: >1 washes toward white for hit flashes)", Color) = (1,1,1,1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0.2
        [NoScaleOffset] _MetallicGlossMap("Surface response (R metallic, A smoothness)", 2D) = "white" {}
        [HDR] _EmissionColor("Emission", Color) = (0,0,0,1)
        [NoScaleOffset] _EmissionMap("Emission map", 2D) = "white" {}

        [Header(Toon)]
        _ShadeColor("Shade tint", Color) = (0.75, 0.63, 0.81, 1)
        _RampThreshold("Terminator (half-Lambert)", Range(0, 1)) = 0.45
        _RampSmoothness("Terminator softness", Range(0.001, 0.5)) = 0.035
        _ShadowThreshold("Received shadow step", Range(0, 1)) = 0.5
        _ShadowSmoothness("Received shadow softness", Range(0.001, 0.5)) = 0.08
        _RimColor("Rim colour (A = strength)", Color) = (1, 0.88, 0.68, 0.3)
        _RimPower("Rim power", Range(0.5, 8)) = 3
        _SpecColor("Glint colour", Color) = (1, 0.97, 0.9, 1)
        _SpecSize("Glint size", Range(0, 1)) = 0.14

        [Header(Wreck response)]
        _WreckAmount("Wreck response (0 = off)", Range(0, 1)) = 0
        _WreckColor("Husk colour", Color) = (0.23, 0.21, 0.25, 1)
        [HDR] _WreckEmber("Ember specks", Color) = (2.2, 0.75, 0.15, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _ShadeColor;
            half4 _RimColor;
            half4 _SpecColor;
            half4 _WreckColor;
            half4 _WreckEmber;
            half _WreckAmount;
            half _Metallic;
            half _Smoothness;
            half _RampThreshold;
            half _RampSmoothness;
            half _ShadowThreshold;
            half _ShadowSmoothness;
            half _RimPower;
            half _SpecSize;
        CBUFFER_END

        TEXTURE2D(_BaseMap);          SAMPLER(sampler_BaseMap);
        TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
        TEXTURE2D(_EmissionMap);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer

            #include "ArcadeLighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS    : TEXCOORD2;
                half fogFactor    : TEXCOORD3;
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                half3 vertexLight : TEXCOORD4;
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                float4 shadowCoord : TEXCOORD5;
            #endif
                float3 positionOS : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ToonVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normal = GetVertexNormalInputs(input.normalOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = half3(normal.normalWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ArcadeFogFactor(position.positionWS);
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                output.vertexLight = VertexLighting(position.positionWS, output.normalWS);
            #endif
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                output.shadowCoord = GetShadowCoord(position);
            #endif
                return output;
            }

            half4 ToonFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 albedo = texColor * _BaseColor.rgb;
                // Hit flashes push _BaseColor above 1: wash gently toward white so the hue and ink survive.
                albedo += max(_BaseColor.rgb - 1.0h, 0.0h) * 0.25h;

                // Wreck response: a dark tint (max channel < 0.5) reads as a charcoal husk with panel contrast kept.
                half tintMax = max(_BaseColor.r, max(_BaseColor.g, _BaseColor.b));
                half wreck = _WreckAmount * saturate((0.5h - tintMax) * 4.0h);
                half texLuma = dot(texColor, half3(0.2126h, 0.7152h, 0.0722h));
                albedo = lerp(albedo, _WreckColor.rgb * (0.65h + 0.7h * texLuma), wreck);
                float3 emberCell = floor(input.positionOS * 9.0);
                half emberHash = half(frac(sin(dot(emberCell, float3(12.9898, 78.233, 37.719))) * 43758.5453));
                half ember = step(0.955h, emberHash) * wreck;

                half metallic = _Metallic;
                half smoothness = _Smoothness;
            #if defined(_METALLICSPECGLOSSMAP)
                half4 response = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, input.uv);
                metallic = response.r;
                smoothness = response.a * _Smoothness;
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = NormalizeNormalPerPixel(input.normalWS);
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
            #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
                inputData.shadowCoord = ArcadeFragmentShadowCoord(input.positionWS, input.shadowCoord);
            #else
                inputData.shadowCoord = ArcadeFragmentShadowCoord(input.positionWS, float4(0, 0, 0, 0));
            #endif
                inputData.shadowMask = half4(1, 1, 1, 1);
                half3 n = inputData.normalWS;
                half3 v = inputData.viewDirectionWS;

                Light mainLight = GetMainLight(inputData.shadowCoord, inputData.positionWS, inputData.shadowMask);
                half3 sun = ArcadeMainLightColor(mainLight);
                half ramp = ArcadeStep(dot(n, mainLight.direction) * 0.5h + 0.5h, _RampThreshold, _RampSmoothness);
                half received = ArcadeReceivedShadow(mainLight, inputData.shadowCoord, inputData.positionWS, _ShadowThreshold, _ShadowSmoothness);
                half lit = ramp * received;

                half3 diffuse = albedo * (1.0h - 0.35h * metallic);
                half3 extra = ArcadeAdditionalLights(inputData, n);
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                extra += input.vertexLight;
            #endif
                half3 color = ArcadeToonCombine(diffuse, sun, _ShadeColor.rgb, ArcadeAmbient(n), extra, lit);

                // Hard-edged glint, only on glossy cells (paint, steel, glass) and only where the sun reaches.
                half3 h = SafeNormalize(float3(mainLight.direction) + float3(v));
                half glintEdge = 1.0h - _SpecSize * 0.2h;
                half glint = smoothstep(glintEdge - 0.006h, glintEdge + 0.006h, saturate(dot(n, h)));
                half gloss = saturate((smoothness - 0.15h) * 3.0h) * (1.0h - wreck);
                color += glint * gloss * lit * sun * _SpecColor.rgb * lerp(half3(0.5h, 0.5h, 0.5h), albedo + 0.3h, metallic);

                // Warm rim on silhouette-facing faces, stronger on the lit side.
                half rimTerm = pow(1.0h - saturate(dot(n, v)), _RimPower);
                half rim = smoothstep(0.3h, 0.42h, rimTerm) * _RimColor.a;
                color += rim * _RimColor.rgb * (albedo * 0.8h + 0.2h) * (0.35h + 0.65h * lit);

                color += ember * _WreckEmber.rgb;
            #if defined(_EMISSION)
                color += SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, input.uv).rgb * _EmissionColor.rgb;
            #endif
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(DepthVaryings input) : SV_TARGET
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS    : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output = (DepthNormalsVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = half3(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            void DepthNormalsFrag(DepthNormalsVaryings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(float3(input.normalWS));
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remapped = saturate(octNormalWS * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(remapped), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
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
