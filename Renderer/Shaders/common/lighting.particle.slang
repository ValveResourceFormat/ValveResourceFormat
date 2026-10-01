#version 460

#ifndef VRF_LIGHTING_PARTICLE_INCLUDE
#define VRF_LIGHTING_PARTICLE_INCLUDE

#include "features.slang"

#define S_SCENE_PROBE_TYPE 0 // 1 = Individual, 2 = Atlas

float GetParticleCosine(vec3 normalWs, vec3 lightDir, float directionality)
{
    return mix(1.0, max(dot(normalWs, lightDir), 0.0), directionality);
}

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    #define ENABLE_LPV_BLENDING

    #include "LightingConstants.slang"
    #include "lighting_common.slang"
    #include "lighting.lpv.slang"
    #include "shadowmapping.slang"

    float g_flParticleLightDirectionality = 0.0;

    void CalculateShading(inout LightingTerms_t lighting, vec3 lightVector, vec3 lightColor, MaterialProperties_t mat)
    {
        lighting.DiffuseDirect += lightColor * GetParticleCosine(mat.Normal, lightVector, g_flParticleLightDirectionality);
    }

    #include "lighting.barn.slang"
#endif

struct ParticleLight_t
{
    vec3 Ambient;
    vec3 Direct; // The sun behind its baked and cascade shadows, plus the culled barn lights
};

// Unlit, as ambient 1 and no direct light, in scenes without probes and in fullbright
ParticleLight_t CalculateParticleLighting(vec3 positionWs, vec3 normalWs, float directionality)
{
    ParticleLight_t light = ParticleLight_t(vec3(1.0), vec3(0.0));

#if (D_BAKED_LIGHTING_FROM_PROBE == 1)
    if (g_iRenderMode == renderMode_FullBright)
    {
        return light;
    }

    vec3 indirectCoords = CalculateProbeIndirectCoords(positionWs);
    light.Ambient = vec3(0.0);

    if (directionality < 1.0)
    {
        const vec3 diagonal = vec3(0.57735027);
        light.Ambient = 0.5 * (SampleProbeAmbientCube(indirectCoords, diagonal) + SampleProbeAmbientCube(indirectCoords, -diagonal));
    }

    if (directionality > 0.0)
    {
        light.Ambient = mix(light.Ambient, SampleProbeAmbientCube(indirectCoords, normalWs), directionality);
    }

    vec4 bakedShadow = SampleProbeDirectLightShadows(positionWs);

    float sunVisibility = GetParticleCosine(normalWs, g_vSunDirection.xyz, directionality)
        * saturate(1.0 - dot(bakedShadow, g_vSunLightBakedShadowMask));

    if (sunVisibility > 0.0)
    {
        sunVisibility *= CalculateSunShadowMapVisibility(positionWs);
    }

    light.Direct = g_vSunColor.rgb * sunVisibility;

    #if (S_LIGHTMAP_VERSION_MINOR != 1)
        MaterialProperties_t mat;
        mat.PositionWS = positionWs;
        mat.Normal = normalWs;
        mat.Roughness = vec2(1.0);
        mat.IsometricRoughness = 1.0;

        LightingTerms_t barnLighting = InitLighting();
        g_flParticleLightDirectionality = directionality;
        CalculateBarnLighting(barnLighting, mat, bakedShadow);

        light.Direct += barnLighting.DiffuseDirect;
    #endif
#endif

    return light;
}

#endif
