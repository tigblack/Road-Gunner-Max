#ifndef ROADGUNNER_ARCADE_LIGHTING_INCLUDED
#define ROADGUNNER_ARCADE_LIGHTING_INCLUDED

// Shared toon helpers for RoadGunner/Arcade Toon and RoadGunner/Concept Desert.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// URP derives fog from clip-space z, which does not encode distance for an orthographic camera.
// View depth works for both projections, so the haze reads the same on the ortho follow camera.
half ArcadeFogFactor(float3 positionWS)
{
    float viewZ = -TransformWorldToView(positionWS).z;
    return half(ComputeFogFactorZ0ToFar(max(viewZ - _ProjectionParams.y, 0.0)));
}

// Hemispheric trilight ambient from the RenderSettings colours URP publishes every frame. Deterministic, and it
// avoids the per-instance SH arrays, which are not filled for GPU-instanced draws of renderers with property blocks.
half3 ArcadeAmbient(half3 normalWS)
{
    half up = normalWS.y;
    half3 upper = lerp(unity_AmbientEquator.rgb, unity_AmbientSky.rgb, smoothstep(0.0h, 1.0h, up));
    half3 lower = lerp(unity_AmbientEquator.rgb, unity_AmbientGround.rgb, smoothstep(0.0h, 1.0h, -up));
    return up >= 0.0h ? upper : lower;
}

half ArcadeStep(half value, half threshold, half softness)
{
    return smoothstep(threshold - softness, threshold + softness, value);
}

// Crisp toon received shadow. Only the realtime shadow-map term is stepped; URP's distance fade is applied after the
// step, otherwise the step turns the soft fade into a hard arc across the top of the portrait screen.
half ArcadeReceivedShadow(Light mainLight, float4 shadowCoord, float3 positionWS, half threshold, half softness)
{
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    half stepped = ArcadeStep(MainLightRealtimeShadow(shadowCoord), threshold, softness);
    return lerp(stepped, 1.0h, GetMainLightShadowFade(positionWS));
#else
    return ArcadeStep(mainLight.shadowAttenuation, threshold, softness);
#endif
}

// One shadow formula for every arcade surface: the shaded colour may gain at most +0.1 saturation over its albedo
// (max channel kept, chroma scaled), so the warm-violet shade tint never turns lilac stone into a purple slab.
half3 ArcadeShadeCap(half3 shaded, half3 albedo)
{
    half maxS = max(shaded.r, max(shaded.g, shaded.b));
    half minS = min(shaded.r, min(shaded.g, shaded.b));
    half maxA = max(albedo.r, max(albedo.g, albedo.b));
    half minA = min(albedo.r, min(albedo.g, albedo.b));
    half satA = (maxA - minA) / max(maxA, 0.001h);
    half satS = (maxS - minS) / max(maxS, 0.001h);
    half limit = min(satA + 0.1h, 1.0h);
    half k = satS > limit ? limit / max(satS, 0.001h) : 1.0h;
    return maxS - (maxS - shaded) * k;
}

// lit = 1: albedo * (sun + ambient); lit = 0: capped albedo * (sun * shade + ambient). 'extra' (fill/point lights) is
// added to both so the step stays a pure sun-shadow step.
half3 ArcadeToonCombine(half3 albedo, half3 sun, half3 shade, half3 ambient, half3 extra, half lit)
{
    half3 litColor = albedo * (sun + ambient + extra);
    half3 shadeColor = ArcadeShadeCap(albedo * (sun * shade + ambient + extra), albedo);
    return lerp(shadeColor, litColor, lit);
}

float4 ArcadeFragmentShadowCoord(float3 positionWS, float4 interpolated)
{
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    return interpolated;
#elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    return TransformWorldToShadowCoord(positionWS);
#else
    return float4(0, 0, 0, 0);
#endif
}

// Additional lights stay soft (fill / gameplay accents); the stepped look comes from the sun.
half3 ArcadeSoftLight(Light light, half3 normalWS)
{
    half ndl = saturate(dot(normalWS, light.direction));
    return light.color * (light.distanceAttenuation * light.shadowAttenuation * ndl);
}

// Classic Forward (mobile/WebGL) and Forward+ (PC cluster loop) additional light accumulation.
// LIGHT_LOOP_BEGIN expects a variable called inputData in scope.
half3 ArcadeAdditionalLights(InputData inputData, half3 normalWS)
{
    half3 sum = half3(0, 0, 0);
#if defined(_ADDITIONAL_LIGHTS)
    uint meshRenderingLayers = GetMeshRenderingLayer();
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        #endif
            sum += ArcadeSoftLight(light, normalWS);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData.positionWS, inputData.shadowMask);
        #ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        #endif
            sum += ArcadeSoftLight(light, normalWS);
    LIGHT_LOOP_END
#endif
    return sum;
}

half3 ArcadeMainLightColor(Light mainLight)
{
    half3 color = mainLight.color * mainLight.distanceAttenuation;
#ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer())) color = half3(0, 0, 0);
#endif
    return color;
}

#endif
