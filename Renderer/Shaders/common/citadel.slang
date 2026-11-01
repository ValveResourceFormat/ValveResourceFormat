#version 460

#ifndef VRF_CITADEL_INCLUDE
#define VRF_CITADEL_INCLUDE

#include "utils.slang"
#include "material_properties.slang"

// The usual dielectric/metal mix, faded out on near black albedo: anything darker than
// 0.04 in its brightest channel loses its specular entirely.
vec3 GetSpecularColorCitadel(vec3 albedo, float metalness)
{
    const vec3 F0 = vec3(0.04);
    float albedoMax = Max3(albedo);

    return mix(F0, albedo, metalness) * saturate(albedoMax * 25.0);
}

// The diffuse and specular colors of the albedo and metalness
void ResolveDiffuseSpecularCitadel(inout MaterialProperties_t mat)
{
    mat.DiffuseColor = mat.Albedo - mat.Albedo * mat.Metalness;
    mat.SpecularColor = GetSpecularColorCitadel(mat.Albedo, mat.Metalness);
}

#endif
