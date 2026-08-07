#version 460

#ifndef VRF_CITADEL_INCLUDE
#define VRF_CITADEL_INCLUDE

#include "utils.slang"

// The usual dielectric/metal mix, faded out on near black albedo: anything darker than
// 0.04 in its brightest channel loses its specular entirely.
vec3 GetSpecularColorCitadel(vec3 albedo, float metalness)
{
    const vec3 F0 = vec3(0.04);
    float albedoMax = max(albedo.x, max(albedo.y, albedo.z));

    return mix(F0, albedo, metalness) * saturate(albedoMax * 25.0);
}

// ---------------------------------------------------------------------------------------
// Non-photorealistic lighting.
//
// Wiring it up means, per light in common/lighting.slang: replacing NdotL in the diffuse term
// with NprToonDiffuse, replacing the GGX result with NprSteppedSpecular scaled by
// NprSpecularTint, and adding NprRimLight on top. The rim's optional depth occlusion needs a
// scene depth texture bound in the forward pass.
// ---------------------------------------------------------------------------------------
#if (F_USE_NPR_LIGHTING == 1)
    // Diffuse
    uniform int g_bNPRDirectDiffuse = 0;
    uniform int g_bNPRBounceDiffuse = 0;
    uniform float g_flNPRDirectLightWrap = 0.0;
    uniform float g_flNPRDiffuseStepSharpness = 0.0;
    uniform float g_flNPRDiffusePbrBlend = 0.0;
    uniform float g_flOoNPRDirectLightNormalization = 0.0;
    uniform vec3 g_vNPRLightWeights = vec3(0.0);
    uniform vec2 g_vNPRDfaoInfluenceRange = vec2(0.0);

    // Specular. g_nNPRSpecularSteps is a float despite the name, and is divided by.
    uniform int g_bNPRDirectSpecular = 0;
    uniform float g_nNPRSpecularSteps = 0.0;
    uniform float g_flNPRSpecularStepSharpness = 0.0;
    uniform float g_flNPRSpecularTint = 0.0;
    uniform float g_flNPRSpecularReflectance = 0.0;
    uniform float g_fNPRSpecularRoughnessBias = 0.0;
    uniform int g_bNoSpecularAtFullRoughness = 0;

    // Rim light
    uniform int g_bNPRRimLighting = 0;
    uniform float g_flNPRRimLightWrap = 0.0;
    uniform float g_flNPRRimLightFalloff = 0.0;
    uniform float g_flNPRRimLightStrength = 0.0;
    uniform vec2 g_vNPRRimLightUpRamp = vec2(0.0);
    uniform int g_bNPRRimLightingDepthOcclusion = 0;
    uniform float g_flNPRRimOcclusionSampleDistance = 0.0;
    uniform float g_flNPRRimOcclusionDepthToleranceMin = 0.0;
    uniform float g_flNPRRimOcclusionDepthToleranceRange = 0.0;

    // Exposure control
    uniform int g_bNPRExposureControl = 0;
    uniform vec3 g_vNPRExposureTargets = vec3(0.0);
    uniform float g_flNPRExposureControlPbrBlend = 0.0;
#endif

// Symmetric S-curve about 0.5, shared by the toon diffuse and the specular banding. k is the
// hardness: 1 is a straight line, larger values push the curve toward a hard step.
float NprGain(float x, float k)
{
    bool upper = x > 0.5;
    float folded = upper ? (1.0 - x) : x;
    float curved = pow(0.5, 1.0 - k) * pow(folded, k);

    return upper ? (1.0 - curved) : curved;
}

// Replaces NdotL in one light's diffuse term. wrap pushes light around the terminator before
// the curve is applied, normalization is g_flOoNPRDirectLightNormalization, and pbrBlend
// fades the whole thing back to plain lambert.
float NprToonDiffuse(float NdotL, float wrap, float stepSharpness, float normalization, float pbrBlend)
{
    float wrapped = saturate(0.5 + (((wrap - 0.5) + NdotL) - 0.5) * 2.0);
    float toon = NprGain(wrapped, 1.0 / (1.0 - stepSharpness));

    return mix(normalization * toon, NdotL, pbrBlend);
}

// Quantises one light's GGX specular into bands and softens each band edge. Pass the roughness
// the material had before g_fNPRSpecularRoughnessBias was applied; the bias belongs to the GGX
// evaluation feeding this, and to the energy term below.
float NprSteppedSpecular(float specular, float roughness, float biasedRoughness, float steps, float stepSharpness)
{
    float scaled = saturate(specular) * steps;
    float band = fract(scaled);
    float k = 1.0 / (0.01 - roughness * (stepSharpness - 0.99));

    float stepped = ((scaled - band) + NprGain(band, k)) / steps;
    float energy = 0.5 / (1.0 - exp2(-3.3219299 * biasedRoughness * biasedRoughness));

    return stepped * energy;
}

// The colour the stepped specular is multiplied by. tint slides from a desaturated specular
// colour toward the albedo, the result is renormalised to reflectance, and metals take the
// albedo outright.
vec3 NprSpecularTint(vec3 specularColor, vec3 albedo, float metalness, float tint, float reflectance)
{
    vec3 tinted = mix(vec3(GetLuma(specularColor)), albedo, tint);
    float luma = GetLuma(tinted);

    if (luma > 1e-4)
    {
        tinted *= reflectance / luma;
    }

    return mix(tinted, albedo, metalness);
}

// Additive rim term for one light. upRamp gates the effect by how far the normal points up,
// which is what keeps it on the tops of shoulders and heads. occlusion is the material's
// ambient occlusion and rimMask is g_tTintMaskRimLightMask.y.
float NprRimLight(vec3 normal, vec3 lightVector, float wrap, float falloff, vec2 upRamp,
    float strength, float occlusion, float rimMask)
{
    float wrapScale = 1.0 + wrap;
    float rim = pow(saturate((dot(normal, lightVector) + wrap) / (wrapScale * wrapScale)), falloff);
    float up = saturate((normal.z - upRamp.x) / (upRamp.y - upRamp.x));

    return rim * up * strength * occlusion * rimMask;
}

#endif
