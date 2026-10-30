#version 460

#ifndef VRF_SURFACE_LEGACY_MATERIAL_INCLUDE
#define VRF_SURFACE_LEGACY_MATERIAL_INCLUDE

// Steps of GetMaterial() for the generic shaders, whose materials are authored for the oldest lighting model.

// The albedo glows where the self illumination mask is, instead of being lit there
void ApplyAlbedoSelfIllum(inout MaterialProperties_t mat, float illumMask, float scale, vec3 tint)
{
    mat.IllumColor = scale * tint * mat.Albedo * illumMask;
    mat.Albedo *= 1.0 - illumMask;
}

// The roughness texture holds the normal map variance in rg and the roughness in ba
float GetRoughnessFromGlossinessRange(vec4 roughnessTexel, vec2 glossinessRange)
{
    float roughnessScale = max(glossinessRange.y - glossinessRange.x, 0.0);
    float roughnessBias = 1.0 - glossinessRange.y;
    float normalVariance = (roughnessTexel.r + roughnessTexel.g) * 0.5;
    float roughness = (roughnessTexel.b + roughnessTexel.a) * 0.5 * roughnessScale + roughnessBias + normalVariance;

    return min(roughness, 1.0);
}

#endif
