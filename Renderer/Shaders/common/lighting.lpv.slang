#version 460

#ifndef VRF_LIGHTING_LPV_INCLUDE
#define VRF_LIGHTING_LPV_INCLUDE

#include "texturing.slang"

#define MAX_LIGHT_PROBES 128

struct LightProbeVolume
{
    mat4x4 WorldToLocalVolumeNormalized;
    vec4 BorderMin;
    vec4 BorderMax;
    vec4 AtlasScale;
    vec4 AtlasOffset;
};

flat in uint g_nVisibleLPV;

layout(std140, binding = 3) uniform LightProbeVolumeArray
{
    LightProbeVolume g_LPV[MAX_LIGHT_PROBES];
};

uniform sampler3D g_tLPV_Irradiance;

#if (S_LIGHTMAP_VERSION_MINOR == 1)
    uniform sampler3D g_tLPV_Indices;
    uniform sampler3D g_tLPV_Scalars;
#elif (S_LIGHTMAP_VERSION_MINOR >= 2)
    uniform sampler3D g_tLPV_Shadows;
#endif

vec3 CalculateProbeSampleCoords(vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = mat4x3(g_LPV[g_nVisibleLPV].WorldToLocalVolumeNormalized) * vec4(fragPosition, 1.0);
    return vLightProbeLocalPos;
}

vec3 CalculateProbeShadowCoords(vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = CalculateProbeSampleCoords(fragPosition);

    #if (S_SCENE_PROBE_TYPE == 2)
        vLightProbeLocalPos = fma(saturate(vLightProbeLocalPos), g_LPV[g_nVisibleLPV].AtlasScale.xyz, g_LPV[g_nVisibleLPV].AtlasOffset.xyz);
    #endif

    return vLightProbeLocalPos;
}

vec3 CalculateProbeIndirectCoords(vec3 fragPosition)
{
    LightProbeVolume lpv = g_LPV[g_nVisibleLPV];
    vec3 indirectCoords = CalculateProbeSampleCoords(fragPosition);

    #if (S_SCENE_PROBE_TYPE == 1)
        indirectCoords.z /= 6;
        // clamp(indirectCoords, lpv.Layer0TextureMin.xyz, lpv.Layer0TextureMax.xyz);
    #elif (S_SCENE_PROBE_TYPE == 2)
        indirectCoords.z /= 6;
        indirectCoords = clamp(indirectCoords, lpv.BorderMin.xyz, lpv.BorderMax.xyz);

        indirectCoords.z *= 6;
        indirectCoords = fma(indirectCoords, lpv.AtlasScale.xyz, lpv.AtlasOffset.xyz);

        indirectCoords.z /= 6;
    #endif

    return indirectCoords;
}

vec3 ComputeLightProbeShading(in MaterialProperties_t mat)
{
    vec3 vIndirectSampleCoords = CalculateProbeIndirectCoords(mat.PositionWS);

    // Take up to 3 samples along the normal direction
    vec3 vDepthSliceOffsets = mix(vec3(0, 1, 2) / 6.0, vec3(3, 4, 5) / 6.0, step(mat.AmbientNormal, vec3(0.0)));
    vec3 vAmbient[3];

    vec3 vNormalSquared = pow2(mat.AmbientNormal);

    vec3 indirectDiffuse = vec3(0.0);

    for (int i = 0; i < 3; i++)
    {
        vAmbient[i] = textureLod(g_tLPV_Irradiance, vIndirectSampleCoords + vec3(0, 0, vDepthSliceOffsets[i]), 0.0).rgb;
        indirectDiffuse += vAmbient[i] * vNormalSquared[i];
    }

    // SteamVR Home lpv irradiance is RGBM Dxt5
    #if (S_LIGHTMAP_VERSION_MINOR == 0)
        indirectDiffuse = pow2(indirectDiffuse); // not bothering with RGBM
    #endif

    return indirectDiffuse;
}

#endif
