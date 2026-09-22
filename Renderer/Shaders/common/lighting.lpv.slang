#version 460

#ifndef VRF_LIGHTING_LPV_INCLUDE
#define VRF_LIGHTING_LPV_INCLUDE

#include "texturing.slang"
#include "light_cull.slang"
#include "object_data.slang"

#define MAX_LIGHT_PROBES 128

struct LightProbeVolume
{
    mat4x4 WorldToLocalVolumeNormalized;
    vec4 BorderMin;
    vec4 BorderMax;
    vec4 AtlasScale;
    vec4 AtlasOffset;
};

layout(std140, binding = 3) uniform LightProbeVolumeArray
{
    LightProbeVolume g_LPV[MAX_LIGHT_PROBES];
};

uniform sampler3D g_tLPV_Irradiance; // SrgbRead(true) reserved

#if (S_LIGHTMAP_VERSION_MINOR == 1)
    uniform sampler3D g_tLPV_Indices; // reserved
    uniform sampler3D g_tLPV_Scalars; // reserved
#elif (S_LIGHTMAP_VERSION_MINOR >= 2)
    uniform sampler3D g_tLPV_Shadows; // reserved
#endif

vec3 CalculateProbeSampleCoords(uint nVolume, vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = mat4x3(g_LPV[nVolume].WorldToLocalVolumeNormalized) * vec4(fragPosition, 1.0);
    return vLightProbeLocalPos;
}

bool FindLightProbeVolume(vec3 positionWs, out uint nVolume, out vec3 localCoords)
{
    bool bFound = false;
    nVolume = 0u;
    localCoords = vec3(0.0);

    CullMasks_t masks = GetLightProbeCullMasks(positionWs);
    for (uint word = 0u; word < masks.m_nWords && AnyLaneWantsMore(!bFound); word++)
    {
        uint mask = GetCullMaskWord(masks, word, GetLightProbeLiveMask(word));

        while (mask != 0u && !bFound)
        {
            int bit = findLSB(mask);
            mask &= ~(1u << bit);

            nVolume = (word * 32u) + uint(bit);
            localCoords = CalculateProbeSampleCoords(nVolume, positionWs);

            bFound = all(greaterThanEqual(localCoords, vec3(0.0))) && all(lessThanEqual(localCoords, vec3(1.0)));
        }
    }

    return bFound;
}

uint GetFragmentLPV(vec3 fragPosition)
{
    #if (S_SCENE_PROBE_TYPE == 2) && defined(ENABLE_LPV_BLENDING)
        uint nVolume;
        vec3 localCoords;

        if (FindLightProbeVolume(fragPosition, nVolume, localCoords))
        {
            return nVolume;
        }
    #endif

    return GetObjectLightingData().VisibleLPV & 0xFFFFu;
}

vec3 CalculateProbeShadowCoords(vec3 fragPosition)
{
    uint nVolume = GetFragmentLPV(fragPosition);
    vec3 vLightProbeLocalPos = CalculateProbeSampleCoords(nVolume, fragPosition);

    #if (S_SCENE_PROBE_TYPE == 2)
        vLightProbeLocalPos = fma(saturate(vLightProbeLocalPos), g_LPV[nVolume].AtlasScale.xyz, g_LPV[nVolume].AtlasOffset.xyz);
    #endif

    return vLightProbeLocalPos;
}

// Zero below lightmap version 8.2, which reads as unshadowed
vec4 SampleProbeDirectLightShadows(vec3 fragPosition)
{
    #if (S_LIGHTMAP_VERSION_MINOR >= 2)
        return textureLod(g_tLPV_Shadows, CalculateProbeShadowCoords(fragPosition), 0.0);
    #else
        return vec4(0.0);
    #endif
}

vec3 CalculateProbeIndirectCoords(vec3 fragPosition)
{
    uint nVolume = GetFragmentLPV(fragPosition);
    LightProbeVolume lpv = g_LPV[nVolume];
    vec3 indirectCoords = CalculateProbeSampleCoords(nVolume, fragPosition);

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

vec3 DecodeProbeSample(vec4 value)
{
    #if (S_LIGHTMAP_VERSION_MINOR == 0)
        return DecodeRGBM(value);
    #endif

    return value.rgb;
}

vec3 SampleProbeAmbientCube(vec3 vIndirectSampleCoords, vec3 normal)
{
    // Take up to 3 samples along the normal direction
    vec3 vDepthSliceOffsets = mix(vec3(0, 1, 2) / 6.0, vec3(3, 4, 5) / 6.0, step(normal, vec3(0.0)));
    vec3 vAmbient[3];

    vec3 vNormalSquared = pow2(normal);

    vec3 indirectDiffuse = vec3(0.0);

    for (int i = 0; i < 3; i++)
    {
        vAmbient[i] = DecodeProbeSample(textureLod(g_tLPV_Irradiance, vIndirectSampleCoords + vec3(0, 0, vDepthSliceOffsets[i]), 0.0));
        indirectDiffuse += vAmbient[i] * vNormalSquared[i];
    }

    return indirectDiffuse;
}

vec3 ComputeLightProbeShading(in MaterialProperties_t mat)
{
    return SampleProbeAmbientCube(CalculateProbeIndirectCoords(mat.PositionWS), mat.AmbientNormal);
}

#endif
