#version 460

#ifndef VRF_LIGHTING_LPV_INCLUDE
#define VRF_LIGHTING_LPV_INCLUDE

#include "texturing.slang"
#include "light_cull.slang"
#include "object_data.slang"
#include "LightingConstants.slang"

#define MAX_LIGHT_PROBES 320

// Volumes one fragment blends between. Past this the remainder is lit by the ambient fallback.
#define MAX_BLENDED_LIGHT_PROBES 4

struct LightProbeVolume
{
    mat4x4 WorldToLocalVolumeNormalized;
    vec4 BorderMin;
    vec4 BorderMax;
    vec4 AtlasScale;
    vec4 AtlasOffset;
    vec4 InvEdgeWidth;
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

struct LightProbeBlend_t
{
    uint Volume[MAX_BLENDED_LIGHT_PROBES];
    float Weight[MAX_BLENDED_LIGHT_PROBES];
    uint Count;
    float Coverage;
};

LightProbeBlend_t g_LightProbeBlend = LightProbeBlend_t(uint[](0u, 0u, 0u, 0u), float[](0.0, 0.0, 0.0, 0.0), 0u, 0.0);
bool g_bLightProbeBlendReady = false;

vec3 CalculateProbeSampleCoords(uint nVolume, vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = mat4x3(g_LPV[nVolume].WorldToLocalVolumeNormalized) * vec4(fragPosition, 1.0);
    return vLightProbeLocalPos;
}

// Bit order is priority order, so each volume only gets what the ones before it left uncovered
void FindLightProbeVolumes(vec3 positionWs)
{
    bool bFound = false;

    CullMasks_t masks = GetLightProbeCullMasks(positionWs);
    for (uint word = 0u; word < masks.m_nWords && AnyLaneWantsMore(!bFound); word++)
    {
        uint mask = GetCullMaskWord(masks, word, GetLightProbeLiveMask(word));

        while (mask != 0u && !bFound)
        {
            int bit = findLSB(mask);
            mask &= ~(1u << bit);

            uint nVolume = (word * 32u) + uint(bit);
            vec3 localCoords = CalculateProbeSampleCoords(nVolume, positionWs);

            vec3 vEdgeFade = saturate(min(localCoords, 1.0 - localCoords) * g_LPV[nVolume].InvEdgeWidth.xyz);
            float flWeight = smoothstep(0.0, 1.0, min(vEdgeFade.x, min(vEdgeFade.y, vEdgeFade.z))) * (1.0 - g_LightProbeBlend.Coverage);

            if (flWeight > 0.0)
            {
                g_LightProbeBlend.Volume[g_LightProbeBlend.Count] = nVolume;
                g_LightProbeBlend.Weight[g_LightProbeBlend.Count] = flWeight;
                g_LightProbeBlend.Count++;
                g_LightProbeBlend.Coverage += flWeight;
            }

            bFound = g_LightProbeBlend.Coverage > 0.99 || g_LightProbeBlend.Count == uint(MAX_BLENDED_LIGHT_PROBES);
        }
    }
}

LightProbeBlend_t GetFragmentLPV(vec3 fragPosition)
{
    if (!g_bLightProbeBlendReady)
    {
        g_bLightProbeBlendReady = true;

        #if (S_SCENE_PROBE_TYPE == 2) && (defined(ENABLE_LPV_BLENDING) || (D_LPV_BLENDING == 1))
            FindLightProbeVolumes(fragPosition);
        #else
            g_LightProbeBlend.Volume[0] = GetObjectLightingData().VisibleLPV & 0xFFFFu;
            g_LightProbeBlend.Weight[0] = 1.0;
            g_LightProbeBlend.Count = 1u;
            g_LightProbeBlend.Coverage = 1.0;
        #endif
    }

    return g_LightProbeBlend;
}

vec3 CalculateProbeShadowCoords(uint nVolume, vec3 fragPosition)
{
    vec3 vLightProbeLocalPos = CalculateProbeSampleCoords(nVolume, fragPosition);

    #if (S_SCENE_PROBE_TYPE == 2)
        vLightProbeLocalPos = fma(saturate(vLightProbeLocalPos), g_LPV[nVolume].AtlasScale.xyz, g_LPV[nVolume].AtlasOffset.xyz);
    #endif

    return vLightProbeLocalPos;
}

vec3 CalculateProbeShadowCoords(vec3 fragPosition)
{
    return CalculateProbeShadowCoords(GetObjectLightingData().VisibleLPV & 0xFFFFu, fragPosition);
}

// Zero below lightmap version 8.2, which reads as unshadowed
vec4 SampleProbeDirectLightShadows(vec3 fragPosition)
{
    vec4 shadows = vec4(0.0);

    #if (S_LIGHTMAP_VERSION_MINOR >= 2)
        LightProbeBlend_t blend = GetFragmentLPV(fragPosition);

        for (uint i = 0u; i < blend.Count; i++)
        {
            shadows += textureLod(g_tLPV_Shadows, CalculateProbeShadowCoords(blend.Volume[i], fragPosition), 0.0) * blend.Weight[i];
        }
    #endif

    return shadows;
}

vec3 CalculateProbeIndirectCoords(uint nVolume, vec3 fragPosition)
{
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

vec3 SampleLightProbeIrradiance(vec3 positionWs, vec3 normal)
{
    LightProbeBlend_t blend = GetFragmentLPV(positionWs);

    // Sky from above and its ground bounce from below, for what no volume covers
    vec3 irradiance = vec4(normal, 1.0) * mat3x4(g_vAmbientLightingSH[0], g_vAmbientLightingSH[1], g_vAmbientLightingSH[2]);
    irradiance *= blend.Coverage < 0.99 ? 1.0 - blend.Coverage : 0.0;

    for (uint i = 0u; i < blend.Count; i++)
    {
        irradiance += SampleProbeAmbientCube(CalculateProbeIndirectCoords(blend.Volume[i], positionWs), normal) * blend.Weight[i];
    }

    return irradiance;
}

vec3 ComputeLightProbeShading(in MaterialProperties_t mat)
{
    return SampleLightProbeIrradiance(mat.PositionWS, mat.AmbientNormal);
}

#endif
